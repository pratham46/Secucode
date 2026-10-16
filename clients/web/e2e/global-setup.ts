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
    'Logging__LogLevel__Microsoft.EntityFrameworkCore': 'Warning', // keep the test output readable
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
