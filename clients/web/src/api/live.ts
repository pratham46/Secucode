import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import type { ApiClient } from './client';
import type { LiveAttemptMessage } from './types';

export type LiveHandlers = Partial<Record<'DeadlineChanged' | 'AttemptVoided' | 'AttemptReopened', (m: LiveAttemptMessage) => void>>;

// The server puts each user in their own group on connect, so a student hears about their attempt without joining
// anything. Pushes sent while disconnected are lost: onConnected runs after every connect and reconnect so the
// caller re-reads the attempt (the backend's live contract).
export function connectLive(api: ApiClient, handlers: LiveHandlers, onConnected: () => void): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl('/hubs/exams', { accessTokenFactory: () => api.freshAccessToken() })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: c => Math.min(30_000, 1000 * 2 ** c.previousRetryCount) })
    .configureLogging(LogLevel.Warning)
    .build();
  for (const [name, handler] of Object.entries(handlers)) connection.on(name, handler);
  connection.onreconnected(() => onConnected());

  let stopped = false;
  // Automatic reconnect covers drops after the first connect; this loop covers the first connect itself.
  void (async () => {
    while (!stopped) {
      try {
        await connection.start();
        onConnected();
        return;
      } catch {
        await new Promise(r => setTimeout(r, 5000));
      }
    }
  })();

  return () => {
    stopped = true;
    void connection.stop();
  };
}
