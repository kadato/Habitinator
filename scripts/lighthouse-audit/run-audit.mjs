import { chromium } from 'playwright';
import lighthouse from 'lighthouse';
import fs from 'node:fs';

async function run() {
  console.log('Launching browser with remote debugging port 9222...');
  const browser = await chromium.launch({
    headless: true,
    args: ['--remote-debugging-port=9222']
  });

  // Wait a bit to ensure it is listening
  await new Promise(resolve => setTimeout(resolve, 3000));
  
  const baseUrl = 'http://localhost:5033';
  
  // Open a page to do login
  const page = await browser.newPage();
  
  // Warm up the server to avoid JIT cold start on the first audit
  console.log('Warming up the server...');
  await page.goto(baseUrl);
  await new Promise(resolve => setTimeout(resolve, 2000));
  await page.reload();
  await new Promise(resolve => setTimeout(resolve, 1000));
  console.log('Server warmed up!');
  
  // 1. Audit landing page, anonymous
  console.log('Auditing anonymous landing page...');
  const landingReport = await runLighthouse(baseUrl, 9222, false);
  fs.writeFileSync('report-landing.json', JSON.stringify(landingReport.lhr, null, 2));
  fs.writeFileSync('report-landing.html', landingReport.report[1]); // output is array of [json, html]
  console.log('Anonymous landing page scores:', getScores(landingReport.lhr));
  
  // 2. Audit login page, anonymous
  console.log('Auditing anonymous login page...');
  const loginReport = await runLighthouse(`${baseUrl}/auth/login`, 9222, false);
  fs.writeFileSync('report-login.json', JSON.stringify(loginReport.lhr, null, 2));
  fs.writeFileSync('report-login.html', loginReport.report[1]);
  console.log('Anonymous login page scores:', getScores(loginReport.lhr));

  // 3. Login the user using Playwright page
  console.log('Logging in via guest-login...');
  await page.goto(`${baseUrl}/auth/login`);
  await page.click('form[action="/api/auth/guest-login"] button[type="submit"]');
  // Let's wait for navigation to "/" and wait for the board container to ensure we are logged in.
  await page.waitForURL(`${baseUrl}/`);
  await page.waitForSelector('.board-shell', { timeout: 15000 });
  console.log('Logged in successfully!');


  // 4. Audit authenticated board page
  console.log('Auditing authenticated board page...');
  const boardReport = await runLighthouse(baseUrl, 9222, true);
  fs.writeFileSync('report-board.json', JSON.stringify(boardReport.lhr, null, 2));
  fs.writeFileSync('report-board.html', boardReport.report[1]);
  console.log('Authenticated board page scores:', getScores(boardReport.lhr));

  // 5. Audit settings page
  console.log('Auditing settings page...');
  const settingsReport = await runLighthouse(`${baseUrl}/settings`, 9222, true);
  fs.writeFileSync('report-settings.json', JSON.stringify(settingsReport.lhr, null, 2));
  fs.writeFileSync('report-settings.html', settingsReport.report[1]);
  console.log('Settings page scores:', getScores(settingsReport.lhr));

  // 6. Audit statistics page
  console.log('Auditing statistics page...');
  const statsReport = await runLighthouse(`${baseUrl}/stats`, 9222, true);
  fs.writeFileSync('report-statistics.json', JSON.stringify(statsReport.lhr, null, 2));
  fs.writeFileSync('report-statistics.html', statsReport.report[1]);
  console.log('Statistics page scores:', getScores(statsReport.lhr));

  // Close the page and browser
  await page.close();
  await browser.close();
  console.log('All audits completed successfully!');
}

async function runLighthouse(url, port, keepStorage = false) {
  const options = {
    logLevel: 'info',
    output: ['json', 'html'],
    port: port,
    onlyCategories: ['performance', 'accessibility', 'best-practices', 'seo'],
    extraHeaders: { 'x-lighthouse': 'true' }
  };
  
  if (keepStorage) {
    options.disableStorageReset = true;
  }
  
  const runnerResult = await lighthouse(url, options);
  return runnerResult;
}

function getScores(lhr) {
  return {
    performance: lhr.categories.performance.score * 100,
    accessibility: lhr.categories.accessibility.score * 100,
    bestPractices: lhr.categories['best-practices'].score * 100,
    seo: lhr.categories.seo.score * 100
  };
}

try {
  await run();
} catch (err) {
  console.error(err);
  process.exit(1);
}
