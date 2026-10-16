import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client';
import type { AttemptDto } from '../api/types';
import { AttemptQueue, type AttemptSender } from './attemptQueue';

const submitted = { id: 1, status: 'Submitted' } as AttemptDto;
const blob = new Blob(['x']);

function fakeSender() {
  const log: string[] = [];
  const failures: Record<string, ApiError[]> = {};
  let nextId = 100;
  const maybeFail = (op: string) => {
    const e = failures[op]?.shift();
    if (e) throw e;
  };
  const sender: AttemptSender = {
    saveCode: async code => { maybeFail('code'); log.push(`code:${code}`); },
    uploadEvidence: async kind => { maybeFail('evidence'); const id = nextId++; log.push(`evidence:${kind}:${id}`); return id; },
    postEvent: async (type, _detail, evidenceId) => { maybeFail('event'); log.push(`event:${type}:${evidenceId ?? '-'}`); },
    submit: async (code, _language, reason) => { maybeFail('submit'); log.push(`submit:${code}:${reason}`); return submitted; },
  };
  const fail = (op: string, ...errors: ApiError[]) => { (failures[op] ??= []).push(...errors); };
  return { sender, log, fail };
}

const offline = () => ApiError.network();
const flush = () => vi.advanceTimersByTimeAsync(0);
const code = (c: string) => ({ kind: 'code', code: c, language: 'Python' }) as const;
const submit = (c = 'a') => ({ kind: 'submit', code: c, language: 'Python', reason: 'Done.' }) as const;

beforeEach(() => { vi.useFakeTimers(); });
afterEach(() => { vi.useRealTimers(); });

describe('AttemptQueue', () => {
  it('sends in order and links an uploaded frame to its event', async () => {
    const { sender, log } = fakeSender();
    const q = new AttemptQueue(sender);
    q.enqueue(code('a'));
    q.enqueue({ kind: 'event', type: 'FaceMissing', evidence: { kind: 'Webcam', blob } });
    q.enqueue({ kind: 'evidence', evidenceKind: 'Audio', blob });
    q.enqueue(submit());
    await flush();
    expect(log).toEqual(['code:a', 'evidence:Webcam:100', 'event:FaceMissing:100', 'evidence:Audio:101', 'submit:a:Done.']);
  });

  it('keeps only the latest unsent code save', async () => {
    const { sender, log } = fakeSender();
    const q = new AttemptQueue(sender);
    q.enqueue(code('a'));
    q.enqueue(code('b'));
    q.enqueue(code('c'));
    await flush();
    expect(log).toEqual(['code:a', 'code:c']);
  });

  it('retries with growing backoff while offline and reports it', async () => {
    const { sender, log, fail } = fakeSender();
    fail('code', offline(), offline());
    const q = new AttemptQueue(sender);
    const states: boolean[] = [];
    q.onOfflineChange = v => states.push(v);

    q.enqueue(code('x'));
    await flush();
    expect(states).toEqual([true]);
    await vi.advanceTimersByTimeAsync(1000); // second failure; next wait is 2 s
    await vi.advanceTimersByTimeAsync(1999);
    expect(log).toEqual([]);
    await vi.advanceTimersByTimeAsync(1);
    expect(log).toEqual(['code:x']);
    expect(states).toEqual([true, false]);
  });

  it('a submit cuts the backoff short', async () => {
    const { sender, log, fail } = fakeSender();
    fail('code', offline());
    const q = new AttemptQueue(sender);
    q.enqueue(code('x'));
    await flush();
    q.enqueue(submit('x'));
    await flush();
    expect(log).toEqual(['code:x', 'submit:x:Done.']);
  });

  it('a 409 closes the queue and drops everything, now and later', async () => {
    const { sender, log, fail } = fakeSender();
    fail('event', new ApiError(409, 'Conflict'));
    const q = new AttemptQueue(sender);
    const closed = vi.fn();
    q.onClosed = closed;

    q.enqueue({ kind: 'event', type: 'FocusLost' });
    q.enqueue(code('y'));
    await flush();
    q.enqueue(submit());
    await flush();

    expect(closed).toHaveBeenCalledOnce();
    expect(log).toEqual([]);
    expect(q.pending).toEqual([]);
    expect(q.isClosed).toBe(true);
  });

  it('drops an item the server rejects and carries on; a rejected frame still sends its event', async () => {
    const { sender, log, fail } = fakeSender();
    fail('code', new ApiError(400, 'Bad'));
    fail('evidence', new ApiError(413, 'Too big'));
    const q = new AttemptQueue(sender);
    q.enqueue(code('bad'));
    q.enqueue({ kind: 'event', type: 'FaceMissing', evidence: { kind: 'Webcam', blob } });
    q.enqueue(code('good'));
    await flush();
    expect(log).toEqual(['event:FaceMissing:-', 'code:good']);
  });

  it('does not upload a frame twice when its event has to be retried', async () => {
    const { sender, log, fail } = fakeSender();
    fail('event', offline());
    const q = new AttemptQueue(sender);
    q.enqueue({ kind: 'event', type: 'MultipleFaces', evidence: { kind: 'Webcam', blob } });
    await flush();
    await vi.advanceTimersByTimeAsync(1000);
    expect(log).toEqual(['evidence:Webcam:100', 'event:MultipleFaces:100']);
  });

  it('reports the submitted attempt and sends nothing afterwards', async () => {
    const { sender, log } = fakeSender();
    const q = new AttemptQueue(sender);
    const done = vi.fn();
    q.onSubmitted = done;
    q.enqueue(submit());
    await flush();
    q.enqueue({ kind: 'evidence', evidenceKind: 'Audio', blob });
    q.enqueue(submit('again'));
    await flush();
    expect(done).toHaveBeenCalledOnce();
    expect(done).toHaveBeenCalledWith(submitted);
    expect(log).toEqual(['submit:a:Done.']);
  });

  it('close() during a backoff stops the queue', async () => {
    const { sender, log, fail } = fakeSender();
    fail('code', offline());
    const q = new AttemptQueue(sender);
    q.enqueue(code('x'));
    await flush();
    q.close();
    await vi.advanceTimersByTimeAsync(5000);
    expect(log).toEqual([]);
  });

  it('a submit resets a grown backoff, so its own retries start at 1 s', async () => {
    const { sender, log, fail } = fakeSender();
    fail('code', ...Array.from({ length: 7 }, offline)); // waits 1,2,4,8,16 s, then 30 s
    const q = new AttemptQueue(sender);
    q.enqueue(code('x'));
    await flush();
    await vi.advanceTimersByTimeAsync(31_000); // 6 failures so far, now waiting 30 s
    fail('submit', offline(), offline());
    q.enqueue(submit('x'));
    await flush(); // woken: the 7th failure; the wait is 1 s, not 30 s
    expect(log).toEqual([]);
    await vi.advanceTimersByTimeAsync(1000); // code saved, submit fails once
    expect(log).toEqual(['code:x']);
    await vi.advanceTimersByTimeAsync(1000); // second submit failure, next wait 2 s
    await vi.advanceTimersByTimeAsync(1999);
    expect(log).toEqual(['code:x']);
    await vi.advanceTimersByTimeAsync(1);
    expect(log).toEqual(['code:x', 'submit:x:Done.']);
  });

  it('a rejected submit closes the queue and is reported', async () => {
    const { sender, log, fail } = fakeSender();
    const err = new ApiError(400, 'Too large');
    fail('submit', err);
    const q = new AttemptQueue(sender);
    const rejected = vi.fn();
    q.onSubmitRejected = rejected;
    q.enqueue(submit());
    await flush();
    q.enqueue(code('late'));
    await flush();
    expect(rejected).toHaveBeenCalledOnce();
    expect(rejected).toHaveBeenCalledWith(err);
    expect(q.isClosed).toBe(true);
    expect(log).toEqual([]);
  });
});
