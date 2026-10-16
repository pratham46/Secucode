import type { AttemptEventType, AttemptStatus, ExamMode } from '../api/types';

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });

export const formatDateTime = (iso: string) => dateTime.format(new Date(iso));
export const formatWindow = (startIso: string, endIso: string) => `${formatDateTime(startIso)} – ${formatDateTime(endIso)}`;

export const statusLabel = (s: AttemptStatus) =>
  ({ InProgress: 'In progress', Submitted: 'Submitted', AutoSubmitted: 'Auto-submitted', Voided: 'Voided' })[s];

export const modeLabel = (m: ExamMode) => (m === 'WebAllowed' ? 'Web allowed' : 'Desktop only');

export const eventLabel = (t: AttemptEventType) =>
  ({
    FaceMissing: 'No face', MultipleFaces: 'More than one face', FaceMismatch: 'Face mismatch',
    FocusLost: 'Left the window', Minimized: 'Minimized', TabHidden: 'Switched tab',
    FullscreenExited: 'Left fullscreen', AudioUnavailable: 'No microphone',
  })[t];
