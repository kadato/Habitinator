import { chromium } from 'playwright';
import lighthouse from 'lighthouse';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const BASE_URL = process.env.LIGHTHOUSE_BASE_URL ?? 'http://127.0.0.1:5033';
const OUT_DIR = process.env.LIGHTHOUSE_OUT_DIR
  ?? new URL('../../docs/lighthouse', import.meta.url).pathname;
const DEBUG_PORT = 9333;

fs.mkdirSync(OUT_DIR, { recursive: true });

function scoresOf(lhr) {
  const pick = (k) => (lhr.categories[k]?.score == null ? null : Math.round(lhr.categories[k].score * 100));
  return {
    performance: pick('performance'),
    accessibility: pick('accessibility'),
    bestPractices: pick('best-practices'),
    seo: pick('seo'),
  };
}

async function runLighthouse(url, keepStorage, extraHeaders) {
  const options = {
    logLevel: 'error',
    output: ['json', 'html'],
    port: DEBUG_PORT,
    onlyCategories: ['performance', 'accessibility', 'best-practices', 'seo'],
    extraHeaders: { 'x-lighthouse': 'true', ...(extraHeaders ?? {}) },
  };
  if (keepStorage) {
    options.disableStorageReset = true;
  }
  return await lighthouse(url, options);
}

async function audit(name, url, keepStorage, extraHeaders) {
  console.log(`Auditing ${name} (${url}) keepStorage=${keepStorage}...`);
  const result = await runLighthouse(url, keepStorage, extraHeaders);
  fs.writeFileSync(path.join(OUT_DIR, `report-${name}.json`), JSON.stringify(result.lhr, null, 2));
  const html = Array.isArray(result.report) ? result.report[1] : result.report;
  fs.writeFileSync(path.join(OUT_DIR, `report-${name}.html`), html);
  const scores = scoresOf(result.lhr);
  console.log(`${name} scores:`, JSON.stringify(scores));
  return { name, url, scores };
}

async function run() {
  // Persistent profile so the Playwright login and the Lighthouse targets
  // share one cookie jar and cache. browser.newPage() uses an isolated
  // context per page, and Cookie cannot be set via extraHeaders.
  const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'habitinator-lh-'));
  console.log(`Launching Chrome with remote debugging port ${DEBUG_PORT}...`);
  const context = await chromium.launchPersistentContext(userDataDir, {
    channel: 'chrome',
    headless: true,
    colorScheme: 'dark',
    args: [`--remote-debugging-port=${DEBUG_PORT}`, '--no-sandbox', '--disable-dev-shm-usage'],
  });
  await new Promise((r) => setTimeout(r, 2000));

  const page = await context.newPage();

  console.log('Warming up the server...');
  await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
  await new Promise((r) => setTimeout(r, 2000));
  await page.reload({ waitUntil: 'domcontentloaded' });
  await new Promise((r) => setTimeout(r, 1000));

  const results = [];

  // Anonymous pages use fresh storage each run.
  results.push(await audit('landing', `${BASE_URL}/`, false));
  results.push(await audit('login', `${BASE_URL}/auth/login`, false));
  results.push(await audit('register', `${BASE_URL}/auth/register`, false));
  results.push(await audit('not-found', `${BASE_URL}/not-found`, false));
  results.push(await audit('error', `${BASE_URL}/Error`, false));
  results.push(await audit('stats-anon', `${BASE_URL}/stats`, false));
  results.push(await audit('settings-anon', `${BASE_URL}/settings`, false));

  // Login via guest-login
  console.log('Logging in via guest-login...');
  await page.goto(`${BASE_URL}/auth/login`, { waitUntil: 'domcontentloaded' });
  await page.click('form[action="/api/auth/guest-login"] button[type="submit"]');
  await page.waitForURL(`${BASE_URL}/`, { timeout: 15000 });
  await page.waitForSelector('.board-shell', { timeout: 20000 });
  console.log('Logged in successfully!');

  // Authenticated pages. The persistent profile shares cookies and cache
  // with the Lighthouse targets. disableStorageReset keeps them.
  results.push(await audit('board', `${BASE_URL}/`, true));
  results.push(await audit('statistics', `${BASE_URL}/stats`, true));
  results.push(await audit('settings', `${BASE_URL}/settings`, true));

  await context.close();
  fs.rmSync(userDataDir, { recursive: true, force: true });

  const summary = {
    generatedAt: new Date().toISOString(),
    baseUrl: BASE_URL,
    results,
  };
  fs.writeFileSync(path.join(OUT_DIR, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log('All audits completed successfully!');
  console.log(JSON.stringify(summary, null, 2));
}

try {
  await run();
} catch (err) {
  console.error(err);
  process.exit(1);
}
