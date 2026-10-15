import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// Dev: the API runs separately (default :8080); /api and /hubs are proxied so the app stays same-origin.
const api = process.env.SECUCODE_API ?? 'http://localhost:8080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api': api, '/hubs': { target: api, ws: true } },
  },
  build: { outDir: '../../server/Secucode.Api/wwwroot', emptyOutDir: true },
  test: { environment: 'jsdom', setupFiles: ['src/test/setup.ts'], include: ['src/**/*.test.{ts,tsx}'] },
});
