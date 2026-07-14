let latestPreflightReport = null;
let currentRunSnapshot = null;
let currentUserSnapshot = null;
let systemInfoSnapshot = null;
let operatorRunSnapshot = null;

async function fetchJsonOrNull(url){
  const res = await fetch(url, {headers:{'Content-Type':'application/json'}});
  const text = await res.text();
  let data = null;
  try{ data = text ? JSON.parse(text) : {}; }catch(e){ data = {raw:text}; }
  if(res.status === 404) return null;
  if(!res.ok){
    const msg = displayLogText(data.detail || data.message || ('请求失败：' + res.status));
    toast(msg, true);
    const error = new Error(msg);
    error.status = res.status;
    error.data = data;
    throw error;
  }
  return data;
}

async function loadRunContext(){
  const [run, operatorSnapshot, systemInfo] = await Promise.all([
    fetchJsonOrNull('/api/runs/current'),
    fetchJsonOrNull('/api/operator/snapshot'),
    fetchJsonOrNull('/api/system/info')
  ]);
  currentRunSnapshot = run;
  operatorRunSnapshot = operatorSnapshot;
  currentUserSnapshot = operatorSnapshot?.activeUser || null;
  systemInfoSnapshot = systemInfo || {deviceMode:'Mock'};
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
  const result = await operatorConfirm({
    title:'确认整机停止',
    message:'普通整机停止将在当前原子动作完成后停止后续调度。',
    warning:'停止不会把未完成动作自动视为完成，后续需要按正式状态处理。',
    confirmText:'确认停止',
    danger:true
  });
  if(result.confirmed) await runAction('stop');
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
    if(body) body.innerHTML = `<div class="validation-issue"><b>预检失败</b><span>${escapeHtml(displayLogText(e.message || '正式预检接口调用失败'))}</span><em>失败</em></div>`;
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
  setText('validationInit', systemInfoSnapshot?.deviceMode === 'Real' ? '真实设备模式' : '模拟设备模式');
  setText('validationInitState', failures.some(x => x.area === 'Device') ? 'BLOCK' : 'PASS');

  if(body){
    const summary = `<div class="validation-ok"><b>正式预检报告</b><span>预检时间：${escapeHtml(formatDateTime(report.generatedAtUtc))} · 关联任务：${Number(report.taskCount || 0)} · 失败：${failures.length} · 警告：${warnings.length} · 允许启动：${report.ok ? '是' : '否'}</span><small>设备状态来自正式后端快照；预检通过后才允许创建并启动运行。</small></div>`;
    const grouped = groupBy(issues, x => x.area || 'Other');
    const rows = Object.entries(grouped).map(([area, items]) => `<div class="validation-group"><h3>${escapeHtml(preflightAreaLabel(area))}</h3>${items.map(item => `<div class="validation-issue ${String(item.severity || '').toLowerCase() === 'warning' ? 'warning' : ''}"><b>${escapeHtml(displayLogText(item.code))}</b><span>${escapeHtml(displayLogText(item.message))}</span><em>${escapeHtml(severityText(item.severity || 'Fail'))}</em></div>`).join('') || '<div class="validation-ok"><b>通过</b><span>当前分组无阻断项。</span></div>'}</div>`).join('');
    body.innerHTML = summary + (Object.keys(grouped).length ? rows : '<div class="validation-ok"><b>全部关键项通过</b><span>通道脚本、任务、试剂与 Mock 设备状态满足启动条件。</span></div>');
  }
  if(btn){
    btn.disabled = !report.ok;
    btn.textContent = report.ok ? '启动运行' : '预检失败，禁止启动';
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
  const decision = await operatorConfirm({
    title:'确认启动运行',
    message:'启动后通道批次与 Slot 将锁定，并按正式命令账本推进。',
    warning:'请确认样本、试剂、DAB、设备预检均已通过。',
    confirmText:'启动运行'
  });
  if(!decision.confirmed) return;

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

  const snapshot = operatorRunSnapshot || await api('/api/operator/snapshot');
  const taskIds = (snapshot.channels || [])
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
  const message = await operatorPrompt('选择或输入故障类型：dispense_failure / temperature_failure / communication_timeout / device_disconnected', 'dispense_failure', {
    title:'注入模拟故障',
    inputRequired:true,
    requiredMessage:'请输入故障类型。'
  });
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
  const reason = await operatorPrompt('请输入大步骤重做原因。', '', {
    title:'大步骤重做',
    inputRequired:true,
    requiredMessage:'大步骤重做必须填写原因。'
  });
  if(!reason || !reason.trim()){
    toast('大步骤重做必须填写原因。', true);
    return;
  }
  const decision = await operatorConfirm({
    title:'确认大步骤重做',
    message:`将从当前大步骤重做。原因：${reason}`,
    warning:'该操作可能额外消耗试剂和 DAB，并会写入审计。',
    confirmText:'确认重做',
    danger:true
  });
  if(!decision.confirmed) return;
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
      <div class="runtime-head"><b>${escapeHtml(batch.drawerCode)} 通道</b><span>${escapeHtml(displayStatusText(batch.status))}</span></div>
      <div class="progress-track"><span style="width:${progress}%"></span></div>
      <p><b>${progress}%</b> · ${escapeHtml(batch.experimentType || '--')} · ${escapeHtml(displayStatusText(batch.workflowSelectionStatus || '--'))}</p>
      <div class="runtime-slides">
        ${(batch.slides || []).map(slide => {
          const workflow = (currentRunSnapshot?.workflowExecutions || []).find(x => x.slideTaskId === slide.id);
          const step = currentStep(workflow);
          return `<div class="runtime-slide loaded"><b>${escapeHtml(slide.slotCode)}</b><small>${escapeHtml(slide.taskType)}</small><em>${escapeHtml(displayStatusText(slide.status))}</em><small>${escapeHtml(displayLogText(step?.stepName || step?.majorStepCode || 'Waiting'))}</small><small>试剂：${escapeHtml(step?.reagentCode || '--')}</small><small>体积：${escapeHtml(step?.volumeUl ?? '--')} uL</small></div>`;
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
    status.innerHTML = '<i></i><b data-status-label>' + escapeHtml(displayStatusText(runStatus)) + '</b>';
  }
  setText('runIdSmall', run ? `${run.runCode || run.id} · ${systemInfoSnapshot?.deviceMode || 'Mock'} · ${majorStepForRun(run)}` : '未生成批号 · Mock');
  renderChannels(run?.channelBatches || []);
  renderRunCommandVisibility(run);
  renderRunResources(operatorRunSnapshot, run);
  const logRoot = document.getElementById('logList');
  if(logRoot){
    const alarms = (operatorRunSnapshot?.alarmDetails || run?.alarms || []).map(x => `[${x.severity}] ${x.code}: ${x.message}`);
    const events = (operatorRunSnapshot?.recentEvents || []).map(x => `${formatDateTime(x.occurredAtUtc)} · ${x.title} · ${x.detail}`);
    logRoot.innerHTML = alarms.concat(events).slice(0, 30).map(x => `<div>${escapeHtml(displayLogText(x))}</div>`).join('') || '<div>暂无正式运行事件</div>';
  }
}

function renderRunResources(snapshot, run){
  if(!snapshot) return;
  const executions = run?.workflowExecutions || [];
  const activeStep = executions.flatMap(x => x.steps || []).find(x => ['Running','Failed','Unknown'].includes(x.status))
    || executions.flatMap(x => x.steps || []).find(x => x.status === 'Pending');
  const command = (snapshot.deviceCommands || [])[0];
  const cooling = snapshot.thermal?.cooling;
  const points = snapshot.thermal?.points || [];
  const abnormalPoints = points.filter(x => !x.isConnected || ['Faulted','Unknown','TimedOut'].includes(x.status));
  const pumps = snapshot.fluidics?.pumps || [];
  const mixers = snapshot.fluidics?.mixers || [];
  const liquidIssues = (snapshot.fluidics?.liquidLevels || []).filter(x => !x.isConnected || ['Low','Depleted','Full','Faulted','Unknown'].includes(x.levelStatus));
  const dab = (snapshot.dabPositions || []).find(x => x.activeDabBatchId);
  const waiting = (snapshot.resourceLeases || []).filter(x => x.status === 'Waiting' || x.status === 'NeedsManualResolution');
  const root = document.getElementById('runResourceGrid');
  if(root){
    const rows = [
      ['当前大步骤', run?.currentMajorStepCode || snapshot.currentMajorStepCode || '--', displayStatusText(activeStep?.status || run?.status || '--')],
      ['原子动作', displayLogText(activeStep?.stepName || activeStep?.actionType || '--'), activeStep ? `${commandTypeText(activeStep.actionType)} · ${activeStep.reagentCode || '无试剂'}` : '--'],
      ['设备命令', commandTypeText(command?.commandType || '--'), displayStatusText(command?.status || '无命令')],
      ['温控', points.length ? `${points.length - abnormalPoints.length}/${points.length} 正常` : '--', abnormalPoints.length ? `${abnormalPoints.length} 异常` : '正常'],
      ['制冷', cooling ? `${(Number(cooling.currentTemperatureDeciC || 0) / 10).toFixed(1)}℃` : '--', displayStatusText(cooling?.status || '--')],
      ['泵 / 混匀', `${pumps.filter(x => x.isConnected).length}/${pumps.length} · ${mixers.filter(x => x.isConnected).length}/${mixers.length}`, snapshot.fluidics?.ready ? '就绪' : '阻断'],
      ['液位', liquidIssues.length ? `${liquidIssues.length} 项需处理` : '正常', liquidIssues.map(x => displayLogText(x.displayName)).join('、') || '就绪'],
      ['DAB', dab ? `${dab.code} · ${formatVolume(dab.remainingVolumeUl)}` : '无活动批次', displayStatusText(dab?.batchStatus || '--')],
      ['资源等待', waiting.length ? `${waiting.length} 项` : '无', displayLogText(waiting[0]?.waitReason || '未发生抢占')]
    ];
    root.innerHTML = rows.map(([label, value, state]) => `<div><b>${escapeHtml(label)}</b><span>${escapeHtml(value)}</span><em>${escapeHtml(state)}</em></div>`).join('');
  }

  const needleRoot = document.getElementById('runNeedles');
  if(needleRoot){
    needleRoot.innerHTML = (snapshot.needles || []).map(needle => `<div><b>${escapeHtml(needle.needleCode)}</b><span>${escapeHtml(needle.loadedReagentCode || needle.loadedSourceType)} · ${escapeHtml(needle.volumeUl)} uL</span><em>${escapeHtml(displayStatusText(needle.status))}${needle.needsWash ? ' · 待洗针' : ''}${needle.lastErrorCode ? ` · ${escapeHtml(displayLogText(needle.lastErrorCode))}` : ''}</em></div>`).join('') || '<div><b>双针状态</b><span>暂无正式针头状态</span><em>待人工确认</em></div>';
  }

  const commandRoot = document.getElementById('runCommandStages');
  if(commandRoot){
    commandRoot.innerHTML = (snapshot.deviceCommands || []).slice(0, 8).map(item => `<div><b>${escapeHtml(commandTypeText(item.commandType))}</b><span>${escapeHtml(displayStatusText(item.status))}</span><em>${escapeHtml(formatDateTime(item.completedAtUtc || item.acknowledgedAtUtc || item.commandSentAtUtc || item.createdAtUtc))}</em></div>`).join('') || '<div><b>暂无设备命令</b><span>已计划 → 命令已下发 → 设备已确认 → 已完成 / 失败 / 待人工确认</span></div>';
  }
}

function renderRunCommandVisibility(run){
  const role = String(currentUserSnapshot?.activeRole || currentUserSnapshot?.role || '').toLowerCase();
  const canEngineer = ['engineer','admin'].includes(role);
  const hasRun = !!run && !['Completed','Stopped','completed','stopped'].includes(run.status);
  const status = String(run?.status || '').toLowerCase();
  setButtonDisabledReason(document.getElementById('runStartButton'), hasRun && !['created','pending'].includes(status), '已有运行正在执行或等待处理');
  setButtonDisabledReason(document.getElementById('runPauseButton'), status !== 'running', status ? `当前状态 ${displayStatusText(run.status)} 不能暂停` : '当前没有正式运行');
  setButtonDisabledReason(document.getElementById('runResumeButton'), status !== 'paused', status ? `当前状态 ${displayStatusText(run.status)} 不能恢复` : '当前没有正式运行');
  setButtonDisabledReason(document.getElementById('runStopButton'), !['running','paused'].includes(status), status ? `当前状态 ${displayStatusText(run.status)} 不能普通停止` : '当前没有正式运行');
  document.getElementById('mockFaultButton')?.classList.toggle('hidden', !(canEngineer && hasRun && (systemInfoSnapshot?.deviceMode || 'Mock') === 'Mock'));
  document.getElementById('redoMajorStepButton')?.classList.toggle('hidden', !(canEngineer && hasRun));
}

document.addEventListener('DOMContentLoaded', () => {
  window.refreshRunView = refreshRun;
  window.invalidatePreflightView = markPreflightInvalid;
  refreshRun();
});
