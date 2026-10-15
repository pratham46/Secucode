// Copies MediaPipe's WASM runtime into public/ so the app serves it itself (no CDN). Runs after npm install.
import { cpSync, existsSync, mkdirSync } from 'node:fs';

const from = 'node_modules/@mediapipe/tasks-vision/wasm';
if (existsSync(from)) {
  mkdirSync('public/mediapipe', { recursive: true });
  cpSync(from, 'public/mediapipe/wasm', { recursive: true });
} else {
  console.warn('copy-mediapipe: @mediapipe/tasks-vision is not installed yet.');
}
