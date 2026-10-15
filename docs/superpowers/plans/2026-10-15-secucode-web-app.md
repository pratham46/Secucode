# SecuCode Web App (Sub-project 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `clients/web`, a React app that gives staff the desktop app's exam pages in a browser and lets students take `WebAllowed` exams with fullscreen and focus strikes, webcam face detection and webcam and microphone evidence.

**Architecture:** Vite + React 18 + TypeScript (strict), a hand-built CSS design system copied from the desktop tokens, a small fetch-based `ApiClient` with one shared token refresh, TanStack Query for page data and SignalR for live attempt changes. Exam logic that can be pure (strike counting, the ordered attempt queue, timer maths, local code copy) lives in small tested modules; browser-only pieces (MediaPipe face detection, MediaRecorder audio, focus/fullscreen events) are thin wrappers. The API serves the built app from `wwwroot` on the same origin.

**Tech Stack:** React 18.3, react-router-dom 6 (data router), @tanstack/react-query 5, @microsoft/signalr 8, CodeMirror 6, @mediapipe/tasks-vision 0.10, Vite 5, Vitest 2 + Testing Library, Playwright 1.49. Server side: ASP.NET Core 8 (existing).

**Spec:** `docs/superpowers/specs/2026-10-15-secucode-web-app-design.md`

## Global Constraints

- Branch `feature/web-app` (from `feature/desktop-client`). Windows dev machine; shell examples are PowerShell. Docker Desktop must be running for API tests and e2e tests.
- Node 22+ (the machine has 24). `clients/web/package-lock.json` is committed. Package versions are pinned exactly as written in Task 2; if npm cannot resolve an exact version, use the nearest existing patch of the same minor and say so in your report.
- Packages allowed — runtime: `react`, `react-dom`, `react-router-dom`, `@tanstack/react-query`, `@microsoft/signalr`, `codemirror`, `@codemirror/state`, `@codemirror/view`, `@codemirror/lang-python`, `@codemirror/lang-javascript`, `@codemirror/lang-java`, `@codemirror/lang-cpp`, `@mediapipe/tasks-vision`. Dev: `vite`, `@vitejs/plugin-react`, `typescript`, `@types/react`, `@types/react-dom`, `vitest`, `jsdom`, `@testing-library/react`, `@testing-library/dom`, `@playwright/test`. Nothing else.
- TypeScript `strict`. No `any` outside test fakes and the DEV-only `window.__secucodeFakeFaces` hook.
- All API calls use same-origin relative paths (`/api/...`, `/hubs/exams`). Sign-in sends `client: "Web"`.
- Access token in memory only. Refresh token in `sessionStorage` key `secucode.refresh`. Other storage keys: `localStorage` `secucode.theme` (`light` | `dark`; absent = System) and `secucode.code.{attemptId}`; `sessionStorage` `secucode.strikes.{attemptId}`.
- Colours are the desktop's exact values (Task 2 lists them). Every animation and transition is disabled under `prefers-reduced-motion: reduce`.
- Timestamps travel as UTC ISO strings and are shown in local time.
- Exam rules (same as desktop): face check every 3 s; 3 face failures in a row auto-submit; 3 trips away from the exam auto-submit; auto-save every 30 s; audio chunks of 60 s; queue backoff 1 s doubling to 30 s; submit accepted until `deadlineAt + 30 s`; Start offered from 5 minutes before the window opens.
- Languages and their exact API names: `Python`, `JavaScript`, `Java`, `C++`.
- Never commit `server/Secucode.Api/wwwroot/`, `clients/web/node_modules/`, `clients/web/public/mediapipe/wasm/`, `clients/web/test-results/`, `clients/web/playwright-report/`, or `clients/Secucode_installer/Debug/`. Stage files by path only (never `git add -A` / `git add .`).
- Every commit message ends with:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB
  ```
- Commands run from the repo root `E:\Secucode` unless a step says `clients/web`.

## Review Focus

1. **Reloading or crashing the tab mid-exam** — the student must get their code back (local copy), keep their strike count, and re-enter through Resume with the camera reopened. Pinned by `pickCode` and strike persistence tests (Task 10) and the reload e2e test (Task 14).
2. **The network dropping during the exam or at submit** — saves, events and the submit must be sent in order when the connection returns, with the offline banner meanwhile. Pinned by the queue tests (Task 10) and the offline e2e test (Task 14).
3. **Ordinary in-page actions must not count as leaving** — changing the language dropdown, pressing Run and opening the submit dialog must leave the strike count at 0. Pinned by the strike-count check in the student e2e test (Task 14).
4. **Two auto-submit triggers at once, or anything queued after the submit** — exactly one submit is sent and nothing is sent after it succeeds. Pinned by the one-shot `StrikeCounter` test and the queue's "nothing after submit" test (Task 10).
5. **Camera refused, missing or busy; microphone missing** — a clear message naming what to do, the exam refuses to start without a camera, and goes ahead without a microphone. Pinned by the `cameraProblem` test (Task 11) and the start page's checks (Task 12).

---

## File Map

```
server/Secucode.Api/WebApp.cs                         Task 1  serve wwwroot, rewrite app routes to index.html
server/Secucode.Api/ServiceSetup.cs                   Task 1  StaticFileOptions (CSP, no-cache index), UseWebApp
server/Secucode.Api/Features/Attempts/AttemptEndpoints.cs  Task 1  audio/webm evidence
server/Secucode.Api.Tests/WebHostingTests.cs          Task 1
server/Secucode.Api.Tests/AttemptTests.cs             Task 1  webm test
server/Secucode.Api/Dockerfile, .dockerignore         Task 14
clients/web/
  package.json, package-lock.json, tsconfig.json, vite.config.ts, index.html     Task 2
  public/theme-init.js                                Task 2
  src/main.tsx, src/test/setup.ts                     Task 2
  src/theme/tokens.css, theme.ts (+test)              Task 2
  src/api/types.ts, client.ts (+test)                 Task 3
  src/auth/session.ts (+test), AuthContext.tsx        Task 4
  src/theme/controls.css, src/ui/index.tsx, Dialog.tsx (+test)   Task 5
  src/App.tsx, src/shell/*, src/features/login/LoginPage.tsx, settings/SettingsPage.tsx, exams/ExamsHome.tsx, lib/format.ts   Task 6
  src/features/staff/examForm.ts (+test), ExamListPage.tsx, ExamEditorPage.tsx   Task 7
  src/ui/CodeEditor.tsx (+test), features/staff/AttemptListPage.tsx, AttemptDetailPage.tsx, EvidenceView.tsx   Task 8
  src/features/student/studentExams.ts (+test), DashboardPage.tsx, src/proctoring/media.ts (first two functions)   Task 9
  src/proctoring/strikes.ts, attemptQueue.ts, timer.ts, localCopy.ts (+tests)   Task 10
  src/proctoring/media.ts, faceMonitor.ts, audioRecorder.ts, focusGuard.ts (+tests), scripts/copy-mediapipe.mjs, public/mediapipe/blaze_face_short_range.tflite   Task 11
  src/features/student/ExamStartPage.tsx              Task 12
  src/api/live.ts, src/features/student/ExamPage.tsx, useProctoring.ts, exam.css, src/features/login/SignInDialog.tsx   Task 13
  playwright.config.ts, e2e/*                         Task 14
README.md                                             Task 14
```

---

### Task 1: API serves the web app; WebM audio evidence

**Files:**
- Create: `server/Secucode.Api/WebApp.cs`
- Modify: `server/Secucode.Api/ServiceSetup.cs` (`AddSecucodeApi`, `UseSecucodeApi`)
- Modify: `server/Secucode.Api/Features/Attempts/AttemptEndpoints.cs:24-27` and the validation message at line 156
- Create: `server/Secucode.Api.Tests/WebHostingTests.cs`
- Modify: `server/Secucode.Api.Tests/AttemptTests.cs` (one new test)
- Modify: `.gitignore`

**Interfaces:**
- Produces: `GET /` and any extension-less GET outside `/api`, `/hubs`, `/swagger` returns `wwwroot/index.html` (when it exists) with header `Content-Security-Policy`. Files under `wwwroot` are served as-is with the same header. `POST /api/attempts/{id}/evidence` accepts `audio/webm`.

- [ ] **Step 1: Write the failing tests**

Create `server/Secucode.Api.Tests/WebHostingTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class WebHostingTests(ApiFixture api) : IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("secucode_web_").FullName;
    private WebApplicationFactory<Program>? _factory;

    private HttpClient Client()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>SecuCode app</title>");
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app.js"), "console.log('app');");
        _factory = api.Factory.WithWebHostBuilder(b => b.UseWebRoot(_webRoot));
        return _factory.CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/exams")]
    [InlineData("/attempt/5")]
    public async Task App_routes_get_index_html_with_a_content_security_policy(string path)
    {
        var res = await Client().GetAsync(path);

        await res.ExpectAsync(HttpStatusCode.OK);
        Assert.Contains("<title>SecuCode app", await res.Content.ReadAsStringAsync());
        Assert.Contains("default-src 'self'", Assert.Single(res.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("no-cache", res.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Static_files_are_served_with_the_policy()
    {
        var res = await Client().GetAsync("/assets/app.js");

        await res.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("console.log('app');", await res.Content.ReadAsStringAsync());
        Assert.True(res.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Api_hub_and_missing_file_paths_are_never_the_app()
    {
        var c = Client();

        await (await c.GetAsync("/api/nope")).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        var health = await c.GetAsync("/api/health");
        await health.ExpectAsync(HttpStatusCode.OK);
        Assert.False(health.Headers.Contains("Content-Security-Policy"));
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/hubs/nope")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/assets/missing.js")).StatusCode);
    }

    public void Dispose()
    {
        _factory?.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }
}
```

Add to `server/Secucode.Api.Tests/AttemptTests.cs`, after `Evidence_is_validated_stored_and_served_only_to_the_right_people`:

```csharp
    [Fact]
    public async Task Browser_audio_in_webm_is_accepted_as_evidence()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student, ClientKind.Web);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);
        var webm = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3 };
        var file = new ByteArrayContent(webm);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        var form = new MultipartFormDataContent { { new StringContent("Audio"), "kind" }, { file, "file", "chunk.webm" } };

        var evidence = await (await student.PostAsync($"/api/attempts/{attempt.Id}/evidence", form)).ReadAsync<EvidenceDto>(HttpStatusCode.Created);

        Assert.Equal("audio/webm", evidence.ContentType);
        var author = await _world.LoginAsync(x.Author);
        Assert.Equal(webm, await (await author.GetAsync($"/api/evidence/{evidence.Id}")).Content.ReadAsByteArrayAsync());
    }
```

(`AttemptTests.cs` already imports `System.Net.Http.Headers` for its `Upload` helper; if not, add `using System.Net.Http.Headers;`.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test server/Secucode.Api.Tests --filter "FullyQualifiedName~WebHostingTests|FullyQualifiedName~Browser_audio_in_webm"`
Expected: FAIL — `/` and `/exams` return 401 (no static hosting yet); the webm upload returns 400.

- [ ] **Step 3: Implement**

Create `server/Secucode.Api/WebApp.cs`:

```csharp
namespace Secucode.Api;

// Serves the React app (clients/web, built into wwwroot) from the API's own origin.
// App routes such as /exams or /attempt/5 get index.html so the browser router takes over;
// /api, /hubs and /swagger are never rewritten, so unknown API paths still fail as API errors.
public static class WebApp
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; worker-src 'self' blob:; frame-ancestors 'none'";

    public static WebApplication UseWebApp(this WebApplication app)
    {
        app.Use((ctx, next) =>
        {
            var path = ctx.Request.Path;
            if ((HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
                && !path.StartsWithSegments("/api") && !path.StartsWithSegments("/hubs") && !path.StartsWithSegments("/swagger")
                && !Path.HasExtension(path.Value)
                && app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
                ctx.Request.Path = "/index.html";
            return next(ctx);
        });
        app.UseStaticFiles(); // options (CSP header) come from AddSecucodeApi
        return app;
    }
}
```

In `server/Secucode.Api/ServiceSetup.cs`, inside `AddSecucodeApi` right after `services.AddProblemDetails();`, add:

```csharp
        services.Configure<StaticFileOptions>(o => o.OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.ContentSecurityPolicy = WebApp.ContentSecurityPolicy;
            if (ctx.File.Name == "index.html")
                ctx.Context.Response.Headers.CacheControl = "no-cache"; // hashed assets can cache; the page that names them can't
        });
```

and in `UseSecucodeApi`, change the start to:

```csharp
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseApiErrors();
        app.UseWebApp();
        app.UseCors();
```

(`StaticFileOptions` is in `Microsoft.AspNetCore.Builder`, already imported by the Web SDK's implicit usings.)

In `server/Secucode.Api/Features/Attempts/AttemptEndpoints.cs`, change the evidence types and the message:

```csharp
    private static readonly Dictionary<string, string> EvidenceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["audio/wav"] = ".wav", ["audio/x-wav"] = ".wav",
        ["audio/webm"] = ".webm", // browsers record Opus in WebM (web exams)
    };
```

```csharp
            .Rule(extension is not null, "file", "Only JPEG, PNG, WAV and WebM files are accepted.")
```

Append to `.gitignore`:

```
# Web app build output (served by the API) and web tooling
server/Secucode.Api/wwwroot/
clients/web/node_modules/
clients/web/public/mediapipe/wasm/
clients/web/test-results/
clients/web/playwright-report/
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test server/Secucode.Api.Tests --filter "FullyQualifiedName~WebHostingTests|FullyQualifiedName~Browser_audio_in_webm"`
Expected: PASS (5 tests). If `UseWebRoot` is not picked up by the factory (the `/` test still gets 401), use `b.UseSetting(WebHostDefaults.WebRootKey, _webRoot)` instead — same key — and say so in your report.

Then the fast suite: `dotnet test server/Secucode.Api.Tests --filter "Category!=Docker"`
Expected: all pass (the existing `Unknown_routes_are_a_401_problem_when_anonymous` must still pass).

- [ ] **Step 5: Commit**

```powershell
git add server/Secucode.Api/WebApp.cs server/Secucode.Api/ServiceSetup.cs server/Secucode.Api/Features/Attempts/AttemptEndpoints.cs server/Secucode.Api.Tests/WebHostingTests.cs server/Secucode.Api.Tests/AttemptTests.cs .gitignore
git commit -m "feat(api): serve the web app from wwwroot with a CSP; accept WebM audio evidence" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 2: Web project scaffold and theme tokens

**Files:**
- Create: `clients/web/package.json`, `clients/web/tsconfig.json`, `clients/web/vite.config.ts`, `clients/web/index.html`, `clients/web/public/theme-init.js`
- Create: `clients/web/src/main.tsx`, `clients/web/src/test/setup.ts`, `clients/web/src/vite-env.d.ts`
- Create: `clients/web/src/theme/tokens.css`, `clients/web/src/theme/theme.ts`, `clients/web/src/theme/theme.test.ts`

**Interfaces:**
- Produces: `type ThemeChoice = 'light' | 'dark' | 'system'`; `loadTheme(): ThemeChoice`; `applyTheme(choice: ThemeChoice, root?: HTMLElement): void`. CSS variables listed in `tokens.css` (later tasks use only these names). Scripts: `npm run dev`, `npm run build` (type-check + build into `server/Secucode.Api/wwwroot`), `npm test`.

- [ ] **Step 1: Create the project files**

`clients/web/package.json`:

```json
{
  "name": "secucode-web",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc --noEmit && vite build",
    "test": "vitest run",
    "e2e": "playwright test"
  },
  "dependencies": {
    "@codemirror/lang-cpp": "6.0.2",
    "@codemirror/lang-java": "6.0.1",
    "@codemirror/lang-javascript": "6.2.2",
    "@codemirror/lang-python": "6.1.6",
    "@codemirror/state": "6.4.1",
    "@codemirror/view": "6.35.0",
    "@mediapipe/tasks-vision": "0.10.18",
    "@microsoft/signalr": "8.0.7",
    "@tanstack/react-query": "5.62.0",
    "codemirror": "6.0.1",
    "react": "18.3.1",
    "react-dom": "18.3.1",
    "react-router-dom": "6.28.0"
  },
  "devDependencies": {
    "@playwright/test": "1.49.1",
    "@testing-library/dom": "10.4.0",
    "@testing-library/react": "16.1.0",
    "@types/react": "18.3.12",
    "@types/react-dom": "18.3.1",
    "@vitejs/plugin-react": "4.3.4",
    "jsdom": "25.0.1",
    "typescript": "5.6.3",
    "vite": "5.4.11",
    "vitest": "2.1.8"
  }
}
```

`clients/web/tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "moduleResolution": "bundler",
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "isolatedModules": true,
    "skipLibCheck": true,
    "noEmit": true,
    "types": ["vite/client"]
  },
  "include": ["src"]
}
```

`clients/web/vite.config.ts`:

```ts
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
```

`clients/web/index.html`:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>SecuCode</title>
    <script src="/theme-init.js"></script>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

`clients/web/public/theme-init.js`:

```js
// Runs before first paint so a saved theme never flashes. External (not inline) because of the CSP.
try {
  var t = localStorage.getItem('secucode.theme');
  if (t === 'light' || t === 'dark') document.documentElement.dataset.theme = t;
} catch (e) { /* storage blocked: follow the system */ }
```

`clients/web/src/vite-env.d.ts`:

```ts
/// <reference types="vite/client" />
```

`clients/web/src/test/setup.ts`:

```ts
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

afterEach(() => {
  cleanup();
  localStorage.clear();
  sessionStorage.clear();
});
```

`clients/web/src/main.tsx` (Task 6 replaces the placeholder element with `<App />`):

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './theme/tokens.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <p style={{ padding: 24 }}>SecuCode</p>
  </StrictMode>,
);
```

- [ ] **Step 2: Install**

Run (in `clients/web`): `npm install`
Expected: `node_modules` and `package-lock.json` created, no errors.

- [ ] **Step 3: Write the failing theme test**

`clients/web/src/theme/theme.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { applyTheme, loadTheme } from './theme';

describe('theme', () => {
  it('defaults to system and follows it by removing data-theme', () => {
    const root = document.createElement('html');
    root.dataset.theme = 'dark';
    expect(loadTheme()).toBe('system');
    applyTheme('system', root);
    expect(root.dataset.theme).toBeUndefined();
    expect(localStorage.getItem('secucode.theme')).toBeNull();
  });

  it('applies and remembers an explicit choice', () => {
    const root = document.createElement('html');
    applyTheme('dark', root);
    expect(root.dataset.theme).toBe('dark');
    expect(loadTheme()).toBe('dark');
  });

  it('ignores a corrupt saved value', () => {
    localStorage.setItem('secucode.theme', 'purple');
    expect(loadTheme()).toBe('system');
  });
});
```

Run (in `clients/web`): `npm test`
Expected: FAIL — `./theme` not found.

- [ ] **Step 4: Implement the theme**

`clients/web/src/theme/theme.ts`:

```ts
// Light / Dark / System. System is pure CSS (prefers-color-scheme), so it needs no listener here.
export type ThemeChoice = 'light' | 'dark' | 'system';

const KEY = 'secucode.theme';

export function loadTheme(): ThemeChoice {
  try {
    const v = localStorage.getItem(KEY);
    return v === 'light' || v === 'dark' ? v : 'system';
  } catch {
    return 'system';
  }
}

export function applyTheme(choice: ThemeChoice, root: HTMLElement = document.documentElement): void {
  if (choice === 'system') delete root.dataset.theme;
  else root.dataset.theme = choice;
  try {
    if (choice === 'system') localStorage.removeItem(KEY);
    else localStorage.setItem(KEY, choice);
  } catch {
    /* storage blocked: the choice lasts for this page only */
  }
}
```

`clients/web/src/theme/tokens.css` (values are the desktop's `Colors.Light.xaml` / `Colors.Dark.xaml`; WPF `#AARRGGBB` alpha colours converted to `rgb(... / a)`):

```css
/* Design tokens shared with the desktop app. Components use only these variables. */
:root {
  --surface: #F6F6F8;
  --surface-raised: #FFFFFF;
  --surface-sunken: #EEEEF2;
  --border: #E2E2E8;
  --border-strong: #C6C6D0;
  --text-primary: #1A1A1F;
  --text-secondary: #5D5D69;
  --text-disabled: #A2A2AC;
  --accent: #2B5FD9;
  --accent-hover: #2452C0;
  --accent-pressed: #1E45A3;
  --accent-soft: #E6EDFC;
  --on-accent: #FFFFFF;
  --danger: #C8322B;
  --danger-soft: #FBE9E8;
  --warning: #A65F0A;
  --warning-soft: #FDF1DE;
  --success: #1F8A4C;
  --success-soft: #E3F4EA;
  --focus: #2B5FD9;
  --subtle-hover: rgb(0 0 0 / 0.06);
  --subtle-pressed: rgb(0 0 0 / 0.11);

  --font: "Segoe UI Variable", "Segoe UI", system-ui, sans-serif;
  --font-code: "Cascadia Mono", Consolas, ui-monospace, monospace;
  --fs-caption: 12px;
  --fs-body: 14px;
  --fs-subtitle: 20px;
  --fs-title: 28px;
  --fs-display: 40px;

  --radius: 8px;
  --radius-card: 12px;

  --dur-fast: 100ms;
  --dur-pop: 150ms;
  --dur-page: 200ms;
  --ease-out: cubic-bezier(0.1, 0.9, 0.2, 1);

  color-scheme: light;
}

@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --surface: #1B1B1F;
    --surface-raised: #25252B;
    --surface-sunken: #151518;
    --border: #33333B;
    --border-strong: #4A4A55;
    --text-primary: #F1F1F4;
    --text-secondary: #A9A9B5;
    --text-disabled: #686872;
    --accent: #6E9BFF;
    --accent-hover: #86ACFF;
    --accent-pressed: #5A86E8;
    --accent-soft: #24304D;
    --on-accent: #0D1220;
    --danger: #FF7B72;
    --danger-soft: #3D2423;
    --warning: #F2B45A;
    --warning-soft: #3B2F1C;
    --success: #5CD08A;
    --success-soft: #1E3527;
    --focus: #86ACFF;
    --subtle-hover: rgb(255 255 255 / 0.08);
    --subtle-pressed: rgb(255 255 255 / 0.14);
    color-scheme: dark;
  }
}

:root[data-theme="dark"] {
  --surface: #1B1B1F;
  --surface-raised: #25252B;
  --surface-sunken: #151518;
  --border: #33333B;
  --border-strong: #4A4A55;
  --text-primary: #F1F1F4;
  --text-secondary: #A9A9B5;
  --text-disabled: #686872;
  --accent: #6E9BFF;
  --accent-hover: #86ACFF;
  --accent-pressed: #5A86E8;
  --accent-soft: #24304D;
  --on-accent: #0D1220;
  --danger: #FF7B72;
  --danger-soft: #3D2423;
  --warning: #F2B45A;
  --warning-soft: #3B2F1C;
  --success: #5CD08A;
  --success-soft: #1E3527;
  --focus: #86ACFF;
  --subtle-hover: rgb(255 255 255 / 0.08);
  --subtle-pressed: rgb(255 255 255 / 0.14);
  color-scheme: dark;
}

@media (prefers-reduced-motion: reduce) {
  :root { --dur-fast: 0ms; --dur-pop: 0ms; --dur-page: 0ms; }
  *, *::before, *::after { animation: none !important; transition: none !important; }
}

html, body {
  margin: 0;
  min-height: 100%;
  background: var(--surface);
  color: var(--text-primary);
  font: var(--fs-body) / 1.43 var(--font);
}
```

- [ ] **Step 5: Run the tests and the build**

Run (in `clients/web`): `npm test`
Expected: PASS (3 tests).

Run (in `clients/web`): `npm run build`
Expected: type-check passes and `server/Secucode.Api/wwwroot/index.html` exists. `git status` must NOT list anything under `server/Secucode.Api/wwwroot/`.

- [ ] **Step 6: Commit**

```powershell
git add clients/web/package.json clients/web/package-lock.json clients/web/tsconfig.json clients/web/vite.config.ts clients/web/index.html clients/web/public/theme-init.js clients/web/src/main.tsx clients/web/src/vite-env.d.ts clients/web/src/test/setup.ts clients/web/src/theme/tokens.css clients/web/src/theme/theme.ts clients/web/src/theme/theme.test.ts
git commit -m "feat(web): Vite + React + TS scaffold with the desktop theme tokens" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 3: API types and ApiClient

**Files:**
- Create: `clients/web/src/api/types.ts`
- Create: `clients/web/src/api/client.ts`
- Test: `clients/web/src/api/client.test.ts`

**Interfaces:**
- Produces (types): every type in `types.ts` below; `LANGUAGES`, `isLanguage(v)`.
- Produces (client):
  - `class ApiError extends Error { status: number; title: string; detail?: string; errors?: Record<string, string[]> }`, `ApiError.network()`, `ApiError.from(res: Response): Promise<ApiError>`.
  - `interface TokenStore { get(): string | null; set(token: string | null): void }`, `sessionTokenStore`.
  - `class ApiClient(fetchFn?: typeof fetch, refreshStore?: TokenStore)` with `onSessionExpired: () => void`, `isSignedIn: boolean`, `login(username, password): Promise<MeResponse>`, `restore(): Promise<MeResponse | null>`, `logout(): Promise<void>`, `get<T>(path)`, `post<T>(path, body?)` (body may be `FormData`), `put<T>(path, body)`, `patch<T>(path, body)`, `del<T>(path)`, `freshAccessToken(): Promise<string>`. Responses with a JSON content type are parsed; others come back as `Blob`; 204 returns `undefined`.

- [ ] **Step 1: Write the types**

`clients/web/src/api/types.ts` (mirrors `server/Secucode.Contracts`; JSON is camelCase, enums are strings, dates are UTC ISO strings):

```ts
// Mirrors server/Secucode.Contracts. Keep in step with the C# records; the e2e tests run against the real API.
export type ClientKind = 'Desktop' | 'Web';
export type ExamMode = 'DesktopOnly' | 'WebAllowed';
export type AttemptStatus = 'InProgress' | 'Submitted' | 'AutoSubmitted' | 'Voided';
export type Reach = 'InScope' | 'OwnOnly';
export type Capability =
  | 'ManageSettings' | 'ManageMasters' | 'ManageRoles' | 'AssignRoles' | 'ManageUsers'
  | 'CreateExams' | 'ManageExams'
  | 'ResetAttempts' | 'ReopenAttempts' | 'ExtendTime' | 'EditQuestionsAfterAttempts'
  | 'ViewResults' | 'ViewEvidence' | 'ViewAudit';
export type AttemptEventType =
  | 'FaceMissing' | 'MultipleFaces' | 'FaceMismatch' | 'FocusLost' | 'Minimized' | 'TabHidden' | 'FullscreenExited' | 'AudioUnavailable';
export type EvidenceKind = 'ReferenceFace' | 'Webcam' | 'Screenshot' | 'Audio';

export type Language = 'Python' | 'JavaScript' | 'Java' | 'C++';
export const LANGUAGES: readonly Language[] = ['Python', 'JavaScript', 'Java', 'C++'];
export const isLanguage = (v: unknown): v is Language => LANGUAGES.includes(v as Language);

export interface ScopeDto { branchId: number | null; classId: number | null; batchId: number | null }

export interface AssignmentDto {
  id: number; roleId: number; roleName: string;
  branchId: number | null; classId: number | null; batchId: number | null;
  validFrom: string | null; validTo: string | null; isActive: boolean; isStudent: boolean;
}

export interface UserDto {
  id: number; username: string; fullName: string; email: string | null;
  isActive: boolean; hasPin: boolean; assignments: AssignmentDto[];
}

export interface EffectiveCapabilityDto { capability: Capability; reach: Reach; scope: ScopeDto; roleName: string }
export interface MeResponse { user: UserDto; capabilities: EffectiveCapabilityDto[] }
export interface LoginResponse { accessToken: string; refreshToken: string; expiresAt: string; user: UserDto }

export interface MasterDto { id: number; name: string }

export interface QuestionDto { id: number; text: string; sortOrder: number }
export interface QuestionInput { id: number | null; text: string }

export interface ExamRequest {
  title: string; startsAt: string; endsAt: string; durationMinutes: number; mode: ExamMode; isActive: boolean;
  branchId: number; classId: number | null; batchId: number | null;
  allowQuestionEditAfterAttempts: boolean; allowAttemptReset: boolean; allowAttemptReopen: boolean; allowTimeExtension: boolean;
  questions: QuestionInput[];
}

export interface ExamDto {
  id: number; title: string; startsAt: string; endsAt: string; durationMinutes: number; mode: ExamMode; isActive: boolean;
  branchId: number; classId: number | null; batchId: number | null; createdBy: number; createdByName: string;
  allowQuestionEditAfterAttempts: boolean; allowAttemptReset: boolean; allowAttemptReopen: boolean; allowTimeExtension: boolean;
  questions: QuestionDto[] | null;
}

export interface StudentExamDto {
  id: number; title: string; startsAt: string; endsAt: string; durationMinutes: number; mode: ExamMode;
  inProgressAttemptId: number | null;
}

export interface AttemptDto {
  id: number; examId: number; studentId: number; attemptNumber: number; questionText: string;
  status: AttemptStatus; client: ClientKind; startedAt: string; deadlineAt: string; submittedAt: string | null;
  language: string | null; code: string; strikes: number; submitReason: string | null; serverTime: string;
}

export interface AttemptEventDto { id: number; type: AttemptEventType; occurredAt: string; detail: string | null; evidenceId: number | null }
export interface EvidenceDto { id: number; kind: EvidenceKind; contentType: string; sizeBytes: number; createdAt: string }
export interface AttemptDetailDto { attempt: AttemptDto; studentName: string; events: AttemptEventDto[]; evidence: EvidenceDto[] }

export interface AttemptSummaryDto {
  id: number; studentId: number; studentName: string; attemptNumber: number; status: AttemptStatus; client: ClientKind;
  startedAt: string; deadlineAt: string; submittedAt: string | null; strikes: number;
}

export interface RunResult { stdout: string; stderr: string; exitCode: number; timedOut: boolean; durationMs: number }

export interface LiveAttemptMessage {
  attemptId: number; examId: number; studentId: number; status: AttemptStatus; deadlineAt: string; strikes: number;
  eventType: AttemptEventType | null;
}

export interface ProblemDetails { status?: number; title?: string; detail?: string | null; errors?: Record<string, string[]> }
```

- [ ] **Step 2: Write the failing client tests**

`clients/web/src/api/client.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest';
import { ApiClient, ApiError, type TokenStore } from './client';

function memoryStore(initial: string | null = null): TokenStore {
  let v = initial;
  return { get: () => v, set: t => { v = t; } };
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': status >= 400 ? 'application/problem+json' : 'application/json' },
  });

const login = (n: number) => ({
  accessToken: `a${n}`, refreshToken: `r${n}`, expiresAt: new Date(Date.now() + 900_000).toISOString(), user: {},
});
const me = { user: { id: 1, username: 'u', fullName: 'U', email: null, isActive: true, hasPin: false, assignments: [] }, capabilities: [] };

type Call = { method: string; path: string; auth: string | null; body: unknown };

function server(route: (c: Call) => Response | Promise<Response>) {
  const calls: Call[] = [];
  const fetchFn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const headers = new Headers(init?.headers);
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : init?.body;
    const c: Call = { method: init?.method ?? 'GET', path: String(input), auth: headers.get('Authorization'), body };
    calls.push(c);
    return route(c);
  });
  return { calls, fetchFn: fetchFn as unknown as typeof fetch };
}

async function signedIn(route: (c: Call) => Response | Promise<Response>, store = memoryStore()) {
  const s = server(c => (c.path === '/api/auth/login' ? json(login(1)) : c.path === '/api/auth/me' ? json(me) : route(c)));
  const api = new ApiClient(s.fetchFn, store);
  await api.login('u', 'p');
  s.calls.length = 0;
  return { api, calls: s.calls, store };
}

describe('ApiClient', () => {
  it('signs in as the Web client and keeps the refresh token in the store', async () => {
    const store = memoryStore();
    const s = server(c => (c.path === '/api/auth/login' ? json(login(1)) : json(me)));
    const api = new ApiClient(s.fetchFn, store);

    expect(await api.login('u', 'p')).toEqual(me);

    expect(s.calls[0]).toMatchObject({ method: 'POST', body: { username: 'u', password: 'p', client: 'Web' }, auth: null });
    expect(s.calls[1]).toMatchObject({ path: '/api/auth/me', auth: 'Bearer a1' });
    expect(store.get()).toBe('r1');
    expect(api.isSignedIn).toBe(true);
  });

  it('refreshes once on 401 and retries with the new token', async () => {
    const { api, calls } = await signedIn(c =>
      c.path === '/api/auth/refresh' ? json(login(2)) : c.auth === 'Bearer a2' ? json({ ok: true }) : json({ title: 'Unauthorized' }, 401));

    expect(await api.get('/api/exams')).toEqual({ ok: true });

    expect(calls.map(c => `${c.path} ${c.auth}`)).toEqual(['/api/exams Bearer a1', '/api/auth/refresh null', '/api/exams Bearer a2']);
    expect(calls[1].body).toEqual({ refreshToken: 'r1' });
  });

  it('shares one refresh between concurrent 401s', async () => {
    const { api, calls } = await signedIn(c =>
      c.path === '/api/auth/refresh' ? json(login(2)) : c.auth === 'Bearer a2' ? json(1) : json({}, 401));

    await Promise.all([api.get('/api/a'), api.get('/api/b'), api.get('/api/c')]);

    expect(calls.filter(c => c.path === '/api/auth/refresh')).toHaveLength(1);
  });

  it('ends the session once when the server refuses the refresh', async () => {
    const { api, store } = await signedIn(c => (c.path === '/api/auth/refresh' ? json({ title: 'Bad' }, 401) : json({}, 401)));
    const expired = vi.fn();
    api.onSessionExpired = expired;

    await Promise.all([
      expect(api.get('/api/a')).rejects.toMatchObject({ status: 401 }),
      expect(api.get('/api/b')).rejects.toMatchObject({ status: 401 }),
    ]);

    expect(expired).toHaveBeenCalledOnce();
    expect(api.isSignedIn).toBe(false);
    expect(store.get()).toBeNull();
  });

  it('keeps the session when the refresh fails for another reason', async () => {
    const { api, store } = await signedIn(c => (c.path === '/api/auth/refresh' ? json({ title: 'Down' }, 503) : json({}, 401)));
    const expired = vi.fn();
    api.onSessionExpired = expired;

    await expect(api.get('/api/exams')).rejects.toMatchObject({ status: 503 });

    expect(expired).not.toHaveBeenCalled();
    expect(api.isSignedIn).toBe(true);
    expect(store.get()).toBe('r1');
  });

  it('turns problem+json into ApiError', async () => {
    const { api } = await signedIn(() => json({ title: 'Forbidden', detail: 'Incorrect PIN.', status: 403 }, 403));

    const e = await api.post('/api/exams/1/attempts', { pin: '0' }).catch((x: unknown) => x);

    expect(e).toBeInstanceOf(ApiError);
    expect(e).toMatchObject({ status: 403, title: 'Forbidden', detail: 'Incorrect PIN.' });
  });

  it('reports a network failure as status 0', async () => {
    const { api } = await signedIn(() => { throw new TypeError('Failed to fetch'); });

    await expect(api.get('/api/exams')).rejects.toMatchObject({ status: 0, title: "Can't reach the server" });
  });

  it('returns undefined for 204 and a Blob for non-JSON bodies', async () => {
    const { api } = await signedIn(c =>
      c.path === '/api/x' ? new Response(null, { status: 204 }) : new Response('img', { headers: { 'content-type': 'image/jpeg' } }));

    expect(await api.put('/api/x', {})).toBeUndefined();
    const blob = await api.get<Blob>('/api/evidence/1');
    expect(blob).toBeInstanceOf(Blob);
    expect(await blob.text()).toBe('img');
  });

  it('restores a session from a stored refresh token', async () => {
    const store = memoryStore('r1');
    const s = server(c => (c.path === '/api/auth/refresh' ? json(login(2)) : json(me)));
    const api = new ApiClient(s.fetchFn, store);

    expect(await api.restore()).toEqual(me);

    expect(s.calls.map(c => c.path)).toEqual(['/api/auth/refresh', '/api/auth/me']);
    expect(store.get()).toBe('r2');
  });

  it('restores nothing without a stored token, and clears a refused one', async () => {
    expect(await new ApiClient(server(() => json({})).fetchFn, memoryStore()).restore()).toBeNull();

    const store = memoryStore('old');
    expect(await new ApiClient(server(() => json({}, 401)).fetchFn, store).restore()).toBeNull();
    expect(store.get()).toBeNull();
  });

  it('logout tells the server and always clears locally', async () => {
    const { api, calls, store } = await signedIn(() => { throw new TypeError('offline'); });

    await api.logout();

    expect(calls[0]).toMatchObject({ path: '/api/auth/logout', body: { refreshToken: 'r1' } });
    expect(api.isSignedIn).toBe(false);
    expect(store.get()).toBeNull();
  });
});
```

Run (in `clients/web`): `npm test`
Expected: FAIL — `./client` not found.

- [ ] **Step 3: Implement the client**

`clients/web/src/api/client.ts`:

```ts
import type { LoginResponse, MeResponse, ProblemDetails } from './types';

// Every failure the app sees is an ApiError: a server problem (status = HTTP status) or no connection (status 0).
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail?: string,
    readonly errors?: Record<string, string[]>,
  ) {
    super(detail ?? title);
    this.name = 'ApiError';
  }

  static network(): ApiError {
    return new ApiError(0, "Can't reach the server", 'Check your connection and try again.');
  }

  static async from(res: Response): Promise<ApiError> {
    let p: ProblemDetails = {};
    try {
      p = (await res.json()) as ProblemDetails;
    } catch {
      /* not JSON */
    }
    return new ApiError(res.status, p.title || res.statusText || `Error ${res.status}`, p.detail ?? undefined, p.errors);
  }
}

export interface TokenStore {
  get(): string | null;
  set(token: string | null): void;
}

const REFRESH_KEY = 'secucode.refresh';

// Survives a reload, cleared when the tab closes. Readable by page scripts, so the API serves the app with a strict CSP.
export const sessionTokenStore: TokenStore = {
  get: () => {
    try {
      return sessionStorage.getItem(REFRESH_KEY);
    } catch {
      return null;
    }
  },
  set: token => {
    try {
      if (token) sessionStorage.setItem(REFRESH_KEY, token);
      else sessionStorage.removeItem(REFRESH_KEY);
    } catch {
      /* storage blocked: the session lasts until reload */
    }
  },
};

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

// The app's only door to the server. The access token lives in memory; concurrent 401s share one refresh.
export class ApiClient {
  private accessToken: string | null = null;
  private expiresAt = 0;
  private refreshing: Promise<boolean> | null = null;
  private restoring: Promise<MeResponse | null> | null = null;

  // Called once when the server refuses to refresh: the user must sign in again.
  onSessionExpired: () => void = () => {};

  constructor(
    private readonly fetchFn: typeof fetch = (input, init) => fetch(input, init),
    private readonly refreshStore: TokenStore = sessionTokenStore,
  ) {}

  get isSignedIn(): boolean {
    return this.accessToken !== null;
  }

  async login(username: string, password: string): Promise<MeResponse> {
    this.setTokens(await this.request<LoginResponse>('POST', '/api/auth/login', { username, password, client: 'Web' }, false));
    return this.get<MeResponse>('/api/auth/me');
  }

  // Page load: turn a stored refresh token back into a session. Null means "sign in". Idempotent (StrictMode runs effects twice).
  restore(): Promise<MeResponse | null> {
    return (this.restoring ??= this.doRestore().finally(() => { this.restoring = null; }));
  }

  private async doRestore(): Promise<MeResponse | null> {
    if (this.refreshStore.get() === null) return null;
    if (!(await this.refresh(null))) {
      this.clear();
      return null;
    }
    return this.get<MeResponse>('/api/auth/me');
  }

  async logout(): Promise<void> {
    const token = this.refreshStore.get();
    try {
      if (token) await this.request('POST', '/api/auth/logout', { refreshToken: token }, true, false);
    } catch {
      /* signing out locally matters more than telling the server */
    } finally {
      this.clear();
    }
  }

  get<T>(path: string): Promise<T> {
    return this.request<T>('GET', path);
  }
  post<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('POST', path, body);
  }
  put<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>('PUT', path, body);
  }
  patch<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>('PATCH', path, body);
  }
  del<T>(path: string): Promise<T> {
    return this.request<T>('DELETE', path);
  }

  // For SignalR: a token valid for at least another minute. Offline, the old token is returned and the hub retries.
  async freshAccessToken(): Promise<string> {
    const token = this.accessToken;
    if (token && this.expiresAt - Date.now() < 60_000) {
      try {
        await this.refresh(token);
      } catch {
        /* offline: try with what we have */
      }
    }
    return this.accessToken ?? '';
  }

  private async request<T>(method: Method, path: string, body?: unknown, authenticated = true, signalExpiry = true): Promise<T> {
    const res = await this.send(method, path, body, authenticated, signalExpiry);
    if (res.status === 204) return undefined as T;
    const type = res.headers.get('content-type') ?? '';
    return (type.includes('json') ? await res.json() : await res.blob()) as T;
  }

  private async send(method: Method, path: string, body: unknown, authenticated: boolean, signalExpiry: boolean): Promise<Response> {
    const used = this.accessToken;
    let res = await this.sendOnce(method, path, body, authenticated);
    if (res.status === 401 && authenticated && used !== null) {
      if (!(await this.refresh(used))) {
        if (this.clear() && signalExpiry) this.onSessionExpired();
        throw new ApiError(401, 'Session expired', 'Your session expired. Please sign in again.');
      }
      res = await this.sendOnce(method, path, body, authenticated);
    }
    if (!res.ok) throw await ApiError.from(res);
    return res;
  }

  private async sendOnce(method: Method, path: string, body: unknown, authenticated: boolean): Promise<Response> {
    const headers: Record<string, string> = {};
    if (authenticated && this.accessToken) headers.Authorization = `Bearer ${this.accessToken}`;
    let payload: BodyInit | undefined;
    if (body instanceof FormData) payload = body;
    else if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }
    try {
      return await this.fetchFn(path, { method, headers, body: payload });
    } catch {
      throw ApiError.network();
    }
  }

  // A caller whose token was already replaced just retries with the new one.
  private refresh(stale: string | null): Promise<boolean> {
    if (this.accessToken !== stale) return Promise.resolve(this.accessToken !== null);
    const token = this.refreshStore.get();
    if (!token) return Promise.resolve(false);
    return (this.refreshing ??= this.doRefresh(token).finally(() => { this.refreshing = null; }));
  }

  private async doRefresh(token: string): Promise<boolean> {
    const res = await this.sendOnce('POST', '/api/auth/refresh', { refreshToken: token }, false);
    if (res.status === 400 || res.status === 401 || res.status === 403) return false;
    if (!res.ok) throw await ApiError.from(res); // 5xx, 429...: not a refusal, keep the session
    const login = (await res.json()) as LoginResponse;
    if (this.refreshStore.get() !== token) return false; // signed out meanwhile
    this.setTokens(login);
    return true;
  }

  private setTokens(r: LoginResponse): void {
    this.accessToken = r.accessToken;
    this.expiresAt = Date.parse(r.expiresAt);
    this.refreshStore.set(r.refreshToken);
  }

  // True only for the call that actually ended a signed-in session.
  private clear(): boolean {
    const was = this.accessToken !== null;
    this.accessToken = null;
    this.refreshStore.set(null);
    return was;
  }
}
```

- [ ] **Step 4: Run the tests**

Run (in `clients/web`): `npm test`
Expected: PASS (all client and theme tests).

- [ ] **Step 5: Commit**

```powershell
git add clients/web/src/api/types.ts clients/web/src/api/client.ts clients/web/src/api/client.test.ts
git commit -m "feat(web): API types and ApiClient with a shared token refresh" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 4: Session rules and AuthProvider

**Files:**
- Create: `clients/web/src/auth/session.ts`
- Test: `clients/web/src/auth/session.test.ts`
- Create: `clients/web/src/auth/AuthContext.tsx`

**Interfaces:**
- Consumes: `ApiClient`, `ApiError` (Task 3); `MeResponse`, `ExamDto`, `Capability` types.
- Produces:
  - `class Session(me: MeResponse)` with `user: UserDto`, `isStudent: boolean`, `canCreateExams: boolean`, `roleNames: string`, `canManage(exam: ExamDto): boolean`, `canViewResults(exam: ExamDto): boolean`.
  - `AuthProvider({ api, children })`; `useAuth(): { api: ApiClient; session: Session | null; expired: boolean; login(u, p): Promise<void>; logout(): Promise<void> }`; `useSession(): Session` (throws without a session); `RequireAuth({ children, keepOnExpiry? })`; `StaffOnly({ children })`; `StudentOnly({ children })`.

- [ ] **Step 1: Write the failing tests**

`clients/web/src/auth/session.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import type { AssignmentDto, Capability, EffectiveCapabilityDto, ExamDto, MeResponse, Reach } from '../api/types';
import { Session } from './session';

const assignment = (over: Partial<AssignmentDto> = {}): AssignmentDto => ({
  id: 1, roleId: 1, roleName: 'Faculty', branchId: 1, classId: null, batchId: null,
  validFrom: null, validTo: null, isActive: true, isStudent: false, ...over,
});
const cap = (capability: Capability, reach: Reach = 'InScope', branchId: number | null = 1): EffectiveCapabilityDto => ({
  capability, reach, scope: { branchId, classId: null, batchId: null }, roleName: 'Faculty',
});
const me = (assignments: AssignmentDto[], capabilities: EffectiveCapabilityDto[] = []): MeResponse => ({
  user: { id: 7, username: 'u', fullName: 'U', email: null, isActive: true, hasPin: false, assignments }, capabilities,
});
const exam = (over: Partial<ExamDto> = {}): ExamDto => ({
  id: 1, title: 'T', startsAt: '', endsAt: '', durationMinutes: 30, mode: 'WebAllowed', isActive: true,
  branchId: 1, classId: 2, batchId: null, createdBy: 99, createdByName: 'X',
  allowQuestionEditAfterAttempts: true, allowAttemptReset: true, allowAttemptReopen: true, allowTimeExtension: true,
  questions: null, ...over,
});

describe('Session', () => {
  it('is a student only through an active student assignment', () => {
    expect(new Session(me([assignment({ isStudent: true })])).isStudent).toBe(true);
    expect(new Session(me([assignment({ isStudent: true, isActive: false })])).isStudent).toBe(false);
    expect(new Session(me([assignment()])).isStudent).toBe(false);
  });

  it('offers New exam only with CreateExams', () => {
    expect(new Session(me([assignment()], [cap('CreateExams')])).canCreateExams).toBe(true);
    expect(new Session(me([assignment()], [cap('ViewResults')])).canCreateExams).toBe(false);
  });

  it('lets the creator manage and view results without a grant', () => {
    const s = new Session(me([assignment()]));
    expect(s.canManage(exam({ createdBy: 7 }))).toBe(true);
    expect(s.canViewResults(exam({ createdBy: 7 }))).toBe(true);
    expect(s.canManage(exam())).toBe(false);
  });

  it('checks InScope grants against the exam target', () => {
    const s = new Session(me([assignment()], [cap('ManageExams', 'InScope', 1), cap('ViewResults', 'InScope', 2)]));
    expect(s.canManage(exam({ branchId: 1 }))).toBe(true);
    expect(s.canManage(exam({ branchId: 3 }))).toBe(false);
    expect(s.canViewResults(exam({ branchId: 1 }))).toBe(false);
    expect(new Session(me([assignment()], [cap('ManageExams', 'InScope', null)])).canManage(exam({ branchId: 5 }))).toBe(true);
  });

  it('limits OwnOnly grants to the creator', () => {
    const s = new Session(me([assignment()], [cap('ManageExams', 'OwnOnly')]));
    expect(s.canManage(exam())).toBe(false);
    expect(s.canManage(exam({ createdBy: 7 }))).toBe(true);
  });
});
```

Run (in `clients/web`): `npm test`
Expected: FAIL — `./session` not found.

- [ ] **Step 2: Implement Session**

`clients/web/src/auth/session.ts`:

```ts
import type { Capability, ExamDto, MeResponse, ScopeDto } from '../api/types';

// Who is signed in and what the UI should offer them. The API stays the authority:
// this only hides actions it would refuse, and a 403 still shows the server's reason.
export class Session {
  constructor(readonly me: MeResponse) {}

  get user() {
    return this.me.user;
  }
  get isStudent(): boolean {
    return this.me.user.assignments.some(a => a.isActive && a.isStudent);
  }
  get canCreateExams(): boolean {
    return this.me.capabilities.some(c => c.capability === 'CreateExams');
  }
  get roleNames(): string {
    return [...new Set(this.me.user.assignments.filter(a => a.isActive).map(a => a.roleName))].join(', ');
  }

  canManage(exam: ExamDto): boolean {
    return this.isCreator(exam) || this.grants('ManageExams', exam);
  }
  canViewResults(exam: ExamDto): boolean {
    return this.isCreator(exam) || this.grants('ViewResults', exam);
  }

  private isCreator(exam: ExamDto): boolean {
    return exam.createdBy === this.me.user.id;
  }

  // ponytail: scope containment by ids only (same as the desktop); the API's checks cover anything this misses.
  private grants(capability: Capability, exam: ExamDto): boolean {
    return this.me.capabilities.some(c =>
      c.capability === capability && (c.reach === 'OwnOnly' ? this.isCreator(exam) : covers(c.scope, exam)));
  }
}

const covers = (s: ScopeDto, e: ExamDto) =>
  (s.branchId === null || s.branchId === e.branchId) &&
  (s.classId === null || s.classId === e.classId) &&
  (s.batchId === null || s.batchId === e.batchId);
```

- [ ] **Step 3: Run the tests**

Run (in `clients/web`): `npm test`
Expected: PASS.

- [ ] **Step 4: Implement AuthProvider and the route guards**

`clients/web/src/auth/AuthContext.tsx` (it uses `Spinner`, `ErrorBar` and `Button` from Task 5's `ui/index.tsx`; to keep this task compiling on its own, it renders plain elements for boot states — Task 5 does not need to change it):

```tsx
import { useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { ApiError, type ApiClient } from '../api/client';
import { Session } from './session';

interface Auth {
  api: ApiClient;
  session: Session | null;
  // True after the server refused to refresh. The exam page stays mounted and asks for the password in a dialog.
  expired: boolean;
  login(username: string, password: string): Promise<void>;
  logout(): Promise<void>;
}

const AuthContext = createContext<Auth | null>(null);

export function useAuth(): Auth {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('useAuth must be used inside AuthProvider');
  return auth;
}

export function useSession(): Session {
  const session = useAuth().session;
  if (!session) throw new Error('No session: wrap the route in RequireAuth');
  return session;
}

export function AuthProvider({ api, children }: { api: ApiClient; children: ReactNode }) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState<Session | null>(null);
  const [expired, setExpired] = useState(false);
  const [boot, setBoot] = useState<'loading' | 'ready' | ApiError>('loading');

  const restore = useCallback(() => {
    setBoot('loading');
    api.restore().then(
      me => {
        if (me) setSession(new Session(me));
        setBoot('ready');
      },
      (e: unknown) => setBoot(e instanceof ApiError ? e : ApiError.network()),
    );
  }, [api]);

  useEffect(() => {
    api.onSessionExpired = () => setExpired(true);
    restore();
  }, [api, restore]);

  const value = useMemo<Auth>(() => ({
    api,
    session,
    expired,
    async login(username, password) {
      const me = await api.login(username, password);
      setSession(new Session(me));
      setExpired(false);
    },
    async logout() {
      await api.logout();
      setSession(null);
      setExpired(false);
      queryClient.clear();
    },
  }), [api, session, expired, queryClient]);

  if (boot === 'loading') return <div className="boot" aria-busy="true">Loading…</div>;
  if (boot instanceof ApiError)
    return (
      <div className="boot" role="alert">
        <p><strong>{boot.title}</strong> {boot.detail}</p>
        <button type="button" className="btn" onClick={restore}>Retry</button>
      </div>
    );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function RequireAuth({ children, keepOnExpiry = false }: { children: ReactNode; keepOnExpiry?: boolean }) {
  const { session, expired } = useAuth();
  const location = useLocation();
  if (!session || (expired && !keepOnExpiry))
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search, expired }} />;
  return <>{children}</>;
}

export function StaffOnly({ children }: { children: ReactNode }) {
  return useSession().isStudent ? <Navigate to="/exams" replace /> : <>{children}</>;
}

export function StudentOnly({ children }: { children: ReactNode }) {
  return useSession().isStudent ? <>{children}</> : <Navigate to="/exams" replace />;
}
```

- [ ] **Step 5: Type-check and test**

Run (in `clients/web`): `npx tsc --noEmit; npm test`
Expected: no type errors; tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add clients/web/src/auth/session.ts clients/web/src/auth/session.test.ts clients/web/src/auth/AuthContext.tsx
git commit -m "feat(web): session permission rules, AuthProvider and route guards" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---
### Task 5: UI kit and control styles

**Files:**
- Create: `clients/web/src/theme/controls.css`
- Create: `clients/web/src/ui/index.tsx`
- Create: `clients/web/src/ui/Dialog.tsx`
- Test: `clients/web/src/ui/ui.test.tsx`
- Modify: `clients/web/src/main.tsx` (import `controls.css`)

**Interfaces:**
- Consumes: `ApiError` (Task 3).
- Produces (all from `ui/index.tsx` unless noted):
  - `Button(props: ButtonHTMLAttributes & { variant?: 'primary' | 'standard' | 'subtle' | 'danger'; busy?: boolean })` — defaults to `type="button"`.
  - `TextField(props: InputHTMLAttributes & { label: string; error?: string; hint?: string })`, `TextArea(props: TextareaHTMLAttributes & { label: string; error?: string })`, `Select(props: SelectHTMLAttributes & { label: string; error?: string })`.
  - `Checkbox({ label, checked, onChange(v: boolean), disabled? })`, `Toggle({ label, checked, onChange(v: boolean), disabled? })`.
  - `Card({ children, className?, as? })`, `Spinner({ size? })`, `Badge({ tone?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger', children })`.
  - `InfoBar({ kind?: 'info' | 'success' | 'warning' | 'error', title, children?, action? })`, `ErrorBar({ error: unknown, onRetry?: () => void })`.
  - `DataTable<T>({ rows: T[], columns: Column<T>[], rowKey(row): string | number, empty: string, caption: string })`, `interface Column<T> { header: string; cell(row: T): ReactNode; width?: string }`.
  - `ui/Dialog.tsx`: `Dialog({ open, title, children, actions, onClose })` on the native `<dialog>` (Escape calls `onClose`).

- [ ] **Step 1: Write the failing tests**

`clients/web/src/ui/ui.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client';
import { Button, DataTable, ErrorBar, InfoBar, TextField } from './index';

describe('ui', () => {
  it('announces errors and warnings as alerts, other InfoBars as status', () => {
    render(<><InfoBar kind="error" title="Broken" /><InfoBar title="Note" /></>);
    expect(screen.getByRole('alert').textContent).toContain('Broken');
    expect(screen.getByRole('status').textContent).toContain('Note');
  });

  it('offers Retry only for connection failures', () => {
    const retry = vi.fn();
    const { rerender } = render(<ErrorBar error={ApiError.network()} onRetry={retry} />);
    screen.getByRole('button', { name: 'Retry' }).click();
    expect(retry).toHaveBeenCalledOnce();

    rerender(<ErrorBar error={new ApiError(403, 'Forbidden', 'Not yours.')} onRetry={retry} />);
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    expect(screen.getByRole('alert').textContent).toContain('Not yours.');
  });

  it('shows unexpected errors with a generic title', () => {
    render(<ErrorBar error={new Error('boom')} />);
    expect(screen.getByRole('alert').textContent).toContain('Something went wrong');
  });

  it('disables a busy button and defaults to type=button', () => {
    render(<Button busy>Save</Button>);
    const b = screen.getByRole('button', { name: /Save/ }) as HTMLButtonElement;
    expect(b.disabled).toBe(true);
    expect(b.type).toBe('button');
  });

  it('labels fields and links their error', () => {
    render(<TextField label="Title" error="Enter a title." value="" onChange={() => {}} />);
    const input = screen.getByLabelText('Title');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(document.getElementById(input.getAttribute('aria-describedby')!)?.textContent).toBe('Enter a title.');
  });

  it('renders rows, or the empty text', () => {
    const columns = [{ header: 'Name', cell: (r: { n: string }) => r.n }];
    const { rerender } = render(<DataTable caption="People" rows={[{ n: 'Asha' }]} columns={columns} rowKey={r => r.n} empty="Nobody." />);
    expect(screen.getByRole('table', { name: 'People' }).textContent).toContain('Asha');
    rerender(<DataTable caption="People" rows={[]} columns={columns} rowKey={r => r.n} empty="Nobody." />);
    expect(screen.getByText('Nobody.')).toBeTruthy();
  });
});
```

Run (in `clients/web`): `npm test`
Expected: FAIL — `./index` not found.

- [ ] **Step 2: Implement the components**

`clients/web/src/ui/index.tsx`:

```tsx
import {
  useId,
  type ButtonHTMLAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react';
import { ApiError } from '../api/client';

type Variant = 'primary' | 'standard' | 'subtle' | 'danger';

export function Button({ variant = 'standard', busy = false, className = '', disabled, children, ...rest }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean }) {
  return (
    <button type="button" {...rest} className={`btn btn-${variant} ${className}`.trim()} disabled={disabled || busy} aria-busy={busy || undefined}>
      {busy && <Spinner size={14} />}
      {children}
    </button>
  );
}

function Field({ id, label, error, hint, children }: { id: string; label: string; error?: string; hint?: string; children: ReactNode }) {
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {children}
      {error ? <small id={`${id}-err`} className="error">{error}</small> : hint && <small className="hint">{hint}</small>}
    </div>
  );
}

const fieldProps = (id: string, error?: string) => ({
  id,
  className: 'input',
  'aria-invalid': error ? true : undefined,
  'aria-describedby': error ? `${id}-err` : undefined,
});

export function TextField({ label, error, hint, id, ...rest }: InputHTMLAttributes<HTMLInputElement> & { label: string; error?: string; hint?: string }) {
  const auto = useId();
  const fid = id ?? auto;
  return <Field id={fid} label={label} error={error} hint={hint}><input {...rest} {...fieldProps(fid, error)} /></Field>;
}

export function TextArea({ label, error, id, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement> & { label: string; error?: string }) {
  const auto = useId();
  const fid = id ?? auto;
  return <Field id={fid} label={label} error={error}><textarea {...rest} {...fieldProps(fid, error)} /></Field>;
}

export function Select({ label, error, id, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement> & { label: string; error?: string }) {
  const auto = useId();
  const fid = id ?? auto;
  return <Field id={fid} label={label} error={error}><select {...rest} {...fieldProps(fid, error)}>{children}</select></Field>;
}

interface CheckProps { label: string; checked: boolean; onChange(value: boolean): void; disabled?: boolean }

export function Checkbox({ label, checked, onChange, disabled }: CheckProps) {
  return (
    <label className="check">
      <input type="checkbox" checked={checked} disabled={disabled} onChange={e => onChange(e.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

export function Toggle({ label, checked, onChange, disabled }: CheckProps) {
  return (
    <label className="toggle">
      <input type="checkbox" role="switch" checked={checked} disabled={disabled} onChange={e => onChange(e.target.checked)} />
      <span className="track" aria-hidden="true" />
      <span>{label}</span>
    </label>
  );
}

export function Card({ children, className = '', as: Tag = 'section' }: { children: ReactNode; className?: string; as?: 'section' | 'article' | 'div' }) {
  return <Tag className={`card ${className}`.trim()}>{children}</Tag>;
}

export function Spinner({ size = 20 }: { size?: number }) {
  return <span className="spinner" role="progressbar" aria-label="Loading" style={{ width: size, height: size }} />;
}

export function Badge({ tone = 'neutral', children }: { tone?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger'; children: ReactNode }) {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}

export type InfoKind = 'info' | 'success' | 'warning' | 'error';

export function InfoBar({ kind = 'info', title, children, action }: { kind?: InfoKind; title: string; children?: ReactNode; action?: ReactNode }) {
  const urgent = kind === 'error' || kind === 'warning';
  return (
    <div className={`infobar infobar-${kind}`} role={urgent ? 'alert' : 'status'}>
      <div><strong>{title}</strong>{children && <> {children}</>}</div>
      {action}
    </div>
  );
}

export function ErrorBar({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const e = error instanceof ApiError ? error : new ApiError(-1, 'Something went wrong.', error instanceof Error ? error.message : undefined);
  return (
    <InfoBar kind="error" title={e.title} action={e.status === 0 && onRetry ? <Button variant="subtle" onClick={onRetry}>Retry</Button> : undefined}>
      {e.detail}
    </InfoBar>
  );
}

export interface Column<T> { header: string; cell(row: T): ReactNode; width?: string }

export function DataTable<T>({ rows, columns, rowKey, empty, caption }:
  { rows: T[]; columns: Column<T>[]; rowKey(row: T): string | number; empty: string; caption: string }) {
  if (rows.length === 0) return <p className="empty">{empty}</p>;
  return (
    <div className="table-wrap">
      <table className="table">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>{columns.map(c => <th key={c.header} scope="col" style={{ width: c.width }}>{c.header}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map(r => <tr key={rowKey(r)}>{columns.map(c => <td key={c.header}>{c.cell(r)}</td>)}</tr>)}
        </tbody>
      </table>
    </div>
  );
}
```

`clients/web/src/ui/Dialog.tsx`:

```tsx
import { useEffect, useId, useRef, type ReactNode } from 'react';

// The native <dialog> gives a modal with focus containment, Escape and a backdrop for free.
export function Dialog({ open, title, children, actions, onClose }:
  { open: boolean; title: string; children?: ReactNode; actions: ReactNode; onClose(): void }) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();

  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal?.();
    if (!open && d.open) d.close();
  }, [open]);

  return (
    <dialog ref={ref} className="dialog" aria-labelledby={titleId} onCancel={e => { e.preventDefault(); onClose(); }}>
      {open && (
        <>
          <h2 id={titleId}>{title}</h2>
          {children && <div className="dialog-body">{children}</div>}
          <div className="dialog-actions">{actions}</div>
        </>
      )}
    </dialog>
  );
}
```

`clients/web/src/theme/controls.css`:

```css
/* Control styles. Colours, radii, type and motion come only from tokens.css. */
*, *::before, *::after { box-sizing: border-box; }
h1, h2, h3, p { margin: 0; }
h1, h2, h3 { font-weight: 600; }
.display { font-size: var(--fs-display); line-height: 1.2; font-weight: 600; }
.title { font-size: var(--fs-title); line-height: 1.25; font-weight: 600; }
.subtitle { font-size: var(--fs-subtitle); line-height: 1.3; font-weight: 600; }
.caption, small { font-size: var(--fs-caption); }
.secondary { color: var(--text-secondary); }
a { color: var(--accent); }
:focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
.stack { display: grid; gap: 16px; align-content: start; }
.row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.boot { display: grid; place-content: center; gap: 12px; min-height: 100vh; text-align: center; }

.btn {
  display: inline-flex; align-items: center; justify-content: center; gap: 8px;
  min-height: 32px; padding: 0 14px; border-radius: var(--radius);
  border: 1px solid var(--border); background: var(--surface-raised); color: var(--text-primary);
  font: inherit; cursor: pointer; white-space: nowrap;
  transition: background-color var(--dur-fast), border-color var(--dur-fast), color var(--dur-fast);
}
.btn:hover:not(:disabled) { background: var(--surface-sunken); }
.btn:active:not(:disabled) { background: var(--subtle-pressed); }
.btn:disabled { color: var(--text-disabled); cursor: default; }
.btn-primary { background: var(--accent); border-color: transparent; color: var(--on-accent); }
.btn-primary:hover:not(:disabled) { background: var(--accent-hover); }
.btn-primary:active:not(:disabled) { background: var(--accent-pressed); }
.btn-primary:disabled { background: var(--surface-sunken); }
.btn-subtle { background: transparent; border-color: transparent; }
.btn-subtle:hover:not(:disabled) { background: var(--subtle-hover); }
.btn-danger { background: var(--danger); border-color: transparent; color: var(--surface-raised); }
.btn-danger:hover:not(:disabled) { background: var(--danger); filter: brightness(1.08); }
.btn.icon { width: 32px; padding: 0; }

.field { display: grid; gap: 4px; }
.field > label { color: var(--text-secondary); }
.input {
  min-height: 32px; padding: 5px 10px; width: 100%;
  border: 1px solid var(--border-strong); border-radius: var(--radius);
  background: var(--surface-raised); color: var(--text-primary); font: inherit;
  transition: border-color var(--dur-fast);
}
textarea.input { min-height: 72px; resize: vertical; }
.input:hover:not(:disabled) { border-color: var(--text-secondary); }
.input:focus-visible { outline: 2px solid var(--focus); outline-offset: 0; border-color: var(--focus); }
.input:disabled { color: var(--text-disabled); background: var(--surface-sunken); }
.input[aria-invalid="true"] { border-color: var(--danger); }
.field .error { color: var(--danger); }
.field .hint { color: var(--text-secondary); }

.check, .toggle { display: inline-flex; align-items: center; gap: 8px; cursor: pointer; }
.check input { width: 18px; height: 18px; margin: 0; accent-color: var(--accent); }
.toggle { position: relative; }
.toggle input { position: absolute; opacity: 0; width: 1px; height: 1px; }
.toggle .track {
  position: relative; width: 40px; height: 20px; border-radius: 10px;
  border: 1px solid var(--border-strong); background: var(--surface-raised);
  transition: background-color var(--dur-fast), border-color var(--dur-fast);
}
.toggle .track::after {
  content: ""; position: absolute; top: 3px; left: 3px; width: 12px; height: 12px; border-radius: 50%;
  background: var(--text-secondary); transition: transform var(--dur-pop) var(--ease-out), background-color var(--dur-fast);
}
.toggle input:checked + .track { background: var(--accent); border-color: var(--accent); }
.toggle input:checked + .track::after { transform: translateX(20px); background: var(--on-accent); }
.toggle input:focus-visible + .track { outline: 2px solid var(--focus); outline-offset: 2px; }
.toggle input:disabled + .track { opacity: 0.5; }

.card { background: var(--surface-raised); border: 1px solid var(--border); border-radius: var(--radius-card); padding: 20px; }

.badge { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: var(--fs-caption); background: var(--surface-sunken); color: var(--text-secondary); }
.badge-accent { background: var(--accent-soft); color: var(--accent); }
.badge-success { background: var(--success-soft); color: var(--success); }
.badge-warning { background: var(--warning-soft); color: var(--warning); }
.badge-danger { background: var(--danger-soft); color: var(--danger); }

.infobar {
  display: flex; align-items: center; justify-content: space-between; gap: 12px;
  padding: 10px 14px; border-radius: var(--radius); border: 1px solid var(--border);
  animation: pop-in var(--dur-pop) var(--ease-out);
}
.infobar-info { background: var(--accent-soft); }
.infobar-success { background: var(--success-soft); }
.infobar-warning { background: var(--warning-soft); }
.infobar-error { background: var(--danger-soft); }
.infobar-success strong { color: var(--success); }
.infobar-warning strong { color: var(--warning); }
.infobar-error strong { color: var(--danger); }

.dialog {
  width: min(440px, calc(100vw - 32px)); padding: 24px;
  border: 1px solid var(--border); border-radius: var(--radius-card);
  background: var(--surface-raised); color: var(--text-primary);
}
.dialog[open] { animation: pop-in var(--dur-pop) var(--ease-out); }
.dialog::backdrop { background: rgb(0 0 0 / 0.4); }
.dialog h2 { font-size: var(--fs-subtitle); margin-bottom: 12px; }
.dialog-body { display: grid; gap: 12px; }
.dialog-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 20px; }

.table-wrap { overflow-x: auto; border: 1px solid var(--border); border-radius: var(--radius-card); background: var(--surface-raised); }
.table { width: 100%; border-collapse: collapse; }
.table th {
  text-align: left; font-weight: 600; font-size: var(--fs-caption); color: var(--text-secondary);
  background: var(--surface-sunken); padding: 8px 12px; border-bottom: 1px solid var(--border);
}
.table td { padding: 10px 12px; border-bottom: 1px solid var(--border); vertical-align: middle; }
.table tbody tr:last-child td { border-bottom: 0; }
.table tbody tr:hover { background: var(--subtle-hover); }
.empty { color: var(--text-secondary); padding: 24px; text-align: center; }

.spinner {
  display: inline-block; flex: none; border: 2px solid var(--border-strong); border-top-color: var(--accent);
  border-radius: 50%; animation: spin 800ms linear infinite;
}

@keyframes spin { to { transform: rotate(360deg); } }
@keyframes pop-in { from { opacity: 0; transform: scale(0.96); } to { opacity: 1; transform: none; } }
@keyframes page-in { from { opacity: 0; transform: translateY(12px); } to { opacity: 1; transform: none; } }
```

In `clients/web/src/main.tsx`, add below the tokens import:

```tsx
import './theme/controls.css';
```

- [ ] **Step 3: Run the tests**

Run (in `clients/web`): `npm test`
Expected: PASS.

- [ ] **Step 4: Commit**

```powershell
git add clients/web/src/theme/controls.css clients/web/src/ui/index.tsx clients/web/src/ui/Dialog.tsx clients/web/src/ui/ui.test.tsx clients/web/src/main.tsx
git commit -m "feat(web): hand-built UI kit and control styles" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 6: App shell, routing, sign-in and settings

**Files:**
- Create: `clients/web/src/App.tsx`
- Create: `clients/web/src/shell/AppShell.tsx`, `clients/web/src/shell/PageErrorBoundary.tsx`, `clients/web/src/shell/shell.css`
- Create: `clients/web/src/features/login/LoginPage.tsx`
- Create: `clients/web/src/features/settings/SettingsPage.tsx`
- Create: `clients/web/src/features/exams/ExamsHome.tsx`
- Create: `clients/web/src/lib/format.ts`
- Modify: `clients/web/src/main.tsx` (render `<App />`)
- Test: `clients/web/src/shell/shell.test.tsx`

**Interfaces:**
- Consumes: `ApiClient`, `ApiError` (Task 3); `AuthProvider`, `RequireAuth`, `useAuth`, `useSession` (Task 4); UI kit (Task 5); `applyTheme`, `loadTheme` (Task 2).
- Produces:
  - `App.tsx` exports `App` and the route table `routes: RouteObject[]` (later tasks add routes at the marked comments).
  - `AppShell` (sidebar + `<Outlet/>` with page transition and per-page error boundary). Shell CSS classes used later: `.page-header`, `.grid-2`, `.grid-3`.
  - `lib/format.ts`: `formatDateTime(iso)`, `formatWindow(startIso, endIso)`, `statusLabel(AttemptStatus)`, `modeLabel(ExamMode)`, `eventLabel(AttemptEventType)`.
  - `ExamsHome` — the `/exams` page. In this task it renders only the heading `Exams`; Task 7 (staff) and Task 9 (students) replace its body.

- [ ] **Step 1: Write the failing shell test**

`clients/web/src/shell/shell.test.tsx`:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { ApiClient, type TokenStore } from '../api/client';
import { routes } from '../App';
import { AuthProvider } from '../auth/AuthContext';

const store = (v: string | null): TokenStore => ({ get: () => v, set: () => {} });
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
const me = {
  user: { id: 1, username: 'hod', fullName: 'Dr. Asha', email: null, isActive: true, hasPin: false,
    assignments: [{ id: 1, roleId: 2, roleName: 'HOD', branchId: 1, classId: null, batchId: null, validFrom: null, validTo: null, isActive: true, isStudent: false }] },
  capabilities: [],
};

function renderAt(path: string, refreshToken: string | null) {
  const api = new ApiClient(async input => (String(input) === '/api/auth/refresh'
    ? json({ accessToken: 'a', refreshToken: 'r', expiresAt: new Date(Date.now() + 9e5).toISOString(), user: me.user })
    : json(me)), store(refreshToken));
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  render(<QueryClientProvider client={new QueryClient()}><AuthProvider api={api}><RouterProvider router={router} /></AuthProvider></QueryClientProvider>);
  return router;
}

describe('shell', () => {
  it('sends a signed-out visitor to sign-in, remembering where they were going', async () => {
    const router = renderAt('/settings', null);
    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeTruthy();
    expect(router.state.location.pathname).toBe('/login');
    expect(router.state.location.state).toMatchObject({ from: '/settings' });
  });

  it('restores a session and shows the sidebar with the current page marked', async () => {
    renderAt('/settings', 'r0');
    expect(await screen.findByRole('heading', { name: 'Settings' })).toBeTruthy();
    expect(screen.getByRole('navigation', { name: 'Main' }).textContent).toContain('Exams');
    expect(screen.getByRole('link', { name: 'Settings' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getAllByText('Dr. Asha').length).toBeGreaterThan(0);
  });
});
```

(`App.tsx` imports CSS through its pages; Vitest handles CSS imports. The module-level `createBrowserRouter` in `App.tsx` must not run at import time in tests — Step 3 builds it lazily.)

Run (in `clients/web`): `npm test`
Expected: FAIL — `../App` not found.

- [ ] **Step 2: Implement format helpers**

`clients/web/src/lib/format.ts`:

```ts
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
```

- [ ] **Step 3: Implement the shell, pages and routes**

`clients/web/src/shell/PageErrorBoundary.tsx`:

```tsx
import { Component, type ReactNode } from 'react';

// One broken page shouldn't take the shell down with it.
export class PageErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error: unknown) {
    console.error('Page crashed', error);
  }

  render() {
    if (!this.state.failed) return this.props.children;
    return (
      <div className="infobar infobar-error" role="alert">
        <div><strong>Something went wrong.</strong> Reload the page to try again.</div>
        <button type="button" className="btn btn-subtle" onClick={() => location.reload()}>Reload</button>
      </div>
    );
  }
}
```

`clients/web/src/shell/AppShell.tsx`:

```tsx
import { useLayoutEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { PageErrorBoundary } from './PageErrorBoundary';
import './shell.css';

const NAV = [
  { to: '/exams', label: 'Exams', icon: 'M5 4h14v16H5z M9 9h6 M9 13h6 M9 17h4' },
  { to: '/settings', label: 'Settings', icon: 'M12 15.5a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z M4 12h2 M18 12h2 M12 4v2 M12 18v2 M6.3 6.3l1.4 1.4 M16.3 16.3l1.4 1.4 M6.3 17.7l1.4-1.4 M16.3 7.7l1.4-1.4' },
];

export function AppShell() {
  const { session } = useAuth();
  const location = useLocation();
  const [collapsed, setCollapsed] = useState(() => window.matchMedia?.('(max-width: 899px)').matches ?? false);
  const navRef = useRef<HTMLElement>(null);
  const [indicator, setIndicator] = useState<{ top: number; height: number } | null>(null);

  // The selection indicator slides to the active item (CSS transition on transform).
  useLayoutEffect(() => {
    const active = navRef.current?.querySelector<HTMLElement>('a.active');
    setIndicator(active ? { top: active.offsetTop, height: active.offsetHeight } : null);
  }, [location.pathname, collapsed]);

  return (
    <div className={`shell${collapsed ? ' collapsed' : ''}`}>
      <aside className="sidebar">
        <div className="brand">
          <button type="button" className="btn btn-subtle icon" aria-label={collapsed ? 'Expand menu' : 'Collapse menu'}
            aria-expanded={!collapsed} onClick={() => setCollapsed(c => !c)}>
            <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true"><path d="M2 4h12M2 8h12M2 12h12" stroke="currentColor" strokeWidth="1.5" /></svg>
          </button>
          <span className="brand-name">SecuCode</span>
        </div>
        <nav ref={navRef} aria-label="Main" className="nav">
          {indicator && <span className="nav-indicator" aria-hidden="true" style={{ transform: `translateY(${indicator.top}px)`, height: indicator.height }} />}
          {NAV.map(n => (
            <NavLink key={n.to} to={n.to} className="nav-item" title={collapsed ? n.label : undefined} aria-label={collapsed ? n.label : undefined}>
              <svg className="nav-icon" width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
                <path d={n.icon} fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
              <span className="nav-label">{n.label}</span>
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-user nav-label">{session?.user.fullName}</div>
      </aside>
      <main className="content">
        <PageErrorBoundary key={location.pathname}>
          <div className="page" key={location.pathname}>
            <Outlet />
          </div>
        </PageErrorBoundary>
      </main>
    </div>
  );
}
```

`clients/web/src/shell/shell.css`:

```css
.shell { display: grid; grid-template-columns: 240px 1fr; min-height: 100vh; transition: grid-template-columns var(--dur-page) var(--ease-out); }
.shell.collapsed { grid-template-columns: 56px 1fr; }
.sidebar {
  position: sticky; top: 0; height: 100vh; display: flex; flex-direction: column; gap: 8px;
  padding: 8px; background: var(--surface-sunken); border-right: 1px solid var(--border); overflow: hidden;
}
.brand { display: flex; align-items: center; gap: 8px; min-height: 40px; }
.brand-name { font-weight: 600; font-size: 16px; white-space: nowrap; }
.nav { position: relative; display: grid; gap: 2px; }
.nav-item {
  position: relative; display: flex; align-items: center; gap: 12px; min-height: 36px; padding: 0 10px;
  border-radius: var(--radius); color: var(--text-primary); text-decoration: none; white-space: nowrap;
  transition: background-color var(--dur-fast);
}
.nav-item:hover { background: var(--subtle-hover); }
.nav-item.active { background: var(--subtle-hover); font-weight: 600; }
.nav-icon { flex: none; }
.nav-indicator {
  position: absolute; left: 0; top: 0; width: 3px; border-radius: 2px; background: var(--accent);
  transition: transform var(--dur-page) var(--ease-out), height var(--dur-page) var(--ease-out);
}
.sidebar-user { margin-top: auto; padding: 8px 10px; color: var(--text-secondary); font-size: var(--fs-caption); white-space: nowrap; }
.collapsed .nav-label, .collapsed .brand-name { display: none; }
.content { min-width: 0; padding: 32px; }
.page { max-width: 1200px; margin: 0 auto; display: grid; gap: 20px; animation: page-in var(--dur-page) var(--ease-out); }
.page-header { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
.page-header .title { flex: 1; }
.grid-2 { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }
.grid-3 { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; }

.login-page { display: grid; place-items: center; min-height: 100vh; padding: 16px; }
.login-card { width: min(380px, 100%); display: grid; gap: 16px; animation: pop-in var(--dur-pop) var(--ease-out); }

@media (max-width: 899px) {
  .content { padding: 16px; }
  .grid-2, .grid-3 { grid-template-columns: 1fr; }
}
```

`clients/web/src/features/login/LoginPage.tsx`:

```tsx
import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { Button, ErrorBar, InfoBar, TextField } from '../../ui';

export function LoginPage() {
  const { login, session, expired } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const state = location.state as { from?: string; expired?: boolean } | null;
  const from = state?.from && state.from !== '/login' ? state.from : '/exams';
  const [username, setUsername] = useState(session?.user.username ?? '');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  if (session && !expired) return <Navigate to={from} replace />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(username.trim(), password);
      navigate(from, { replace: true });
    } catch (x) {
      setError(x);
      setBusy(false);
    }
  }

  return (
    <div className="login-page">
      <form className="card login-card" onSubmit={submit}>
        <div>
          <h1 className="title">SecuCode</h1>
          <p className="secondary">Sign in to continue.</p>
        </div>
        {(state?.expired || expired) && error === null && <InfoBar kind="warning" title="Your session expired.">Please sign in again.</InfoBar>}
        {error !== null && <ErrorBar error={error} />}
        <TextField label="Username" autoComplete="username" value={username} onChange={e => setUsername(e.target.value)} required autoFocus />
        <TextField label="Password" type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required />
        <Button type="submit" variant="primary" busy={busy}>Sign in</Button>
      </form>
    </div>
  );
}
```

`clients/web/src/features/settings/SettingsPage.tsx`:

```tsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth, useSession } from '../../auth/AuthContext';
import { applyTheme, loadTheme, type ThemeChoice } from '../../theme/theme';
import { Button, Card } from '../../ui';

const CHOICES: { value: ThemeChoice; label: string }[] = [
  { value: 'system', label: 'Use system setting' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
];

export function SettingsPage() {
  const { logout } = useAuth();
  const session = useSession();
  const navigate = useNavigate();
  const [theme, setTheme] = useState(loadTheme);
  const [busy, setBusy] = useState(false);

  function choose(choice: ThemeChoice) {
    setTheme(choice);
    applyTheme(choice);
  }

  async function signOut() {
    setBusy(true);
    await logout();
    navigate('/login', { replace: true });
  }

  return (
    <>
      <h1 className="title">Settings</h1>
      <Card className="stack">
        <h2 className="subtitle">Appearance</h2>
        <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0, gap: 8 }}>
          <legend className="sr-only">Theme</legend>
          {CHOICES.map(c => (
            <label key={c.value} className="check">
              <input type="radio" name="theme" value={c.value} checked={theme === c.value} onChange={() => choose(c.value)} />
              <span>{c.label}</span>
            </label>
          ))}
        </fieldset>
      </Card>
      <Card className="stack">
        <h2 className="subtitle">Account</h2>
        <p>{session.user.fullName} <span className="secondary">({session.user.username}{session.roleNames && ` · ${session.roleNames}`})</span></p>
        <div><Button onClick={signOut} busy={busy}>Sign out</Button></div>
      </Card>
    </>
  );
}
```

`clients/web/src/features/exams/ExamsHome.tsx`:

```tsx
// The /exams page. Task 7 fills in the staff list and Task 9 the student dashboard.
export function ExamsHome() {
  return <h1 className="title">Exams</h1>;
}
```

`clients/web/src/App.tsx`:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { createBrowserRouter, Navigate, RouterProvider, type RouteObject } from 'react-router-dom';
import { ApiClient, ApiError } from './api/client';
import { AuthProvider, RequireAuth } from './auth/AuthContext';
import { ExamsHome } from './features/exams/ExamsHome';
import { LoginPage } from './features/login/LoginPage';
import { SettingsPage } from './features/settings/SettingsPage';
import { AppShell } from './shell/AppShell';

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginPage /> },
  // Full-screen routes without the shell (the student exam page, Task 13) go here.
  {
    element: <RequireAuth><AppShell /></RequireAuth>,
    children: [
      { path: '/', element: <Navigate to="/exams" replace /> },
      { path: '/exams', element: <ExamsHome /> },
      // Staff routes (Tasks 7–8) and student routes (Task 12) go here.
      { path: '/settings', element: <SettingsPage /> },
      { path: '*', element: <Navigate to="/exams" replace /> },
    ],
  },
];

const api = new ApiClient();
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      // Retry connection and server failures, not refusals (4xx).
      retry: (count, error) => count < 2 && !(error instanceof ApiError && error.status >= 400 && error.status < 500),
    },
  },
});

export function App() {
  const [router] = useState(() => createBrowserRouter(routes));
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider api={api}>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  );
}
```

Replace `clients/web/src/main.tsx`:

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import './theme/tokens.css';
import './theme/controls.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
```

- [ ] **Step 4: Run the tests and build**

Run (in `clients/web`): `npm test; npm run build`
Expected: tests PASS; build succeeds.

- [ ] **Step 5: Check it in a browser**

Start the API (repo root, separate terminal):

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"; $env:ASPNETCORE_URLS="http://localhost:8080"
dotnet run --project server/Secucode.Api
```

Then (in `clients/web`) `npm run dev`, open `http://localhost:5173`, sign in as `hod.cse / Hod@123`. Check: the sidebar shows Exams and Settings with the indicator beside the current page and sliding when you switch; switching theme on Settings changes colours at once and survives a reload; collapsing the sidebar works; Sign out returns to sign-in; a wrong password shows the server's error. Stop the dev server.

- [ ] **Step 6: Commit**

```powershell
git add clients/web/src/App.tsx clients/web/src/main.tsx clients/web/src/shell/AppShell.tsx clients/web/src/shell/PageErrorBoundary.tsx clients/web/src/shell/shell.css clients/web/src/shell/shell.test.tsx clients/web/src/features/login/LoginPage.tsx clients/web/src/features/settings/SettingsPage.tsx clients/web/src/features/exams/ExamsHome.tsx clients/web/src/lib/format.ts
git commit -m "feat(web): app shell with sliding sidebar, sign-in, settings and routing" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 7: Staff exam list and editor

**Files:**
- Create: `clients/web/src/features/staff/examForm.ts`
- Test: `clients/web/src/features/staff/examForm.test.ts`
- Create: `clients/web/src/features/staff/ExamListPage.tsx`
- Create: `clients/web/src/features/staff/ExamEditorPage.tsx`
- Modify: `clients/web/src/features/exams/ExamsHome.tsx`, `clients/web/src/App.tsx` (routes), `clients/web/src/shell/shell.css`

**Interfaces:**
- Consumes: `useAuth`, `useSession`, `StaffOnly` (Task 4); UI kit, `Dialog` (Task 5); `formatWindow`, `modeLabel` (Task 6); types (Task 3).
- Produces:
  - `examForm.ts`: `interface QuestionDraft { key: string; id: number | null; text: string }`, `interface ExamForm`, `type FormErrors`, `toLocalInput(iso): string`, `fromLocalInput(value): string`, `newQuestion(text?, id?)`, `emptyForm(now?)`, `formFromExam(exam)`, `validate(form): FormErrors`, `toRequest(form): ExamRequest`, `move<T>(list, index, delta): T[]`, `interface DateRange { from; to }`, `dateRangeDefaults(now?)`, `rangeQuery(range): string`.
  - Routes `/exams/new` and `/exams/:id/edit` (staff only). `StaffExamList` component. Buttons named `New exam`, `Edit`, `Attempts`, `Activate`/`Deactivate`, `Delete`, `Save exam`; question fields labelled `Question 1`, `Question 2`…

- [ ] **Step 1: Write the failing tests**

`clients/web/src/features/staff/examForm.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import type { ExamDto } from '../../api/types';
import { emptyForm, formFromExam, fromLocalInput, move, newQuestion, rangeQuery, toLocalInput, toRequest, validate } from './examForm';

const exam: ExamDto = {
  id: 3, title: 'Arrays', startsAt: '2026-11-02T04:30:00.000Z', endsAt: '2026-11-02T06:30:00.000Z', durationMinutes: 45,
  mode: 'WebAllowed', isActive: false, branchId: 1, classId: 2, batchId: null, createdBy: 5, createdByName: 'P',
  allowQuestionEditAfterAttempts: false, allowAttemptReset: true, allowAttemptReopen: false, allowTimeExtension: true,
  questions: [{ id: 11, text: 'Second', sortOrder: 1 }, { id: 10, text: 'First', sortOrder: 0 }],
};

describe('examForm', () => {
  it('round-trips local input and UTC', () => {
    expect(fromLocalInput(toLocalInput('2026-11-02T04:30:00.000Z'))).toBe('2026-11-02T04:30:00.000Z');
    expect(toLocalInput('2026-11-02T04:30:00.000Z')).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
  });

  it('loads an exam with its questions in order', () => {
    const f = formFromExam(exam);
    expect(f.questions.map(q => [q.id, q.text])).toEqual([[10, 'First'], [11, 'Second']]);
    expect(f).toMatchObject({ title: 'Arrays', durationMinutes: '45', mode: 'WebAllowed', isActive: false, branchId: '1', classId: '2', batchId: '' });
  });

  it('builds the request: numbers, nulls, UTC times, blank questions dropped', () => {
    const f = { ...formFromExam(exam), questions: [newQuestion('  First  ', 10), newQuestion('   '), newQuestion('New')] };
    expect(toRequest(f)).toEqual({
      title: 'Arrays', startsAt: '2026-11-02T04:30:00.000Z', endsAt: '2026-11-02T06:30:00.000Z', durationMinutes: 45,
      mode: 'WebAllowed', isActive: false, branchId: 1, classId: 2, batchId: null,
      allowQuestionEditAfterAttempts: false, allowAttemptReset: true, allowAttemptReopen: false, allowTimeExtension: true,
      questions: [{ id: 10, text: 'First' }, { id: null, text: 'New' }],
    });
  });

  it('explains what is missing', () => {
    const f = { ...emptyForm(), durationMinutes: '0', questions: [newQuestion(' ')] };
    f.endsAt = f.startsAt;
    expect(validate(f)).toEqual({
      title: 'Enter a title.',
      endsAt: 'The end must be after the start.',
      durationMinutes: 'Enter a whole number of minutes above 0.',
      branchId: 'Choose a department.',
      questions: 'Add at least one question.',
    });
    expect(validate(formFromExam(exam))).toEqual({});
  });

  it('moves items within bounds only', () => {
    expect(move(['a', 'b', 'c'], 0, 1)).toEqual(['b', 'a', 'c']);
    expect(move(['a', 'b', 'c'], 2, -1)).toEqual(['a', 'c', 'b']);
    const same = ['a'];
    expect(move(same, 0, -1)).toBe(same);
  });

  it('turns a date range into whole local days in UTC', () => {
    const q = new URLSearchParams(rangeQuery({ from: '2026-11-01', to: '2026-11-03' }));
    expect(new Date(q.get('from')!).getTime()).toBe(new Date('2026-11-01T00:00').getTime());
    expect(new Date(q.get('to')!).getTime()).toBe(new Date('2026-11-03T23:59:59.999').getTime());
  });
});
```

Run (in `clients/web`): `npm test`
Expected: FAIL — `./examForm` not found.

- [ ] **Step 2: Implement the form logic**

`clients/web/src/features/staff/examForm.ts`:

```ts
import type { ExamDto, ExamMode, ExamRequest } from '../../api/types';

export interface QuestionDraft { key: string; id: number | null; text: string }

// Form state is strings (what the inputs hold); toRequest converts once, on save.
export interface ExamForm {
  title: string;
  startsAt: string; // datetime-local value, local time
  endsAt: string;
  durationMinutes: string;
  mode: ExamMode;
  isActive: boolean;
  branchId: string;
  classId: string;
  batchId: string;
  allowQuestionEditAfterAttempts: boolean;
  allowAttemptReset: boolean;
  allowAttemptReopen: boolean;
  allowTimeExtension: boolean;
  questions: QuestionDraft[];
}

export type FormErrors = Partial<Record<keyof ExamForm, string>>;

const pad = (n: number) => String(n).padStart(2, '0');

export function toLocalInput(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

// "2026-11-02T10:00" without an offset is local time by the JS spec.
export const fromLocalInput = (value: string) => new Date(value).toISOString();

let nextKey = 0;
export const newQuestion = (text = '', id: number | null = null): QuestionDraft => ({ key: `q${nextKey++}`, id, text });

export function emptyForm(now = new Date()): ExamForm {
  const start = new Date(now);
  start.setMinutes(0, 0, 0);
  start.setHours(start.getHours() + 1);
  const end = new Date(start.getTime() + 2 * 3_600_000);
  return {
    title: '', startsAt: toLocalInput(start.toISOString()), endsAt: toLocalInput(end.toISOString()), durationMinutes: '60',
    mode: 'DesktopOnly', isActive: true, branchId: '', classId: '', batchId: '',
    allowQuestionEditAfterAttempts: true, allowAttemptReset: true, allowAttemptReopen: true, allowTimeExtension: true,
    questions: [newQuestion()],
  };
}

const idText = (v: number | null) => (v === null ? '' : String(v));

export function formFromExam(e: ExamDto): ExamForm {
  return {
    title: e.title, startsAt: toLocalInput(e.startsAt), endsAt: toLocalInput(e.endsAt), durationMinutes: String(e.durationMinutes),
    mode: e.mode, isActive: e.isActive, branchId: String(e.branchId), classId: idText(e.classId), batchId: idText(e.batchId),
    allowQuestionEditAfterAttempts: e.allowQuestionEditAfterAttempts, allowAttemptReset: e.allowAttemptReset,
    allowAttemptReopen: e.allowAttemptReopen, allowTimeExtension: e.allowTimeExtension,
    questions: [...(e.questions ?? [])].sort((a, b) => a.sortOrder - b.sortOrder).map(q => newQuestion(q.text, q.id)),
  };
}

export function validate(f: ExamForm): FormErrors {
  const errors: FormErrors = {};
  if (!f.title.trim()) errors.title = 'Enter a title.';
  if (!f.startsAt || !f.endsAt) errors.endsAt = 'Enter the start and end.';
  else if (new Date(f.endsAt) <= new Date(f.startsAt)) errors.endsAt = 'The end must be after the start.';
  const minutes = Number(f.durationMinutes);
  if (!Number.isInteger(minutes) || minutes <= 0) errors.durationMinutes = 'Enter a whole number of minutes above 0.';
  if (!f.branchId) errors.branchId = 'Choose a department.';
  if (!f.questions.some(q => q.text.trim())) errors.questions = 'Add at least one question.';
  return errors;
}

const optionalId = (v: string) => (v ? Number(v) : null);

export function toRequest(f: ExamForm): ExamRequest {
  return {
    title: f.title.trim(), startsAt: fromLocalInput(f.startsAt), endsAt: fromLocalInput(f.endsAt),
    durationMinutes: Number(f.durationMinutes), mode: f.mode, isActive: f.isActive,
    branchId: Number(f.branchId), classId: optionalId(f.classId), batchId: optionalId(f.batchId),
    allowQuestionEditAfterAttempts: f.allowQuestionEditAfterAttempts, allowAttemptReset: f.allowAttemptReset,
    allowAttemptReopen: f.allowAttemptReopen, allowTimeExtension: f.allowTimeExtension,
    questions: f.questions.filter(q => q.text.trim()).map(q => ({ id: q.id, text: q.text.trim() })),
  };
}

export function move<T>(list: T[], index: number, delta: -1 | 1): T[] {
  const to = index + delta;
  if (to < 0 || to >= list.length) return list;
  const copy = [...list];
  [copy[index], copy[to]] = [copy[to], copy[index]];
  return copy;
}

export interface DateRange { from: string; to: string } // yyyy-mm-dd, local

const day = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

export function dateRangeDefaults(now = new Date()): DateRange {
  const from = new Date(now);
  from.setDate(from.getDate() - 7);
  const to = new Date(now);
  to.setDate(to.getDate() + 30);
  return { from: day(from), to: day(to) };
}

export function rangeQuery(r: DateRange): string {
  return new URLSearchParams({
    from: new Date(`${r.from}T00:00`).toISOString(),
    to: new Date(`${r.to}T23:59:59.999`).toISOString(),
  }).toString();
}
```

Run (in `clients/web`): `npm test`
Expected: PASS.

- [ ] **Step 3: Implement the list page**

`clients/web/src/features/staff/ExamListPage.tsx`:

```tsx
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiError } from '../../api/client';
import type { ExamDto } from '../../api/types';
import { useAuth, useSession } from '../../auth/AuthContext';
import { formatWindow, modeLabel } from '../../lib/format';
import { Badge, Button, DataTable, ErrorBar, Spinner, TextField, type Column } from '../../ui';
import { Dialog } from '../../ui/Dialog';
import { dateRangeDefaults, rangeQuery } from './examForm';

export function StaffExamList() {
  const { api } = useAuth();
  const session = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [range, setRange] = useState(dateRangeDefaults);
  const [toDelete, setToDelete] = useState<ExamDto | null>(null);
  const [actionError, setActionError] = useState<unknown>(null);

  const exams = useQuery({ queryKey: ['exams', range], queryFn: () => api.get<ExamDto[]>(`/api/exams?${rangeQuery(range)}`) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['exams'] });

  const setActive = useMutation({
    mutationFn: (e: ExamDto) => api.patch(`/api/exams/${e.id}/active`, { isActive: !e.isActive }),
    onMutate: () => setActionError(null),
    onSuccess: refresh,
    onError: setActionError,
  });
  const remove = useMutation({
    mutationFn: (e: ExamDto) => api.del(`/api/exams/${e.id}`),
    onMutate: () => setActionError(null),
    onSuccess: refresh,
    onError: setActionError,
    onSettled: () => setToDelete(null),
  });

  const columns: Column<ExamDto>[] = [
    { header: 'Title', cell: e => <strong>{e.title}</strong> },
    { header: 'Window', cell: e => formatWindow(e.startsAt, e.endsAt) },
    { header: 'Duration', cell: e => `${e.durationMinutes} min` },
    { header: 'Mode', cell: e => <Badge tone={e.mode === 'WebAllowed' ? 'accent' : 'neutral'}>{modeLabel(e.mode)}</Badge> },
    { header: 'Status', cell: e => <Badge tone={e.isActive ? 'success' : 'neutral'}>{e.isActive ? 'Active' : 'Inactive'}</Badge> },
    { header: 'Created by', cell: e => e.createdByName },
    {
      header: 'Actions',
      cell: e => (
        <div className="row">
          {session.canViewResults(e) && <Button variant="subtle" onClick={() => navigate(`/exams/${e.id}/attempts`)}>Attempts</Button>}
          {session.canManage(e) && (
            <>
              <Button variant="subtle" onClick={() => navigate(`/exams/${e.id}/edit`)}>Edit</Button>
              <Button variant="subtle" busy={setActive.isPending && setActive.variables?.id === e.id} onClick={() => setActive.mutate(e)}>
                {e.isActive ? 'Deactivate' : 'Activate'}
              </Button>
              <Button variant="subtle" onClick={() => setToDelete(e)}>Delete</Button>
            </>
          )}
        </div>
      ),
    },
  ];

  const shownError = actionError instanceof ApiError && actionError.status === 409
    ? new ApiError(409, actionError.title, `${actionError.detail ?? ''} Deactivate the exam instead to hide it from students.`.trim())
    : actionError;

  return (
    <>
      <div className="page-header">
        <h1 className="title">Exams</h1>
        {session.canCreateExams && <Button variant="primary" onClick={() => navigate('/exams/new')}>New exam</Button>}
      </div>
      <div className="row" style={{ alignItems: 'end' }}>
        <TextField label="From" type="date" value={range.from} max={range.to} onChange={e => e.target.value && setRange(r => ({ ...r, from: e.target.value }))} />
        <TextField label="To" type="date" value={range.to} min={range.from} onChange={e => e.target.value && setRange(r => ({ ...r, to: e.target.value }))} />
        <Button onClick={() => exams.refetch()}>Refresh</Button>
      </div>
      {shownError !== null && <ErrorBar error={shownError} />}
      {exams.isPending ? <Spinner size={28} />
        : exams.isError ? <ErrorBar error={exams.error} onRetry={() => exams.refetch()} />
        : <DataTable caption="Exams" rows={exams.data} columns={columns} rowKey={e => e.id} empty="No exams in this date range." />}
      <Dialog open={toDelete !== null} title="Delete this exam?" onClose={() => setToDelete(null)}
        actions={<>
          <Button onClick={() => setToDelete(null)}>Cancel</Button>
          <Button variant="danger" busy={remove.isPending} onClick={() => toDelete && remove.mutate(toDelete)}>Delete</Button>
        </>}>
        <p>“{toDelete?.title}” will be deleted. Exams that students have attempted can't be deleted.</p>
      </Dialog>
    </>
  );
}
```

- [ ] **Step 4: Implement the editor page**

`clients/web/src/features/staff/ExamEditorPage.tsx`:

```tsx
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useBlocker, useNavigate, useParams } from 'react-router-dom';
import type { ExamDto, ExamMode, MasterDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { Button, Card, ErrorBar, Select, Spinner, TextArea, TextField, Toggle } from '../../ui';
import { Dialog } from '../../ui/Dialog';
import { emptyForm, formFromExam, move, newQuestion, toRequest, validate, type ExamForm, type FormErrors } from './examForm';

export function ExamEditorPage() {
  const { id } = useParams();
  const editing = id !== undefined;
  const { api } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const masters = (path: string) => ({ queryKey: [path], queryFn: () => api.get<MasterDto[]>(`/api/${path}`), staleTime: 300_000 });
  const branches = useQuery(masters('branches'));
  const classes = useQuery(masters('classes'));
  const batches = useQuery(masters('batches'));
  const exam = useQuery({ queryKey: ['exam', id], queryFn: () => api.get<ExamDto>(`/api/exams/${id}`), enabled: editing });

  const [form, setForm] = useState<ExamForm | null>(() => (editing ? null : emptyForm()));
  const [errors, setErrors] = useState<FormErrors>({});
  const [saveError, setSaveError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const dirty = useRef(false);

  useEffect(() => {
    if (exam.data && form === null) setForm(formFromExam(exam.data));
  }, [exam.data, form]);

  // Leaving with unsaved changes: in-app navigation asks; closing the tab gets the browser's own prompt.
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty.current && currentLocation.pathname !== nextLocation.pathname);
  useEffect(() => {
    const onUnload = (e: BeforeUnloadEvent) => { if (dirty.current) e.preventDefault(); };
    addEventListener('beforeunload', onUnload);
    return () => removeEventListener('beforeunload', onUnload);
  }, []);

  const update = (patch: Partial<ExamForm>) => {
    dirty.current = true;
    setForm(f => (f ? { ...f, ...patch } : f));
  };

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!form) return;
    const found = validate(form);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    setSaving(true);
    setSaveError(null);
    try {
      const body = toRequest(form);
      if (editing) await api.put(`/api/exams/${id}`, body);
      else await api.post('/api/exams', body);
      dirty.current = false;
      await queryClient.invalidateQueries({ queryKey: ['exams'] });
      await queryClient.invalidateQueries({ queryKey: ['exam', id] });
      navigate('/exams');
    } catch (x) {
      setSaveError(x);
      setSaving(false);
    }
  }

  const loadError = exam.error ?? branches.error ?? classes.error ?? batches.error;
  if (loadError)
    return <ErrorBar error={loadError} onRetry={() => { void exam.refetch(); void branches.refetch(); void classes.refetch(); void batches.refetch(); }} />;
  if (!form || !branches.data || !classes.data || !batches.data) return <Spinner size={28} />;

  const options = (list: MasterDto[]) => list.map(m => <option key={m.id} value={m.id}>{m.name}</option>);
  const setQuestions = (questions: ExamForm['questions']) => update({ questions });

  return (
    <form className="stack" onSubmit={save} noValidate>
      <div className="page-header">
        <h1 className="title">{editing ? 'Edit exam' : 'New exam'}</h1>
        <Button onClick={() => navigate('/exams')}>Cancel</Button>
        <Button type="submit" variant="primary" busy={saving}>Save exam</Button>
      </div>
      {saveError !== null && <ErrorBar error={saveError} />}

      <Card className="stack">
        <TextField label="Title" value={form.title} error={errors.title} onChange={e => update({ title: e.target.value })} />
        <div className="grid-2">
          <TextField label="Starts" type="datetime-local" value={form.startsAt} onChange={e => update({ startsAt: e.target.value })} hint="Your local time." />
          <TextField label="Ends" type="datetime-local" value={form.endsAt} error={errors.endsAt} onChange={e => update({ endsAt: e.target.value })} />
          <TextField label="Duration (minutes)" type="number" min={1} inputMode="numeric" value={form.durationMinutes} error={errors.durationMinutes}
            onChange={e => update({ durationMinutes: e.target.value })} />
          <Select label="Mode" value={form.mode} onChange={e => update({ mode: e.target.value as ExamMode })}>
            <option value="DesktopOnly">Desktop only (strict proctoring)</option>
            <option value="WebAllowed">Web allowed</option>
          </Select>
        </div>
      </Card>

      <Card className="stack">
        <h2 className="subtitle">Who takes it</h2>
        <div className="grid-3">
          <Select label="Department" value={form.branchId} error={errors.branchId} onChange={e => update({ branchId: e.target.value })}>
            <option value="">Choose…</option>{options(branches.data)}
          </Select>
          <Select label="Year (optional)" value={form.classId} onChange={e => update({ classId: e.target.value })}>
            <option value="">Whole department</option>{options(classes.data)}
          </Select>
          <Select label="Batch (optional)" value={form.batchId} onChange={e => update({ batchId: e.target.value })}>
            <option value="">Whole year</option>{options(batches.data)}
          </Select>
        </div>
      </Card>

      <Card className="stack">
        <h2 className="subtitle">Settings</h2>
        <Toggle label="Active (students can see it)" checked={form.isActive} onChange={v => update({ isActive: v })} />
        <Toggle label="Allow editing questions after attempts" checked={form.allowQuestionEditAfterAttempts} onChange={v => update({ allowQuestionEditAfterAttempts: v })} />
        <Toggle label="Allow resetting attempts" checked={form.allowAttemptReset} onChange={v => update({ allowAttemptReset: v })} />
        <Toggle label="Allow reopening attempts" checked={form.allowAttemptReopen} onChange={v => update({ allowAttemptReopen: v })} />
        <Toggle label="Allow time extensions" checked={form.allowTimeExtension} onChange={v => update({ allowTimeExtension: v })} />
      </Card>

      <Card className="stack">
        <h2 className="subtitle">Questions</h2>
        <p className="secondary">Each student gets one of these at random.</p>
        {errors.questions && <p role="alert" style={{ color: 'var(--danger)' }}>{errors.questions}</p>}
        {form.questions.map((q, i) => (
          <div key={q.key} className="question-row">
            <TextArea label={`Question ${i + 1}`} value={q.text} rows={3}
              onChange={e => setQuestions(form.questions.map(x => (x.key === q.key ? { ...x, text: e.target.value } : x)))} />
            <div className="row">
              <Button variant="subtle" aria-label={`Move question ${i + 1} up`} disabled={i === 0} onClick={() => setQuestions(move(form.questions, i, -1))}>↑</Button>
              <Button variant="subtle" aria-label={`Move question ${i + 1} down`} disabled={i === form.questions.length - 1} onClick={() => setQuestions(move(form.questions, i, 1))}>↓</Button>
              <Button variant="subtle" aria-label={`Remove question ${i + 1}`} disabled={form.questions.length === 1}
                onClick={() => setQuestions(form.questions.filter(x => x.key !== q.key))}>Remove</Button>
            </div>
          </div>
        ))}
        <div><Button onClick={() => setQuestions([...form.questions, newQuestion()])}>Add question</Button></div>
      </Card>

      <Dialog open={blocker.state === 'blocked'} title="Leave without saving?" onClose={() => blocker.reset?.()}
        actions={<>
          <Button onClick={() => blocker.reset?.()}>Stay</Button>
          <Button variant="danger" onClick={() => blocker.proceed?.()}>Leave</Button>
        </>}>
        <p>Your changes to this exam will be lost.</p>
      </Dialog>
    </form>
  );
}
```

Append to `clients/web/src/shell/shell.css`:

```css
.question-row { display: grid; grid-template-columns: 1fr auto; gap: 8px; align-items: end; }
@media (max-width: 899px) { .question-row { grid-template-columns: 1fr; } }
```

- [ ] **Step 5: Wire the routes**

Replace `clients/web/src/features/exams/ExamsHome.tsx`:

```tsx
import { useSession } from '../../auth/AuthContext';
import { StaffExamList } from '../staff/ExamListPage';

// Task 9 adds the student dashboard branch.
export function ExamsHome() {
  return useSession().isStudent ? <h1 className="title">Exams</h1> : <StaffExamList />;
}
```

In `clients/web/src/App.tsx`, import `StaffOnly` from `./auth/AuthContext` and `ExamEditorPage` from `./features/staff/ExamEditorPage`, and add under the `// Staff routes` comment:

```tsx
      { path: '/exams/new', element: <StaffOnly><ExamEditorPage /></StaffOnly> },
      { path: '/exams/:id/edit', element: <StaffOnly><ExamEditorPage /></StaffOnly> },
```

- [ ] **Step 6: Test, build, check in a browser**

Run (in `clients/web`): `npm test; npm run build`
Expected: PASS; build succeeds.

With the API and `npm run dev` running, sign in as `faculty.cse / Faculty@123`: create an exam (local start/end, Web allowed, Computer Engineering, two questions, reorder them), save, see it in the list; edit it and check the times and question order came back unchanged; deactivate/activate; try leaving the editor with unsaved changes (dialog appears); delete an unattempted exam (confirm dialog). Sign in as `hod.cse` and check the seeded exam's buttons match their permissions.

- [ ] **Step 7: Commit**

```powershell
git add clients/web/src/features/staff/examForm.ts clients/web/src/features/staff/examForm.test.ts clients/web/src/features/staff/ExamListPage.tsx clients/web/src/features/staff/ExamEditorPage.tsx clients/web/src/features/exams/ExamsHome.tsx clients/web/src/App.tsx clients/web/src/shell/shell.css
git commit -m "feat(web): staff exam list and editor with local-time entry and question ordering" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 8: Code editor component, staff attempts and evidence

**Files:**
- Create: `clients/web/src/ui/CodeEditor.tsx`
- Test: `clients/web/src/ui/CodeEditor.test.tsx`
- Create: `clients/web/src/features/staff/AttemptListPage.tsx`, `AttemptDetailPage.tsx`, `EvidenceView.tsx`
- Modify: `clients/web/src/App.tsx` (routes), `clients/web/src/shell/shell.css`

**Interfaces:**
- Consumes: types, `useAuth`, UI kit, `format.ts`.
- Produces:
  - `CodeEditor({ value: string; language: Language; onChange?(code: string): void; readOnly?: boolean; locked?: boolean; label: string })` — `locked` blocks paste, copy, cut, drop and the context menu inside the editor. The editable area is `.cm-content` with `aria-label={label}`.
  - Routes `/exams/:id/attempts` and `/attempts/:id` (staff only). Attempt list links each student name to `/attempts/:id`. Evidence images have `alt` starting with the evidence kind (e.g. `Webcam · …`).

- [ ] **Step 1: Write the failing editor test**

`clients/web/src/ui/CodeEditor.test.tsx`:

```tsx
import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { CodeEditor } from './CodeEditor';

const fire = (target: Element, type: string) => {
  const e = new Event(type, { bubbles: true, cancelable: true });
  target.dispatchEvent(e);
  return e.defaultPrevented;
};

describe('CodeEditor', () => {
  it('shows the code and labels the editable area', () => {
    const { container } = render(<CodeEditor value={'print(1)'} language="Python" label="Your code" />);
    const content = container.querySelector('.cm-content')!;
    expect(content.textContent).toContain('print(1)');
    expect(content.getAttribute('aria-label')).toBe('Your code');
  });

  it('blocks clipboard, drop and the context menu when locked', () => {
    const { container } = render(<CodeEditor value="" language="Python" label="Code" locked />);
    const content = container.querySelector('.cm-content')!;
    for (const type of ['paste', 'copy', 'cut', 'drop', 'contextmenu']) expect(fire(content, type)).toBe(true);
  });

  it('leaves the context menu alone when not locked', () => {
    const { container } = render(<CodeEditor value="" language="Python" label="Code" />);
    expect(fire(container.querySelector('.cm-content')!, 'contextmenu')).toBe(false);
  });
});
```

Run (in `clients/web`): `npm test`
Expected: FAIL — `./CodeEditor` not found.

- [ ] **Step 2: Implement the editor**

`clients/web/src/ui/CodeEditor.tsx`:

```tsx
import { cpp } from '@codemirror/lang-cpp';
import { java } from '@codemirror/lang-java';
import { javascript } from '@codemirror/lang-javascript';
import { python } from '@codemirror/lang-python';
import { Compartment, EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import { basicSetup } from 'codemirror';
import { useEffect, useRef } from 'react';
import type { Language } from '../api/types';

const languageSupport = (l: Language) =>
  l === 'Python' ? python() : l === 'JavaScript' ? javascript() : l === 'Java' ? java() : cpp();

const stop = (e: Event) => { e.preventDefault(); return true; };
const blockClipboard = EditorView.domEventHandlers({ paste: stop, copy: stop, cut: stop, drop: stop, contextmenu: stop });

const theme = EditorView.theme({
  '&': { height: '100%', backgroundColor: 'var(--surface-raised)', color: 'var(--text-primary)', fontSize: '14px' },
  '&.cm-focused': { outline: '2px solid var(--focus)', outlineOffset: '-2px' },
  '.cm-scroller': { fontFamily: 'var(--font-code)' },
  '.cm-content': { caretColor: 'var(--text-primary)' },
  '.cm-gutters': { backgroundColor: 'var(--surface-sunken)', color: 'var(--text-secondary)', borderRight: '1px solid var(--border)' },
  '.cm-activeLine, .cm-activeLineGutter': { backgroundColor: 'var(--subtle-hover)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': { backgroundColor: 'var(--accent-soft)' },
});

export function CodeEditor({ value, language, onChange, readOnly = false, locked = false, label }:
  { value: string; language: Language; onChange?(code: string): void; readOnly?: boolean; locked?: boolean; label: string }) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const languageSlot = useRef(new Compartment());
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  // Created once; value and language changes are pushed in by the effects below.
  useEffect(() => {
    const v = new EditorView({
      parent: host.current!,
      state: EditorState.create({
        doc: value,
        extensions: [
          basicSetup,
          theme,
          languageSlot.current.of(languageSupport(language)),
          EditorState.readOnly.of(readOnly),
          EditorView.editable.of(!readOnly),
          EditorView.contentAttributes.of({ 'aria-label': label }),
          locked ? blockClipboard : [],
          EditorView.updateListener.of(u => { if (u.docChanged) onChangeRef.current?.(u.state.doc.toString()); }),
        ],
      }),
    });
    view.current = v;
    return () => { v.destroy(); view.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    view.current?.dispatch({ effects: languageSlot.current.reconfigure(languageSupport(language)) });
  }, [language]);

  useEffect(() => {
    const v = view.current;
    if (v && value !== v.state.doc.toString()) v.dispatch({ changes: { from: 0, to: v.state.doc.length, insert: value } });
  }, [value]);

  return <div className="code-editor" ref={host} />;
}
```

Run (in `clients/web`): `npm test`
Expected: PASS. (If jsdom lacks something CodeMirror calls, e.g. `Range.prototype.getClientRects`, add the smallest stub to `src/test/setup.ts`, such as `Range.prototype.getClientRects = () => [] as unknown as DOMRectList;`, and say so in your report.)

- [ ] **Step 3: Implement the attempt pages**

`clients/web/src/features/staff/EvidenceView.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';
import { ApiError } from '../../api/client';
import type { EvidenceDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { formatDateTime } from '../../lib/format';
import { Spinner } from '../../ui';

// Evidence needs the bearer token, so it's fetched as a blob and shown through an object URL.
export function EvidenceView({ evidence }: { evidence: EvidenceDto }) {
  const { api } = useAuth();
  const file = useQuery({ queryKey: ['evidence', evidence.id], queryFn: () => api.get<Blob>(`/api/evidence/${evidence.id}`), staleTime: Infinity });
  const url = useMemo(() => (file.data ? URL.createObjectURL(file.data) : null), [file.data]);
  useEffect(() => () => { if (url) URL.revokeObjectURL(url); }, [url]);

  const caption = `${evidence.kind} · ${formatDateTime(evidence.createdAt)}`;
  let body;
  if (file.isPending) body = <Spinner />;
  else if (file.isError)
    body = <p className="secondary">{file.error instanceof ApiError && file.error.status === 404 ? "You can't view this file." : 'Could not load this file.'}</p>;
  else if (evidence.contentType.startsWith('audio/')) body = <audio controls src={url!} aria-label={caption} />;
  else body = <img src={url!} alt={caption} />;

  return (
    <figure className="evidence" id={`evidence-${evidence.id}`}>
      {body}
      <figcaption className="caption secondary">{caption}</figcaption>
    </figure>
  );
}
```

`clients/web/src/features/staff/AttemptListPage.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router-dom';
import type { AttemptSummaryDto, ExamDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { formatDateTime, statusLabel } from '../../lib/format';
import { Badge, Button, DataTable, ErrorBar, Spinner, type Column } from '../../ui';

export function AttemptListPage() {
  const { id } = useParams();
  const { api } = useAuth();
  const navigate = useNavigate();
  const exam = useQuery({ queryKey: ['exam', id], queryFn: () => api.get<ExamDto>(`/api/exams/${id}`) });
  const attempts = useQuery({ queryKey: ['attempts', id], queryFn: () => api.get<AttemptSummaryDto[]>(`/api/exams/${id}/attempts`) });

  const columns: Column<AttemptSummaryDto>[] = [
    { header: 'Student', cell: a => <Link to={`/attempts/${a.id}`}>{a.studentName}</Link> },
    { header: 'Attempt', cell: a => a.attemptNumber },
    { header: 'Status', cell: a => <Badge tone={a.status === 'InProgress' ? 'accent' : a.status === 'Voided' ? 'warning' : 'success'}>{statusLabel(a.status)}</Badge> },
    { header: 'App', cell: a => a.client },
    { header: 'Started', cell: a => formatDateTime(a.startedAt) },
    { header: 'Submitted', cell: a => (a.submittedAt ? formatDateTime(a.submittedAt) : '—') },
    { header: 'Strikes', cell: a => (a.strikes > 0 ? <Badge tone="danger">{a.strikes}</Badge> : 0) },
  ];

  return (
    <>
      <div className="page-header">
        <h1 className="title">{exam.data ? `Attempts · ${exam.data.title}` : 'Attempts'}</h1>
        <Button onClick={() => attempts.refetch()}>Refresh</Button>
        <Button onClick={() => navigate('/exams')}>Back to exams</Button>
      </div>
      {attempts.isPending ? <Spinner size={28} />
        : attempts.isError ? <ErrorBar error={attempts.error} onRetry={() => attempts.refetch()} />
        : <DataTable caption="Attempts" rows={attempts.data} columns={columns} rowKey={a => a.id} empty="Nobody has started this exam yet." />}
    </>
  );
}
```

`clients/web/src/features/staff/AttemptDetailPage.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { isLanguage, type AttemptDetailDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { eventLabel, formatDateTime, statusLabel } from '../../lib/format';
import { Badge, Button, Card, DataTable, ErrorBar, Spinner } from '../../ui';
import { CodeEditor } from '../../ui/CodeEditor';
import { EvidenceView } from './EvidenceView';

export function AttemptDetailPage() {
  const { id } = useParams();
  const { api } = useAuth();
  const navigate = useNavigate();
  const detail = useQuery({ queryKey: ['attempt', id], queryFn: () => api.get<AttemptDetailDto>(`/api/attempts/${id}`) });

  if (detail.isPending) return <Spinner size={28} />;
  if (detail.isError) return <ErrorBar error={detail.error} onRetry={() => detail.refetch()} />;
  const { attempt, studentName, events, evidence } = detail.data;
  const language = isLanguage(attempt.language) ? attempt.language : 'Python';

  return (
    <>
      <div className="page-header">
        <h1 className="title">{studentName}</h1>
        <Badge tone={attempt.status === 'InProgress' ? 'accent' : 'neutral'}>{statusLabel(attempt.status)}</Badge>
        <Button onClick={() => navigate(`/exams/${attempt.examId}/attempts`)}>Back to attempts</Button>
      </div>
      <Card className="stack">
        <p className="secondary">
          Attempt {attempt.attemptNumber} · {attempt.client} · started {formatDateTime(attempt.startedAt)}
          {attempt.submittedAt && ` · submitted ${formatDateTime(attempt.submittedAt)}`} · {attempt.strikes} strikes
        </p>
        {attempt.submitReason && <p><strong>Submit reason:</strong> {attempt.submitReason}</p>}
        <h2 className="subtitle">Question</h2>
        <p className="question-text">{attempt.questionText}</p>
      </Card>
      <Card className="stack">
        <h2 className="subtitle">Code ({attempt.language ?? 'not saved yet'})</h2>
        <div className="code-box"><CodeEditor value={attempt.code} language={language} readOnly label="Submitted code" /></div>
      </Card>
      <Card className="stack">
        <h2 className="subtitle">Proctoring events</h2>
        <DataTable caption="Proctoring events" rows={events} rowKey={e => e.id} empty="No events."
          columns={[
            { header: 'Time', cell: e => formatDateTime(e.occurredAt) },
            { header: 'Event', cell: e => eventLabel(e.type) },
            { header: 'Detail', cell: e => e.detail ?? '' },
            { header: 'Evidence', cell: e => (e.evidenceId ? <a href={`#evidence-${e.evidenceId}`}>View</a> : '') },
          ]} />
      </Card>
      <Card className="stack">
        <h2 className="subtitle">Evidence</h2>
        {evidence.length === 0 ? <p className="secondary">No evidence uploaded.</p>
          : <div className="evidence-grid">{evidence.map(ev => <EvidenceView key={ev.id} evidence={ev} />)}</div>}
      </Card>
    </>
  );
}
```

Append to `clients/web/src/shell/shell.css`:

```css
.code-editor { height: 100%; border: 1px solid var(--border); border-radius: var(--radius); overflow: hidden; }
.code-box { height: 360px; }
.question-text { white-space: pre-wrap; }
.evidence-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 16px; }
.evidence { margin: 0; display: grid; gap: 6px; }
.evidence img { width: 100%; border-radius: var(--radius); border: 1px solid var(--border); background: var(--surface-sunken); }
.evidence audio { width: 100%; }
```

In `clients/web/src/App.tsx` import both pages and add under the staff routes:

```tsx
      { path: '/exams/:id/attempts', element: <StaffOnly><AttemptListPage /></StaffOnly> },
      { path: '/attempts/:id', element: <StaffOnly><AttemptDetailPage /></StaffOnly> },
```

- [ ] **Step 4: Test, build, check**

Run (in `clients/web`): `npm test; npm run build`
Expected: PASS; build succeeds.

In the browser as `faculty.cse`: open Attempts on the seeded exam (empty-list message). If you have a desktop-app attempt from earlier testing, open it and check code, events and evidence images/audio render; otherwise the Task 14 e2e test covers it.

- [ ] **Step 5: Commit**

```powershell
git add clients/web/src/ui/CodeEditor.tsx clients/web/src/ui/CodeEditor.test.tsx clients/web/src/features/staff/AttemptListPage.tsx clients/web/src/features/staff/AttemptDetailPage.tsx clients/web/src/features/staff/EvidenceView.tsx clients/web/src/App.tsx clients/web/src/shell/shell.css
git commit -m "feat(web): CodeMirror editor and staff attempt pages with evidence" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 9: Student dashboard

**Files:**
- Create: `clients/web/src/features/student/studentExams.ts`
- Test: `clients/web/src/features/student/studentExams.test.ts`
- Create: `clients/web/src/features/student/DashboardPage.tsx`
- Create: `clients/web/src/proctoring/media.ts` (two functions now; Task 11 adds the rest)
- Modify: `clients/web/src/features/exams/ExamsHome.tsx`, `clients/web/src/shell/shell.css`

**Interfaces:**
- Consumes: `StudentExamDto`, `ApiError`, `useAuth`, UI kit, `format.ts`.
- Produces:
  - `type ExamAction = { kind: 'resume'; attemptId: number } | { kind: 'start' } | { kind: 'desktop' } | { kind: 'upcoming'; opensAt: number }`; `START_SLACK_MS`; `examAction(exam, now?): ExamAction`.
  - `inProgressId(error: unknown): number | null` — the attempt id from a 409 "attempt in progress (id N)" error.
  - `proctoring/media.ts`: `currentMedia(): MediaStream | null`, `closeMedia(): void` (module-level holder).
  - `StudentDashboard`. Each exam is an `<article>` card; buttons `Start` → `/exams/:id/start`, `Resume` → `/exams/:id/start?resume=:attemptId`; desktop-only exams show the text `Desktop app only`.

- [ ] **Step 1: Write the failing tests**

`clients/web/src/features/student/studentExams.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client';
import type { StudentExamDto } from '../../api/types';
import { examAction, inProgressId } from './studentExams';

const now = Date.parse('2026-11-02T10:00:00Z');
const exam = (over: Partial<StudentExamDto> = {}): StudentExamDto => ({
  id: 1, title: 'T', startsAt: '2026-11-02T10:00:00Z', endsAt: '2026-11-02T12:00:00Z', durationMinutes: 60,
  mode: 'WebAllowed', inProgressAttemptId: null, ...over,
});

describe('examAction', () => {
  it('sends desktop-only exams to the desktop app, even when in progress there', () => {
    expect(examAction(exam({ mode: 'DesktopOnly', inProgressAttemptId: 4 }), now)).toEqual({ kind: 'desktop' });
  });

  it('resumes an attempt in progress', () => {
    expect(examAction(exam({ inProgressAttemptId: 4 }), now)).toEqual({ kind: 'resume', attemptId: 4 });
  });

  it('offers Start from five minutes before the window opens', () => {
    expect(examAction(exam({ startsAt: '2026-11-02T10:05:00Z' }), now)).toEqual({ kind: 'start' });
    expect(examAction(exam({ startsAt: '2026-11-02T10:06:00Z' }), now)).toEqual({ kind: 'upcoming', opensAt: Date.parse('2026-11-02T10:06:00Z') });
  });

  it('offers Start after the window when the list still has the exam (a staff reset allowance)', () => {
    expect(examAction(exam({ endsAt: '2026-11-02T09:00:00Z' }), now)).toEqual({ kind: 'start' });
  });
});

describe('inProgressId', () => {
  it('reads the attempt id from a 409', () => {
    expect(inProgressId(new ApiError(409, 'Conflict', 'You already have an attempt in progress (id 42).'))).toBe(42);
    expect(inProgressId(new ApiError(409, 'Conflict', 'You have already submitted this exam.'))).toBeNull();
    expect(inProgressId(new ApiError(403, 'Forbidden', 'in progress (id 1)'))).toBeNull();
    expect(inProgressId(new Error('x'))).toBeNull();
  });
});
```

Run (in `clients/web`): `npm test`
Expected: FAIL — `./studentExams` not found.

- [ ] **Step 2: Implement**

`clients/web/src/features/student/studentExams.ts`:

```ts
import { ApiError } from '../../api/client';
import type { StudentExamDto } from '../../api/types';

export type ExamAction =
  | { kind: 'resume'; attemptId: number }
  | { kind: 'start' }
  | { kind: 'desktop' }
  | { kind: 'upcoming'; opensAt: number };

// The PC clock may be a little off; the server makes the final call when Start is pressed.
export const START_SLACK_MS = 5 * 60_000;

export function examAction(e: StudentExamDto, now = Date.now()): ExamAction {
  if (e.mode === 'DesktopOnly') return { kind: 'desktop' };
  if (e.inProgressAttemptId !== null) return { kind: 'resume', attemptId: e.inProgressAttemptId };
  const opensAt = Date.parse(e.startsAt);
  if (now < opensAt - START_SLACK_MS) return { kind: 'upcoming', opensAt };
  // After the window the API lists an exam only when staff allowed a restart, so Start stays on.
  return { kind: 'start' };
}

export function inProgressId(error: unknown): number | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null;
  const m = /\(id (\d+)\)/.exec(error.detail ?? '');
  return m ? Number(m[1]) : null;
}
```

`clients/web/src/proctoring/media.ts`:

```ts
// The exam's webcam/microphone stream, shared by the start page and the exam page (one permission prompt).
let current: MediaStream | null = null;

export function currentMedia(): MediaStream | null {
  return current;
}

export function closeMedia(): void {
  current?.getTracks().forEach(t => t.stop());
  current = null;
}
```

`clients/web/src/features/student/DashboardPage.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import type { StudentExamDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { formatDateTime, formatWindow } from '../../lib/format';
import { closeMedia } from '../../proctoring/media';
import { Badge, Button, Card, ErrorBar, Spinner } from '../../ui';
import { examAction } from './studentExams';

export function StudentDashboard() {
  const { api } = useAuth();
  const navigate = useNavigate();
  const exams = useQuery({
    queryKey: ['student-exams'],
    queryFn: () => api.get<StudentExamDto[]>('/api/exams'),
    refetchInterval: 30_000,
  });

  useEffect(() => closeMedia(), []); // back from the start page: turn the camera off

  return (
    <>
      <div className="page-header">
        <h1 className="title">Exams</h1>
        <Button onClick={() => exams.refetch()}>Refresh</Button>
      </div>
      {exams.isPending ? <Spinner size={28} />
        : exams.isError ? <ErrorBar error={exams.error} onRetry={() => exams.refetch()} />
        : exams.data.length === 0 ? <p className="empty">You have no exams open now or coming up.</p>
        : (
          <div className="exam-cards">
            {exams.data.map(e => {
              const action = examAction(e);
              return (
                <Card as="article" key={e.id} className="exam-card">
                  <h2 className="subtitle">{e.title}</h2>
                  <p className="secondary">{formatWindow(e.startsAt, e.endsAt)} · {e.durationMinutes} min</p>
                  <div className="row">
                    {action.kind === 'desktop' && <Badge>Desktop app only</Badge>}
                    {action.kind === 'upcoming' && <span className="secondary">Opens {formatDateTime(new Date(action.opensAt).toISOString())}</span>}
                    {action.kind === 'start' && <Button variant="primary" onClick={() => navigate(`/exams/${e.id}/start`)}>Start</Button>}
                    {action.kind === 'resume' && (
                      <Button variant="primary" onClick={() => navigate(`/exams/${e.id}/start?resume=${action.attemptId}`)}>Resume</Button>
                    )}
                  </div>
                </Card>
              );
            })}
          </div>
        )}
    </>
  );
}
```

Replace `clients/web/src/features/exams/ExamsHome.tsx`:

```tsx
import { useSession } from '../../auth/AuthContext';
import { StaffExamList } from '../staff/ExamListPage';
import { StudentDashboard } from '../student/DashboardPage';

export function ExamsHome() {
  return useSession().isStudent ? <StudentDashboard /> : <StaffExamList />;
}
```

Append to `clients/web/src/shell/shell.css`:

```css
.exam-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 16px; }
.exam-card { display: grid; gap: 12px; align-content: start; }
```

- [ ] **Step 3: Test, build, check**

Run (in `clients/web`): `npm test; npm run build`
Expected: PASS.

In the browser as `student1 / Student@123`: the seeded "Sample Coding Test" shows **Start**. Create a Desktop-only exam for Computer Engineering as `faculty.cse` and check `student1` sees "Desktop app only" with no button.

- [ ] **Step 4: Commit**

```powershell
git add clients/web/src/features/student/studentExams.ts clients/web/src/features/student/studentExams.test.ts clients/web/src/features/student/DashboardPage.tsx clients/web/src/features/exams/ExamsHome.tsx clients/web/src/proctoring/media.ts clients/web/src/shell/shell.css
git commit -m "feat(web): student dashboard with Start, Resume and desktop-only exams" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 10: Exam rules — strikes, attempt queue, timer, local copy

**Files:**
- Create: `clients/web/src/proctoring/strikes.ts`, `attemptQueue.ts`, `timer.ts`, `localCopy.ts`
- Test: `clients/web/src/proctoring/strikes.test.ts`, `attemptQueue.test.ts`, `timer.test.ts`, `localCopy.test.ts`

**Interfaces:**
- Consumes: `ApiClient`, `ApiError` (Task 3); types.
- Produces:
  - `strikes.ts`: `type LeaveSignal = 'TabHidden' | 'FocusLost' | 'FullscreenExited'`; `type FaceResult = 'ok' | 'missing' | 'multiple'`; `MAX_STRIKES = 3`; `interface StrikeState { trips: number; faceRun: number }`; `class StrikeCounter(state?)` with `state`, `leave(signal): { event: LeaveSignal | null; autoSubmit: string | null }`, `back(): void`, `face(result): { event: 'FaceMissing' | 'MultipleFaces' | null; autoSubmit: string | null }`; `loadStrikes(attemptId)`, `saveStrikes(attemptId, state)`, `clearStrikes(attemptId)`.
  - `attemptQueue.ts`: `type QueueItem` (below); `interface AttemptSender`; `class AttemptQueue(sender, maxBackoffMs?)` with `enqueue(item)`, `retryNow()`, `close()`, `pending`, `isClosed`, callbacks `onOfflineChange(offline: boolean)`, `onClosed()`, `onSubmitted(attempt: AttemptDto)`; `apiSender(api, attemptId): AttemptSender`; `uploadEvidence(api, attemptId, kind, blob): Promise<EvidenceDto>`.
  - `timer.ts`: `SUBMIT_GRACE_MS`, `clockOffset(serverTimeIso, localNow?)`, `remainingMs(deadlineIso, offsetMs, localNow?)`, `submitWindowOver(deadlineIso, offsetMs, localNow?)`, `formatRemaining(ms)`.
  - `localCopy.ts`: `interface LocalCopy { code: string; language: Language; savedAt: number }`, `saveLocal(attemptId, code, language)`, `loadLocal(attemptId): LocalCopy | null`, `clearLocal(attemptId)`, `pickCode(attempt, local): { code: string; language: Language }`.

- [ ] **Step 1: Write the failing tests**

`clients/web/src/proctoring/strikes.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { clearStrikes, loadStrikes, saveStrikes, StrikeCounter } from './strikes';

describe('StrikeCounter', () => {
  it('counts one strike per trip away, however many signals it raises', () => {
    const c = new StrikeCounter();
    expect(c.leave('FullscreenExited')).toEqual({ event: 'FullscreenExited', autoSubmit: null });
    expect(c.leave('FocusLost')).toEqual({ event: null, autoSubmit: null });
    expect(c.leave('TabHidden')).toEqual({ event: null, autoSubmit: null });
    expect(c.state.trips).toBe(1);
  });

  it('auto-submits on the third trip, exactly once', () => {
    const c = new StrikeCounter();
    c.leave('TabHidden'); c.back();
    c.leave('FocusLost'); c.back();
    expect(c.leave('FullscreenExited')).toEqual({ event: 'FullscreenExited', autoSubmit: 'Left the exam 3 times.' });
    c.back();
    expect(c.leave('TabHidden').autoSubmit).toBeNull();
  });

  it('auto-submits after three failed face checks in a row; a good check resets the run', () => {
    const c = new StrikeCounter();
    expect(c.face('missing')).toEqual({ event: 'FaceMissing', autoSubmit: null });
    expect(c.face('ok')).toEqual({ event: null, autoSubmit: null });
    c.face('missing');
    expect(c.face('multiple')).toEqual({ event: 'MultipleFaces', autoSubmit: null });
    expect(c.state.faceRun).toBe(2);
    expect(c.face('missing')).toEqual({ event: 'FaceMissing', autoSubmit: 'Face check failed 3 times in a row.' });
    expect(c.face('missing').autoSubmit).toBeNull();
  });

  it('continues from saved counts', () => {
    const c = new StrikeCounter({ trips: 2, faceRun: 0 });
    expect(c.leave('TabHidden').autoSubmit).toBe('Left the exam 3 times.');
  });
});

describe('strike persistence', () => {
  it('keeps counts across a reload of the tab and forgets corrupt values', () => {
    saveStrikes(5, { trips: 2, faceRun: 1 });
    expect(loadStrikes(5)).toEqual({ trips: 2, faceRun: 1 });
    clearStrikes(5);
    expect(loadStrikes(5)).toEqual({ trips: 0, faceRun: 0 });
    sessionStorage.setItem('secucode.strikes.5', 'junk');
    expect(loadStrikes(5)).toEqual({ trips: 0, faceRun: 0 });
  });
});
```

`clients/web/src/proctoring/timer.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { clockOffset, formatRemaining, remainingMs, submitWindowOver } from './timer';

const local = Date.parse('2026-11-02T10:00:00Z');

describe('timer', () => {
  it('counts down on the server clock', () => {
    const offset = clockOffset('2026-11-02T10:01:00Z', local); // server is a minute ahead
    expect(offset).toBe(60_000);
    expect(remainingMs('2026-11-02T10:31:00Z', offset, local)).toBe(30 * 60_000);
    expect(remainingMs('2026-11-02T09:00:00Z', offset, local)).toBe(0);
  });

  it('allows the submit for 30 s after the deadline', () => {
    expect(submitWindowOver('2026-11-02T10:00:00Z', 0, local + 30_000)).toBe(false);
    expect(submitWindowOver('2026-11-02T10:00:00Z', 0, local + 30_001)).toBe(true);
  });

  it('formats the time left', () => {
    expect(formatRemaining(3_661_000)).toBe('1:01:01');
    expect(formatRemaining(59_500)).toBe('01:00');
    expect(formatRemaining(0)).toBe('00:00');
  });
});
```

`clients/web/src/proctoring/localCopy.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import type { AttemptDto } from '../api/types';
import { clearLocal, loadLocal, pickCode, saveLocal } from './localCopy';

const attempt = (over: Partial<AttemptDto> = {}) => ({ code: 'server', language: 'Java', ...over }) as AttemptDto;

describe('local copy', () => {
  it('prefers the local copy, else the server code with a known language', () => {
    expect(pickCode(attempt(), null)).toEqual({ code: 'server', language: 'Java' });
    expect(pickCode(attempt({ language: 'Rust' }), null)).toEqual({ code: 'server', language: 'Python' });
    expect(pickCode(attempt({ language: null }), null)).toEqual({ code: 'server', language: 'Python' });

    saveLocal(3, 'local', 'C++');
    expect(pickCode(attempt(), loadLocal(3))).toEqual({ code: 'local', language: 'C++' });

    clearLocal(3);
    expect(loadLocal(3)).toBeNull();
  });

  it('ignores a corrupt local copy', () => {
    localStorage.setItem('secucode.code.3', '{bad');
    expect(loadLocal(3)).toBeNull();
    localStorage.setItem('secucode.code.3', JSON.stringify({ code: 1, language: 'Python' }));
    expect(loadLocal(3)).toBeNull();
    localStorage.setItem('secucode.code.3', JSON.stringify({ code: 'x', language: 'Rust' }));
    expect(loadLocal(3)).toBeNull();
  });
});
```

`clients/web/src/proctoring/attemptQueue.test.ts`:

```ts
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
});
```

Run (in `clients/web`): `npm test`
Expected: FAIL — modules not found.

- [ ] **Step 2: Implement**

`clients/web/src/proctoring/strikes.ts`:

```ts
// Strike rules shared with the desktop app: 3 trips away from the exam, or 3 failed face checks in a row, submit it.
export type LeaveSignal = 'TabHidden' | 'FocusLost' | 'FullscreenExited';
export type FaceResult = 'ok' | 'missing' | 'multiple';
export const MAX_STRIKES = 3;

export interface StrikeState { trips: number; faceRun: number }
export interface Outcome<E> { event: E | null; autoSubmit: string | null }

export class StrikeCounter {
  private away = false;

  constructor(readonly state: StrikeState = { trips: 0, faceRun: 0 }) {}

  // Leaving fires several signals at once (exit fullscreen + blur + hidden); only the first of a trip counts.
  leave(signal: LeaveSignal): Outcome<LeaveSignal> {
    if (this.away) return { event: null, autoSubmit: null };
    this.away = true;
    this.state.trips++;
    return { event: signal, autoSubmit: this.state.trips === MAX_STRIKES ? `Left the exam ${MAX_STRIKES} times.` : null };
  }

  back(): void {
    this.away = false;
  }

  face(result: FaceResult): Outcome<'FaceMissing' | 'MultipleFaces'> {
    if (result === 'ok') {
      this.state.faceRun = 0;
      return { event: null, autoSubmit: null };
    }
    this.state.faceRun++;
    return {
      event: result === 'missing' ? 'FaceMissing' : 'MultipleFaces',
      autoSubmit: this.state.faceRun === MAX_STRIKES ? `Face check failed ${MAX_STRIKES} times in a row.` : null,
    };
  }
}

const key = (attemptId: number) => `secucode.strikes.${attemptId}`;

export function loadStrikes(attemptId: number): StrikeState {
  try {
    const v = JSON.parse(sessionStorage.getItem(key(attemptId)) ?? 'null') as Partial<StrikeState> | null;
    if (v && typeof v.trips === 'number' && typeof v.faceRun === 'number') return { trips: v.trips, faceRun: v.faceRun };
  } catch {
    /* corrupt or blocked: start from zero */
  }
  return { trips: 0, faceRun: 0 };
}

export function saveStrikes(attemptId: number, state: StrikeState): void {
  try {
    sessionStorage.setItem(key(attemptId), JSON.stringify(state));
  } catch {
    /* blocked: counts last until reload */
  }
}

export function clearStrikes(attemptId: number): void {
  try {
    sessionStorage.removeItem(key(attemptId));
  } catch {
    /* blocked */
  }
}
```

`clients/web/src/proctoring/timer.ts`:

```ts
// The server clock decides exam time; the browser only tracks its offset from it.
export const SUBMIT_GRACE_MS = 30_000;

export const clockOffset = (serverTimeIso: string, localNow = Date.now()) => Date.parse(serverTimeIso) - localNow;

export const remainingMs = (deadlineIso: string, offsetMs: number, localNow = Date.now()) =>
  Math.max(0, Date.parse(deadlineIso) - (localNow + offsetMs));

export const submitWindowOver = (deadlineIso: string, offsetMs: number, localNow = Date.now()) =>
  localNow + offsetMs > Date.parse(deadlineIso) + SUBMIT_GRACE_MS;

export function formatRemaining(ms: number): string {
  const total = Math.ceil(ms / 1000);
  const h = Math.floor(total / 3600);
  const mm = String(Math.floor((total % 3600) / 60)).padStart(2, '0');
  const ss = String(total % 60).padStart(2, '0');
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}
```

`clients/web/src/proctoring/localCopy.ts`:

```ts
import { isLanguage, type AttemptDto, type Language } from '../api/types';

// A copy of the student's code in this browser, so a reload or crash loses nothing that wasn't auto-saved yet.
export interface LocalCopy { code: string; language: Language; savedAt: number }

const key = (attemptId: number) => `secucode.code.${attemptId}`;

export function saveLocal(attemptId: number, code: string, language: Language): void {
  try {
    localStorage.setItem(key(attemptId), JSON.stringify({ code, language, savedAt: Date.now() }));
  } catch {
    /* storage full or blocked: the 30 s server save still runs */
  }
}

export function loadLocal(attemptId: number): LocalCopy | null {
  try {
    const v = JSON.parse(localStorage.getItem(key(attemptId)) ?? 'null') as Partial<LocalCopy> | null;
    return v && typeof v.code === 'string' && isLanguage(v.language) ? (v as LocalCopy) : null;
  } catch {
    return null;
  }
}

export function clearLocal(attemptId: number): void {
  try {
    localStorage.removeItem(key(attemptId));
  } catch {
    /* blocked */
  }
}

// ponytail: this browser is the only one writing this attempt's code, so its copy is never older than the
// server's; prefer it. A student switching browsers mid-attempt has no local copy and gets the server's.
export function pickCode(attempt: AttemptDto, local: LocalCopy | null): { code: string; language: Language } {
  if (local) return { code: local.code, language: local.language };
  return { code: attempt.code, language: isLanguage(attempt.language) ? attempt.language : 'Python' };
}
```

`clients/web/src/proctoring/attemptQueue.ts`:

```ts
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
```

- [ ] **Step 3: Run the tests**

Run (in `clients/web`): `npm test`
Expected: PASS. (`postEvent` returns 201 with a JSON body; `api.post<void>` parses and ignores it.)

- [ ] **Step 4: Commit**

```powershell
git add clients/web/src/proctoring/strikes.ts clients/web/src/proctoring/strikes.test.ts clients/web/src/proctoring/attemptQueue.ts clients/web/src/proctoring/attemptQueue.test.ts clients/web/src/proctoring/timer.ts clients/web/src/proctoring/timer.test.ts clients/web/src/proctoring/localCopy.ts clients/web/src/proctoring/localCopy.test.ts
git commit -m "feat(web): exam rules - strike counting, ordered attempt queue, server-clock timer, local code copy" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 11: Browser monitors — camera, face detection, audio, focus

**Files:**
- Modify: `clients/web/src/proctoring/media.ts` (add `openMedia`, `hasAudio`, `browserProblem`, `cameraProblem`)
- Create: `clients/web/src/proctoring/faceMonitor.ts`, `audioRecorder.ts`, `focusGuard.ts`
- Test: `clients/web/src/proctoring/media.test.ts`, `faceMonitor.test.ts`, `audioRecorder.test.ts`, `focusGuard.test.ts`
- Create: `clients/web/scripts/copy-mediapipe.mjs`, `clients/web/public/mediapipe/blaze_face_short_range.tflite`
- Modify: `clients/web/package.json` (`postinstall`)

**Interfaces:**
- Consumes: `LeaveSignal`, `FaceResult` (Task 10).
- Produces:
  - `media.ts`: `openMedia(): Promise<MediaStream>` (reuses a live stream; throws the browser's error when the camera can't be used), `currentMedia()`, `closeMedia()`, `hasAudio(stream): boolean`, `browserProblem(): string | null`, `cameraProblem(error: unknown): string`.
  - `faceMonitor.ts`: `classify(faces: number): FaceResult`, `countFaces(video): Promise<number>`, `captureJpeg(video): Promise<Blob | null>`. In DEV builds only, a number in `window.__secucodeFakeFaces` replaces detection (the e2e tests' fake camera shows no face).
  - `audioRecorder.ts`: `class AudioRecorder(stream, onChunk: (blob: Blob) => void, chunkMs = 60_000)` with `start()`, `stop()`. Each chunk is a self-contained `audio/webm` file.
  - `focusGuard.ts`: `watchFocus(onLeave: (s: LeaveSignal) => void, onBack: () => void, doc?, win?): () => void`.

- [ ] **Step 1: Write the failing tests**

`clients/web/src/proctoring/media.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { cameraProblem } from './media';

describe('cameraProblem', () => {
  it('names what to do for each camera failure', () => {
    expect(cameraProblem(new DOMException('', 'NotAllowedError'))).toMatch(/blocked.*Allow the camera/);
    expect(cameraProblem(new DOMException('', 'NotFoundError'))).toMatch(/No webcam was found/);
    expect(cameraProblem(new DOMException('', 'NotReadableError'))).toMatch(/in use by another app/);
    expect(cameraProblem(new Error('?'))).toMatch(/couldn't be started/);
  });
});
```

`clients/web/src/proctoring/faceMonitor.test.ts`:

```ts
import { afterEach, describe, expect, it } from 'vitest';
import { classify, countFaces } from './faceMonitor';

afterEach(() => { delete window.__secucodeFakeFaces; });

describe('faceMonitor', () => {
  it('classifies face counts', () => {
    expect([0, 1, 2, 5].map(classify)).toEqual(['missing', 'ok', 'multiple', 'multiple']);
  });

  it('sees no face when the camera is not running', async () => {
    expect(await countFaces(document.createElement('video'))).toBe(0);
  });

  it('uses the test override in development builds', async () => {
    window.__secucodeFakeFaces = 2;
    expect(await countFaces(document.createElement('video'))).toBe(2);
  });
});
```

`clients/web/src/proctoring/audioRecorder.test.ts`:

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AudioRecorder } from './audioRecorder';

class FakeRecorder {
  static made: FakeRecorder[] = [];
  state: 'inactive' | 'recording' = 'inactive';
  ondataavailable: ((e: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  constructor(readonly stream: unknown, readonly options: unknown) { FakeRecorder.made.push(this); }
  start() { this.state = 'recording'; }
  stop() { this.state = 'inactive'; this.ondataavailable?.({ data: new Blob(['opus']) }); this.onstop?.(); }
}

beforeEach(() => {
  FakeRecorder.made = [];
  vi.useFakeTimers();
  vi.stubGlobal('MediaRecorder', FakeRecorder);
  vi.stubGlobal('MediaStream', class { constructor(readonly tracks: unknown[]) {} });
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const stream = { getAudioTracks: () => ['mic'] } as unknown as MediaStream;

describe('AudioRecorder', () => {
  it('delivers a self-contained WebM chunk every interval and a last one on stop', () => {
    const chunks: Blob[] = [];
    const r = new AudioRecorder(stream, b => chunks.push(b), 60_000);
    r.start();
    vi.advanceTimersByTime(60_000);
    expect(chunks).toHaveLength(1);
    expect(chunks[0].type).toBe('audio/webm');
    expect(FakeRecorder.made).toHaveLength(2); // a new recorder per chunk, so each file has its own header

    r.stop();
    expect(chunks).toHaveLength(2);
    expect(FakeRecorder.made).toHaveLength(2); // nothing restarts after stop
    vi.advanceTimersByTime(120_000);
    expect(chunks).toHaveLength(2);
  });
});
```

`clients/web/src/proctoring/focusGuard.test.ts`:

```ts
import { afterEach, describe, expect, it, vi } from 'vitest';
import { watchFocus } from './focusGuard';

let fullscreen: Element | null = null;
let visibility: DocumentVisibilityState = 'visible';
let focused = true;

Object.defineProperty(document, 'fullscreenElement', { configurable: true, get: () => fullscreen });
Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
vi.spyOn(document, 'hasFocus').mockImplementation(() => focused);

afterEach(() => { fullscreen = document.body; visibility = 'visible'; focused = true; });

describe('watchFocus', () => {
  it('reports each way of leaving, and comes back only when fully present again', () => {
    fullscreen = document.body;
    const left: string[] = [];
    let back = 0;
    const stop = watchFocus(s => left.push(s), () => back++);

    fullscreen = null;
    document.dispatchEvent(new Event('fullscreenchange'));
    focused = false;
    window.dispatchEvent(new Event('blur'));
    visibility = 'hidden';
    document.dispatchEvent(new Event('visibilitychange'));
    expect(left).toEqual(['FullscreenExited', 'FocusLost', 'TabHidden']);

    visibility = 'visible';
    document.dispatchEvent(new Event('visibilitychange')); // still unfocused and not fullscreen
    focused = true;
    window.dispatchEvent(new Event('focus')); // still not fullscreen
    expect(back).toBe(0);

    fullscreen = document.body;
    document.dispatchEvent(new Event('fullscreenchange'));
    expect(back).toBe(1);

    stop();
    fullscreen = null;
    document.dispatchEvent(new Event('fullscreenchange'));
    expect(left).toHaveLength(3);
  });
});
```

Run (in `clients/web`): `npm test`
Expected: FAIL — modules/functions not found.

- [ ] **Step 2: Implement**

Replace `clients/web/src/proctoring/media.ts`:

```ts
// The exam's webcam/microphone stream, shared by the start page and the exam page (one permission prompt).
let current: MediaStream | null = null;

export function currentMedia(): MediaStream | null {
  return current;
}

export function closeMedia(): void {
  current?.getTracks().forEach(t => t.stop());
  current = null;
}

export const hasAudio = (stream: MediaStream) => stream.getAudioTracks().length > 0;

// Camera required, microphone optional: if asking for both fails (no mic, or mic refused), try the camera alone.
// Throws the browser's error when the camera itself can't be used.
export async function openMedia(): Promise<MediaStream> {
  if (current?.getVideoTracks().some(t => t.readyState === 'live')) return current;
  closeMedia();
  const video = { width: { ideal: 640 }, height: { ideal: 480 } };
  try {
    current = await navigator.mediaDevices.getUserMedia({ video, audio: true });
  } catch {
    current = await navigator.mediaDevices.getUserMedia({ video });
  }
  return current;
}

export function browserProblem(): string | null {
  if (!window.isSecureContext) return 'Web exams need a secure connection. Open SecuCode over https (or at localhost on this computer).';
  if (!navigator.mediaDevices?.getUserMedia) return "This browser can't use a webcam. Use the latest Chrome or Edge.";
  if (!document.documentElement.requestFullscreen) return "This browser can't go fullscreen. Use the latest Chrome or Edge.";
  if (window.matchMedia?.('(pointer: coarse)').matches && !window.matchMedia('(any-pointer: fine)').matches)
    return 'Web exams need a computer with a keyboard and mouse.';
  return null;
}

export function cameraProblem(error: unknown): string {
  const name = error instanceof DOMException ? error.name : '';
  if (name === 'NotAllowedError' || name === 'SecurityError')
    return 'Camera access is blocked. Allow the camera for this site (the camera icon in the address bar), then reload the page.';
  if (name === 'NotFoundError' || name === 'OverconstrainedError') return 'No webcam was found. Connect one, then reload the page.';
  if (name === 'NotReadableError') return 'The webcam is in use by another app. Close that app, then reload the page.';
  return "The webcam couldn't be started. Reload the page to try again.";
}
```

`clients/web/src/proctoring/faceMonitor.ts`:

```ts
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
```

`clients/web/src/proctoring/audioRecorder.ts`:

```ts
// Records the microphone in fixed-length chunks. Each chunk gets its own MediaRecorder, so every uploaded
// file is a complete WebM that plays on its own (timeslice chunks after the first have no header).
export class AudioRecorder {
  private recorder: MediaRecorder | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private stopped = true;

  constructor(
    private readonly stream: MediaStream,
    private readonly onChunk: (blob: Blob) => void,
    private readonly chunkMs = 60_000,
  ) {}

  start(): void {
    this.stopped = false;
    this.next();
  }

  // The current partial chunk is still delivered.
  stop(): void {
    this.stopped = true;
    clearTimeout(this.timer);
    if (this.recorder && this.recorder.state !== 'inactive') this.recorder.stop();
  }

  private next(): void {
    if (this.stopped) return;
    const recorder = new MediaRecorder(new MediaStream(this.stream.getAudioTracks()), { mimeType: 'audio/webm;codecs=opus' });
    const parts: Blob[] = [];
    recorder.ondataavailable = e => { if (e.data.size > 0) parts.push(e.data); };
    recorder.onstop = () => {
      if (parts.length > 0) this.onChunk(new Blob(parts, { type: 'audio/webm' })); // the API accepts exactly "audio/webm"
      this.next();
    };
    recorder.start();
    this.recorder = recorder;
    this.timer = setTimeout(() => { if (recorder.state !== 'inactive') recorder.stop(); }, this.chunkMs);
  }
}
```

`clients/web/src/proctoring/focusGuard.ts`:

```ts
import type { LeaveSignal } from './strikes';

// Reports every sign of leaving the exam, and "back" once the page is visible, focused and fullscreen again.
// Browser shortcuts (Alt+Tab, Win key) can't be blocked from a page; leaving that way is caught here instead.
export function watchFocus(onLeave: (s: LeaveSignal) => void, onBack: () => void, doc: Document = document, win: Window = window): () => void {
  const present = () => doc.visibilityState === 'visible' && doc.hasFocus() && doc.fullscreenElement !== null;
  const maybeBack = () => { if (present()) onBack(); };
  const onVisibility = () => (doc.visibilityState === 'hidden' ? onLeave('TabHidden') : maybeBack());
  const onBlur = () => onLeave('FocusLost');
  const onFullscreen = () => (doc.fullscreenElement ? maybeBack() : onLeave('FullscreenExited'));

  doc.addEventListener('visibilitychange', onVisibility);
  win.addEventListener('blur', onBlur);
  win.addEventListener('focus', maybeBack);
  doc.addEventListener('fullscreenchange', onFullscreen);
  return () => {
    doc.removeEventListener('visibilitychange', onVisibility);
    win.removeEventListener('blur', onBlur);
    win.removeEventListener('focus', maybeBack);
    doc.removeEventListener('fullscreenchange', onFullscreen);
  };
}
```

`clients/web/scripts/copy-mediapipe.mjs`:

```js
// Copies MediaPipe's WASM runtime into public/ so the app serves it itself (no CDN). Runs after npm install.
import { cpSync, existsSync, mkdirSync } from 'node:fs';

const from = 'node_modules/@mediapipe/tasks-vision/wasm';
if (existsSync(from)) {
  mkdirSync('public/mediapipe', { recursive: true });
  cpSync(from, 'public/mediapipe/wasm', { recursive: true });
} else {
  console.warn('copy-mediapipe: @mediapipe/tasks-vision is not installed yet.');
}
```

In `clients/web/package.json` add to `scripts`:

```json
    "postinstall": "node scripts/copy-mediapipe.mjs",
```

Download the face model (≈230 KB, Apache-2.0, committed so the app never fetches it from Google at exam time):

```powershell
Invoke-WebRequest https://storage.googleapis.com/mediapipe-models/face_detector/blaze_face_short_range/float16/1/blaze_face_short_range.tflite -OutFile clients/web/public/mediapipe/blaze_face_short_range.tflite
```

Then (in `clients/web`) `npm install` once more so `postinstall` copies the WASM files; check `public/mediapipe/wasm/` now has `vision_wasm_internal.js` and `.wasm` files and that `git status` does not list them.

- [ ] **Step 3: Run the tests and build**

Run (in `clients/web`): `npm test; npm run build`
Expected: PASS; build succeeds and `server/Secucode.Api/wwwroot/mediapipe/` contains the model and `wasm/`.

- [ ] **Step 4: Commit**

```powershell
git add clients/web/src/proctoring/media.ts clients/web/src/proctoring/media.test.ts clients/web/src/proctoring/faceMonitor.ts clients/web/src/proctoring/faceMonitor.test.ts clients/web/src/proctoring/audioRecorder.ts clients/web/src/proctoring/audioRecorder.test.ts clients/web/src/proctoring/focusGuard.ts clients/web/src/proctoring/focusGuard.test.ts clients/web/scripts/copy-mediapipe.mjs clients/web/public/mediapipe/blaze_face_short_range.tflite clients/web/package.json
git commit -m "feat(web): browser monitors - camera, in-browser face detection, chunked audio, focus guard" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 12: Exam start page (checks, preview, PIN, fullscreen)

**Files:**
- Create: `clients/web/src/features/student/ExamStartPage.tsx`
- Create: `clients/web/src/features/student/exam.css`
- Modify: `clients/web/src/App.tsx` (route)

**Interfaces:**
- Consumes: `openMedia`, `hasAudio`, `browserProblem`, `cameraProblem` (Task 11); `countFaces`, `classify`, `captureJpeg` (Task 11); `uploadEvidence` (Task 10); `inProgressId` (Task 9); `StudentOnly` (Task 4).
- Produces: route `/exams/:id/start` (`?resume=<attemptId>` for Resume). On success it navigates to `/attempt/:attemptId` with router state `{ title }`, fullscreen on, and the media stream left open in `media.ts` for the exam page. UI text used by tests: status `Face detected.`, field `PIN`, buttons `Start exam` / `Resume exam`.

- [ ] **Step 1: Implement the page**

`clients/web/src/features/student/ExamStartPage.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../api/client';
import type { AttemptDetailDto, AttemptDto, StudentExamDto } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { uploadEvidence } from '../../proctoring/attemptQueue';
import { captureJpeg, classify, countFaces } from '../../proctoring/faceMonitor';
import { browserProblem, cameraProblem, hasAudio, openMedia } from '../../proctoring/media';
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
      await document.documentElement.requestFullscreen(); // browsers allow this only straight after a click
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
```

`clients/web/src/features/student/exam.css`:

```css
.start-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; align-items: start; }
.start-preview {
  width: 100%; aspect-ratio: 4 / 3; border-radius: var(--radius-card);
  background: var(--surface-sunken); object-fit: cover; transform: scaleX(-1);
}
.rules { margin: 0; padding-left: 20px; display: grid; gap: 6px; }
@media (max-width: 899px) { .start-grid { grid-template-columns: 1fr; } }
```

In `clients/web/src/App.tsx`, import `StudentOnly` and `ExamStartPage`, and add under the `// ... student routes (Task 12)` comment:

```tsx
      { path: '/exams/:id/start', element: <StudentOnly><ExamStartPage /></StudentOnly> },
```

- [ ] **Step 2: Type-check, test, build**

Run (in `clients/web`): `npx tsc --noEmit; npm test; npm run build`
Expected: no errors; tests PASS.

- [ ] **Step 3: Check it in a browser**

With the API and `npm run dev` running, sign in as `student1` in Chrome and press Start on "Sample Coding Test":
- the browser asks for camera and microphone; the preview shows you and "Face detected."; covering the camera shows "No face found…";
- Start exam stays disabled without a PIN; a wrong PIN shows "Incorrect PIN." and leaves fullscreen;
- block the camera in site settings and reload: the page explains how to allow it.
Do **not** press Start exam with the right PIN yet (the exam page arrives in Task 13; the attempt would be created and stay in progress). If you do, it's harmless: Task 13 resumes it.

If nobody is at the webcam, say so in your report and test with `window.__secucodeFakeFaces = 1` in the dev-tools console instead.

- [ ] **Step 4: Commit**

```powershell
git add clients/web/src/features/student/ExamStartPage.tsx clients/web/src/features/student/exam.css clients/web/src/App.tsx
git commit -m "feat(web): exam start page with browser checks, camera preview, PIN and fullscreen" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 13: Exam page

**Files:**
- Create: `clients/web/src/api/live.ts`
- Create: `clients/web/src/features/student/useProctoring.ts`
- Create: `clients/web/src/features/student/ExamPage.tsx`
- Create: `clients/web/src/features/login/SignInDialog.tsx`
- Modify: `clients/web/src/features/student/exam.css`, `clients/web/src/App.tsx` (route)

**Interfaces:**
- Consumes: everything in `proctoring/` (Tasks 10–11); `ApiClient.freshAccessToken` (Task 3); `useAuth().expired`, `RequireAuth keepOnExpiry`, `StudentOnly` (Task 4); `CodeEditor` (Task 8); `Dialog`, UI kit.
- Produces:
  - `live.ts`: `connectLive(api, handlers: LiveHandlers, onConnected: () => void): () => void`, `type LiveHandlers = Partial<Record<'DeadlineChanged' | 'AttemptVoided' | 'AttemptReopened', (m: LiveAttemptMessage) => void>>`.
  - `useProctoring({ attemptId, video, active, report, onAutoSubmit }): { fullscreen: boolean; strikes: StrikeState }`.
  - Route `/attempt/:id` (full screen, no shell). UI text used by the e2e tests: `role="timer"`, strike line `Left exam: N of 3 · Face checks failed: N of 3`, field `Language`, editor `Your code`, region `Output`, buttons `Run`, `Submit`, `Return to fullscreen`, dialog `Submit your exam?` with `Keep working` / `Submit`, headings `Submitted.` and `Submitting…`, offline texts starting `Your work is kept in this tab` and `Your code will be submitted when the connection returns.`

- [ ] **Step 1: Implement the live connection**

`clients/web/src/api/live.ts`:

```ts
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
```

- [ ] **Step 2: Implement the proctoring hook**

`clients/web/src/features/student/useProctoring.ts`:

```ts
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
```

- [ ] **Step 3: Implement the sign-in dialog**

`clients/web/src/features/login/SignInDialog.tsx`:

```tsx
import { useState, type FormEvent } from 'react';
import { useAuth } from '../../auth/AuthContext';
import { Button, ErrorBar, TextField } from '../../ui';
import { Dialog } from '../../ui/Dialog';

// Shown over the exam page when the session expires: the exam keeps running underneath and nothing is lost.
export function SignInDialog({ onSignedIn }: { onSignedIn(): void }) {
  const { login, session } = useAuth();
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!session) return;
    setBusy(true);
    setError(null);
    try {
      await login(session.user.username, password);
      onSignedIn();
    } catch (x) {
      setError(x);
      setBusy(false);
    }
  }

  return (
    <Dialog open title="Sign in to keep going" onClose={() => {}}
      actions={<Button type="submit" form="reauth" variant="primary" busy={busy}>Sign in</Button>}>
      <p>Your session expired. Your exam is still running and your work is kept in this tab.</p>
      {error !== null && <ErrorBar error={error} />}
      <form id="reauth" className="stack" onSubmit={submit}>
        <TextField label="Password" type="password" autoComplete="current-password" value={password}
          onChange={e => setPassword(e.target.value)} required autoFocus />
      </form>
    </Dialog>
  );
}
```

- [ ] **Step 4: Implement the exam page**

`clients/web/src/features/student/ExamPage.tsx`:

```tsx
import { useCallback, useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../../api/client';
import { connectLive } from '../../api/live';
import { LANGUAGES, type AttemptDetailDto, type AttemptDto, type Language, type RunResult } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { apiSender, AttemptQueue, type QueueItem } from '../../proctoring/attemptQueue';
import { captureJpeg } from '../../proctoring/faceMonitor';
import { clearLocal, loadLocal, pickCode, saveLocal } from '../../proctoring/localCopy';
import { closeMedia, currentMedia } from '../../proctoring/media';
import { clearStrikes, MAX_STRIKES } from '../../proctoring/strikes';
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
    queue.current?.close();
    leaveExam();
    setPhase({ kind: 'done', title: heading, message });
  }, []);

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
      fetchAttempt().then(a => end(CLOSED, closedMessage(a)), () => end(CLOSED, 'The server no longer accepts changes to this attempt.'));
    };
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
  }, [api, attemptId, fetchAttempt, end]);

  const enqueue = useCallback((item: QueueItem) => queue.current?.enqueue(item), []);

  // Load. After a reload the camera is gone, so the student goes back through Resume on the start page.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const attempt = await fetchAttempt();
        if (cancelled) return;
        if (attempt.status !== 'InProgress') return end(CLOSED, closedMessage(attempt));
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
  }, [attemptId, fetchAttempt, navigate, end]);

  const finish = useCallback(async (reason: string) => {
    if (finishing.current) return; // timer, strikes and the button can all fire; only one submit
    finishing.current = true;
    setConfirming(false);
    const frame = video.current ? await captureJpeg(video.current) : null; // before the camera stops
    setPhase({ kind: 'submitting' });
    if (frame) enqueue({ kind: 'evidence', evidenceKind: 'Webcam', blob: frame });
    const l = latest.current;
    enqueue({ kind: 'submit', code: l.code, language: l.language, reason });
  }, [enqueue]);

  const running = phase.kind === 'running';
  const { fullscreen, strikes } = useProctoring({ attemptId, video, active: running, report: enqueue, onAutoSubmit: r => void finish(r) });

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
    if (phase.kind === 'submitting' && deadline !== null && submitWindowOver(deadline, offset, now))
      end('The submit window has passed.', 'The connection did not come back in time. The server has submitted your last saved code.');
  }, [phase.kind, deadline, offset, now, end]);

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
      AttemptVoided: m => { if (m.attemptId === attemptId) end(CLOSED, 'Staff voided this attempt. Ask them what to do next.'); },
    }, () => {
      fetchAttempt().then(a => { if (a.status !== 'InProgress' && !finishing.current) end(CLOSED, closedMessage(a)); }, () => {});
    });
  }, [running, api, attemptId, fetchAttempt, end]);

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
              <Button variant="primary" onClick={() => void document.documentElement.requestFullscreen().catch(() => {})}>Return to fullscreen</Button>
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
```

Append to `clients/web/src/features/student/exam.css`:

```css
.exam { display: grid; grid-template-rows: auto auto 1fr; height: 100vh; background: var(--surface); }
.exam-bar { display: flex; align-items: center; gap: 16px; padding: 8px 16px; background: var(--surface-raised); border-bottom: 1px solid var(--border); }
.exam-title { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.strikes { color: var(--text-secondary); font-size: var(--fs-caption); }
.timer { font: 600 var(--fs-subtitle) / 1 var(--font-code); font-variant-numeric: tabular-nums; }
.timer.low { color: var(--danger); }
.exam > .infobar { margin: 8px 16px 0; }
.exam-body { display: grid; grid-template-columns: minmax(260px, 1fr) 2fr; gap: 16px; padding: 16px; min-height: 0; }
.exam-question {
  display: flex; flex-direction: column; gap: 12px; overflow: auto; padding: 16px; user-select: none;
  background: var(--surface-raised); border: 1px solid var(--border); border-radius: var(--radius-card);
}
.self-view {
  width: 160px; aspect-ratio: 4 / 3; margin-top: auto; border-radius: var(--radius);
  background: var(--surface-sunken); object-fit: cover; transform: scaleX(-1);
}
.exam-work { display: grid; grid-template-rows: auto 1fr minmax(120px, 30%); gap: 12px; min-height: 0; }
.exam-tools { align-items: end; justify-content: space-between; }
.exam-tools .field { min-width: 180px; }
.exam-editor { min-height: 0; }
.exam-output {
  overflow: auto; padding: 12px; background: var(--surface-raised);
  border: 1px solid var(--border); border-radius: var(--radius-card);
}
.exam-output pre { margin: 0 0 8px; font: 13px / 1.5 var(--font-code); white-space: pre-wrap; }
.exam-output .stderr { color: var(--danger); }
.fs-overlay {
  position: fixed; inset: 0; z-index: 10; display: grid; place-items: center;
  background: rgb(0 0 0 / 0.6); animation: pop-in var(--dur-pop) var(--ease-out);
}
.exam-center { display: grid; place-items: center; min-height: 100vh; padding: 16px; }
.exam-message { width: min(480px, 100%); }
```

In `clients/web/src/App.tsx`, import `ExamPage` and `StudentOnly`, and add under the `// Full-screen routes` comment (outside the shell):

```tsx
  { path: '/attempt/:id', element: <RequireAuth keepOnExpiry><StudentOnly><ExamPage /></StudentOnly></RequireAuth> },
```

- [ ] **Step 5: Type-check, test, build**

Run (in `clients/web`): `npx tsc --noEmit; npm test; npm run build`
Expected: no errors; tests PASS.

- [ ] **Step 6: Walk through it in a browser**

With the API and `npm run dev` running, as `faculty.cse` create a `WebAllowed` exam for Computer Engineering (window open now, 10-minute duration). As `student1` in Chrome:
1. Start it: fullscreen exam page, question, timer counting down, `Left exam: 0 of 3`.
2. Type Python, press Run, see output; switch language; open Submit then Keep working — the strike line stays at 0.
3. Press Esc to leave fullscreen: overlay appears and `Left exam: 1 of 3`; Return to fullscreen hides it.
4. Reload the page: you land on the start page in Resume mode; Resume exam brings your code back and the strike count stays at 1.
5. In dev tools, set Network to Offline: the offline banner appears; back Online: it goes away.
6. Submit: "Submitted." and fullscreen ends. As `faculty.cse`, open the attempt: code, events (`Left fullscreen`), reference face, webcam frame and audio chunk (if a minute passed) are there.

If nobody is at the webcam, use `window.__secucodeFakeFaces = 1` in the console (before pressing Start) and say so in your report; set it to `0` once on the exam page to see three face strikes submit the exam.

- [ ] **Step 7: Commit**

```powershell
git add clients/web/src/api/live.ts clients/web/src/features/student/useProctoring.ts clients/web/src/features/student/ExamPage.tsx clients/web/src/features/login/SignInDialog.tsx clients/web/src/features/student/exam.css clients/web/src/App.tsx
git commit -m "feat(web): proctored exam page with live deadline, auto-save, strikes, offline queue and re-sign-in" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```

---

### Task 14: End-to-end tests, Docker image, README

**Files:**
- Create: `clients/web/playwright.config.ts`
- Create: `clients/web/e2e/global-setup.ts`, `clients/web/e2e/helpers.ts`, `clients/web/e2e/staff.spec.ts`, `clients/web/e2e/student.spec.ts`, `clients/web/e2e/screens.spec.ts`
- Modify: `server/Secucode.Api/Dockerfile`, `.dockerignore`, `README.md`

**Interfaces:**
- Consumes: the whole app; the seeded demo users (`faculty.cse / Faculty@123`, `student1` = Neha Sharma, `student2` = Aman Verma, both `Student@123`, PIN `1234`, department "Computer Engineering").
- Produces: `npm run e2e` (in `clients/web`) — starts a throwaway MySQL container on port 3317, seeds it, runs the API on `:8090` and Vite on `:5174`, runs Chromium with a fake camera and microphone, and removes the container afterwards. Screenshots land in `clients/web/test-results/screens/`.

- [ ] **Step 1: Install the browser**

Run (in `clients/web`): `npx playwright install chromium`
Expected: Chromium downloaded.

- [ ] **Step 2: Write the e2e harness**

`clients/web/playwright.config.ts`:

```ts
import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: 'e2e',
  timeout: 90_000,
  workers: 1, // one API and database for the run
  reporter: 'list',
  globalSetup: './e2e/global-setup.ts',
  use: {
    baseURL: 'http://localhost:5174',
    trace: 'retain-on-failure',
    permissions: ['camera', 'microphone'],
    launchOptions: { args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1366, height: 860 } } }],
  webServer: {
    command: 'npm run dev -- --port 5174',
    url: 'http://localhost:5174',
    reuseExistingServer: false,
    env: { SECUCODE_API: 'http://localhost:8090' },
  },
});
```

`clients/web/e2e/global-setup.ts`:

```ts
import { execSync, spawn } from 'node:child_process';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

export const API_URL = 'http://localhost:8090';
const DB = 'secucode-e2e-db';

const sh = (cmd: string) => execSync(cmd, { stdio: 'pipe' }).toString();

async function waitFor(what: string, check: () => boolean | Promise<boolean>, ms = 180_000) {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    try {
      if (await check()) return;
    } catch {
      /* not ready yet */
    }
    await new Promise(r => setTimeout(r, 1000));
  }
  throw new Error(`Timed out waiting for ${what}.`);
}

// A fresh MySQL in Docker, seeded demo data, and the API on :8090 (the dev API on :8080 is left alone).
export default async function globalSetup() {
  try { sh(`docker rm -f ${DB}`); } catch { /* not there */ }
  sh(`docker run -d --name ${DB} -e MYSQL_ROOT_PASSWORD=e2e -p 3317:3306 mysql:8.4`);
  // Over TCP: during first-start initialisation MySQL only listens on its socket.
  await waitFor('MySQL', () => { sh(`docker exec ${DB} mysql -h127.0.0.1 -uroot -pe2e -e "select 1"`); return true; });

  const project = resolve('../../server/Secucode.Api');
  const env = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: API_URL,
    ConnectionStrings__Default: 'Server=127.0.0.1;Port=3317;Database=secucode_e2e;User Id=root;Password=e2e;',
    Runner__Mode: 'Local',
    Evidence__Root: mkdtempSync(join(tmpdir(), 'secucode-e2e-evidence-')),
  };
  execSync(`dotnet run --project "${project}" --no-launch-profile -- seed`, { env, stdio: 'inherit' });
  // Run the built DLL directly so killing it stops the API (dotnet run would leave a child process behind).
  const api = spawn('dotnet', [join(project, 'bin', 'Debug', 'net8.0', 'Secucode.Api.dll')], { env, cwd: project, stdio: 'inherit' });
  await waitFor('the API', async () => (await fetch(`${API_URL}/api/health`)).ok);

  return async () => {
    api.kill();
    try { sh(`docker rm -f ${DB}`); } catch { /* already gone */ }
  };
}
```

`clients/web/e2e/helpers.ts`:

```ts
import { expect, request, type APIRequestContext, type Page } from '@playwright/test';
import { API_URL } from './global-setup';

export async function apiAs(username: string, password: string): Promise<APIRequestContext> {
  const anon = await request.newContext({ baseURL: API_URL });
  const res = await anon.post('/api/auth/login', { data: { username, password, client: 'Desktop' } });
  expect(res.ok()).toBe(true);
  const { accessToken } = (await res.json()) as { accessToken: string };
  return request.newContext({ baseURL: API_URL, extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` } });
}

// An exam open now for the whole Computer Engineering department (student1 and student2 are in it).
export async function createExam(title: string, mode: 'WebAllowed' | 'DesktopOnly', durationMinutes = 30): Promise<{ id: number }> {
  const faculty = await apiAs('faculty.cse', 'Faculty@123');
  const branches = (await (await faculty.get('/api/branches')).json()) as { id: number; name: string }[];
  const now = Date.now();
  const res = await faculty.post('/api/exams', {
    data: {
      title, mode, durationMinutes, isActive: true,
      startsAt: new Date(now - 60_000).toISOString(), endsAt: new Date(now + 3_600_000).toISOString(),
      branchId: branches.find(b => b.name === 'Computer Engineering')!.id, classId: null, batchId: null,
      allowQuestionEditAfterAttempts: true, allowAttemptReset: true, allowAttemptReopen: true, allowTimeExtension: true,
      questions: [{ id: null, text: 'Print a greeting.' }],
    },
  });
  expect(res.status()).toBe(201);
  return (await res.json()) as { id: number };
}

// The fake camera shows a test pattern, not a face; DEV builds read the face count from this global.
export const fakeFaces = (page: Page, count: number) => page.addInitScript(c => { window.__secucodeFakeFaces = c; }, count);
export const setFaces = (page: Page, count: number) => page.evaluate(c => { window.__secucodeFakeFaces = c; }, count);

export async function signIn(page: Page, username: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Exams' })).toBeVisible();
}

export async function openStart(page: Page, title: string) {
  await page.getByRole('article').filter({ hasText: title }).getByRole('button', { name: 'Start' }).click();
  await expect(page.getByText('Face detected.')).toBeVisible();
}

export async function startExam(page: Page, title: string) {
  await openStart(page, title);
  await page.getByLabel('PIN').fill('1234');
  await page.getByRole('button', { name: 'Start exam' }).click();
  await expect(page.getByRole('timer')).toBeVisible();
}

export async function typeCode(page: Page, text: string) {
  await page.getByLabel('Your code').click();
  await page.keyboard.type(text);
}

export async function submitExam(page: Page) {
  await page.getByRole('button', { name: 'Submit', exact: true }).click();
  await page.getByRole('dialog', { name: 'Submit your exam?' }).getByRole('button', { name: 'Submit' }).click();
}
```

(`window.__secucodeFakeFaces` is typed by the `declare global` in `src/proctoring/faceMonitor.ts`; if the e2e files don't see it, cast: `(window as unknown as { __secucodeFakeFaces?: number })`.)

`clients/web/e2e/staff.spec.ts`:

```ts
import { expect, test } from '@playwright/test';
import { signIn } from './helpers';

test('staff create, edit and deactivate an exam', async ({ page }) => {
  const title = `E2E staff ${Date.now()}`;
  await signIn(page, 'faculty.cse', 'Faculty@123');

  await page.getByRole('button', { name: 'New exam' }).click();
  await page.getByLabel('Title').fill(title);
  await page.getByLabel('Mode').selectOption('WebAllowed');
  await page.getByLabel('Department').selectOption({ label: 'Computer Engineering' });
  await page.getByLabel('Question 1').fill('Print the numbers 1 to 5.');
  await page.getByRole('button', { name: 'Save exam' }).click();

  const row = page.getByRole('row').filter({ hasText: title });
  await expect(row).toContainText('Web allowed');

  await row.getByRole('button', { name: 'Edit' }).click();
  await expect(page.getByLabel('Question 1')).toHaveValue('Print the numbers 1 to 5.');
  await page.getByLabel('Duration (minutes)').fill('45');
  await page.getByRole('button', { name: 'Save exam' }).click();
  await expect(row).toContainText('45 min');

  await row.getByRole('button', { name: 'Deactivate' }).click();
  await expect(row).toContainText('Inactive');
});
```

`clients/web/e2e/student.spec.ts`:

```ts
import { expect, test } from '@playwright/test';
import { apiAs, createExam, fakeFaces, setFaces, signIn, startExam, submitExam, typeCode } from './helpers';

const submitted = (page: import('@playwright/test').Page) => expect(page.getByRole('heading', { name: 'Submitted.' })).toBeVisible({ timeout: 40_000 });

test.beforeEach(async ({ page }) => { await fakeFaces(page, 1); });

test('a student takes a web exam and staff see the code and evidence', async ({ page, browser }) => {
  const title = `E2E web ${Date.now()}`;
  const desktopTitle = `E2E desktop ${Date.now()}`;
  await createExam(title, 'WebAllowed');
  await createExam(desktopTitle, 'DesktopOnly');

  await signIn(page, 'student1', 'Student@123');
  const desktopCard = page.getByRole('article').filter({ hasText: desktopTitle });
  await expect(desktopCard).toContainText('Desktop app only');
  await expect(desktopCard.getByRole('button')).toHaveCount(0);

  await startExam(page, title);
  await page.getByLabel('Language').selectOption('JavaScript');
  await page.getByLabel('Language').selectOption('Python');
  await typeCode(page, 'print("hello web")');
  await page.getByRole('button', { name: 'Run' }).click();
  await expect(page.getByLabel('Output', { exact: true })).toContainText('hello web');
  await page.getByRole('button', { name: 'Submit', exact: true }).click();
  await page.getByRole('dialog', { name: 'Submit your exam?' }).getByRole('button', { name: 'Keep working' }).click();
  await expect(page.getByText('Left exam: 0 of 3')).toBeVisible(); // dropdown, Run and the dialog are not leaving
  await submitExam(page);
  await submitted(page);

  const staff = await browser.newPage();
  await signIn(staff, 'faculty.cse', 'Faculty@123');
  await staff.getByRole('row').filter({ hasText: title }).getByRole('button', { name: 'Attempts' }).click();
  await staff.getByRole('link', { name: 'Neha Sharma' }).click();
  await expect(staff.getByLabel('Submitted code')).toContainText('hello web');
  await expect(staff.getByRole('img', { name: /^ReferenceFace/ })).toBeVisible();
  await expect(staff.getByRole('img', { name: /^Webcam/ })).toBeVisible();
});

test('reloading mid-exam keeps the code and resumes', async ({ page }) => {
  const title = `E2E reload ${Date.now()}`;
  await createExam(title, 'WebAllowed');
  await signIn(page, 'student1', 'Student@123');
  await startExam(page, title);
  await typeCode(page, 'x = 41');
  await page.waitForTimeout(1500); // the local copy is written 1 s after the last keystroke

  await page.reload();
  await expect(page.getByText('Face detected.')).toBeVisible();
  await page.getByRole('button', { name: 'Resume exam' }).click();
  await expect(page.getByLabel('Your code')).toContainText('x = 41');
});

test('leaving fullscreen three times submits the exam', async ({ page }) => {
  const title = `E2E fullscreen ${Date.now()}`;
  await createExam(title, 'WebAllowed');
  await signIn(page, 'student1', 'Student@123');
  await startExam(page, title);

  for (let i = 1; i <= 2; i++) {
    await page.evaluate(() => document.exitFullscreen());
    await page.getByRole('button', { name: 'Return to fullscreen' }).click();
    await expect(page.getByText(`Left exam: ${i} of 3`)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Return to fullscreen' })).toBeHidden();
  }
  await page.evaluate(() => document.exitFullscreen());
  await submitted(page);
});

test('three failed face checks in a row submit the exam', async ({ page }) => {
  const title = `E2E face ${Date.now()}`;
  await createExam(title, 'WebAllowed');
  await signIn(page, 'student1', 'Student@123');
  await startExam(page, title);
  await setFaces(page, 0);
  await submitted(page);
});

test('a submit made offline goes through when the connection returns', async ({ page, context }) => {
  const title = `E2E offline ${Date.now()}`;
  const exam = await createExam(title, 'WebAllowed');
  await signIn(page, 'student2', 'Student@123');
  await startExam(page, title);
  await typeCode(page, 'print("offline")');

  await context.setOffline(true);
  await submitExam(page);
  await expect(page.getByText('Your code will be submitted when the connection returns.')).toBeVisible();
  await context.setOffline(false);
  await submitted(page);

  const faculty = await apiAs('faculty.cse', 'Faculty@123');
  const [attempt] = (await (await faculty.get(`/api/exams/${exam.id}/attempts`)).json()) as { id: number }[];
  const detail = (await (await faculty.get(`/api/attempts/${attempt.id}`)).json()) as { attempt: { code: string; status: string } };
  expect(detail.attempt.status).toBe('Submitted');
  expect(detail.attempt.code).toContain('offline');
});
```

`clients/web/e2e/screens.spec.ts`:

```ts
import { expect, test, type Page } from '@playwright/test';
import { createExam, fakeFaces, openStart, signIn, startExam, submitExam, typeCode } from './helpers';

// Screenshots for a human to check, light and dark. Not assertions about pixels.
for (const scheme of ['light', 'dark'] as const) {
  test(`screens in ${scheme}`, async ({ page, browser }) => {
    const shot = (name: string, p: Page = page) => p.screenshot({ path: `test-results/screens/${name}-${scheme}.png`, fullPage: true });
    const title = `Screens ${scheme} ${Date.now()}`;
    await createExam(title, 'WebAllowed');
    await page.emulateMedia({ colorScheme: scheme });
    await fakeFaces(page, 1);

    await page.goto('/login');
    await shot('01-sign-in');
    await signIn(page, 'student2', 'Student@123');
    await shot('02-student-exams');
    await openStart(page, title);
    await shot('03-exam-start');
    await page.getByRole('button', { name: 'Back' }).click();
    await startExam(page, title);
    await typeCode(page, 'for i in range(3):\n    print(i)');
    await page.getByRole('button', { name: 'Run' }).click();
    await expect(page.getByLabel('Output', { exact: true })).toContainText('2');
    await shot('04-exam-page');
    await submitExam(page);
    await expect(page.getByRole('heading', { name: 'Submitted.' })).toBeVisible();
    await shot('05-submitted');

    const staff = await browser.newPage();
    await staff.emulateMedia({ colorScheme: scheme });
    await signIn(staff, 'faculty.cse', 'Faculty@123');
    await shot('06-staff-exams', staff);
    await staff.getByRole('button', { name: 'New exam' }).click();
    await shot('07-exam-editor', staff);
    await staff.getByRole('button', { name: 'Cancel' }).click();
    await staff.getByRole('row').filter({ hasText: title }).getByRole('button', { name: 'Attempts' }).click();
    await shot('08-attempts', staff);
    await staff.getByRole('link', { name: 'Aman Verma' }).click();
    await expect(staff.getByRole('img', { name: /^Webcam/ })).toBeVisible();
    await shot('09-attempt-detail', staff);
  });
}
```

(Python autoindents after `:` in CodeMirror; if the typed program ends up with doubled indentation, type `for i in range(3): print(i)` instead.)

- [ ] **Step 3: Run the e2e suite**

Docker Desktop must be running and nothing else on ports 3317, 5174 or 8090. Stop any API you started with `dotnet run` first: it locks `bin/Debug`, and the e2e setup rebuilds the API. Run (in `clients/web`): `npm run e2e`
Expected: 8 tests pass (1 staff, 5 student, 2 screens). On a failure, open the trace (`npx playwright show-trace test-results/<test>/trace.zip`), fix the app or the test, and say in your report what was wrong.

Open every PNG in `clients/web/test-results/screens/` and check both themes: readable contrast, nothing clipped or overlapping, focus rings visible, the exam page fills the screen. Fix what's wrong, rerun, and copy the final screenshots to `.superpowers/sdd/2026-10-15-secucode-web-app/screens/` (git-ignored) for the final review.

- [ ] **Step 4: Package the web app in the API image**

Replace `server/Secucode.Api/Dockerfile`:

```dockerfile
FROM node:22-alpine AS web
WORKDIR /web
COPY clients/web/package.json clients/web/package-lock.json ./
COPY clients/web/scripts/ scripts/
RUN npm ci
COPY clients/web/ ./
RUN npx tsc --noEmit && npx vite build --outDir dist --emptyOutDir

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/Secucode.Contracts/Secucode.Contracts.csproj server/Secucode.Contracts/
COPY server/Secucode.Api/Secucode.Api.csproj server/Secucode.Api/
RUN dotnet restore server/Secucode.Api/Secucode.Api.csproj
COPY server/Secucode.Contracts/ server/Secucode.Contracts/
COPY server/Secucode.Api/ server/Secucode.Api/
RUN dotnet publish server/Secucode.Api/Secucode.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Secucode.Api.dll"]
```

In `.dockerignore`, replace the line `clients/` with:

```
clients/*
!clients/web
clients/web/node_modules/
clients/web/public/mediapipe/wasm/
clients/web/test-results/
clients/web/playwright-report/
server/Secucode.Api/wwwroot/
```

Check the image (repo root):

```powershell
docker build -f server/Secucode.Api/Dockerfile -t secucode-api:web .
docker run --rm --entrypoint ls secucode-api:web /app/wwwroot /app/wwwroot/mediapipe
```

Expected: the build succeeds; the listing shows `index.html`, `theme-init.js`, `assets` and `mediapipe/` with the model and `wasm`.

- [ ] **Step 5: README**

In `README.md`:
- In the intro paragraph, after the desktop app sentence, add: "The web app (React, `clients/web`) gives staff the same exam pages in a browser and lets students take exams marked *Web allowed*."
- In **Features**, add a bullet: "**Web exams** (exams marked *Web allowed*): fullscreen required; leaving fullscreen or switching tab/window 3 times auto-submits; in-browser face detection (no face / more than one face, 3 in a row auto-submits — no face matching); webcam frames and 60 s microphone chunks uploaded as evidence; copy/paste blocked. Strict exams stay *Desktop only*."
- Replace the heading `## 🌐 Platform API (in progress)` with `## 🌐 Platform API` and its first paragraph with: "An ASP.NET Core 8 API (`server/Secucode.Api`) that the Windows app and the web app both use. It also serves the built web app at `/`. Designs: `docs/superpowers/specs/`."
- After the **Desktop tests** paragraph, add:

````markdown
**Run the web app** (Node 22+; start the API first, above):

```powershell
cd clients/web
npm install                 # also copies MediaPipe's WASM into public/
npm run dev                 # http://localhost:5173, proxies /api and /hubs to http://localhost:8080
npm run build               # builds into server/Secucode.Api/wwwroot; the API then serves it at /
```

Students need Chrome or Edge on a computer with a webcam. Browsers only allow the webcam on `https://` or `localhost`, so a college server needs HTTPS in front of the API.

**Web tests:** `npm test` (unit). `npx playwright install chromium` once, then `npm run e2e` (Docker Desktop running): starts a throwaway MySQL on port 3317, the API on 8090 and the app on 5174, and runs Chromium with a fake camera and microphone. Screenshots go to `clients/web/test-results/screens/`.
````

- In **Project layout**, add after the desktop lines:

```
clients/web/                     React + TypeScript web app (Vite)
  src/api, auth, ui, theme       API client, session rules, hand-built UI kit and theme tokens
  src/features/                  login, settings, staff (exams, editor, attempts), student (dashboard, start, exam)
  src/proctoring/                strikes, attempt queue, timer, face detection, audio, focus guard
  e2e/                           Playwright tests
```

- In **Future improvements**, remove "Web app for students and staff".

- [ ] **Step 6: Final checks**

Run, from the repo root:

```powershell
dotnet build Secucode.sln
dotnet test server/Secucode.Api.Tests --filter "Category!=Docker"
dotnet test clients/Secucode.Desktop.Tests
```

and in `clients/web`: `npm test; npm run build; npm run e2e`.
Expected: everything passes. `git status` shows only the files this task changed (no `wwwroot`, `node_modules`, `test-results`, `public/mediapipe/wasm`).

Production build under the CSP (the e2e tests run the Vite dev server, which the API's CSP doesn't cover): run `npm run build`, start the API as in Task 6 Step 5, open `http://localhost:8080` in Chrome, sign in as a student and open an exam's start page. The face status must change from "Starting the camera…" to "Face detected." or "No face found…" — not "Face detection couldn't start…" (`window.__secucodeFakeFaces` does nothing in this build) — and the dev-tools console must show no `Content-Security-Policy` violations. If MediaPipe needs more than `'wasm-unsafe-eval'`, widen `WebApp.ContentSecurityPolicy` by the smallest directive that fixes it and say so in your report.

Manual (spec section 8): one run in Chrome and in Edge with a real webcam and microphone — face status follows you covering and uncovering the camera, a second person in view shows "More than one face", the permission prompts work, and the audio chunk plays in the attempt detail. If nobody can sit at the webcam, say so in your report; the final review will list it as unverified.

- [ ] **Step 7: Commit**

```powershell
git add clients/web/playwright.config.ts clients/web/e2e/global-setup.ts clients/web/e2e/helpers.ts clients/web/e2e/staff.spec.ts clients/web/e2e/student.spec.ts clients/web/e2e/screens.spec.ts server/Secucode.Api/Dockerfile .dockerignore README.md
git commit -m "test(web): Playwright end-to-end tests; package the web app in the API image; README" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01RDs5ajBysHCtCvQB7qVigB"
```
