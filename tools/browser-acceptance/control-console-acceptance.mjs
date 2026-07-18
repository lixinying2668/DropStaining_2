/**
 * control-console-acceptance.mjs  (w3-fix)
 *
 * Browser acceptance tests for /control-console (twin/index.html).
 *
 * What this suite verifies (each case throws on failure → process.exitCode=1):
 *   TC-01  Admin login + logout via UI
 *   TC-02  Operator login + logout via UI
 *   TC-03  Session expiry (401) returns UI to login screen
 *   TC-04  Mock reagent scan completes (twin mode)
 *   TC-05  Precheck shows >=11 steps with explicit statuses (blocking reasons surfaced)
 *   TC-06  IHC intake — TRUE IHC task creation:
 *            * 1st prompt answered with 'IHC', 2nd prompt with a real slot only
 *            * Pre-checked: a published default IHC workflow exists
 *            * POST /api/tasks/ihc returns 200 with non-empty taskId and experimentType='IHC'
 *            * Frozen antibody ConfirmedPrimaryAntibodyCode (read from
 *              /api/operator/snapshot for the new slide) equals the workflow PRIMARY step
 *              ReagentCode (read dynamically from /api/workflows + /steps)
 *            * Zero calls to /api/lis/mock-query
 *   TC-07  Workflow info visible BEFORE the slot prompt fires:
 *            * Captures detailBox content at the moment the 2nd prompt appears
 *              (via a monkey-patched window.prompt that records DOM state synchronously
 *              before the native prompt blocks the main JS thread)
 *            * Asserts detailBox contains: 已选流程 (workflow name), 流程版本 (version
 *              label), 流程一抗编码 (primary antibody code) — all dynamically read from
 *              /api/channel-batches/experiment-type-selection response
 *            * No fixed delay(1000); no swallowed waitForFunction failures
 *   TC-08  Mock runtime reset
 *   TC-09  Debug mode — Mock buttons hidden, precheck shows real-device failure reasons
 *   TC-10  Production mode — Mock buttons hidden
 *   TC-11  Twin mock scan single API call completes
 *   TC-12  Precheck single-module detection returns a definitive status
 *
 * Harness:
 *   - DEFAULT: spawns an isolated dotnet process from bin/tmp-build-w3fix/Stainer.Web.dll
 *     on a random free port, with a temp SQLite DB under D:\tmp, mock-demo-data seeded,
 *     and tear-down on exit. Never touches the user's running Stainer.Web.exe.
 *   - External mode (STAINER_BASE_URL set): REFUSED unless
 *     STAINER_ALLOW_DESTRUCTIVE_EXTERNAL=1 is also set, because every case performs
 *     writes (login, task creation, mock scan, mock reset) that would pollute the
 *     target service.
 *
 * Environment:
 *   STAINER_BASE_URL                       — opt-in external target (requires opt-in below)
 *   STAINER_ALLOW_DESTRUCTIVE_EXTERNAL=1   — acknowledge writes against external URL
 *   STAINER_BROWSER_PORT                   — force self-start port (debugging)
 *   STAINER_KEEP_BROWSER_TEMP=1            — keep temp DB/logs for inspection
 *   STAINER_TEMP_ROOT                      — override temp parent (default D:\tmp)
 *   PLAYWRIGHT_CHROME_EXECUTABLE           — Chrome/Edge path
 *   TMP / TEMP / TMPDIR                    — already redirected to D:\tmp by default
 *
 * Usage:
 *   node tools/browser-acceptance/control-console-acceptance.mjs
 */

import { chromium, request as playwrightRequest } from 'playwright-core';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import fs from 'node:fs';
import fsp from 'node:fs/promises';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const webProjectRoot = path.join(repoRoot, 'backend', 'Stainer.Web');
// Per task: build output goes to bin/tmp-build-w3fix (bin/ is gitignored).
const webDll = path.join(repoRoot, 'bin', 'tmp-build-w3fix', 'Stainer.Web.dll');
const viewport = { width: 1920, height: 1080 };

// Redirect temp dirs to D:\tmp so the test suite does not fill C: with SQLite temp DBs
// (per project MEMORY.md: test-temp-disk-gotcha.md).
if (!process.env.TMPDIR) process.env.TMPDIR = 'D:\\tmp';
if (!process.env.TMP) process.env.TMP = 'D:\\tmp';
if (!process.env.TEMP) process.env.TEMP = 'D:\\tmp';
const TEMP_ROOT_PARENT = process.env.STAINER_TEMP_ROOT || 'D:\\tmp';

const DEFAULT_PASSWORD = '123456'; // seeder convention for operator/admin demo accounts

let serverProcess;
let browser;
let tempRoot;
const baseURL = process.env.STAINER_BASE_URL || '';
const externalOptIn = process.env.STAINER_ALLOW_DESTRUCTIVE_EXTERNAL === '1';
const selfStarted = !baseURL;
let activeBaseURL = '';
const serverOutput = [];

// ─── assert / utils ───────────────────────────────────────────────────

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function commandId(prefix) {
  return `${prefix}-${randomUUID()}`;
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function getFreePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      const port = typeof address === 'object' && address ? address.port : 0;
      server.close(() => resolve(port));
    });
  });
}

function resolveBrowserExecutable() {
  const configured = process.env.PLAYWRIGHT_CHROME_EXECUTABLE;
  if (configured && fs.existsSync(configured)) return configured;
  const candidates = [
    path.join(process.env.ProgramFiles || 'C:\\Program Files', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    path.join(process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    path.join(process.env.ProgramFiles || 'C:\\Program Files', 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
    path.join(process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)', 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
  ];
  const found = candidates.find(candidate => fs.existsSync(candidate));
  assert(found, 'No Chrome/Edge found. Set PLAYWRIGHT_CHROME_EXECUTABLE.');
  return found;
}

// ─── server lifecycle ─────────────────────────────────────────────────

function startServer(url, paths) {
  const env = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: url,
    STAINER_DATABASE_URL: `Data Source=${paths.dbPath}`,
    Device__Mode: 'Mock',
    MachineExecutor__LeasePath: paths.lockPath,
    Safety__LogDirectory: paths.logDir,
    Database__BackupDirectory: paths.backupDir,
  };
  serverProcess = spawn('dotnet', [webDll, '--contentRoot', webProjectRoot], {
    cwd: repoRoot, env, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
  });
  serverProcess.stdout.on('data', rememberServerOutput);
  serverProcess.stderr.on('data', rememberServerOutput);
}

function rememberServerOutput(chunk) {
  serverOutput.push(chunk.toString());
  while (serverOutput.length > 80) serverOutput.shift();
}

async function waitForHealth(url) {
  const deadline = Date.now() + 120000;
  while (Date.now() < deadline) {
    if (serverProcess && serverProcess.exitCode !== null) {
      throw new Error(`Server exited early code=${serverProcess.exitCode}.\n${serverOutput.join('')}`);
    }
    try {
      const response = await fetch(`${url}/health`);
      if (response.ok) return;
    } catch { /* still starting */ }
    await delay(500);
  }
  throw new Error(`Timed out waiting for ${url}/health.\n${serverOutput.join('')}`);
}

async function stopServer() {
  if (!serverProcess || serverProcess.exitCode !== null) return;
  serverProcess.kill();
  const deadline = Date.now() + 10000;
  while (Date.now() < deadline && serverProcess.exitCode === null) await delay(250);
  if (serverProcess.exitCode === null) serverProcess.kill('SIGKILL');
}

// ─── API helpers ──────────────────────────────────────────────────────

async function loginApi(role) {
  const api = await playwrightRequest.newContext({ baseURL: activeBaseURL });
  const response = await api.post('/api/login', {
    data: { username: role, password: DEFAULT_PASSWORD, role },
  });
  assert(response.ok(), `API login as ${role} failed: ${response.status()} ${await response.text()}`);
  return api;
}

async function postOk(api, url, data) {
  const response = await api.post(url, { data });
  assert(response.ok(), `POST ${url} failed: ${response.status()} ${await response.text()}`);
  return await response.json();
}

async function getOk(api, url) {
  const response = await api.get(url);
  assert(response.ok(), `GET ${url} failed: ${response.status()} ${await response.text()}`);
  return await response.json();
}

/**
 * Resolve the published default IHC workflow's PRIMARY step ReagentCode dynamically
 * (expected frozen antibody). Source of truth: /api/workflows → versions where
 * status='Published' AND defaultExperimentType='IHC'; then /api/workflow-versions/{id}/steps.
 */
async function resolveExpectedIhcAntibody(api) {
  const workflows = await getOk(api, '/api/workflows');
  let ihcVersion = null;
  let ihcWorkflow = null;
  for (const w of workflows || []) {
    for (const v of w.versions || []) {
      if (v.status === 'Published' && v.defaultExperimentType === 'IHC') {
        ihcWorkflow = w;
        ihcVersion = v;
        break;
      }
    }
    if (ihcVersion) break;
  }
  assert(ihcVersion,
    'No published IHC workflow marked as default for IHC found in seed data. ' +
    'Ensure mock-demo-data/seed has been called and the IHC workflow is published + set as default.');
  const steps = await getOk(api, `/api/workflow-versions/${encodeURIComponent(ihcVersion.id)}/steps`);
  const primaryStep = (steps || []).find(s => String(s.majorStepCode || '').toUpperCase().startsWith('PRIMARY'));
  assert(primaryStep,
    `IHC workflow ${ihcWorkflow.code} v${ihcVersion.versionNo} has no PRIMARY_* step; cannot resolve antibody.`);
  const code = primaryStep.reagentCode;
  assert(typeof code === 'string' && code.trim() !== '',
    `IHC workflow ${ihcWorkflow.code} PRIMARY step is missing a non-empty reagentCode.`);
  return {
    workflowCode: ihcWorkflow.code,
    workflowName: ihcWorkflow.name,
    versionLabel: ihcVersion.versionLabel || `v${ihcVersion.versionNo}`,
    versionId: ihcVersion.id,
    primaryAntibodyCode: code.trim(),
  };
}

// ─── test utilities ───────────────────────────────────────────────────

async function waitForLoginHidden(page, timeoutMs = 15000) {
  await page.waitForFunction(
    () => document.getElementById('loginScreen')?.classList.contains('hidden'),
    null,
    { timeout: timeoutMs }
  );
}

async function waitForLoginVisible(page, timeoutMs = 15000) {
  await page.waitForFunction(
    () => !document.getElementById('loginScreen')?.classList.contains('hidden'),
    null,
    { timeout: timeoutMs }
  );
}

async function loginAndEnter(context, role = 'admin', username = role) {
  const res = await context.request.post('/api/login', {
    data: { username, password: DEFAULT_PASSWORD, role },
  });
  assert(res.ok(), `API login as ${role} failed: ${res.status()} ${await res.text()}`);
  const page = await context.newPage();
  await page.goto(`${activeBaseURL}/control-console`);
  await waitForLoginHidden(page, 15000);
  return page;
}

/**
 * Monkey-patch window.prompt so that every time the page calls prompt(), the
 * info-panel (#detailBox) and log (#logList) text are captured synchronously
 * BEFORE the native prompt blocks the main JS thread. Playwright's dialog handler
 * cannot page.evaluate() while the prompt is open (main thread is suspended), so
 * we snapshot inside the patched function itself.
 *
 * Stored on window.__ccPromptSnapshots as a list of {message, defaultValue, detail, logText, at}.
 */
const PROMPT_CAPTURE_SCRIPT = `
(() => {
  const nativePrompt = window.prompt;
  window.__ccPromptSnapshots = window.__ccPromptSnapshots || [];
  window.prompt = function(message, defaultValue) {
    let detail = '';
    let logText = '';
    try { detail = document.getElementById('detailBox')?.textContent || ''; } catch (e) {}
    try { logText = document.getElementById('logList')?.textContent || ''; } catch (e) {}
    window.__ccPromptSnapshots.push({
      message: String(message || ''),
      defaultValue: String(defaultValue || ''),
      detail,
      logText,
      at: Date.now()
    });
    return nativePrompt.call(this, message, defaultValue);
  };
})();
`;

async function installPromptCapture(context) {
  await context.addInitScript(PROMPT_CAPTURE_SCRIPT);
}

async function readPromptSnapshots(page) {
  return await page.evaluate(() => window.__ccPromptSnapshots || []);
}

async function resetPromptSnapshots(page) {
  await page.evaluate(() => { if (window.__ccPromptSnapshots) window.__ccPromptSnapshots.length = 0; });
}

/**
 * Find the slide for a given channel/slot in /api/operator/snapshot.
 * Slot normalization: slot "1" on channel letter 'A' → 'A-01'.
 */
function normalizeSlotCode(letter, slotNo) {
  return `${letter}-${String(slotNo).padStart(2, '0')}`;
}

async function findSlideInSnapshot(api, letter, slotNo) {
  const snap = await getOk(api, '/api/operator/snapshot');
  const expectedSlot = normalizeSlotCode(letter, slotNo);
  for (const ch of snap.channels || []) {
    if (String(ch.drawerCode || '').toUpperCase() !== letter) continue;
    for (const slide of ch.slides || []) {
      if (String(slide.slotCode || '').toUpperCase() === expectedSlot) return slide;
    }
  }
  return null;
}

// ─── test cases ────────────────────────────────────────────────────────

async function testLoginLogout(context) {
  console.log('[TC-01] Login / logout');
  const page = await context.newPage();
  await page.goto(`${activeBaseURL}/control-console`);
  await page.waitForSelector('#loginScreen', { state: 'visible', timeout: 10000 });

  await page.fill('#loginUsername', 'admin');
  await page.fill('#loginPassword', DEFAULT_PASSWORD);
  await page.click('#adminLoginBtn');
  await waitForLoginHidden(page);

  const loginHidden = await page.evaluate(() =>
    document.getElementById('loginScreen')?.classList.contains('hidden')
  );
  assert(loginHidden, 'Login screen should be hidden after admin login.');

  await page.waitForSelector('#userBtn', { state: 'visible', timeout: 10000 });

  await page.click('#userBtn');
  const logoutBtn = page.locator('#userLogoutBtn');
  await logoutBtn.waitFor({ state: 'visible', timeout: 5000 });
  await logoutBtn.click();
  await waitForLoginVisible(page);

  const loginVisible = await page.evaluate(() =>
    !document.getElementById('loginScreen')?.classList.contains('hidden')
  );
  assert(loginVisible, 'Login screen should be visible after logout.');
  await page.close();
  console.log('[TC-01] PASS');
}

async function testOperatorLoginLogout(context) {
  console.log('[TC-02] Operator login / logout');
  const page = await context.newPage();
  await page.goto(`${activeBaseURL}/control-console`);
  await page.waitForSelector('#loginScreen', { state: 'visible', timeout: 10000 });

  await page.fill('#loginUsername', 'operator');
  await page.fill('#loginPassword', DEFAULT_PASSWORD);
  await page.click('#operatorLoginBtn');
  await waitForLoginHidden(page);

  await page.waitForSelector('#modeTwinBtn', { state: 'visible', timeout: 10000 });

  await page.click('#userBtn');
  const logoutBtn = page.locator('#userLogoutBtn');
  await logoutBtn.waitFor({ state: 'visible', timeout: 5000 });
  await logoutBtn.click();
  await waitForLoginVisible(page);

  await page.close();
  console.log('[TC-02] PASS');
}

async function testSessionExpiry(context) {
  console.log('[TC-03] Session expiry detection');
  const page = await context.newPage();
  await page.goto(`${activeBaseURL}/control-console`);
  await page.waitForSelector('#loginScreen', { state: 'visible', timeout: 10000 });

  await context.request.post('/api/login', {
    data: { username: 'operator', password: DEFAULT_PASSWORD, role: 'operator' },
  });
  await page.reload();
  await waitForLoginHidden(page);

  await context.request.post('/api/logout');

  await page.waitForFunction(() => {
    return !document.getElementById('loginScreen')?.classList.contains('hidden');
  }, null, { timeout: 20000 });

  const loginVisible = await page.evaluate(() =>
    !document.getElementById('loginScreen')?.classList.contains('hidden')
  );
  assert(loginVisible, 'Login screen should reappear after session expiry (401).');
  await page.close();
  console.log('[TC-03] PASS');
}

async function testMockReagentScan(context) {
  console.log('[TC-04] Mock reagent scan');
  const page = await loginAndEnter(context, 'admin');

  await page.click('#modeTwinBtn');
  await page.click('#precheckTab');
  await page.waitForSelector('#precheckPane.active', { timeout: 5000 });

  await page.click('#scanReagentsBtn');

  // Deterministic wait for scan completion log/info — no fixed delay.
  await page.waitForFunction(() => {
    const logText = document.getElementById('logList')?.textContent || '';
    const detail = document.getElementById('detailBox')?.textContent || '';
    return logText.includes('Mock 扫描完成') || logText.includes('试剂区 Mock')
      || detail.includes('Mock 扫描完成') || detail.includes('Mock 扫描失败');
  }, null, { timeout: 60000 });

  // Explicitly fail if the scan itself errored.
  const detail = await page.evaluate(() => document.getElementById('detailBox')?.textContent || '');
  assert(!detail.includes('Mock 扫描失败'),
    `Mock reagent scan reported failure: "${detail.substring(0, 200)}"`);

  await page.close();
  console.log('[TC-04] PASS');
}

async function testPrecheckBlockingReasons(context) {
  console.log('[TC-05] Precheck with blocking reasons');
  const page = await loginAndEnter(context, 'admin');

  await page.click('#precheckTab');
  await page.waitForSelector('#precheckPane.active', { timeout: 5000 });

  await page.click('#precheckRunAllBtn');

  await page.waitForFunction(() => {
    const steps = document.querySelectorAll('.precheck-step');
    if (!steps.length) return false;
    for (const step of steps) {
      const text = step.querySelector('.precheck-status')?.textContent || '';
      if (text !== '检查中') return true;
    }
    return false;
  }, null, { timeout: 30000 });

  const precheckResults = await page.evaluate(() => {
    const steps = document.querySelectorAll('.precheck-step');
    return Array.from(steps).map(step => ({
      name: step.querySelector('.precheck-name')?.textContent || '',
      status: step.querySelector('.precheck-status')?.textContent || '',
    }));
  });

  assert(precheckResults.length >= 11,
    `Expected at least 11 precheck steps, got ${precheckResults.length}`);
  console.log('[TC-05] Precheck results:');
  for (const r of precheckResults) {
    console.log(`  ${r.name}: ${r.status}`);
  }

  const detailText = await page.evaluate(() =>
    document.getElementById('detailBox')?.textContent || ''
  );
  assert(detailText.length > 0, 'Precheck should render a result summary in detail box.');

  await page.close();
  console.log('[TC-05] PASS');
}

/**
 * TC-06: TRUE IHC intake.
 * Steps:
 *   1. Pre-check via API: published default IHC workflow exists with a resolvable
 *      PRIMARY step ReagentCode (expected antibody).
 *   2. Open channel A (letter A, channelId=1). Fresh isolated DB → no existing batch.
 *   3. Trigger intakeChannelSamples(1). Handle dialogs sequentially:
 *        1st prompt → accept('IHC')
 *        2nd prompt → accept('1')   (slot input only — no antibody entered)
 *   4. Capture POST /api/tasks/ihc response: ok=true, taskId non-empty,
 *      experimentType='IHC'. No 409.
 *   5. After completion, query /api/operator/snapshot, find slide on A-01,
 *      assert confirmedPrimaryAntibodyCode === workflow PRIMARY ReagentCode.
 *   6. Assert zero requests to /api/lis/mock-query across the whole case.
 * Any failure → throws → process.exitCode=1.
 */
async function testIhcIntakeSlotOnly(context, api) {
  console.log('[TC-06] IHC intake — true IHC task creation');
  const expected = await resolveExpectedIhcAntibody(api);
  console.log(`  Expected IHC workflow: ${expected.workflowName} (${expected.workflowCode}) ` +
    `v${expected.versionLabel}, PRIMARY antibody='${expected.primaryAntibodyCode}'`);

  const page = await loginAndEnter(context, 'admin');
  await resetPromptSnapshots(page);

  // Track all API requests to assert no /api/lis/mock-query call.
  const apiCalls = [];
  page.on('request', req => {
    const url = req.url();
    if (url.includes('/api/')) apiCalls.push(url);
  });

  // Capture the IHC task creation response and any selection/experiment-type response.
  const intakeResponsePromise = page.waitForResponse(
    resp => resp.url().includes('/api/tasks/ihc') && resp.request().method() === 'POST',
    { timeout: 30000 }
  ).catch(err => {
    throw new Error(`Expected POST /api/tasks/ihc was not observed: ${err.message}`);
  });

  // Dialog handler: 1st=experiment type → 'IHC'; 2nd=slot → '1'; anything else → dismiss.
  let dialogCount = 0;
  const dialogHandler = async dialog => {
    dialogCount++;
    if (dialogCount === 1) {
      await dialog.accept('IHC');
    } else if (dialogCount === 2) {
      await dialog.accept('1');
    } else {
      await dialog.dismiss();
    }
  };
  page.on('dialog', dialogHandler);

  // Trigger intake on channel 1 (letter A) — fresh in isolated DB.
  // Top-level async function — page.evaluate awaits it to completion.
  const evaluateError = page.evaluate(() => intakeChannelSamples(1));
  // Don't await yet — we need dialog handler + response listener to run concurrently.

  const intakeResponse = await intakeResponsePromise;
  const intakeBody = await intakeResponse.json().catch(async () => {
    const text = await intakeResponse.text();
    throw new Error(`/api/tasks/ihc returned non-JSON (status ${intakeResponse.status()}): ${text.substring(0, 200)}`);
  });

  assert(intakeResponse.ok() && intakeResponse.status() === 200,
    `IHC task creation should return HTTP 200, got ${intakeResponse.status()}: ${JSON.stringify(intakeBody).substring(0, 300)}`);
  assert(intakeBody.ok === true,
    `IHC task response.ok must be true; got: ${JSON.stringify(intakeBody).substring(0, 300)}`);
  assert(intakeBody.experimentType === 'IHC',
    `IHC task response.experimentType must be 'IHC'; got: ${JSON.stringify(intakeBody).substring(0, 300)}`);
  assert(typeof intakeBody.taskId === 'string' && intakeBody.taskId.trim() !== '',
    `IHC task response.taskId must be a non-empty string; got: ${JSON.stringify(intakeBody).substring(0, 300)}`);

  // Wait for the intake function to fully complete (task created + UI updated).
  await evaluateError;
  page.off('dialog', dialogHandler);

  // Verify the frozen antibody on the persisted slide via the operator snapshot.
  const slide = await findSlideInSnapshot(api, 'A', 1);
  assert(slide,
    `After IHC intake on channel A slot 1, expected a slide at A-01 in /api/operator/snapshot; none found.`);
  assert(String(slide.confirmedPrimaryAntibodyCode || '').trim() === expected.primaryAntibodyCode,
    `Slide A-01 confirmedPrimaryAntibodyCode='${slide.confirmedPrimaryAntibodyCode}' ` +
      `does not match workflow PRIMARY reagentCode='${expected.primaryAntibodyCode}'.`);
  // Also assert the snapshot's workflow pointers match the selected IHC workflow.
  assert(slide.workflowVersionId === expected.versionId,
    `Slide A-01 workflowVersionId='${slide.workflowVersionId}' does not match expected IHC version '${expected.versionId}'.`);

  // Verify no /api/lis/mock-query call happened anywhere in this case.
  const lisCalls = apiCalls.filter(url => url.includes('/api/lis/mock-query'));
  assert(lisCalls.length === 0,
    `IHC intake must NOT call /api/lis/mock-query. Observed calls: ${lisCalls.join(', ')}`);

  await page.close();
  console.log(`[TC-06] PASS — taskId=${intakeBody.taskId}, antibody='${slide.confirmedPrimaryAntibodyCode}'`);
}

/**
 * TC-07: Workflow/version/primary-antibody visible in info panel BEFORE the slot prompt.
 *
 * We can't page.evaluate() while window.prompt() is blocking the main thread, so we
 * capture detailBox content from inside a monkey-patched prompt (installPromptCapture).
 * The snapshot for the 2nd prompt is the DOM state the operator saw right before the
 * slot input was requested — which is what we assert against.
 *
 * Asserts (all must hold in the 2nd prompt's captured detail):
 *   - 已选流程:<workflowName>     (workflow name)
 *   - 流程版本:<versionLabel>     (version label)
 *   - 流程一抗编码:<antibody>     (primary antibody code)
 *   - 实验类型：IHC               (experiment type echo)
 *
 * No fixed delay(1000); waitForFunction failures are NOT swallowed.
 */
async function testWorkflowInfoVisibleBeforePrompt(context, api) {
  console.log('[TC-07] Workflow info visible before slot prompt');
  const expected = await resolveExpectedIhcAntibody(api);
  console.log(`  Expecting info panel to show: ${expected.workflowName} / ${expected.versionLabel} / antibody='${expected.primaryAntibodyCode}'`);

  const page = await loginAndEnter(context, 'admin');
  await resetPromptSnapshots(page);

  // Capture the experiment-type-selection response (the authoritative source the UI
  // uses to populate the info panel). On a fresh channel this call fires between
  // the two prompts.
  let selectionResponseBody = null;
  page.on('response', async resp => {
    if (resp.url().includes('/api/channel-batches/experiment-type-selection')
        && resp.request().method() === 'POST'
        && resp.ok()) {
      try { selectionResponseBody = await resp.json(); } catch { /* ignore parse error */ }
    }
  });

  // Dialog handler: 1st prompt → 'IHC'; 2nd prompt → dismiss (no task created).
  let dialogCount = 0;
  const dialogHandler = async dialog => {
    dialogCount++;
    if (dialogCount === 1) {
      await dialog.accept('IHC');
    } else if (dialogCount === 2) {
      // Dismiss the slot prompt — we're only verifying pre-prompt state.
      await dialog.dismiss();
    } else {
      await dialog.dismiss();
    }
  };
  page.on('dialog', dialogHandler);

  // Trigger intake on channel 2 (letter B) — fresh in isolated DB, distinct from TC-06's A.
  const evaluateDone = page.evaluate(() => intakeChannelSamples(2));

  // Wait deterministically for the 2nd prompt to have fired (snapshot count >= 2).
  // No fixed delay — this is a real condition the page reaches only after the
  // info panel has been populated and the slot prompt has been requested.
  await page.waitForFunction(
    () => (window.__ccPromptSnapshots || []).length >= 2,
    null,
    { timeout: 20000 }
  );

  const snapshots = await readPromptSnapshots(page);
  assert(snapshots.length >= 2,
    `Expected at least 2 prompt snapshots (experiment type + slot); got ${snapshots.length}.`);

  const slotPromptSnapshot = snapshots[1];
  console.log(`  Captured pre-slot-prompt detailBox (${slotPromptSnapshot.message.substring(0, 60).replace(/\n/g, ' ')}…):\n    ${slotPromptSnapshot.detail.substring(0, 300).replace(/\n/g, ' ')}`);

  // Required assertions on the captured info panel content.
  assert(slotPromptSnapshot.detail.includes('IHC'),
    'Info panel before slot prompt should reference IHC experiment type.');
  assert(slotPromptSnapshot.detail.includes(`已选流程：${expected.workflowName}`),
    `Info panel should contain '已选流程：${expected.workflowName}'. Got: "${slotPromptSnapshot.detail.substring(0, 300)}"`);
  assert(slotPromptSnapshot.detail.includes(`流程版本：${expected.versionLabel}`),
    `Info panel should contain '流程版本：${expected.versionLabel}'. Got: "${slotPromptSnapshot.detail.substring(0, 300)}"`);
  assert(slotPromptSnapshot.detail.includes(`流程一抗编码：${expected.primaryAntibodyCode}`),
    `Info panel should contain '流程一抗编码：${expected.primaryAntibodyCode}'. Got: "${slotPromptSnapshot.detail.substring(0, 300)}"`);

  // Cross-check: if the selection API response was captured, its primaryAntibodyCode
  // must agree with the workflow-derived expected value.
  if (selectionResponseBody) {
    const sel = selectionResponseBody.primaryAntibodyCode;
    assert(typeof sel === 'string' && sel.trim() === expected.primaryAntibodyCode,
      `/api/channel-batches/experiment-type-selection returned primaryAntibodyCode='${sel}' ` +
        `which disagrees with workflow PRIMARY reagentCode='${expected.primaryAntibodyCode}'.`);
  }

  // Wait for the dismissed intake to settle (deterministic).
  await page.waitForFunction(() => {
    const logText = document.getElementById('logList')?.textContent || '';
    return logText.includes('未选择') || logText.includes('取消') || logText.includes('入库');
  }, null, { timeout: 15000 });

  await evaluateDone;
  page.off('dialog', dialogHandler);

  await page.close();
  console.log('[TC-07] PASS');
}

async function testMockRuntimeReset(context) {
  // Verify the Mock runtime reset success path end-to-end: confirmation dialog →
  // POST /api/mock-runtime/reset → HTTP 200 → detail panel reports success. The reset
  // must actually succeed (return 200 and show "Mock 已恢复初始状态"); a 5xx or a failure
  // panel is a hard failure, not a pass. Backend correctness is also guarded by
  // MockBackendEndToEndAcceptanceTests; this TC guards the operator-facing success path.
  console.log('[TC-08] Mock runtime reset — real success path');
  const page = await loginAndEnter(context, 'admin');

  await page.click('#modeTwinBtn');
  await page.click('#precheckTab');
  await page.waitForSelector('#precheckPane.active', { timeout: 5000 });

  // Capture the outgoing POST so we can correlate the UI outcome with HTTP status.
  const resetResponsePromise = page.waitForResponse(
    resp => resp.url().includes('/api/mock-runtime/reset') && resp.request().method() === 'POST',
    { timeout: 30000 }
  );

  page.once('dialog', async dialog => { await dialog.accept(); });
  await page.click('#mockRuntimeResetBtn');

  const resetResponse = await resetResponsePromise;
  assert(resetResponse.ok(),
    `Mock runtime reset must return HTTP 200. Got: ${resetResponse.status()} ${await resetResponse.text().catch(() => '')}`);

  // Deterministic wait until the detail panel shows the SUCCESS title
  // ("Mock 已恢复初始状态"; the in-progress title lacks 已, the failure title adds 失败).
  await page.waitForFunction(
    () => (document.getElementById('detailBox')?.textContent || '').includes('Mock 已恢复初始状态'),
    null,
    { timeout: 30000 }
  );

  const detailText = await page.evaluate(() =>
    document.getElementById('detailBox')?.textContent || ''
  );
  assert(detailText.includes('Mock 已恢复初始状态') && !detailText.includes('恢复初始状态失败'),
    `Mock runtime reset should report success in the detail panel. Got: "${detailText.substring(0, 200)}"`);

  await page.close();
  console.log('[TC-08] PASS — reset returned 200 and UI reported success');
}

async function testDebugModeGates(context) {
  console.log('[TC-09] Debug mode — Real adapter gates');
  const page = await loginAndEnter(context, 'admin');

  await page.click('#modeDebugBtn');
  await page.waitForFunction(() => {
    const settingsText = document.getElementById('settingsModeText')?.textContent || '';
    return settingsText.includes('调试');
  }, null, { timeout: 5000 });

  const scanBtnHidden = await page.evaluate(() =>
    document.getElementById('scanReagentsBtn')?.hidden === true
  );
  assert(scanBtnHidden, 'Mock scan button should be hidden in debug mode.');

  const resetBtnHidden = await page.evaluate(() =>
    document.getElementById('mockRuntimeResetBtn')?.hidden === true
  );
  assert(resetBtnHidden, 'Mock runtime reset should be hidden in debug mode.');

  await page.click('#precheckTab');
  await page.waitForSelector('#precheckPane.active', { timeout: 5000 });
  await page.click('#precheckRunAllBtn');

  await page.waitForFunction(() => {
    const steps = document.querySelectorAll('.precheck-step');
    if (!steps.length) return false;
    for (const step of steps) {
      const text = step.querySelector('.precheck-status')?.textContent || '';
      if (text !== '检查中') return true;
    }
    return false;
  }, null, { timeout: 30000 });

  const debugResults = await page.evaluate(() => {
    const steps = document.querySelectorAll('.precheck-step');
    return Array.from(steps).map(step => ({
      name: step.querySelector('.precheck-name')?.textContent || '',
      status: step.querySelector('.precheck-status')?.textContent || '',
    }));
  });

  const failedChecks = debugResults.filter(r =>
    r.status === '异常' || r.status === '不可用'
  );
  assert(failedChecks.length > 0,
    `Debug mode without hardware should have failed checks; got all passing: ${JSON.stringify(debugResults)}`);

  await page.close();
  console.log('[TC-09] PASS');
}

async function testProductionModeGates(context) {
  console.log('[TC-10] Production mode — Real adapter gates');
  const page = await loginAndEnter(context, 'admin');

  await page.click('#modeProductionBtn');
  await page.waitForFunction(() => {
    const settingsText = document.getElementById('settingsModeText')?.textContent || '';
    return settingsText.includes('生产');
  }, null, { timeout: 5000 });

  const scanBtnHidden = await page.evaluate(() =>
    document.getElementById('scanReagentsBtn')?.hidden === true
  );
  assert(scanBtnHidden, 'Mock scan button should be hidden in production mode.');

  await page.close();
  console.log('[TC-10] PASS');
}

async function testTwinMockScanDialogHandling(context) {
  console.log('[TC-11] Twin mock scan (single API call)');
  const page = await loginAndEnter(context, 'admin');

  await page.click('#modeTwinBtn');
  await page.click('#precheckTab');
  await page.waitForSelector('#precheckPane.active', { timeout: 5000 });

  await page.click('#scanReagentsBtn');

  await page.waitForFunction(() => {
    const logText = document.getElementById('logList')?.textContent || '';
    const detail = document.getElementById('detailBox')?.textContent || '';
    return logText.includes('Mock 扫描完成') || logText.includes('Mock 扫描失败')
      || detail.includes('Mock 扫描完成') || detail.includes('Mock 扫描失败');
  }, null, { timeout: 60000 });

  const detail = await page.evaluate(() => document.getElementById('detailBox')?.textContent || '');
  assert(!detail.includes('Mock 扫描失败'),
    `Mock scan reported failure: "${detail.substring(0, 200)}"`);

  await page.close();
  console.log('[TC-11] PASS');
}

async function testPrecheckSingleModule(context) {
  console.log('[TC-12] Precheck single module detection');
  const page = await loginAndEnter(context, 'admin');

  await page.click('#modeTwinBtn');
  await page.click('#precheckTab');
  await page.waitForSelector('#precheckPane.active', { timeout: 5000 });

  const firstRunBtn = page.locator('[data-precheck-run]').first();
  await firstRunBtn.waitFor({ state: 'attached', timeout: 5000 });
  await firstRunBtn.click();

  await page.waitForFunction(() => {
    const firstStep = document.querySelector('.precheck-step');
    if (!firstStep) return false;
    const text = firstStep.querySelector('.precheck-status')?.textContent || '';
    return text !== '检查中' && text !== '待检查';
  }, null, { timeout: 30000 });

  const firstResult = await page.evaluate(() => {
    const step = document.querySelector('.precheck-step');
    return {
      name: step?.querySelector('.precheck-name')?.textContent || '',
      status: step?.querySelector('.precheck-status')?.textContent || '',
    };
  });

  assert(['通过', '异常', '不可用'].includes(firstResult.status),
    `Single precheck should return definitive status, got: ${firstResult.status}`);
  console.log(`  Single check: ${firstResult.name} -> ${firstResult.status}`);

  await page.close();
  console.log('[TC-12] PASS');
}

// ─── main ─────────────────────────────────────────────────────────────

async function main() {
  // External URL guard: every case writes data (login/session, tasks, mock scan,
  // mock reset). Refuse to run them against an external URL unless explicitly opted in.
  if (!selfStarted && !externalOptIn) {
    throw new Error(
      `Refusing to run browser acceptance against external STAINER_BASE_URL='${baseURL}'. ` +
      `This suite performs writes (login, IHC task creation, mock scans, mock runtime reset) ` +
      `that pollute the target service. Either:\n` +
      `  (a) unset STAINER_BASE_URL to use an isolated self-started service on a temp DB, or\n` +
      `  (b) set STAINER_ALLOW_DESTRUCTIVE_EXTERNAL=1 to acknowledge the risk.`
    );
  }

  assert(fs.existsSync(webDll),
    `Build output not found at ${webDll}. Run:\n` +
      `  export TMP=/d/tmp TEMP=/d/tmp && dotnet build backend/Stainer.Web/Stainer.Web.csproj -o bin/tmp-build-w3fix -nologo`);

  if (selfStarted) {
    const port = Number(process.env.STAINER_BROWSER_PORT || await getFreePort());
    activeBaseURL = `http://127.0.0.1:${port}`;
    // Temp root under D:\tmp (or override). Avoids filling C: with SQLite temp DBs.
    await fsp.mkdir(TEMP_ROOT_PARENT, { recursive: true });
    tempRoot = await fsp.mkdtemp(path.join(TEMP_ROOT_PARENT, 'stainer-cc-acceptance-'));
    const dbDir = path.join(tempRoot, 'db');
    const lockDir = path.join(tempRoot, 'locks');
    const logDir = path.join(tempRoot, 'logs');
    const backupDir = path.join(tempRoot, 'backups');
    await Promise.all([dbDir, lockDir, logDir, backupDir].map(dir => fsp.mkdir(dir, { recursive: true })));

    console.log(`[control-console] Self-starting isolated server at ${activeBaseURL}`);
    console.log(`[control-console] Temp DB root: ${tempRoot}`);
    startServer(activeBaseURL, {
      dbPath: path.join(dbDir, 'stainer-cc.db'),
      lockPath: path.join(lockDir, 'machine-executor.lock'),
      logDir,
      backupDir,
    });
    await waitForHealth(activeBaseURL);
  } else {
    activeBaseURL = baseURL;
    console.log(`[control-console] Using external STAINER_BASE_URL=${activeBaseURL} (opted in).`);
  }

  try {
    browser = await chromium.launch({
      headless: true,
      executablePath: resolveBrowserExecutable(),
    });

    const context = await browser.newContext({ baseURL: activeBaseURL, viewport });
    await installPromptCapture(context);

    // Seed mock demo data on self-start (publishes IHC workflow + sets default).
    const adminApi = await loginApi('admin');
    if (selfStarted) {
      await postOk(adminApi, '/api/mock-demo-data/seed', {
        commandId: commandId('cc-seed'),
      });
    }

    // Run order chosen for data isolation:
    //   - Login/session cases first (no shared state)
    //   - TC-07 (channel B IHC, dismiss slot — no task left behind)
    //   - TC-06 (channel A IHC, real task on A-01)
    //   - Mock scan / precheck cases
    //   - TC-08 mock reset (clears mock data) — after the cases that need workflow data
    //   - Mode gates (read-only assertions)
    await testLoginLogout(context);
    await testOperatorLoginLogout(context);
    await testSessionExpiry(context);

    await testWorkflowInfoVisibleBeforePrompt(context, adminApi);
    await testIhcIntakeSlotOnly(context, adminApi);

    await testMockReagentScan(context);

    await testPrecheckBlockingReasons(context);
    await testPrecheckSingleModule(context);

    await testMockRuntimeReset(context);

    await testDebugModeGates(context);
    await testProductionModeGates(context);
    await testTwinMockScanDialogHandling(context);

    console.log(`[control-console] ALL PASS — base=${activeBaseURL}`);
  } finally {
    if (browser) await browser.close().catch(() => {});
    if (selfStarted) {
      await stopServer();
      if (!process.env.STAINER_KEEP_BROWSER_TEMP && tempRoot) {
        try {
          await fsp.rm(tempRoot, { recursive: true, force: true });
        } catch { /* ignore */ }
      } else if (tempRoot) {
        console.log(`[control-console] kept temp root: ${tempRoot}`);
      }
    }
  }
}

main().catch(error => {
  console.error(`[control-console] FAILED: ${error.stack || error.message || error}`);
  process.exitCode = 1;
});
