let startAfterValidation = false;
async function runAction(action){
  if(action === 'start'){
    await openValidationModal(true);
    return;
  }
  await api(`/api/run/${action}`, {method:'POST'});
  const label = {pause:'暂停', resume:'恢复', stop:'普通整机停止'}[action] || action;
  toast('命令已下发：' + label);
  await refreshRun();
}
async function confirmStop(){
  if(confirm('普通整机停止将在当前原子动作完成后停止后续调度，并将未完成任务标记为“已停止/待处理”。确认执行？')){
    await runAction('stop');
  }
}
async function openValidationModal(autoStart=false){
  const state = await api('/api/state');
  const issues = [];
  if(!state.initialized) issues.push(['关键设备','初始化未完成','先执行初始化/预检']);
  const slideCount = state.channels.reduce((n,ch)=>n + ch.slides.length, 0);
  if(slideCount === 0) issues.push(['样本任务','未创建已确认任务','返回样本确认页面']);
  if(state.reagents.length === 0) issues.push(['试剂校验','未扫描试剂架','返回试剂扫描页面']);
  if(state.system.waste_tank_full) issues.push(['废液','废液桶已满','清理废液并重试']);
  if(state.system.toxic_tank_full) issues.push(['排毒','排毒桶已满','清理排毒桶并重试']);
  const needed = ['BLOCK','SECONDARY','DAB-A','DAB-B','WATER','HEMATOXYLIN','WASH'];
  const available = new Set(state.reagents.filter(r=>r.available).map(r=>r.code));
  needed.forEach(code=>{ if(state.reagents.length && !available.has(code)) issues.push(['所需试剂', code + ' 缺失/未知', '补齐目录或重新扫码']); });
  const body = document.getElementById('validationBody');
  const btn = document.getElementById('validationStartBtn');
  if(body){
    if(issues.length){
      body.innerHTML = issues.map(x=>`<div class="validation-issue"><b>${x[0]}</b><span>${x[1]}</span><em>${x[2]}</em></div>`).join('') + '<div class="notice-box danger-note">校验失败：不允许管理员或操作员覆盖；请返回处理后再启动。</div>';
      btn.disabled = true;
      btn.textContent = '校验失败，禁止启动';
    }else{
      body.innerHTML = '<div class="validation-ok"><b>全部校验通过</b><span>样本、初始化、试剂与关键设备状态满足启动条件。</span></div>';
      btn.disabled = false;
      btn.textContent = '启动整机运行';
    }
  }
  validationModal.classList.remove('hidden');
  startAfterValidation = autoStart;
}
async function forceStartAfterValidation(){
  validationModal.classList.add('hidden');
  await api('/api/run/start', {method:'POST'});
  toast('启动命令已下发');
  await refreshRun();
}
function majorStepFor(slide){
  const step = slide.current_step || '';
  if(step.includes('DAB')) return 'DAB';
  if(step.includes('二抗')) return '二抗';
  if(step.includes('一抗')) return '一抗';
  if(step.includes('苏木素')) return '苏木素与终洗';
  if(step.includes('阻断')) return '前处理';
  return slide.protocol_code === 'HE' ? '苏木素与终洗' : '待调度';
}
function reagentSourceFor(slide){
  const step = majorStepFor(slide);
  const map = {
    '前处理':'阻断剂 / 清洗液',
    '一抗':'一抗 ' + (slide.antibody_code || '待确认'),
    '二抗':'二抗 / HRP',
    'DAB':'DAB A/B / 水',
    '苏木素与终洗':'苏木素 / 清洗液'
  };
  return map[step] || '无';
}
function etaFor(slide){
  const progress = Number(slide.progress || 0);
  if(progress >= 100) return '已完成';
  if(slide.status === 'error') return '待处理';
  return Math.max(8, Math.round((100 - progress) * 0.9)) + ' 分钟';
}
function renderChannels(channels){
  const root = document.getElementById('runChannels');
  if(!root) return;
  root.innerHTML = channels.map((ch, idx) => {
    const letter = ['A','B','C','D'][idx] || ch.id;
    return `<div class="runtime-channel ${ch.status}">
      <div class="runtime-head"><b>${letter} 通道</b><span>${statusText(ch.status)}</span></div>
      <div class="progress-track"><span style="width:${ch.progress}%"></span></div>
      <p><b>${ch.progress}%</b> · ${ch.current_step || '空闲'}</p>
      <div class="runtime-slides">
        ${[1,2,3,4].map(slot => {
          const s = ch.slides.find(x=>x.slot===slot);
          return `<div class="runtime-slide ${s?'loaded':'empty'}"><b>${letter}-${String(slot).padStart(2,'0')}</b><small>${s?s.barcode:'空位'}</small><em>${s?majorStepFor(s):'EMPTY'}</em><small>${s?s.current_step:''}</small><small>${s?'来源：' + reagentSourceFor(s):''}</small><small>${s?'预计剩余：' + etaFor(s):''}</small></div>`;
        }).join('')}
      </div>
    </div>`;
  }).join('');
}
async function refreshRun(){
  const state = await api('/api/state');
  const status = document.getElementById('runStatus');
  if(status){ status.className = 'status-chip status-' + state.status; status.innerHTML = '<i></i><b data-status-label>'+statusText(state.status)+'</b>'; }
  renderChannels(state.channels);
  const logRoot = document.getElementById('logList');
  if(logRoot){ logRoot.innerHTML = state.logs.slice(0,30).map(x=>`<div>${x}</div>`).join('') || '<div>暂无日志</div>'; }
}
document.addEventListener('DOMContentLoaded',()=>{ refreshRun(); window.refreshRunView = refreshRun; });

let latestPreflightReport = null;
let currentRunSnapshot = null;
let currentUserSnapshot = null;
let systemInfoSnapshot = null;

async function fetchJsonOrNull(url){
  const res = await fetch(url, {headers:{'Content-Type':'application/json'}});
  const text = await res.text();
  let data = null;
  try{ data = text ? JSON.parse(text) : {}; }catch(e){ data = {raw:text}; }
  if(res.status === 404) return null;
  if(!res.ok){
    const msg = data.detail || data.message || ('请求失败：' + res.status);
    toast(msg, true);
    const error = new Error(msg);
    error.status = res.status;
    error.data = data;
    throw error;
  }
  return data;
}

async function loadRunContext(){
  const [run, user, systemInfo] = await Promise.all([
    fetchJsonOrNull('/api/runs/current'),
    fetchJsonOrNull('/api/current-user'),
    fetchJsonOrNull('/api/system/info')
  ]);
  currentRunSnapshot = run;
  currentUserSnapshot = user?.user || user;
  systemInfoSnapshot = systemInfo || {deviceMode:'Mock', deviceStateSource:'MockDeviceState'};
  return run;
}

async function runAction(action){
  if(action === 'start'){
    if(!latestPreflightReport?.ok || latestPreflightReport.invalidated){
      await openValidationModal(false);
      return;
    }
    await forceStartAfterValidation();
    return;
  }

  const run = await ensureCurrentRun();
  if(!run) return;
  const message = {
    pause:'等待当前原子动作完成后暂停',
    resume:'恢复命令已下发',
    stop:'当前原子动作完成后停止'
  }[action] || '命令已下发';

  await api(`/api/runs/${encodeURIComponent(run.id)}/${action}`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('run-' + action)})
  });
  toast(message);
  markPreflightInvalid('运行状态已变化，请在下次启动前重新校验。');
  await refreshRun();
}

async function confirmStop(){
  if(confirm('普通整机停止将在当前原子动作完成后停止后续调度。确认执行？')){
    await runAction('stop');
  }
}

async function openValidationModal(autoStart=false){
  const body = document.getElementById('validationBody');
  const btn = document.getElementById('validationStartBtn');
  if(body) body.innerHTML = '<div class="validation-ok"><b>正在执行正式启动前校验...</b><span>数据来自 /api/run/preflight</span></div>';
  if(btn){
    btn.disabled = true;
    btn.textContent = '正在校验...';
  }
  document.getElementById('validationModal')?.classList.remove('hidden');

  try{
    const report = await api('/api/run/preflight');
    latestPreflightReport = report;
    renderPreflightReport(report);
    if(autoStart && report.ok){
      await forceStartAfterValidation();
    }
  }catch(e){
    latestPreflightReport = null;
    if(body) body.innerHTML = `<div class="validation-issue"><b>预检失败</b><span>${escapeHtml(e.message || '正式预检接口调用失败')}</span><em>Fail</em></div>`;
    if(btn){
      btn.disabled = true;
      btn.textContent = '预检失败，禁止启动';
    }
  }
}

function renderPreflightReport(report){
  const body = document.getElementById('validationBody');
  const btn = document.getElementById('validationStartBtn');
  const issues = report.issues || [];
  const warnings = issues.filter(x => String(x.severity || '').toLowerCase() === 'warning');
  const failures = issues.filter(x => String(x.severity || 'Fail').toLowerCase() !== 'warning');
  setText('validationSlides', `${report.taskCount || 0} 张`);
  setText('validationSlidesState', failures.some(x => x.area === 'Tasks' || x.area === 'Workflow') ? 'FAIL' : 'PASS');
  setText('validationReagents', failures.some(x => x.area === 'Reagents') ? '存在阻断' : `${warnings.length} warning`);
  setText('validationReagentsState', failures.some(x => x.area === 'Reagents') ? 'FAIL' : 'PASS');
  setText('validationInit', 'Mock');
  setText('validationInitState', 'MockDeviceState');

  if(body){
    const summary = `<div class="validation-ok"><b>正式预检报告</b><span>预检时间：${escapeHtml(formatDateTime(report.generatedAtUtc))} · 关联任务：${Number(report.taskCount || 0)} · Fail：${failures.length} · Warning：${warnings.length} · 允许启动：${report.ok ? '是' : '否'}</span><small>当前模式：${escapeHtml(systemInfoSnapshot?.deviceMode || 'Mock')}；设备状态来源：${escapeHtml(systemInfoSnapshot?.deviceStateSource || 'MockDeviceState')}；状态哈希：${escapeHtml((report.stateHash || '').slice(0, 12))}</small></div>`;
    const grouped = groupBy(issues, x => x.area || 'Other');
    const rows = Object.entries(grouped).map(([area, items]) => `<div class="validation-group"><h3>${escapeHtml(preflightAreaLabel(area))}</h3>${items.map(item => `<div class="validation-issue ${String(item.severity || '').toLowerCase() === 'warning' ? 'warning' : ''}"><b>${escapeHtml(item.code)}</b><span>${escapeHtml(item.message)}</span><em>${escapeHtml(item.severity || 'Fail')}</em></div>`).join('') || '<div class="validation-ok"><b>Pass</b><span>当前分组无阻断项。</span></div>'}</div>`).join('');
    body.innerHTML = summary + (Object.keys(grouped).length ? rows : '<div class="validation-ok"><b>全部关键项通过</b><span>通道脚本、任务、试剂与 Mock 设备状态满足启动条件。</span></div>');
  }
  if(btn){
    btn.disabled = !report.ok;
    btn.textContent = report.ok ? '启动整机运行' : '预检失败，禁止启动';
  }
}

function groupBy(items, keySelector){
  return (items || []).reduce((acc, item) => {
    const key = keySelector(item);
    acc[key] = acc[key] || [];
    acc[key].push(item);
    return acc;
  }, {});
}

function preflightAreaLabel(area){
  return {
    Workflow:'A. 通道与实验脚本',
    Tasks:'A. 通道与实验脚本',
    Reagents:'B. 试剂与扫码',
    DAB:'C. DAB',
    Device:'D. Mock 初始化与设备状态'
  }[area] || area;
}

function markPreflightInvalid(reason){
  if(!latestPreflightReport) return;
  latestPreflightReport.invalidated = true;
  const btn = document.getElementById('validationStartBtn');
  if(btn){
    btn.disabled = true;
    btn.textContent = '预检已失效，请重新校验';
  }
  const body = document.getElementById('validationBody');
  if(body && !document.getElementById('validationModal')?.classList.contains('hidden')){
    body.insertAdjacentHTML('afterbegin', `<div class="notice-box danger-note">预检已失效，请重新校验。${escapeHtml(reason || '')}</div>`);
  }
}

async function forceStartAfterValidation(){
  const report = await api('/api/run/preflight');
  latestPreflightReport = report;
  if(!report.ok){
    renderPreflightReport(report);
    toast('预检失败，禁止启动。', true);
    return;
  }

  const run = await ensureRunCreated();
  if(!run) return;
  if(!confirm('确认启动 Mock 运行？启动后 ChannelBatch 与 Slot 将锁定。')) return;

  await api(`/api/runs/${encodeURIComponent(run.id || run.runId)}/start`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('run-start'), preflightStateHash: report.stateHash})
  });
  document.getElementById('validationModal')?.classList.add('hidden');
  toast('启动命令已下发。');
  latestPreflightReport = null;
  await refreshRun();
}

async function ensureRunCreated(){
  let run = await loadRunContext();
  if(run && !['Completed','Stopped','completed','stopped'].includes(run.status)) return run;

  const state = await api('/api/state');
  const taskIds = (state.channels || [])
    .flatMap(channel => channel.slides || [])
    .map(slide => slide.stainingTaskId)
    .filter(Boolean);
  if(taskIds.length === 0){
    toast('没有可启动的已确认任务。', true);
    return null;
  }

  const created = await api('/api/runs', {
    method:'POST',
    body: JSON.stringify({
      commandId: commandId('run-create'),
      stainingTaskIds: taskIds,
      preflightStateHash: latestPreflightReport?.stateHash || null
    })
  });
  return await fetchJsonOrNull(`/api/runs/${encodeURIComponent(created.runId)}`) || {id: created.runId, status: created.status};
}

async function ensureCurrentRun(){
  const run = await loadRunContext();
  if(!run){
    toast('当前没有正式 MachineRun。', true);
    return null;
  }
  return run;
}

async function injectMockFault(){
  const run = await ensureCurrentRun();
  if(!run) return;
  const role = String(currentUserSnapshot?.activeRole || currentUserSnapshot?.role || '').toLowerCase();
  if(!['engineer','admin'].includes(role)){
    toast('故障注入仅 Mock 模式下的工程师或管理员可用。', true);
    return;
  }
  const message = prompt('选择/输入 Mock 故障类型：dispense_failure / temperature_failure / communication_timeout / device_disconnected', 'dispense_failure');
  if(!message) return;
  await api(`/api/runs/${encodeURIComponent(run.id)}/fault`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('run-fault'), message})
  });
  toast('Mock 故障注入命令已下发。');
  markPreflightInvalid('运行故障状态已变化。');
  await refreshRun();
}

async function redoCurrentMajorStep(){
  const run = await ensureCurrentRun();
  if(!run) return;
  const reason = prompt('请输入大步骤重做原因');
  if(!reason || !reason.trim()){
    toast('大步骤重做必须填写原因。', true);
    return;
  }
  if(!confirm(`将从当前大步骤重做，可能额外消耗试剂和 DAB。原因：${reason}`)) return;
  await api(`/api/runs/${encodeURIComponent(run.id)}/redo-current-major-step`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('run-redo'), reason})
  });
  toast('大步骤重做命令已下发。');
  markPreflightInvalid('运行重做状态已变化。');
  await refreshRun();
}

function majorStepForRun(run){
  if(!run) return '--';
  return run.currentMajorStepCode || (run.workflowExecutions || [])
    .flatMap(x => x.steps || [])
    .find(step => ['Running','Failed','Unknown','Pending'].includes(step.status))?.majorStepCode || '--';
}

function renderChannels(channels){
  const root = document.getElementById('runChannels');
  if(!root) return;
  root.innerHTML = (channels || []).map(batch => {
    const workflows = (currentRunSnapshot?.workflowExecutions || []).filter(x => (batch.slides || []).some(slide => slide.id === x.slideTaskId));
    const completed = workflows.flatMap(x => x.steps || []).filter(x => x.status === 'Completed').length;
    const total = workflows.flatMap(x => x.steps || []).length || 1;
    const progress = Math.round(completed * 100 / total);
    return `<div class="runtime-channel ${String(batch.status || '').toLowerCase()}">
      <div class="runtime-head"><b>${escapeHtml(batch.drawerCode)} 通道</b><span>${escapeHtml(batch.status)}</span></div>
      <div class="progress-track"><span style="width:${progress}%"></span></div>
      <p><b>${progress}%</b> · ${escapeHtml(batch.experimentType || '--')} · ${escapeHtml(batch.workflowSelectionStatus || '--')}</p>
      <div class="runtime-slides">
        ${(batch.slides || []).map(slide => {
          const workflow = (currentRunSnapshot?.workflowExecutions || []).find(x => x.slideTaskId === slide.id);
          const step = currentStep(workflow);
          return `<div class="runtime-slide loaded"><b>${escapeHtml(slide.slotCode)}</b><small>${escapeHtml(slide.taskType)}</small><em>${escapeHtml(slide.status)}</em><small>${escapeHtml(step?.stepName || step?.majorStepCode || 'Waiting')}</small><small>试剂：${escapeHtml(step?.reagentCode || '--')}</small><small>体积：${escapeHtml(step?.volumeUl ?? '--')} uL</small></div>`;
        }).join('') || '<div class="runtime-slide empty"><b>EMPTY</b><small>无任务</small></div>'}
      </div>
    </div>`;
  }).join('') || '<div class="empty-state"><b>暂无正式运行</b><span>请先完成样本、试剂扫码和启动前校验。</span></div>';
}

function currentStep(workflow){
  const steps = workflow?.steps || [];
  return steps.find(x => ['Running','Failed','Unknown'].includes(x.status))
    || steps.find(x => x.status === 'Pending')
    || steps[steps.length - 1];
}

async function refreshRun(){
  const run = await loadRunContext();
  const status = document.getElementById('runStatus');
  if(status){
    const runStatus = run?.status || 'idle';
    status.className = 'status-chip status-' + String(runStatus).toLowerCase();
    status.innerHTML = '<i></i><b data-status-label>' + escapeHtml(runStatus) + '</b>';
  }
  setText('runIdSmall', run ? `${run.runCode || run.id} · ${systemInfoSnapshot?.deviceMode || 'Mock'} · ${majorStepForRun(run)}` : '未生成批号 · Mock');
  renderChannels(run?.channelBatches || []);
  renderRunCommandVisibility(run);
  const logRoot = document.getElementById('logList');
  if(logRoot){
    const alarms = (run?.alarms || []).map(x => `[${x.severity}] ${x.code}: ${x.message}`);
    const steps = (run?.workflowExecutions || []).flatMap(x => x.steps || []).slice(0, 20).map(x => `${x.status} · ${x.majorStepCode} · ${x.stepName}`);
    logRoot.innerHTML = alarms.concat(steps).slice(0, 30).map(x => `<div>${escapeHtml(x)}</div>`).join('') || '<div>暂无正式运行事件</div>';
  }
}

function renderRunCommandVisibility(run){
  const role = String(currentUserSnapshot?.activeRole || currentUserSnapshot?.role || '').toLowerCase();
  const canEngineer = ['engineer','admin'].includes(role);
  const hasRun = !!run && !['Completed','Stopped','completed','stopped'].includes(run.status);
  document.getElementById('mockFaultButton')?.classList.toggle('hidden', !(canEngineer && hasRun && (systemInfoSnapshot?.deviceMode || 'Mock') === 'Mock'));
  document.getElementById('redoMajorStepButton')?.classList.toggle('hidden', !(canEngineer && hasRun));
}

document.addEventListener('DOMContentLoaded', () => {
  window.refreshRunView = refreshRun;
  window.invalidatePreflightView = markPreflightInvalid;
});
