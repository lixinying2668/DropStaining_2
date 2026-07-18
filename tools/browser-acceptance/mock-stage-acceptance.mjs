/**
 * [OBSOLETE] mock-stage-acceptance.mjs
 *
 * This script is obsolete and should NOT be run against the current codebase.
 * It depends on deleted routes (/samples, /dashboard, /run, /reagents, /admin, /engineer)
 * and removed browser-global functions (openConfirmModal, confirmTask, machineStateSnapshot,
 * channelByLetter, openChannelScriptModal, applyChannelExperimentTypeSelection,
 * openValidationModal).
 *
 * Replacement: control-console-acceptance.mjs (migrated to /control-console).
 *
 * Obsolete references inventory:
 *   - Line 68:  goto /samples (404)
 *   - Line 75:  goto /dashboard (404)
 *   - Line 99:  goto /reagents (404)
 *   - Line 104: goto /run (404)
 *   - Line 357: goto /samples (404)
 *   - Line 377: goto /engineer (404)
 *   - Line 394: goto /admin (404)
 *   - Lines 96,192,255,285,418: window.machineStateSnapshot (removed)
 *   - Lines 199,203: openChannelScriptModal / channelByLetter (removed)
 *   - Lines 212,213,224,226,237,239,245: openConfirmModal / confirmTask (removed)
 *   - Line 106: openValidationModal (removed)
 *   - Lines 353-364: assertOperatorCannotSeeEngineeringDetails (nav selector a[data-href="/engineer"] removed)
 *   - Lines 367-383: assertEngineerCanSeeDiagnostics (goto /engineer 404)
 *   - Lines 386-398: assertAdminCanOpenManagementPage (goto /admin 404)
 *
 * Migrated to control-console-acceptance.mjs:
 *   - Login/logout (TC-01, TC-02)
 *   - Session expiry detection (TC-03)
 *   - Mock reagent scan (TC-04)
 *   - Precheck blocking reasons (TC-05)
 *   - IHC intake slot-only (TC-06)
 *   - Workflow info before prompt (TC-07)
 *   - Mock runtime reset (TC-08)
 *   - Debug/Production mode gates (TC-09, TC-10)
 *   - Precheck single module (TC-12)
 *
 * NOT migrated (no current business equivalent):
 *   - WebSocket reconnect instrumentation (lines 303-350): architecture changed to SignalR
 *   - Legacy endpoint assertions (lines 409-415): those endpoints no longer exist
 *   - Operator snapshot technical detail filtering (lines 417-419): replaced by /api/twin/snapshot
 *   - DAB batch creation from snapshot (lines 260-281): now handled by ensureMockDabBatch in index.html
 *   - Run creation from current tasks (lines 283-301): now handled by startRealRun in index.html
 *   - Runtime isolation assertion (lines 422-430): test infrastructure concern, not browser acceptance
 */

// ─── OBSOLETE EXIT GUARD ───────────────────────────────────────────────
// Hard exit before any module body logic runs. This script references deleted
// routes (/samples, /dashboard, /run, /reagents, /admin, /engineer) and removed
// browser globals (machineStateSnapshot, openConfirmModal, ...). We must NOT
// execute the test body — doing so would 404 against the current backend.
// Replacement: control-console-acceptance.mjs.
//
// The console.log + process.exit(0) below runs immediately after ES module
// imports resolve, before main() is invoked. Imports are side-effect-free
// (they only declare bindings), so this is safe.
console.log('[obsolete] mock-stage-acceptance.mjs is obsolete and is NOT executed.');
console.log('[obsolete] Replacement: tools/browser-acceptance/control-console-acceptance.mjs');
console.log('[obsolete] This script references deleted routes (/samples, /dashboard, /run, /reagents, /admin, /engineer).');
process.exit(0);

// The original script body is preserved below for reference. It is unreachable
// because of the process.exit(0) above.

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
const webDll = path.join(webProjectRoot, 'bin', 'Release', 'net9.0', 'Stainer.Web.dll');
const tempPrefix = path.join(os.tmpdir(), 'stainer-mock-browser-');
const viewport = { width: 1920, height: 1080 };
const technicalDetailPattern = /%TEMP%|SQLite|AttemptDirectory|StateHash|raw-packet|coordinateSnapshotJson|liquidClassSnapshotJson|raw request|raw response/i;

let serverProcess;
let browser;
let tempRoot;
let baseURL;
const serverOutput = [];
const observedFormalRequests = [];

main().catch(error => {
  console.error(`[browser-acceptance] FAILED: ${error.stack || error.message || error}`);
  process.exitCode = 1;
});

async function main() {
  assert(fs.existsSync(webDll), `Release build not found at ${webDll}. Run "dotnet build Stainer.sln --configuration Release" before npm run test:browser.`);

  const port = Number(process.env.STAINER_BROWSER_PORT || await getFreePort());
  baseURL = `http://127.0.0.1:${port}`;
  tempRoot = await fsp.mkdtemp(tempPrefix);
  const dbDir = path.join(tempRoot, 'db');
  const lockDir = path.join(tempRoot, 'locks');
  const logDir = path.join(tempRoot, 'logs');
  const backupDir = path.join(tempRoot, 'backups');
  await Promise.all([dbDir, lockDir, logDir, backupDir].map(dir => fsp.mkdir(dir, { recursive: true })));

  try {
    startServer({
      dbPath: path.join(dbDir, 'stainer-browser.db'),
      lockPath: path.join(lockDir, 'machine-executor.lock'),
      logDir,
      backupDir
    });
    await waitForHealth();

    const adminApi = await loginApi('admin');
    await postOk(adminApi, '/api/mock-demo-data/seed', {
      commandId: commandId('browser-seed')
    });
    await setMockDemoDefaultWorkflows(adminApi);

    browser = await chromium.launch({
      headless: true,
      executablePath: resolveBrowserExecutable()
    });

    const operatorContext = await browser.newContext({ baseURL, viewport });
    const operatorPage = await operatorContext.newPage();
    trackFormalRequests(operatorPage);
    await loginViaUi(operatorPage, 'operator', 'operator');
    await operatorPage.goto(`${baseURL}/samples`);
    await waitForSnapshot(operatorPage, 'operator');
    observedFormalRequests.length = 0;

    const mirrorPage = await operatorContext.newPage();
    await installReconnectInstrumentation(mirrorPage);
    trackFormalRequests(mirrorPage);
    await mirrorPage.goto(`${baseURL}/dashboard`);
    await waitForSnapshot(mirrorPage, 'operator');
    await waitForMachineSocket(mirrorPage);

    await chooseExperiment(operatorPage, 'A', 'HE');
    await createHeTask(operatorPage, 'A-01');
    await waitForSlideCount(operatorPage, 1);
    await waitForSlideCount(mirrorPage, 1);

    const beforeReconnectFetchCount = await closeMachineSocket(mirrorPage);
    await chooseExperiment(operatorPage, 'B', 'IHC');
    await createIhcHospitalTask(operatorPage, 'B-01', 'HOSP-MOCK-SINGLE');
    await mirrorPage.waitForFunction(
      before => window.__snapshotFetchCount > before,
      beforeReconnectFetchCount,
      { timeout: 15000 });
    await waitForSlideCount(mirrorPage, 2);

    await createIhcHospitalTaskWithMultipleSelection(operatorPage, 'B-02', 'HOSP-MOCK-MULTI', 'P01');
    await waitForSlideCount(operatorPage, 3);

    const snapshotAfterTasks = await operatorPage.evaluate(() => window.machineStateSnapshot);
    await createDabBatchForIhcTasks(adminApi, snapshotAfterTasks);

    await operatorPage.goto(`${baseURL}/reagents`);
    await waitForSnapshot(operatorPage, 'operator');
    await operatorPage.waitForFunction(() => document.querySelectorAll('#reagentDeck > *').length > 0, null, { timeout: 15000 });
    await operatorPage.waitForFunction(() => document.querySelectorAll('#dabPositionGrid > *').length > 0, null, { timeout: 15000 });

    await operatorPage.goto(`${baseURL}/run`);
    await waitForSnapshot(operatorPage, 'operator');
    await operatorPage.evaluate(async () => { await openValidationModal(false); });
    await operatorPage.waitForSelector('#validationModal:not(.hidden)', { timeout: 15000 });
    await operatorPage.waitForFunction(() => (document.getElementById('validationBody')?.innerText || '').trim().length > 0, null, { timeout: 15000 });

    await createRunFromCurrentTasks(operatorPage);
    await operatorPage.waitForFunction(() => document.querySelectorAll('#runChannels .runtime-channel').length >= 2, null, { timeout: 15000 });

    await assertOperatorCannotSeeEngineeringDetails(operatorContext);
    await assertEngineerCanSeeDiagnostics();
    await assertAdminCanOpenManagementPage();
    assertNoLegacyFormalPageCalls();
    await assertOperatorSnapshotHasNoTechnicalDetails(operatorPage);

    await assertRuntimeIsolation(tempRoot);
    console.log(`[browser-acceptance] PASS ${baseURL}`);
  } finally {
    if (browser) {
      await browser.close().catch(() => {});
    }
    await stopServer();
    await cleanupTempRoot();
  }
}

function commandId(prefix) {
  return `${prefix}-${randomUUID()}`;
}

async function loginApi(role) {
  const api = await playwrightRequest.newContext({ baseURL });
  const response = await api.post('/api/login', {
    data: { username: role, password: '123456', role }
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

async function setMockDemoDefaultWorkflows(api) {
  const workflows = await getOk(api, '/api/workflows');
  const heVersionId = findPublishedWorkflowVersionId(workflows, 'MOCK-HE-DEMO');
  const ihcVersionId = findPublishedWorkflowVersionId(workflows, 'MOCK-IHC-P01-DEMO');
  await postOk(api, `/api/workflow-versions/${encodeURIComponent(heVersionId)}/set-default`, {
    commandId: commandId('browser-default-he'),
    experimentType: 'HE'
  });
  await postOk(api, `/api/workflow-versions/${encodeURIComponent(ihcVersionId)}/set-default`, {
    commandId: commandId('browser-default-ihc'),
    experimentType: 'IHC'
  });
}

function findPublishedWorkflowVersionId(workflows, code) {
  const workflow = workflows.find(item => item.code === code);
  assert(workflow, `Mock demo workflow ${code} was not seeded.`);
  const version = (workflow.versions || []).find(item => item.status === 'Published');
  assert(version, `Mock demo workflow ${code} has no Published version.`);
  return version.id;
}

async function loginViaUi(page, username, role) {
  await page.goto(`${baseURL}/`);
  await page.fill('#username', username);
  await page.fill('#password', '123456');
  // 当前旧版登录页是两个身份按钮（管理员 btn-primary / 实验员 btn-operator），不再有 role 单选。
  const selector = role === 'operator' ? 'button.btn-operator' : 'button.btn-primary.btn-xl';
  await Promise.all([
    page.waitForURL(url => url.pathname !== '/', { timeout: 15000 }),
    page.click(selector)
  ]);
  await waitForSnapshot(page, role);
}

async function waitForSnapshot(page, role) {
  await page.waitForFunction(
    expectedRole => window.machineStateSnapshot?.activeUser?.role === expectedRole,
    role,
    { timeout: 15000 });
}

async function chooseExperiment(page, letter, type) {
  await page.evaluate(async ({ letter, type }) => {
    await openChannelScriptModal(letter);
    await applyChannelExperimentTypeSelection(type);
  }, { letter, type });
  await page.waitForFunction(({ letter, type }) => {
    const channel = channelByLetter(letter);
    return channel?.experimentType === type
      && !!channel.workflowVersionId
      && channel.workflowSelectionStatus === 'Selected';
  }, { letter, type }, { timeout: 15000 });
}

async function createHeTask(page, slotCode) {
  await page.evaluate(async slotCode => {
    openConfirmModal('he', slotCode);
    await confirmTask();
  }, slotCode);
  await page.waitForFunction(slotCode => {
    const [letter, slot] = slotCode.split('-');
    const channel = channelByLetter(letter);
    return (channel?.slides || []).some(slide => Number(slide.slot) === Number(slot));
  }, slotCode, { timeout: 15000 });
}

async function createIhcHospitalTask(page, slotCode, rawCode) {
  await page.evaluate(async ({ slotCode, rawCode }) => {
    openConfirmModal('ihc-hospital', slotCode);
    document.getElementById('rawCode').value = rawCode;
    await confirmTask();
  }, { slotCode, rawCode });
  await page.waitForFunction(slotCode => {
    const [letter, slot] = slotCode.split('-');
    const channel = channelByLetter(letter);
    return (channel?.slides || []).some(slide => Number(slide.slot) === Number(slot));
  }, slotCode, { timeout: 15000 });
}

async function createIhcHospitalTaskWithMultipleSelection(page, slotCode, rawCode, selectedPrimaryAntibodyCode) {
  const candidates = await page.evaluate(async ({ slotCode, rawCode }) => {
    openConfirmModal('ihc-hospital', slotCode);
    document.getElementById('rawCode').value = rawCode;
    await confirmTask();
    return Array.from(document.getElementById('primaryAntibodySelect').options).map(option => option.value);
  }, { slotCode, rawCode });
  assert(candidates.length >= 2, `Expected LIS multiple-result choices, got ${JSON.stringify(candidates)}.`);
  assert(candidates.includes(selectedPrimaryAntibodyCode), `Expected ${selectedPrimaryAntibodyCode} among LIS choices ${JSON.stringify(candidates)}.`);
  await page.selectOption('#primaryAntibodySelect', selectedPrimaryAntibodyCode);
  await page.evaluate(async () => { await confirmTask(); });
  await page.waitForFunction(slotCode => {
    const [letter, slot] = slotCode.split('-');
    const channel = channelByLetter(letter);
    return (channel?.slides || []).some(slide => Number(slide.slot) === Number(slot));
  }, slotCode, { timeout: 15000 });
}

async function waitForSlideCount(page, expectedCount) {
  await page.waitForFunction(
    expected => (window.machineStateSnapshot?.channels || []).flatMap(channel => channel.slides || []).length >= expected,
    expectedCount,
    { timeout: 15000 });
}

async function createDabBatchForIhcTasks(api, snapshot) {
  const taskIds = (snapshot.channels || [])
    .flatMap(channel => channel.slides || [])
    .filter(slide => slide.protocolCode === 'IHC' || slide.inputMode === 'HospitalBarcode' || slide.confirmedPrimaryAntibodyCode)
    .map(slide => slide.stainingTaskId)
    .filter(Boolean);
  assert(taskIds.length >= 2, `Expected at least two IHC task ids for DAB batch, got ${JSON.stringify(taskIds)}.`);

  const rack = await getOk(api, '/api/reagents/rack');
  const bottleId = code => rack.find(position => position.bottle?.reagentCode === code && position.bottle?.status === 'Available')?.bottle?.id;
  const dabAReagentBottleId = bottleId('DBA');
  const dabBReagentBottleId = bottleId('DBB');
  assert(dabAReagentBottleId && dabBReagentBottleId, 'Mock demo rack did not expose available DBA/DBB source bottles.');

  await postOk(api, '/api/dab/batches', {
    commandId: commandId('browser-dab-create'),
    taskIds,
    dabAReagentBottleId,
    dabBReagentBottleId,
    positionCode: 'M1'
  });
}

async function createRunFromCurrentTasks(page) {
  await page.evaluate(async () => {
    const taskIds = (window.machineStateSnapshot?.channels || [])
      .flatMap(channel => channel.slides || [])
      .map(slide => slide.stainingTaskId)
      .filter(Boolean);
    await api('/api/runs', {
      method: 'POST',
      body: JSON.stringify({
        commandId: commandId('browser-run-create'),
        stainingTaskIds: taskIds
      })
    });
    await loadHostState();
    if (typeof refreshRun === 'function') {
      await refreshRun();
    }
  });
}

async function installReconnectInstrumentation(page) {
  await page.addInitScript(() => {
    const nativeFetch = window.fetch.bind(window);
    window.__snapshotFetchCount = 0;
    window.fetch = (input, init) => {
      const url = typeof input === 'string' ? input : input?.url || '';
      if (url.includes('/api/operator/snapshot')) {
        window.__snapshotFetchCount += 1;
      }
      return nativeFetch(input, init);
    };

    const NativeWebSocket = window.WebSocket;
    function TrackingWebSocket(url, protocols) {
      const socket = protocols === undefined
        ? new NativeWebSocket(url)
        : new NativeWebSocket(url, protocols);
      window.__machineSockets = window.__machineSockets || [];
      window.__machineSockets.push(socket);
      return socket;
    }
    TrackingWebSocket.prototype = NativeWebSocket.prototype;
    TrackingWebSocket.CONNECTING = NativeWebSocket.CONNECTING;
    TrackingWebSocket.OPEN = NativeWebSocket.OPEN;
    TrackingWebSocket.CLOSING = NativeWebSocket.CLOSING;
    TrackingWebSocket.CLOSED = NativeWebSocket.CLOSED;
    window.WebSocket = TrackingWebSocket;
  });
}

async function waitForMachineSocket(page) {
  await page.waitForFunction(() =>
    (window.__machineSockets || []).some(socket => socket.readyState === WebSocket.OPEN),
    null,
    { timeout: 15000 });
}

async function closeMachineSocket(page) {
  return await page.evaluate(() => {
    const before = window.__snapshotFetchCount || 0;
    for (const socket of window.__machineSockets || []) {
      if (socket.readyState === WebSocket.OPEN) {
        socket.close();
      }
    }
    return before;
  });
}

async function assertOperatorCannotSeeEngineeringDetails(context) {
  const apiResponse = await context.request.get(`${baseURL}/api/engineering/diagnostics/device-state`);
  assert(apiResponse.status() === 403, `Operator diagnostics API should be forbidden, got ${apiResponse.status()}.`);

  const page = await context.newPage();
  await page.goto(`${baseURL}/samples`);
  await waitForSnapshot(page, 'operator');
  const engineerNavVisible = await page.locator('a[data-href="/engineer"]').evaluate(element => {
    const style = window.getComputedStyle(element);
    return style.display !== 'none' && style.visibility !== 'hidden';
  });
  assert(!engineerNavVisible, 'Operator navigation should not expose the engineering page.');
  await page.close();
}

async function assertEngineerCanSeeDiagnostics() {
  const context = await browser.newContext({ baseURL, viewport });
  const login = await context.request.post(`${baseURL}/api/login`, {
    data: { username: 'engineer', password: '123456', role: 'engineer' }
  });
  assert(login.ok(), `Engineer login failed: ${login.status()} ${await login.text()}`);
  const diagnostics = await context.request.get(`${baseURL}/api/engineering/diagnostics/device-state`);
  assert(diagnostics.ok(), `Engineer diagnostics API failed: ${diagnostics.status()} ${await diagnostics.text()}`);

  const page = await context.newPage();
  await page.goto(`${baseURL}/engineer`);
  await waitForSnapshot(page, 'engineer');
  await page.waitForFunction(() => {
    const text = (document.getElementById('engineeringDeviceState')?.innerText || '').trim();
    return text.length > 20 && !text.includes('读取') && !text.includes('璇诲彇');
  }, null, { timeout: 15000 });
  await context.close();
}

async function assertAdminCanOpenManagementPage() {
  const context = await browser.newContext({ baseURL, viewport });
  const login = await context.request.post(`${baseURL}/api/login`, {
    data: { username: 'admin', password: '123456', role: 'admin' }
  });
  assert(login.ok(), `Admin login failed: ${login.status()} ${await login.text()}`);

  const page = await context.newPage();
  await page.goto(`${baseURL}/admin`);
  await waitForSnapshot(page, 'admin');
  await page.waitForFunction(() => Number(document.getElementById('adminUserCount')?.innerText || '0') >= 3, null, { timeout: 15000 });
  await context.close();
}

function trackFormalRequests(page) {
  page.on('request', req => {
    const url = req.url();
    if (url.startsWith(baseURL)) {
      observedFormalRequests.push(url);
    }
  });
}

function assertNoLegacyFormalPageCalls() {
  const legacyCalls = observedFormalRequests.filter(url =>
    /\/api\/state(?:[?#]|$)/.test(url)
    || /\/api\/run\/(?:start|pause|resume|stop)(?:[?#]|$)/.test(url)
    || /\/api\/dab(?:[?#]|$)/.test(url));
  assert(legacyCalls.length === 0, `Formal browser pages called legacy endpoints: ${legacyCalls.join(', ')}`);
}

async function assertOperatorSnapshotHasNoTechnicalDetails(page) {
  const serialized = await page.evaluate(() => JSON.stringify(window.machineStateSnapshot));
  assert(!technicalDetailPattern.test(serialized), 'Operator snapshot exposed technical diagnostic details.');
}

async function assertRuntimeIsolation(root) {
  const defaultDataDb = path.join(repoRoot, 'data', 'stainer.db');
  const tempDb = path.join(root, 'db', 'stainer-browser.db');
  assert(fs.existsSync(tempDb), `Expected browser acceptance temp database at ${tempDb}.`);
  assert(!pathsEqual(defaultDataDb, tempDb), 'Browser acceptance database resolved to the formal data/stainer.db path.');
  assert(fs.existsSync(path.join(root, 'locks', 'machine-executor.lock')), 'Expected isolated executor lock to be created.');
  assert(fs.existsSync(path.join(root, 'logs')), 'Expected isolated safety log directory to exist.');
  assert(fs.existsSync(path.join(root, 'backups')), 'Expected isolated backup directory to exist.');
}

function startServer(paths) {
  const env = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: baseURL,
    STAINER_DATABASE_URL: `Data Source=${paths.dbPath}`,
    Device__Mode: 'Mock',
    MachineExecutor__LeasePath: paths.lockPath,
    Safety__LogDirectory: paths.logDir,
    Database__BackupDirectory: paths.backupDir
  };

  serverProcess = spawn('dotnet', [webDll, '--contentRoot', webProjectRoot], {
    cwd: repoRoot,
    env,
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true
  });
  serverProcess.stdout.on('data', chunk => rememberServerOutput(chunk));
  serverProcess.stderr.on('data', chunk => rememberServerOutput(chunk));
}

async function waitForHealth() {
  const deadline = Date.now() + 90000;
  while (Date.now() < deadline) {
    if (serverProcess.exitCode !== null) {
      throw new Error(`Server exited early with code ${serverProcess.exitCode}.\n${serverOutput.join('')}`);
    }
    try {
      const response = await fetch(`${baseURL}/health`);
      if (response.ok) {
        return;
      }
    } catch {
      // server is still starting
    }
    await delay(500);
  }
  throw new Error(`Timed out waiting for ${baseURL}/health.\n${serverOutput.join('')}`);
}

async function stopServer() {
  if (!serverProcess || serverProcess.exitCode !== null) {
    return;
  }
  serverProcess.kill();
  const deadline = Date.now() + 10000;
  while (Date.now() < deadline && serverProcess.exitCode === null) {
    await delay(250);
  }
  if (serverProcess.exitCode === null) {
    serverProcess.kill('SIGKILL');
  }
}

async function cleanupTempRoot() {
  if (!tempRoot) {
    return;
  }
  if (process.env.STAINER_KEEP_BROWSER_TEMP === '1') {
    console.log(`[browser-acceptance] kept temp root: ${tempRoot}`);
    return;
  }

  const normalized = path.resolve(tempRoot);
  const normalizedPrefix = path.resolve(tempPrefix);
  assert(normalized.startsWith(normalizedPrefix), `Refusing to remove unexpected temp root: ${normalized}`);
  await fsp.rm(normalized, { recursive: true, force: true });
  const exists = fs.existsSync(normalized);
  console.log(`[browser-acceptance] cleaned temp root: ${normalized}; exists=${exists}`);
}

function resolveBrowserExecutable() {
  const configured = process.env.PLAYWRIGHT_CHROME_EXECUTABLE;
  if (configured && fs.existsSync(configured)) {
    return configured;
  }

  const candidates = [
    path.join(process.env.ProgramFiles || 'C:\\Program Files', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    path.join(process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    path.join(process.env.ProgramFiles || 'C:\\Program Files', 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
    path.join(process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)', 'Microsoft', 'Edge', 'Application', 'msedge.exe')
  ];
  const found = candidates.find(candidate => fs.existsSync(candidate));
  assert(found, `No system Chrome/Edge executable found. Set PLAYWRIGHT_CHROME_EXECUTABLE to a Chromium-based browser path.`);
  return found;
}

function rememberServerOutput(chunk) {
  serverOutput.push(chunk.toString());
  while (serverOutput.length > 80) {
    serverOutput.shift();
  }
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

function pathsEqual(left, right) {
  return path.resolve(left).toLowerCase() === path.resolve(right).toLowerCase();
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
