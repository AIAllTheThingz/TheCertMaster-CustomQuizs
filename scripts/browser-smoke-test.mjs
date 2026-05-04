import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

function readArg(name, fallback = '') {
  const prefix = `--${name}=`;
  const arg = process.argv.slice(2).find(value => value.startsWith(prefix));
  return arg ? arg.slice(prefix.length) : fallback;
}

const base = readArg('base-url', process.env.QUIZAPI_BASE_URL || 'http://WIN2K22IIS01').replace(/\/+$/, '');
const email = readArg('admin-email', process.env.QUIZAPI_ADMIN_EMAIL || '');
const password = readArg('admin-password', process.env.QUIZAPI_ADMIN_PASSWORD || '');
const browserPath = readArg('browser-path', process.env.QUIZAPI_BROWSER_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe');
const headless = readArg('headless', process.env.QUIZAPI_HEADLESS || 'true').toLowerCase() !== 'false';
const allowWarnings = process.argv.includes('--allow-warnings') || process.env.QUIZAPI_ALLOW_WARNINGS === 'true';
const outDir = path.resolve(readArg('out-dir', process.env.QUIZAPI_SMOKE_OUT_DIR || path.join('.codex-local-run', 'browser-smoke-' + new Date().toISOString().replace(/[:.]/g, '-'))));
fs.mkdirSync(outDir, { recursive: true });

if (!email || !password) {
  throw new Error('Admin credentials are required. Set QUIZAPI_ADMIN_EMAIL and QUIZAPI_ADMIN_PASSWORD, or pass --admin-email and --admin-password.');
}

const results = [];
const consoleErrors = [];
const failedResponses = [];

function record(area, status, details = '') {
  results.push({ area, status, details });
  console.log(`${status === 'PASS' ? 'PASS' : status === 'WARN' ? 'WARN' : 'FAIL'} | ${area}${details ? ' | ' + details : ''}`);
}

async function snap(page, name) {
  await page.screenshot({ path: path.join(outDir, name + '.png'), fullPage: true });
}

async function visible(page, selector) {
  return await page.locator(selector).isVisible().catch(() => false);
}

async function anyVisible(page, selectors) {
  for (const selector of selectors) {
    if (await visible(page, selector)) return true;
  }
  return false;
}

async function text(page, selector) {
  return (await page.locator(selector).textContent().catch(() => '') || '').trim().replace(/\s+/g, ' ');
}

async function safeGoto(page, url, label) {
  const res = await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 }).catch(e => ({ error: e }));
  if (res?.error) {
    record(label, 'FAIL', `navigation failed: ${res.error.message}`);
    return false;
  }
  const status = typeof res.status === 'function' ? res.status() : undefined;
  if (status && status >= 400) record(label, 'FAIL', `HTTP ${status}`);
  else record(label, 'PASS', `loaded ${page.url()}`);
  await page.waitForLoadState('networkidle', { timeout: 3000 }).catch(() => {});
  return true;
}

const browser = await chromium.launch({
  executablePath: browserPath,
  headless,
  args: ['--no-sandbox', '--disable-gpu']
});
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, ignoreHTTPSErrors: true });
const page = await context.newPage();

page.on('console', msg => {
  if (['error', 'warning'].includes(msg.type())) consoleErrors.push(`${msg.type()}: ${msg.text()}`);
});
page.on('response', response => {
  const url = response.url();
  const status = response.status();
  if (status >= 400 && url.startsWith(base)) failedResponses.push(`${status} ${url}`);
});
page.on('pageerror', err => consoleErrors.push(`pageerror: ${err.message}`));

await safeGoto(page, `${base}/manage.html`, 'Management page');
await snap(page, '01-management-login');
if (await visible(page, '#loginForm')) {
  await page.locator('#email').fill(email);
  await page.locator('#password').fill(password);
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/api/auth/login'), { timeout: 15000 }).catch(() => null),
    page.locator('#loginForm button[type="submit"]').click()
  ]);
  await page.waitForLoadState('networkidle', { timeout: 3000 }).catch(() => {});
}
await page.waitForTimeout(1000);
const loggedIn = await page.evaluate(() => !!localStorage.getItem('jwtToken')).catch(() => false);
record('Admin login', loggedIn ? 'PASS' : 'FAIL', loggedIn ? 'JWT stored and admin UI attempted to load' : 'JWT not stored after login');
await snap(page, '02-management-after-login');

const manageChecks = [
  ['Upload form', '#uploadForm'],
  ['File table', '#fileTable'],
  ['User manager', '#userManager'],
  ['System status', '#systemStatusSection'],
  ['SMTP settings', '#smtpSettings'],
  ['Quiz admin', '#quizSection'],
  ['Question editor', '#questionEditorSection'],
  ['Quiz creator', '#quizCreatorSection'],
  ['Import history', '#historySection'],
  ['Pre-employment settings', '#preEmploymentSettings'],
  ['API reference', '#apiReferenceSection']
];
for (const [label, sel] of manageChecks) record(label, await visible(page, sel) ? 'PASS' : 'FAIL', sel);

if (await visible(page, '#refreshSystemStatusBtn')) {
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/api/admin/system-status'), { timeout: 15000 }).catch(() => null),
    page.locator('#refreshSystemStatusBtn').click()
  ]);
  await page.waitForTimeout(500);
  record('Refresh system status', 'PASS', (await text(page, '#systemStatusSection')).slice(0, 160));
}
if (await visible(page, '#userSearch')) {
  await page.locator('#userSearch').fill('admin');
  await page.waitForTimeout(300);
  record('User search field', 'PASS', 'accepts filtering text');
  await page.locator('#userSearch').fill('');
}

await safeGoto(page, `${base}/user.html#signin`, 'User page');
await page.locator('#loginEmail').fill(email).catch(() => {});
await page.locator('#loginPassword').fill(password).catch(() => {});
if (await visible(page, '#loginForm')) {
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/api/auth/login'), { timeout: 15000 }).catch(() => null),
    page.locator('#loginForm button[type="submit"]').click()
  ]);
  await page.waitForLoadState('networkidle', { timeout: 3000 }).catch(() => {});
}
await page.waitForTimeout(1000);
await snap(page, '03-user-profile');
record('User sign-in/profile', await visible(page, '#profileGrid') || (await text(page, '#loginStatus')).includes('successful') ? 'PASS' : 'WARN', (await text(page, '#loginStatus') || await text(page, '#accountBlock') || await text(page, '#status')).slice(0, 180));
for (const [label, sel] of [['Edit profile form', '#editProfileForm'], ['Change password form', '#changePasswordForm']]) {
  record(label, await visible(page, sel) ? 'PASS' : 'FAIL', sel);
}
record('Quiz history table/empty state', await anyVisible(page, ['#historyTable', '#historyEmpty']) ? 'PASS' : 'FAIL', '#historyTable or #historyEmpty');
if (await visible(page, '#nextPageBtn')) {
  const enabled = await page.locator('#nextPageBtn').isEnabled().catch(() => false);
  record('History pagination controls', 'PASS', `Next button ${enabled ? 'enabled' : 'disabled'} based on data volume`);
}

await safeGoto(page, `${base}/`, 'Quiz selection page');
await page.waitForTimeout(1500);
await snap(page, '04-quiz-selection');
const quizOptionCount = await page.locator('#quizList input[type="checkbox"]').count().catch(() => 0);
record('Quiz list loaded', quizOptionCount > 0 ? 'PASS' : 'FAIL', `${quizOptionCount} quiz options`);
if (await visible(page, '#btnSelectAll')) {
  await page.locator('#btnSelectAll').click();
  record('Select all quizzes', 'PASS', await text(page, '#selectionSummary'));
}
if (await visible(page, '#btnClearAll')) {
  await page.locator('#btnClearAll').click();
  record('Clear all quizzes', 'PASS', await text(page, '#selectionSummary'));
}
if (await visible(page, '#btnStart')) {
  const enabled = await page.locator('#btnStart').isEnabled().catch(() => false);
  if (enabled) {
    await page.locator('#btnStart').click();
    await page.waitForTimeout(500);
    record('Start validation without selection', 'PASS', await text(page, '#status'));
  } else {
    record('Start validation without selection', 'PASS', 'Start button is disabled when no quiz is selected');
  }
}

await safeGoto(page, `${base}/quiz.html`, 'Quiz runner page');
await page.waitForTimeout(1500);
await snap(page, '05-quiz-runner');
record('Quiz runner shell', (await visible(page, '#questionText')) || (await text(page, 'body')).includes('Quiz') ? 'PASS' : 'WARN', (await page.title()).trim());

await safeGoto(page, `${base}/preemployment.html`, 'Pre-employment page');
await page.waitForTimeout(1500);
await snap(page, '06-preemployment');
record('Pre-employment form/config', (await text(page, 'body')).includes('Pre') ? 'PASS' : 'WARN', 'page rendered');
const preInputs = await page.locator('input, textarea, select, button').count().catch(() => 0);
record('Pre-employment controls', preInputs > 0 ? 'PASS' : 'FAIL', `${preInputs} controls present`);

await safeGoto(page, `${base}/api.html`, 'API reference page');
await snap(page, '07-api-reference');
record('API reference navigation', (await text(page, 'body')).includes('API') ? 'PASS' : 'WARN', 'page rendered');

await safeGoto(page, `${base}/upload.html`, 'Upload redirect page');
await page.waitForLoadState('domcontentloaded', { timeout: 10000 }).catch(() => {});
record('Upload redirect', page.url().includes('/manage.html') ? 'PASS' : 'WARN', page.url());

const apiContext = context.request;
const token = await page.evaluate(() => localStorage.getItem('jwtToken') || localStorage.getItem('apiJwtToken') || '');
const apiChecks = [
  ['GET /api/system/version', '/api/system/version'],
  ['GET /api/quiz', '/api/quiz'],
  ['GET /api/categories', '/api/categories'],
  ['GET /api/profile', '/api/profile?page=1&pageSize=5'],
  ['GET /api/users', '/api/users'],
  ['GET /api/files', '/api/files'],
  ['GET /api/admin/system-status', '/api/admin/system-status'],
  ['GET /api/admin/smtp', '/api/admin/smtp'],
  ['GET /api/admin/active-directory', '/api/admin/active-directory'],
  ['GET /api/import/history', '/api/import/history?take=5'],
  ['GET /api/admin/reports/quiz-usage', '/api/admin/reports/quiz-usage'],
  ['GET /api/preemployment/config', '/api/preemployment/config'],
  ['GET /api/preemployment/submissions', '/api/preemployment/submissions?take=10']
];
for (const [label, url] of apiChecks) {
  const r = await apiContext.get(base + url, { headers: token ? { Authorization: `Bearer ${token}` } : {}, timeout: 15000 }).catch(e => ({ error: e }));
  if (r.error) record(label, 'FAIL', r.error.message);
  else record(label, r.status() < 400 ? 'PASS' : 'FAIL', `HTTP ${r.status()}`);
}

const uniqueFailures = [...new Set(failedResponses)].filter(x => !x.includes('/favicon.ico'));
if (uniqueFailures.length) record('HTTP failures observed in browser', 'WARN', uniqueFailures.slice(0, 10).join(' ; '));
else record('HTTP failures observed in browser', 'PASS', 'none');
const uniqueConsole = [...new Set(consoleErrors)];
if (uniqueConsole.length) record('Console warnings/errors', 'WARN', uniqueConsole.slice(0, 10).join(' ; '));
else record('Console warnings/errors', 'PASS', 'none');

fs.writeFileSync(path.join(outDir, 'summary.json'), JSON.stringify({ base, outDir, results, failedResponses: uniqueFailures, consoleErrors: uniqueConsole }, null, 2));
console.log('ARTIFACT_DIR=' + outDir);
await browser.close();

const failCount = results.filter(result => result.status === 'FAIL').length;
const warnCount = results.filter(result => result.status === 'WARN').length;
if (failCount > 0 || (!allowWarnings && warnCount > 0)) {
  console.error(`Smoke test completed with ${failCount} failure(s) and ${warnCount} warning(s).`);
  process.exitCode = 1;
} else {
  console.log(`Smoke test passed with ${warnCount} warning(s).`);
}
