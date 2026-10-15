import { useEffect, useRef, useState, type RefObject } from 'react';
import type { QueueItem } from '../../proctoring/attemptQueue';
import { AudioRecorder } from '../../proctoring/audioRecorder';
import { captureJpeg, classify, countFaces } from '../../proctoring/faceMonitor';
import { watchFocus } from '../../proctoring/focusGuard';
import { currentMedia, hasAudio } from '../../proctoring/media';
import { loadStrikes, saveStrikes, StrikeCounter, type StrikeState } from '../../proctoring/strikes';

const FACE_CHECK_MS = 3000;

// Wires the browser monitors to the strike rules while the exam runs. Everything noticed goes to `report`
// (the attempt queue); a third strike calls onAutoSubmit.
export function useProctoring({ attemptId, video, active, report, onAutoSubmit }: {
  attemptId: number;
  video: RefObject<HTMLVideoElement>;
  active: boolean;
  report(item: QueueItem): void;
  onAutoSubmit(reason: string): void;
}): { fullscreen: boolean; strikes: StrikeState } {
  const [fullscreen, setFullscreen] = useState(() => document.fullscreenElement !== null);
  const [strikes, setStrikes] = useState<StrikeState>(() => loadStrikes(attemptId));
  const reportRef = useRef(report);
  reportRef.current = report;
  const autoSubmitRef = useRef(onAutoSubmit);
  autoSubmitRef.current = onAutoSubmit;

  useEffect(() => {
    if (!active) return;
    const counter = new StrikeCounter(loadStrikes(attemptId));
    const changed = () => {
      saveStrikes(attemptId, counter.state);
      setStrikes({ ...counter.state });
    };

    const stopFocus = watchFocus(signal => {
      const o = counter.leave(signal);
      if (!o.event) return;
      changed();
      reportRef.current({ kind: 'event', type: o.event });
      if (o.autoSubmit) autoSubmitRef.current(o.autoSubmit);
    }, () => counter.back());

    const onFullscreen = () => setFullscreen(document.fullscreenElement !== null);
    document.addEventListener('fullscreenchange', onFullscreen);
    onFullscreen();

    let checking = false;
    const faceTimer = setInterval(async () => {
      const v = video.current;
      if (!v || checking) return;
      checking = true;
      try {
        const before = counter.state.faceRun;
        const o = counter.face(classify(await countFaces(v)));
        if (counter.state.faceRun !== before) changed();
        if (o.event) {
          const frame = await captureJpeg(v);
          reportRef.current({ kind: 'event', type: o.event, evidence: frame ? { kind: 'Webcam', blob: frame } : undefined });
          if (o.autoSubmit) autoSubmitRef.current(o.autoSubmit);
        }
      } catch (e) {
        console.warn('SecuCode: the face check could not run', e); // a broken detector isn't the student's fault: no strike
      } finally {
        checking = false;
      }
    }, FACE_CHECK_MS);

    const stream = currentMedia();
    let recorder: AudioRecorder | null = null;
    const noAudio = (detail: string) => reportRef.current({ kind: 'event', type: 'AudioUnavailable', detail });
    if (stream && hasAudio(stream)) {
      try {
        recorder = new AudioRecorder(stream, blob => reportRef.current({ kind: 'evidence', evidenceKind: 'Audio', blob }));
        recorder.start();
      } catch (e) {
        recorder = null;
        noAudio(`Recording could not start: ${e instanceof Error ? e.message : String(e)}`.slice(0, 500));
      }
    } else noAudio('No microphone, or microphone access was blocked.');

    return () => {
      stopFocus();
      document.removeEventListener('fullscreenchange', onFullscreen);
      clearInterval(faceTimer);
      // ponytail: the last partial audio chunk arrives after the submit is queued and is dropped once the
      // queue closes; queue it before the submit if staff ever need the final seconds.
      recorder?.stop();
    };
  }, [active, attemptId, video]);

  return { fullscreen, strikes };
}
