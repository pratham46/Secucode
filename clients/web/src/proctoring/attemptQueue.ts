import { ApiError, type ApiClient } from '../api/client';
import type { AttemptDto, AttemptEventType, EvidenceDto, EvidenceKind } from '../api/types';

export type QueueItem =
  | { kind: 'code'; code: string; language: string }
  // An event may carry a webcam frame: the frame is uploaded first and its id put on the event.
  | { kind: 'event'; type: AttemptEventType; detail?: string; evidence?: { kind: EvidenceKind; blob: Blob }; evidenceId?: number | null }
  | { kind: 'evidence'; evidenceKind: EvidenceKind; blob: Blob }
  | { kind: 'submit'; code: string; language: string; reason: string };

export interface AttemptSender {
  saveCode(code: string, language: string): Promise<void>;
  uploadEvidence(kind: EvidenceKind, blob: Blob): Promise<number>;
  postEvent(type: AttemptEventType, detail: string | undefined, evidenceId: number | undefined): Promise<void>;
  submit(code: string, language: string, reason: string): Promise<AttemptDto>;
}

// The server refused this item for good (bad, forbidden, too big, wrong type): drop it and carry on.
const REJECTED = new Set([400, 403, 413, 415]);

// One attempt's writes, sent one at a time in order. Network failures back off (1 s doubling to 30 s) and
// retry; a 409 means the attempt is closed. Lives in memory only: closing the tab loses unsent events and
// evidence, while the code survives through the server's auto-close and the local copy.
export class AttemptQueue {
  private items: QueueItem[] = [];
  private sending: QueueItem | null = null;
  private running = false;
  private closed = false;
  private offline = false;
  private wake: (() => void) | null = null;

  onOfflineChange: (offline: boolean) => void = () => {};
  onClosed: () => void = () => {};
  onSubmitted: (attempt: AttemptDto) => void = () => {};

  constructor(private readonly sender: AttemptSender, private readonly maxBackoffMs = 30_000) {}

  get pending(): readonly QueueItem[] {
    return this.items;
  }

  get isClosed(): boolean {
    return this.closed;
  }

  enqueue(item: QueueItem): void {
    if (this.closed) return;
    if (item.kind === 'code') this.items = this.items.filter(i => i === this.sending || i.kind !== 'code');
    this.items.push(item);
    if (item.kind === 'submit') this.retryNow();
    void this.run();
  }

  // Ends a backoff wait early (submit pressed, browser back online, signed in again).
  retryNow(): void {
    this.wake?.();
  }

  close(): void {
    this.closed = true;
    this.items = [];
    this.wake?.();
  }

  private async run(): Promise<void> {
    if (this.running) return;
    this.running = true;
    let backoff = 1000;
    try {
      while (!this.closed && this.items.length > 0) {
        const item = this.items[0];
        this.sending = item;
        try {
          await this.send(item);
          this.remove(item);
          backoff = 1000;
          this.setOffline(false);
        } catch (e) {
          if (this.closed) return;
          const status = e instanceof ApiError ? e.status : 0;
          if (status === 409) {
            this.close();
            this.onClosed();
            return;
          }
          if (REJECTED.has(status)) {
            console.warn('SecuCode: the server rejected an exam item, so it was dropped.', item.kind, e);
            this.remove(item);
            continue;
          }
          this.setOffline(status === 0 || status >= 500);
          await this.sleep(backoff);
          backoff = Math.min(backoff * 2, this.maxBackoffMs);
        }
      }
    } finally {
      this.running = false;
      this.sending = null;
    }
  }

  private async send(item: QueueItem): Promise<void> {
    switch (item.kind) {
      case 'code':
        return this.sender.saveCode(item.code, item.language);
      case 'evidence':
        await this.sender.uploadEvidence(item.evidenceKind, item.blob);
        return;
      case 'event':
        if (item.evidence && item.evidenceId === undefined) {
          try {
            item.evidenceId = await this.sender.uploadEvidence(item.evidence.kind, item.evidence.blob);
          } catch (e) {
            if (!(e instanceof ApiError && REJECTED.has(e.status))) throw e;
            item.evidenceId = null; // the frame was refused; the event still matters
          }
        }
        return this.sender.postEvent(item.type, item.detail, item.evidenceId ?? undefined);
      case 'submit': {
        const attempt = await this.sender.submit(item.code, item.language, item.reason);
        this.closed = true; // nothing may follow a submit
        this.items = [];
        this.onSubmitted(attempt);
        return;
      }
    }
  }

  private remove(item: QueueItem): void {
    this.items = this.items.filter(i => i !== item);
  }

  private setOffline(offline: boolean): void {
    if (this.offline === offline) return;
    this.offline = offline;
    this.onOfflineChange(offline);
  }

  private sleep(ms: number): Promise<void> {
    return new Promise(resolve => {
      const timer = setTimeout(() => { this.wake = null; resolve(); }, ms);
      this.wake = () => { clearTimeout(timer); this.wake = null; resolve(); };
    });
  }
}

export function uploadEvidence(api: ApiClient, attemptId: number, kind: EvidenceKind, blob: Blob): Promise<EvidenceDto> {
  const form = new FormData();
  form.append('kind', kind);
  form.append('file', blob, kind === 'Audio' ? 'audio.webm' : 'frame.jpg');
  return api.post<EvidenceDto>(`/api/attempts/${attemptId}/evidence`, form);
}

export function apiSender(api: ApiClient, attemptId: number): AttemptSender {
  const base = `/api/attempts/${attemptId}`;
  return {
    saveCode: (code, language) => api.put<void>(`${base}/code`, { code, language }),
    uploadEvidence: async (kind, blob) => (await uploadEvidence(api, attemptId, kind, blob)).id,
    postEvent: (type, detail, evidenceId) => api.post<void>(`${base}/events`, { type, detail: detail ?? null, evidenceId: evidenceId ?? null }),
    submit: (code, language, reason) => api.post<AttemptDto>(`${base}/submit`, { code, language, reason }),
  };
}
