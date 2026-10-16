import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../api/client';
import type { AttemptDetailDto, AttemptDto, StudentExamDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { uploadEvidence } from '../../proctoring/attemptQueue';
import { captureJpeg, classify, countFaces } from '../../proctoring/faceMonitor';
import { browserProblem, cameraProblem, enterFullscreen, hasAudio, openMedia } from '../../proctoring/media';
import type { FaceResult } from '../../proctoring/strikes';
import { Button, Card, ErrorBar, InfoBar, TextField } from '../../ui';
import { inProgressId } from './studentExams';
import './exam.css';

const FACE_TEXT: Record<FaceResult | 'starting', string> = {
  starting: 'Starting the camera…',
  ok: 'Face detected.',
  missing: 'No face found. Look at the camera.',
  multiple: 'More than one face. Only you should be in view.',
};

export function ExamStartPage() {
  const examId = Number(useParams().id);
  const [params] = useSearchParams();
  const resumeId = Number(params.get('resume')) || null;
  const { api } = useAuth();
  const navigate = useNavigate();
  const exams = useQuery({ queryKey: ['student-exams'], queryFn: () => api.get<StudentExamDto[]>('/api/exams') });
  const exam = exams.data?.find(e => e.id === examId);

  const video = useRef<HTMLVideoElement>(null);
  const [problem, setProblem] = useState<string | null>(browserProblem);
  const [face, setFace] = useState<FaceResult | 'starting'>('starting');
  const [micMissing, setMicMissing] = useState(false);
  const [pin, setPin] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  // Camera + live face check, once a second, until the student starts.
  useEffect(() => {
    if (problem) return;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    openMedia().then(
      stream => {
        if (cancelled) return;
        setMicMissing(!hasAudio(stream));
        const v = video.current!;
        v.srcObject = stream;
        void v.play().catch(() => {});
        const tick = async () => {
          try {
            const faces = await countFaces(v);
            if (cancelled) return;
            setFace(classify(faces));
          } catch {
            if (!cancelled) setProblem("Face detection couldn't start in this browser. Use the latest Chrome or Edge.");
            return;
          }
          timer = setTimeout(tick, 1000);
        };
        void tick();
      },
      e => { if (!cancelled) setProblem(cameraProblem(e)); },
    );
    return () => { cancelled = true; clearTimeout(timer); };
  }, [problem]);

  async function begin(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await enterFullscreen(); // browsers allow this only straight after a click
    } catch {
      setBusy(false);
      setError(new ApiError(-1, 'Fullscreen was blocked.', 'Allow fullscreen for this site, then try again.'));
      return;
    }
    try {
      const load = async (id: number) => (await api.get<AttemptDetailDto>(`/api/attempts/${id}`)).attempt;
      let attempt: AttemptDto;
      if (resumeId) attempt = await load(resumeId);
      else {
        try {
          attempt = await api.post<AttemptDto>(`/api/exams/${examId}/attempts`, { pin });
        } catch (x) {
          const existing = inProgressId(x); // started in another tab: resume it
          if (existing === null) throw x;
          attempt = await load(existing);
        }
      }
      if (attempt.status !== 'InProgress') throw new ApiError(409, 'This exam is closed.', 'This attempt has already been submitted.');
      const frame = video.current ? await captureJpeg(video.current) : null;
      if (frame) await uploadEvidence(api, attempt.id, 'ReferenceFace', frame).catch(x => console.warn('SecuCode: reference photo upload failed', x));
      navigate(`/attempt/${attempt.id}`, { replace: true, state: { title: exam?.title } });
    } catch (x) {
      if (document.fullscreenElement) await document.exitFullscreen().catch(() => {});
      setError(x);
      setBusy(false);
    }
  }

  const canBegin = !problem && face === 'ok' && (resumeId !== null || pin.trim() !== '') && !busy;

  return (
    <>
      <div className="page-header">
        <h1 className="title">{exam?.title ?? 'Exam'}</h1>
        <Button onClick={() => navigate('/exams')}>Back</Button>
      </div>
      {problem ? (
        <InfoBar kind="error" title="You can't take this exam here.">{problem}</InfoBar>
      ) : (
        <div className="start-grid">
          <Card className="stack">
            <video ref={video} className="start-preview" muted playsInline aria-label="Your webcam preview" />
            <p role="status" className={face === 'ok' ? undefined : 'secondary'}>{FACE_TEXT[face]}</p>
          </Card>
          <Card className="stack">
            <h2 className="subtitle">{resumeId ? 'Resume your exam' : 'Before you start'}</h2>
            <ul className="rules">
              <li>The exam runs in fullscreen. Leaving fullscreen or switching to another tab or window counts as leaving the exam; the third time submits it.</li>
              <li>Your webcam checks every few seconds that only you are in view. Three failed checks in a row submit the exam.</li>
              <li>Copy and paste are turned off. Your code is saved every 30 seconds.</li>
            </ul>
            {micMissing && <InfoBar kind="warning" title="No microphone.">The exam can go ahead; staff will see that audio wasn't recorded.</InfoBar>}
            {error !== null && <ErrorBar error={error} />}
            <form className="stack" onSubmit={begin}>
              {!resumeId && (
                <TextField label="PIN" type="password" inputMode="numeric" autoComplete="off" value={pin} onChange={e => setPin(e.target.value)} />
              )}
              <div>
                <Button type="submit" variant="primary" busy={busy} disabled={!canBegin}>{resumeId ? 'Resume exam' : 'Start exam'}</Button>
              </div>
            </form>
          </Card>
        </div>
      )}
    </>
  );
}
