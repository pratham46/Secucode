import { useCallback, useEffect, useRef, useState } from 'react';
import { useBlocker, useLocation, useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../../api/client';
import { connectLive } from '../../api/live';
import { LANGUAGES, type AttemptDetailDto, type AttemptDto, type Language, type RunResult } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { apiSender, AttemptQueue, type QueueItem } from '../../proctoring/attemptQueue';
import { captureJpeg } from '../../proctoring/faceMonitor';
import { clearLocal, loadLocal, pickCode, saveLocal } from '../../proctoring/localCopy';
import { closeMedia, currentMedia, enterFullscreen } from '../../proctoring/media';
import { clearStrikes, loadStrikes, MAX_STRIKES } from '../../proctoring/strikes';
import { clockOffset, formatRemaining, remainingMs, submitWindowOver } from '../../proctoring/timer';
import { Button, Card, ErrorBar, InfoBar, Select, Spinner } from '../../ui';
import { CodeEditor } from '../../ui/CodeEditor';
import { Dialog } from '../../ui/Dialog';
import { SignInDialog } from '../login/SignInDialog';
import { useProctoring } from './useProctoring';
import './exam.css';

type Phase =
  | { kind: 'loading' }
  | { kind: 'error'; error: unknown }
  | { kind: 'running' }
  | { kind: 'submitting' }
  | { kind: 'done'; title: string; message: string };

type RunState = { busy: boolean; result?: RunResult; error?: unknown };

const CLOSED = 'This exam is closed.';
const closedMessage = (a: AttemptDto) =>
  a.status === 'Voided' ? 'Staff voided this attempt. Ask them what to do next.'
    : a.status === 'AutoSubmitted' ? `The server submitted your last saved code (${a.submitReason ?? 'the deadline passed'}).`
      : a.status === 'InProgress' ? 'Time is up; the server will submit your last saved code.'
        : 'This attempt has already been submitted.';

const block = (e: { preventDefault(): void }) => e.preventDefault();

// Leaves fullscreen and turns the camera and microphone off.
function leaveExam() {
  closeMedia();
  if (document.fullscreenElement) void document.exitFullscreen().catch(() => {});
}

export function ExamPage() {
  const attemptId = Number(useParams().id);
  const title = (useLocation().state as { title?: string } | null)?.title ?? 'Exam';
  const { api, expired } = useAuth();
  const navigate = useNavigate();
  const video = useRef<HTMLVideoElement>(null);
  const queue = useRef<AttemptQueue | null>(null);
  const finishing = useRef(false);
  const ended = useRef(false);
  const latest = useRef({ code: '', language: 'Python' as Language, saved: '' });

  const [phase, setPhase] = useState<Phase>({ kind: 'loading' });
  const [question, setQuestion] = useState('');
  const [code, setCode] = useState('');
  const [language, setLanguage] = useState<Language>('Python');
  const [deadline, setDeadline] = useState<string | null>(null);
  const [offset, setOffset] = useState(0);
  const [now, setNow] = useState(Date.now());
  const [offline, setOffline] = useState(false);
  const [run, setRun] = useState<RunState>({ busy: false });
  const [confirming, setConfirming] = useState(false);

  const end = useCallback((heading: string, message: string) => {
    ended.current = true;
    queue.current?.close();
    leaveExam();
    setPhase({ kind: 'done', title: heading, message });
  }, []);

  // The attempt is over on the server: this browser's copy of the code is no longer needed.
  const endClosed = useCallback((a: AttemptDto) => {
    if (a.status !== 'InProgress') clearLocal(attemptId);
    end(CLOSED, closedMessage(a));
  }, [attemptId, end]);

  const fetchAttempt = useCallback(async () => {
    const { attempt } = await api.get<AttemptDetailDto>(`/api/attempts/${attemptId}`);
    setOffset(clockOffset(attempt.serverTime));
    setDeadline(attempt.deadlineAt);
    return attempt;
  }, [api, attemptId]);

  // The attempt's write queue lives as long as the page.
  useEffect(() => {
    const q = new AttemptQueue(apiSender(api, attemptId));
    q.onOfflineChange = setOffline;
    q.onClosed = () => {
      fetchAttempt().then(endClosed, () => end(CLOSED, 'The server no longer accepts changes to this attempt.'));
    };
    q.onSubmitRejected = error =>
      end('Your submission was refused.', `${error.detail ?? error.title} The server will submit your last auto-saved code at the deadline.`);
    q.onSubmitted = () => {
      clearLocal(attemptId);
      clearStrikes(attemptId);
      end('Submitted.', 'Your code was submitted. You can go back to your exams.');
    };
    queue.current = q;
    const online = () => q.retryNow();
    addEventListener('online', online);
    return () => {
      removeEventListener('online', online);
      q.close();
      queue.current = null;
    };
  }, [api, attemptId, fetchAttempt, end, endClosed]);

  const enqueue = useCallback((item: QueueItem) => queue.current?.enqueue(item), []);

  // Load. After a reload the camera is gone, so the student goes back through Resume on the start page.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const attempt = await fetchAttempt();
        if (cancelled) return;
        if (attempt.status !== 'InProgress') return endClosed(attempt);
        if (!currentMedia()) return navigate(`/exams/${attempt.examId}/start?resume=${attemptId}`, { replace: true });
        const picked = pickCode(attempt, loadLocal(attemptId));
        latest.current = { code: picked.code, language: picked.language, saved: `${attempt.language}\n${attempt.code}` };
        setQuestion(attempt.questionText);
        setCode(picked.code);
        setLanguage(picked.language);
        setPhase({ kind: 'running' });
      } catch (e) {
        if (!cancelled) setPhase({ kind: 'error', error: e });
      }
    })();
    return () => { cancelled = true; };
  }, [attemptId, fetchAttempt, navigate, endClosed]);

  const finish = useCallback(async (reason: string) => {
    if (finishing.current) return; // timer, strikes and the button can all fire; only one submit
    finishing.current = true;
    setConfirming(false);
    const frame = video.current ? await captureJpeg(video.current) : null; // before the camera stops
    if (ended.current) return; // the attempt ended (voided, 409) while the photo was being taken
    setPhase({ kind: 'submitting' });
    if (frame) enqueue({ kind: 'evidence', evidenceKind: 'Webcam', blob: frame });
    const l = latest.current;
    enqueue({ kind: 'submit', code: l.code, language: l.language, reason });
  }, [enqueue]);

  const running = phase.kind === 'running';
  const { fullscreen, strikes, trip } = useProctoring({ attemptId, video, active: running, report: enqueue, onAutoSubmit: r => void finish(r) });

  // Browser Back would leave the exam unrecorded: stay, and count it as leaving.
  const blocker = useBlocker(() => phase.kind === 'running' || phase.kind === 'submitting');
  useEffect(() => {
    if (blocker.state !== 'blocked') return;
    blocker.reset();
    trip();
  }, [blocker, trip]);

  // A reload after the 3rd strike but before the submit landed: the counter only fires at exactly 3, so submit here.
  const strikesChecked = useRef(false);
  useEffect(() => {
    if (!running || strikesChecked.current) return;
    strikesChecked.current = true;
    const s = loadStrikes(attemptId);
    if (s.trips >= MAX_STRIKES) void finish(`Left the exam ${MAX_STRIKES} times.`);
    else if (s.faceRun >= MAX_STRIKES) void finish(`Face check failed ${MAX_STRIKES} times in a row.`);
  }, [running, attemptId, finish]);

  // Clock.
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);
  const remaining = deadline === null ? null : remainingMs(deadline, offset, now);
  useEffect(() => {
    if (running && remaining === 0) void finish('Time up.');
  }, [running, remaining, finish]);
  useEffect(() => {
    if (phase.kind === 'submitting' && deadline !== null && submitWindowOver(deadline, offset, now)) {
      clearLocal(attemptId);
      end('The submit window has passed.', 'The connection did not come back in time. The server has submitted your last saved code.');
    }
  }, [phase.kind, deadline, offset, now, end, attemptId]);

  // Auto-save every 30 s when something changed; local copy 1 s after the last change.
  useEffect(() => {
    if (!running) return;
    const t = setInterval(() => {
      const l = latest.current;
      const key = `${l.language}\n${l.code}`;
      if (key === l.saved) return;
      enqueue({ kind: 'code', code: l.code, language: l.language });
      l.saved = key;
    }, 30_000);
    return () => clearInterval(t);
  }, [running, enqueue]);
  useEffect(() => {
    if (!running) return;
    const t = setTimeout(() => saveLocal(attemptId, code, language), 1000);
    return () => clearTimeout(t);
  }, [running, attemptId, code, language]);

  // Live changes from staff (extensions, voids); re-read the attempt after every (re)connect.
  useEffect(() => {
    if (!running) return;
    return connectLive(api, {
      DeadlineChanged: m => { if (m.attemptId === attemptId) setDeadline(m.deadlineAt); },
      AttemptVoided: m => { if (m.attemptId === attemptId) { clearLocal(attemptId); end(CLOSED, 'Staff voided this attempt. Ask them what to do next.'); } },
    }, () => {
      fetchAttempt().then(a => { if (a.status !== 'InProgress' && !finishing.current) endClosed(a); }, () => {});
    });
  }, [running, api, attemptId, fetchAttempt, end, endClosed]);

  // Self-view.
  useEffect(() => {
    const v = video.current;
    if (!running || !v) return;
    v.srcObject = currentMedia();
    void v.play().catch(() => {});
  }, [running]);

  async function runCode() {
    setRun({ busy: true });
    try {
      setRun({ busy: false, result: await api.post<RunResult>('/api/run', { language, code, stdin: null }) });
    } catch (e) {
      setRun({ busy: false, error: e });
    }
  }

  const onCode = (v: string) => { setCode(v); latest.current.code = v; };
  const onLanguage = (v: Language) => { setLanguage(v); latest.current.language = v; };
  const reauth = expired && <SignInDialog onSignedIn={() => queue.current?.retryNow()} />;

  if (phase.kind === 'loading') return <div className="exam-center"><Spinner size={32} /></div>;
  if (phase.kind === 'error')
    return (
      <div className="exam-center">
        <Card className="stack exam-message">
          <ErrorBar error={phase.error} onRetry={() => location.reload()} />
          <div><Button onClick={() => navigate('/exams')}>Back to exams</Button></div>
        </Card>
      </div>
    );
  if (phase.kind === 'done')
    return (
      <div className="exam-center">
        <Card className="stack exam-message">
          <h1 className="title">{phase.title}</h1>
          <p>{phase.message}</p>
          <div><Button variant="primary" onClick={() => navigate('/exams', { replace: true })}>Back to exams</Button></div>
        </Card>
      </div>
    );
  if (phase.kind === 'submitting')
    return (
      <div className="exam-center">
        <Card className="stack exam-message">
          <div className="row"><Spinner /><h1 className="subtitle">Submitting…</h1></div>
          {offline && <InfoBar kind="warning" title="Offline.">Your code will be submitted when the connection returns.</InfoBar>}
        </Card>
        {reauth}
      </div>
    );

  return (
    <div className="exam">
      <header className="exam-bar">
        <strong className="exam-title">{title}</strong>
        <span className="strikes">Left exam: {strikes.trips} of {MAX_STRIKES} · Face checks failed: {strikes.faceRun} of {MAX_STRIKES}</span>
        <span className={`timer${remaining !== null && remaining < 5 * 60_000 ? ' low' : ''}`} role="timer" aria-label="Time left">
          {remaining === null ? '--:--' : formatRemaining(remaining)}
        </span>
        <Button variant="primary" onClick={() => setConfirming(true)}>Submit</Button>
      </header>
      {offline && <InfoBar kind="warning" title="Offline.">Your work is kept in this tab and will be sent when the connection returns.</InfoBar>}
      <div className="exam-body">
        <aside className="exam-question" onCopy={block} onCut={block} onContextMenu={block}>
          <h2 className="subtitle">Question</h2>
          <p className="question-text">{question}</p>
          <video ref={video} className="self-view" muted playsInline aria-label="Your webcam" />
        </aside>
        <section className="exam-work">
          <div className="row exam-tools">
            <Select label="Language" value={language} onChange={e => onLanguage(e.target.value as Language)}>
              {LANGUAGES.map(l => <option key={l} value={l}>{l}</option>)}
            </Select>
            <Button onClick={runCode} busy={run.busy}>Run</Button>
          </div>
          <div className="exam-editor"><CodeEditor value={code} language={language} onChange={onCode} locked label="Your code" /></div>
          <Output run={run} />
        </section>
      </div>
      {!fullscreen && (
        <div className="fs-overlay" role="dialog" aria-modal="true" aria-labelledby="fs-title">
          <Card className="stack exam-message">
            <h2 id="fs-title" className="subtitle">Return to fullscreen to continue</h2>
            <p>Leaving fullscreen counts as leaving the exam.</p>
            <div>
              <Button variant="primary" onClick={() => void enterFullscreen().catch(() => {})}>Return to fullscreen</Button>
            </div>
          </Card>
        </div>
      )}
      <Dialog open={confirming} title="Submit your exam?" onClose={() => setConfirming(false)}
        actions={<>
          <Button onClick={() => setConfirming(false)}>Keep working</Button>
          <Button variant="primary" onClick={() => void finish('Submitted by the student.')}>Submit</Button>
        </>}>
        <p>You can't change your code after submitting.</p>
      </Dialog>
      {reauth}
    </div>
  );
}

function Output({ run }: { run: RunState }) {
  let body;
  if (run.error !== undefined)
    body = run.error instanceof ApiError && run.error.status === 429
      ? <InfoBar kind="warning" title="Too many runs.">Wait a moment, then run again.</InfoBar>
      : <ErrorBar error={run.error} />;
  else if (run.result)
    body = (
      <>
        {run.result.stdout && <pre>{run.result.stdout}</pre>}
        {run.result.stderr && <pre className="stderr">{run.result.stderr}</pre>}
        <p className="caption secondary">
          {run.result.timedOut ? 'Stopped: it ran longer than 5 seconds.' : `Exit code ${run.result.exitCode}`} · {run.result.durationMs} ms
        </p>
      </>
    );
  else body = <p className="secondary">Run your code to see its output here.</p>;
  return <section className="exam-output" aria-label="Output" aria-live="polite">{body}</section>;
}
