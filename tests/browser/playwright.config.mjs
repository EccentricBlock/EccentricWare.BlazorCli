import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';
import os from 'node:os';
import fs from 'node:fs';

const generated = process.env.EW_GENERATED_APP;
const published = process.env.EW_PUBLISHED_DIR;
if (!generated) throw new Error('EW_GENERATED_APP must name the generated DemoApp root');
const project = path.join(generated, 'src', 'DemoApp', 'DemoApp.csproj');
const dll = published ? path.join(published, 'DemoApp.dll') : undefined;
if (!fs.existsSync(published ? dll : project)) throw new Error(`Missing browser target: ${published ? dll : project}`);
const port = Number(process.env.EW_BROWSER_PORT || '5087');
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Invalid EW_BROWSER_PORT');
const baseURL = `http://127.0.0.1:${port}`;
const command = published
  ? `dotnet "${dll}" --web --port ${port}`
  : `dotnet run --no-build --no-launch-profile -c Release --project "${project}" -- --web --port ${port}`;

export default defineConfig({
  testDir: '.',
  testMatch: '**/*.spec.mjs',
  timeout: 45_000,
  expect: { timeout: 12_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0, // a retry would turn a flaky failure into a release-gate PASS
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL, trace: 'retain-on-failure', screenshot: 'only-on-failure', ...devices['Desktop Chrome'],
    // Optional: use an installed Chrome where Playwright has no Chromium build for the OS.
    // CI leaves this unset and uses the browser installed by `npx playwright install chromium`.
    ...(process.env.EW_BROWSER_EXECUTABLE ? { launchOptions: { executablePath: process.env.EW_BROWSER_EXECUTABLE } } : {}),
  },
  webServer: {
    command,
    url: `${baseURL}/api/v1/status`,
    reuseExistingServer: false,
    timeout: 120_000,
    // Publish test uses an unrelated CWD, proving it doesn't rely on source project files.
    cwd: published ? os.tmpdir() : path.dirname(project),
    env: {
      ASPNETCORE_URLS: 'http://0.0.0.0:57000',
      ASPNETCORE_HTTP_PORTS: '57001',
      DOTNET_URLS: 'http://0.0.0.0:57002',
      Kestrel__Endpoints__Injected__Url: 'http://0.0.0.0:57003'
    }
  }
});
