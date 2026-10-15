import { FaceDetector, FilesetResolver } from '@mediapipe/tasks-vision';
import type { FaceResult } from './strikes';

declare global {
  interface Window {
    // e2e only (DEV builds): the fake camera shows no real face, so tests set the face count here.
    __secucodeFakeFaces?: number;
  }
}

export const classify = (faces: number): FaceResult => (faces === 0 ? 'missing' : faces === 1 ? 'ok' : 'multiple');

let detector: Promise<FaceDetector> | null = null;

// Model and WASM are served by this app (public/mediapipe), never from a CDN. Runs entirely in the browser.
function load(): Promise<FaceDetector> {
  return (detector ??= FilesetResolver.forVisionTasks('/mediapipe/wasm')
    .then(files => FaceDetector.createFromOptions(files, {
      baseOptions: { modelAssetPath: '/mediapipe/blaze_face_short_range.tflite' },
      runningMode: 'VIDEO',
      minDetectionConfidence: 0.5, // ponytail: MediaPipe's default; tune if real rooms give false "no face" strikes
    }))
    .catch((e: unknown) => {
      detector = null; // let the next call try again
      throw e;
    }));
}

export async function countFaces(video: HTMLVideoElement): Promise<number> {
  if (import.meta.env.DEV && typeof window.__secucodeFakeFaces === 'number') return window.__secucodeFakeFaces;
  const track = (video.srcObject as MediaStream | null)?.getVideoTracks()[0];
  if (!track || track.readyState !== 'live' || video.readyState < 2) return 0; // camera unplugged or not started: no face
  return (await load()).detectForVideo(video, performance.now()).detections.length;
}

export function captureJpeg(video: HTMLVideoElement): Promise<Blob | null> {
  if (video.videoWidth === 0) return Promise.resolve(null);
  const canvas = document.createElement('canvas');
  canvas.width = video.videoWidth;
  canvas.height = video.videoHeight;
  canvas.getContext('2d')?.drawImage(video, 0, 0);
  return new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.7));
}
