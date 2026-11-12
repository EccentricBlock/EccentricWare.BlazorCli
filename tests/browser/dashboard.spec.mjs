import { test, expect } from '@playwright/test';

// Deliberately deny every non-local request. No external CDN/font fetch is required.
test.beforeEach(async ({ page, context }) => {
  await context.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    if (url.hostname !== '127.0.0.1' && url.hostname !== 'localhost') {
      await route.abort('blockedbyclient');
    } else {
      await route.continue();
    }
  });
});

test('dashboard uses local MudBlazor and interactive counter', async ({ page }) => {
  const errors = [];
  const outbound = [];
  page.on('pageerror', err => errors.push(`page: ${err.message}`));
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
  page.on('requestfailed', req => errors.push(`network: ${req.url()} ${req.failure()?.errorText}`));
  page.on('request', req => {
    const host = new URL(req.url()).hostname;
    if (host !== '127.0.0.1' && host !== 'localhost') outbound.push(req.url());
  });
  await page.goto('/');
  await expect(page.getByTestId('status-mode')).toContainText('web');
  await expect(page.getByTestId('status-health')).toContainText('healthy');
  // The app bar names the generated application, not fixed template text.
  await expect(page.getByTestId('app-title')).toHaveText((await page.getByTestId('status-application').innerText()).trim());
  await expect(page.getByTestId('counter-value')).toContainText('Counter: 0');
  await page.getByTestId('increment-counter').click();
  await expect(page.getByTestId('counter-value')).toContainText('Counter: 1');
  await page.getByTestId('increment-counter').click();
  await expect(page.getByTestId('counter-value')).toContainText('Counter: 2');
  await expect(page.locator('link[href*="_content/MudBlazor/"][href*=".css"]')).toHaveCount(1);
  await expect(page.locator('script[src*="_content/MudBlazor/"][src*=".js"]')).toHaveCount(1);
  expect(outbound).toEqual([]);
  expect(errors).toEqual([]);
  // The browser must fetch real local assets with correct MIME types, not HTML fallbacks.
  const resources = await page.locator('link[href],script[src]').evaluateAll(nodes =>
    nodes.map(n => n.getAttribute('href') || n.getAttribute('src'))
      .filter(u => u && (u.includes('_content/MudBlazor/') || u.includes('blazor.web.js') || u.includes('app.'))));
  expect(resources.some(u => u.includes('MudBlazor') && u.includes('.css'))).toBe(true);
  expect(resources.some(u => u.includes('MudBlazor') && u.includes('.js'))).toBe(true);
  for (const resource of resources) {
    const response = await page.request.get(new URL(resource, page.url()).toString());
    expect(response.status(), resource).toBe(200);
    const contentType = response.headers()['content-type'] ?? '';
    if (resource.includes('.css')) expect(contentType).toContain('css');
    if (resource.includes('.js')) expect(contentType).toContain('javascript');
    expect((await response.body()).toString('utf8').trimStart().startsWith('<!DOCTYPE html>')).toBe(false);
  }
});

test('dialogs, snackbars and menus are interactive', async ({ page }) => {
  await page.goto('/');
  await page.getByTestId('show-snackbar').click();
  await expect(page.getByText('Sample notification')).toBeVisible();
  await page.getByTestId('show-dialog').click();
  await expect(page.getByText('This dialog demonstrates interactive MudBlazor UI without changing application state.')).toBeVisible();
  await page.getByTestId('close-dialog').click();
  await expect(page.getByText('This dialog demonstrates interactive MudBlazor UI without changing application state.')).not.toBeVisible();
  await page.getByRole('button', { name: 'Actions' }).click();
  await page.getByText('Select sample action').click();
  await expect(page.getByTestId('menu-selection')).toHaveText('Sample action selected');
});

test('supports 375px width, keyboard navigation and 200% zoom', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Application status' })).toBeVisible();
  // No horizontal scrolling at phone width (spec 4.3).
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  // The security caption stays on screen next to the (possibly long) application name.
  const caption = await page.getByTestId('loopback-caption').boundingBox();
  expect(caption.x + caption.width).toBeLessThanOrEqual(375);
  await page.keyboard.press('Tab');
  expect(await page.evaluate(() => document.activeElement?.tagName)).not.toBe('BODY');
  await page.evaluate(() => { document.body.style.zoom = '200%'; });
  await expect(page.getByTestId('increment-counter')).toBeVisible();
  await page.getByTestId('increment-counter').click();
  await expect(page.getByTestId('counter-value')).toContainText('Counter: 1');
});

test('status API is read-only with a minimal DTO', async ({ request }) => {
  const api = await request.get('/api/v1/status');
  expect(api.status()).toBe(200);
  expect(await api.json()).toMatchObject({ mode: 'web', status: 'healthy' });
  expect(Object.keys(await api.json()).sort()).toEqual(['application', 'mode', 'status', 'version']);
  expect((await request.post('/api/v1/status')).status()).toBe(405);
  expect((await request.get('/api/v1/nope')).status()).toBe(404);
  expect(api.headers()['cache-control']).toContain('no-store');
  expect(api.headers()['x-content-type-options']).toBe('nosniff');
  expect(api.headers()['x-frame-options']).toBe('DENY');
  // Blazor's default 'self' would contradict X-Frame-Options: DENY on the document.
  expect((await request.get('/')).headers()['content-security-policy']).toBe("frame-ancestors 'none'");
  const bad = await request.get('/api/v1/status', { headers: { Host: 'evil.invalid' } });
  expect(bad.status()).toBe(400);
  const wrongPort = await request.get('/api/v1/status', {
    headers: { Host: `127.0.0.1:${Number(process.env.EW_BROWSER_PORT || '5087') + 1}` }
  });
  expect(wrongPort.status()).toBe(400);
});


test('counter state belongs to each browser circuit and survives rapid clicks', async ({ browser }) => {
  const endpoint = `http://127.0.0.1:${process.env.EW_BROWSER_PORT || '5087'}/`;
  const a = await browser.newContext();
  const b = await browser.newContext();
  try {
    const first = await a.newPage();
    const second = await b.newPage();
    await Promise.all([first.goto(endpoint), second.goto(endpoint)]);
    await Promise.all([
      expect(first.getByTestId('counter-value')).toContainText('Counter: 0'),
      expect(second.getByTestId('counter-value')).toContainText('Counter: 0'),
    ]);
    for (let i = 0; i < 5; i++) await first.getByTestId('increment-counter').click();
    await expect(first.getByTestId('counter-value')).toContainText('Counter: 5');
    await expect(second.getByTestId('counter-value')).toContainText('Counter: 0');
  } finally {
    await a.close();
    await b.close();
  }
});
