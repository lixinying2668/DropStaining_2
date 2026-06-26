function escapeHtml(value){
  return String(value ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
}

function slideCount(state){
  return (state.channels || []).reduce((count, channel) => count + (channel.slides || []).length, 0);
}

function setText(id, value){
  const el = document.getElementById(id);
  if(el) el.textContent = value;
}

window.machineStateSnapshot = null;
window.sampleWorkflowOptions = null;
window.reagentScanSessionOverview = null;
window.reagentScanGuide = null;
let activeReagentScanPosition = null;
let activeChannelScriptLetter = null;
let activeConfirmMode = null;
let pendingPrimaryAntibodyCandidates = [];

async function loadHostState(){
  try{
    const state = await api('/api/state');
    window.machineStateSnapshot = state;
    renderShellState(state);
    renderDashboard(state);
    renderSamples(state);
    renderReagents(state);
    renderAlerts(state);
    renderHistory(state);
    renderAdmin(state);
    renderEngineer(state);
    renderRunPage(state);
    return state;
  }catch(e){
    return null;
  }
}

function renderShellState(state){
  const count = slideCount(state);
  setText('metricSlides', count);
  setText('metricReagents', (state.reagents || []).length);
  setText('metricAlarms', (state.alarms || []).length);

  const chip = document.getElementById('statusChip');
  if(chip){
    chip.className = 'status-chip status-' + state.status;
    const label = chip.querySelector('[data-status-label]');
    if(label) label.textContent = statusText(state.status);
  }

  const user = state.activeUser;
  setText('operatorName', user ? user.displayName : '未登录');
  setText('operatorRole', user ? user.role : 'guest');
  setText('operatorAvatar', user ? user.displayName.slice(0, 1).toUpperCase() : '访');
  document.querySelectorAll('.admin-only').forEach(el => {
    el.style.display = user && user.role === 'admin' ? '' : 'none';
  });

  const banner = document.getElementById('alertBanner');
  if(banner){
    const hasAlarm = (state.alarms || []).length > 0;
    banner.classList.toggle('hidden', !hasAlarm);
    setText('alertText', hasAlarm ? state.alarms[0] : '');
  }
}

function renderDashboard(state){
  if(!document.getElementById('drawerBoard')) return;
  const count = slideCount(state);
  setText('kpiInit', state.initialized ? '已通过' : '待初始化');
  setText('kpiReagents', (state.reagents || []).length ? '已扫码' : '待扫码');
  setText('kpiRunId', state.runId ? state.runId.slice(-6) : '--');
  setText('kpiRunIdFull', state.runId || '尚未开始实验');
  document.getElementById('stepInit')?.classList.toggle('done', !!state.initialized);
  document.getElementById('stepSamples')?.classList.toggle('done', count > 0);
  document.getElementById('stepReagents')?.classList.toggle('done', (state.reagents || []).length > 0);
  document.getElementById('stepRun')?.classList.toggle('active', state.status === 'running');

  renderDrawerBoard('drawerBoard', state.channels || []);
  renderSystemChecks(state.system || {});
  renderTimeline('dashboardLogs', state.logs || [], 10);
}

function renderDrawerBoard(id, channels){
  const root = document.getElementById(id);
  if(!root) return;
  root.innerHTML = channels.map((channel, index) => {
    const letter = ['A','B','C','D'][index] || channel.id;
    const slides = channel.slides || [];
    const slots = [4,3,2,1].map(slot => {
      const slide = slides.find(x => x.slot === slot);
      return `<div class="drawer-slot ${slide ? 'loaded' : 'empty'}"><b>${letter}-${String(slot).padStart(2,'0')}</b><span>${escapeHtml(slide ? slide.protocolCode : '空闲')}</span><small>${escapeHtml(slide ? slide.currentStep : '可上样')}</small></div>`;
    }).join('');
    return `<article class="drawer-card status-${channel.status}"><header><b>${letter} 抽屉</b><span>${slides.length}/4</span><em>${escapeHtml(channel.currentStep)}</em></header><div class="progress-track"><span style="width:${channel.progress || 0}%"></span></div><div class="drawer-slots">${slots}</div><footer><button class="btn btn-soft" onclick="location.href='/samples'">通道详情</button><button class="btn btn-soft" onclick="location.href='/alerts'">查看告警</button></footer></article>`;
  }).join('');
}

function renderSystemChecks(system){
  const root = document.getElementById('systemChecks');
  if(!root) return;
  const rows = [
    ['roboticArmHome', '机械臂回零'],
    ['reagentCooling', '制冷连接 ' + (system.reagentTemperatureC ?? 8) + '℃'],
    ['scannerOnline', '扫码器在线'],
    ['liquidSensor', '液位/传感器读取'],
    ['needleWash', '洗针准备'],
    ['pureWaterOk', '水/PBS 可用'],
    ['wasteTankFull', '废液未满', true],
    ['toxicTankFull', '排毒桶未满', true]
  ];
  root.innerHTML = rows.map(([key, label, invert]) => {
    const ok = invert ? !system[key] : !!system[key];
    return `<div><span class="check ${ok ? 'ok' : ''}"></span>${label}</div>`;
  }).join('');
}

async function renderReagents(){
  if(!document.getElementById('reagentDeck')) return;
  await Promise.all([refreshReagentRack(), refreshReagentScanSession()]);
}

async function refreshReagentRack(){
  const deck = document.getElementById('reagentDeck');
  if(!deck) return;
  try{
    const rack = await api('/api/reagents/rack');
    window.reagentRackSnapshot = rack || [];
    renderReagentRackFromDatabase(window.reagentRackSnapshot);
  }catch(e){
    deck.innerHTML = `<div class="empty-state"><b>试剂架读取失败</b><span>${escapeHtml(e.message || '请检查后端只读接口')}</span></div>`;
    setText('reagentBadge', '读取失败');
  }
}

async function refreshReagentScanSession(){
  const root = document.getElementById('reagentScanSessionSummary');
  if(!root) return;
  try{
    const overview = await api('/api/reagents/scan-sessions/overview');
    window.reagentScanSessionOverview = overview || {};
    renderReagentScanSessionOverview(window.reagentScanSessionOverview);
  }catch(e){
    root.textContent = '扫码会话读取失败：' + (e.message || '请检查后端接口');
    const startBtn = document.getElementById('startReagentScanSessionBtn');
    const completeBtn = document.getElementById('completeReagentScanSessionBtn');
    if(startBtn) startBtn.disabled = false;
    if(completeBtn) completeBtn.disabled = true;
  }
}

function renderReagentScanSessionOverview(overview){
  const root = document.getElementById('reagentScanSessionSummary');
  if(!root) return;
  const active = overview?.activeSession || null;
  const latest = overview?.latestCompletedSession || null;
  const session = active || latest;
  const startBtn = document.getElementById('startReagentScanSessionBtn');
  const completeBtn = document.getElementById('completeReagentScanSessionBtn');
  if(startBtn) startBtn.disabled = !!active;
  if(completeBtn) completeBtn.disabled = !active;
  if(!session){
    root.innerHTML = '扫码会话：未开始 · 已扫描：0 / 40 · 有效：0 · 无效：0 · 空位：0 · 未扫描：40' + reagentGuideSummaryText();
    return;
  }

  const stateLabel = active ? '进行中' : '已完成';
  const warning = session.hasWarning ? ` · Warning：${escapeHtml(session.message || '')}` : '';
  root.innerHTML = [
    `扫码会话：${stateLabel}`,
    `会话编号：${escapeHtml(session.sessionCode || session.scanSessionId || '--')}`,
    `开始时间：${escapeHtml(formatDateTime(session.startedAtUtc))}`,
    `已扫描：${Number(session.scannedCount || 0)} / ${Number(session.totalPositionCount || 40)}`,
    `有效：${Number(session.validCount || 0)}`,
    `无效：${Number(session.invalidCount || 0)}`,
    `空位：${Number(session.emptyCount || 0)}`,
    `未扫描：${Number(session.unscannedCount || 0)}`
  ].join(' · ') + warning + reagentGuideSummaryText();
}

function reagentGuideSummaryText(){
  const guide = window.reagentScanGuide;
  if(!guide || !guide.positions?.length || guide.cancelled) return '';
  const index = Math.min(Number(guide.index || 0), guide.positions.length - 1);
  const pos = guide.positions[index];
  const current = rackPositionByCode(pos);
  return ` · 当前引导：${escapeHtml(guide.label || '扫码引导')}，第 ${index + 1} / ${guide.positions.length} 位，${escapeHtml(pos)}${current ? ` · ${escapeHtml(scanStateLabel(scanStateOf(current)))}` : ''}`;
}

function renderReagentRackFromDatabase(rack){
  const validCount = rack.filter(x => scanStateOf(x) === 'VALID').length;
  const invalidCount = rack.filter(x => scanStateOf(x) === 'INVALID').length;
  setText('reagentBadge', `${validCount}/40 VALID${invalidCount ? ` · ${invalidCount} INVALID` : ''}`);
  const byPosition = new Map(rack.map(x => [x.position, x]));
  const deck = document.getElementById('reagentDeck');
  if(!deck) return;
  deck.innerHTML = [1,2,3,4,5].map(col => {
    const rows = [1,2,3,4,5,6,7,8].map(row => {
      const pos = 'R' + (((col - 1) * 8) + row);
      const position = byPosition.get(pos);
      const bottle = position?.bottle;
      const scanState = scanStateOf(position);
      const volume = bottle ? formatVolume(bottle.remainingVolumeUl) : scanStateLabel(scanState);
      const title = bottle ? (bottle.name || bottle.reagentCode || scanState) : scanStateTitle(scanState);
      const subtitle = bottle
        ? [bottle.reagentCode, bottle.lotNo, bottle.barcodeSummary || position?.barcodeSummary].filter(Boolean).join(' / ')
        : invalidOrScanMessage(position);
      return `<button type="button" class="vial ${bottle ? 'filled' : 'empty'} scan-${scanState.toLowerCase()} ${escapeHtml(bottle?.reagentType || '')}" onclick="showReagentDetail('${escapeHtml(pos)}')" title="${escapeHtml(pos + ' ' + title + ' ' + invalidOrScanMessage(position))}"><b>${pos}</b><div class="vial-main"><span>${escapeHtml(title)}</span><small>${escapeHtml(subtitle)}</small></div><em>${escapeHtml(volume)}</em></button>`;
    }).join('');
    return `<div class="reagent-rack"><header><b>ch${col}</b><span>R${(col-1)*8+1}-R${col*8}</span></header>${rows}</div>`;
  }).join('');

  const columnStatus = document.getElementById('columnStatus');
  if(columnStatus){
    columnStatus.innerHTML = [1,2,3,4,5].map(col => {
      const column = rack.filter(x => x.columnNo === col);
      const valid = column.filter(x => scanStateOf(x) === 'VALID').length;
      const invalid = column.filter(x => scanStateOf(x) === 'INVALID').length;
      const empty = column.filter(x => scanStateOf(x) === 'EMPTY').length;
      const stateClass = invalid ? 'invalid' : valid ? 'has-data' : 'empty';
      const label = invalid ? `${invalid} INVALID` : `${valid}/8 VALID`;
      return `<div class="${stateClass}"><b>ch${col}</b><span>${escapeHtml(label)}</span><em>${escapeHtml(empty ? `${empty} EMPTY` : `R${(col-1)*8+1}-R${col*8}`)}</em></div>`;
    }).join('');
  }
}

function scanStateOf(position){
  return String(position?.scanState || (position?.bottle ? 'VALID' : 'UNSCANNED')).toUpperCase();
}

function scanStateLabel(scanState){
  return ({VALID:'VALID', INVALID:'INVALID', EMPTY:'EMPTY', UNSCANNED:'未扫码'}[scanState] || scanState || '未扫码');
}

function scanStateTitle(scanState){
  return ({VALID:'有效试剂', INVALID:'无效扫码', EMPTY:'空位', UNSCANNED:'未扫码'}[scanState] || '未扫码');
}

function invalidOrScanMessage(position){
  const state = scanStateOf(position);
  if(state === 'INVALID') return position?.validationMessage || '扫码校验未通过';
  if(state === 'EMPTY') return position?.validationMessage || '空位';
  if(state === 'UNSCANNED') return '未扫码';
  return position?.validationMessage || 'OK';
}

function rackPositionByCode(pos){
  return (window.reagentRackSnapshot || []).find(x => String(x.position || '').toUpperCase() === String(pos || '').toUpperCase());
}

function activeReagentScanSession(){
  return window.reagentScanSessionOverview?.activeSession || null;
}

function formatVolume(volumeUl){
  if(volumeUl === null || volumeUl === undefined || volumeUl === '') return '--';
  return `${Math.round((Number(volumeUl) / 1000) * 10) / 10} mL`;
}

function formatDate(value){
  return value ? String(value).slice(0, 10) : '--';
}

function formatDateTime(value){
  if(!value) return '--';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString('zh-CN', {hour12:false});
}

function renderAlerts(state){
  if(!document.getElementById('alarmList')) return;
  const alarms = state.alarms || [];
  const root = document.getElementById('alarmList');
  root.innerHTML = alarms.length
    ? alarms.map(alarm => `<div class="alarm-row level-high"><b>高</b><span>${escapeHtml(alarm)}</span><em>影响：当前任务 / 启动校验</em><small>建议：查看关联日志并处理对应试剂、设备或通道状态。</small></div>`).join('')
    : '<div class="empty-state"><b>暂无告警</b><span>系统当前没有未处理告警。</span></div>';
  renderTimeline('alertLogs', state.logs || [], 40);
}

function renderHistory(state){
  if(!document.getElementById('historySlides')) return;
  const slideRows = (state.channels || []).flatMap((channel, index) => {
    const letter = ['A','B','C','D'][index] || channel.id;
    return (channel.slides || []).map(slide => `<div class="table-row"><span>${escapeHtml(slide.barcode)}</span><span>${letter}-${String(slide.slot).padStart(2,'0')}</span><span>${escapeHtml(slide.protocolCode)}</span><span>${escapeHtml(slide.status)}</span><span>${escapeHtml(slide.currentStep)}</span></div>`);
  });
  document.getElementById('historySlides').innerHTML = '<div class="table-row head"><span>玻片</span><span>Slot</span><span>流程</span><span>状态</span><span>当前步骤</span></div>' + (slideRows.join('') || '<div class="empty-state"><b>暂无样本记录</b><span>请先扫描样本区。</span></div>');
  const reagentRows = (state.reagents || []).slice(0,20).map(r => `<div class="table-row"><span>${escapeHtml(r.position)}</span><span>${escapeHtml(r.code)}</span><span>${escapeHtml(r.lotNo || '--')}</span><span>${r.volumeMl}mL</span><span>${escapeHtml(r.expireDate || '--')}</span></div>`);
  document.getElementById('historyReagents').innerHTML = '<div class="table-row head"><span>位置</span><span>代码</span><span>批号</span><span>余量</span><span>有效期</span></div>' + (reagentRows.join('') || '<div class="empty-state"><b>暂无试剂记录</b><span>请先扫描试剂区。</span></div>');
  renderTimeline('historyLogs', state.logs || [], 80);
}

async function renderAdmin(state){
  if(!document.getElementById('userTable')) return;
  const users = await api('/api/users');
  setText('adminUserCount', users.length);
  setText('adminReagentCount', (state.reagents || []).length);
  setText('adminLogCount', (state.logs || []).length);
  setText('adminAlarmCount', (state.alarms || []).length);
  document.getElementById('userTable').innerHTML = '<div class="table-row head"><span>用户名</span><span>姓名</span><span>角色</span><span>启用</span><span>操作</span></div>' + users.map(u => `<div class="table-row"><span>${escapeHtml(u.username)}</span><span>${escapeHtml(u.displayName)}</span><span class="badge-soft">${escapeHtml(u.role)}</span><span>${u.enabled ? '是' : '否'}</span><span><button class="btn btn-soft">停用/启用</button></span></div>`).join('');
  renderTimeline('adminLogs', state.logs || [], 50);
}

function renderEngineer(state){
  if(!document.getElementById('engineerRunState')) return;
  setText('engineerRunState', state.status === 'running' ? '存在，默认只读' : '无活动任务');
}

async function renderConfigure(){
  if(!document.getElementById('protocolTable')) return;
  const workflows = await api('/api/workflows');
  const catalog = await api('/api/reagents/catalog');
  const liquidClasses = await api('/api/engineering/liquid-classes');
  const dab = await api('/api/dab');
  document.getElementById('protocolTable').innerHTML = workflows.map(w => {
    const versions = (w.versions || []).slice().sort((a, b) => (a.versionNo || 0) - (b.versionNo || 0));
    const version = versions[versions.length - 1];
    const status = workflowStatusText(version?.status);
    return `<div class="protocol-version-row"><b>${escapeHtml(w.code)}</b><span>${escapeHtml(w.name)}</span><em>${version ? 'v' + escapeHtml(version.versionLabel) + ' · ' + status : '无版本'}</em><small>${escapeHtml(w.description)}</small><div><button class="btn btn-soft" onclick="api('/api/workflows/${w.id}').then(()=>toast('已从数据库读取流程详情'))">查看步骤</button><button class="btn btn-soft" onclick="copyWorkflowDraft('${w.id}')">复制</button><button class="btn btn-soft">停用</button></div></div>`;
  }).join('') || '<div class="empty-state"><b>暂无数据库流程</b><span>请先导入或创建流程版本。</span></div>';
  document.getElementById('dabPreview').innerHTML = `<div><span>IHC 张数</span><b>${dab.slideCount}</b></div><div><span>总量</span><b>${dab.totalMl}</b><em>mL</em></div><div><span>A/B/水</span><b>${dab.dabAMl}/${dab.dabBMl}/${dab.pureWaterMl}</b></div>`;
  document.getElementById('catalogTable').innerHTML = '<div class="table-row head"><span>代码</span><span>名称</span><span>类别</span><span>报警余量</span><span>液体类型</span></div>'
    + (catalog.map(item => `<div class="table-row"><span>${escapeHtml(item.reagentCode)}</span><span>${escapeHtml(item.name)}</span><span>${escapeHtml(item.reagentType || '--')}</span><span>${item.minimumAlarmVolumeUl ?? '--'} μL</span><span>${escapeHtml(item.liquidClassCode || '--')}</span></div>`).join('')
    || '<div class="empty-state"><b>暂无数据库试剂目录</b><span>请先导入或维护试剂目录。</span></div>');
}

function workflowStatusText(status){
  return ({Draft:'草稿', Published:'已发布', Retired:'已停用'}[status] || status || '--');
}

function renderTimeline(id, items, limit){
  const root = document.getElementById(id);
  if(!root) return;
  root.innerHTML = (items || []).slice(0, limit).map(item => `<div><i></i><span>${escapeHtml(item)}</span></div>`).join('') || '<div><i></i><span>暂无日志</span></div>';
}

function renderRunPage(state){
  const status = document.getElementById('runStatus');
  if(status){
    status.className = 'status-chip status-' + state.status;
    status.innerHTML = '<i></i><b data-status-label>' + statusText(state.status) + '</b>';
  }
  setText('runIdSmall', state.runId || 'not started');
  if(typeof renderChannels === 'function'){
    renderChannels(state.channels || []);
  }
  const logRoot = document.getElementById('logList');
  if(logRoot){
    logRoot.innerHTML = (state.logs || []).slice(0,30).map(x => `<div>${escapeHtml(x)}</div>`).join('') || '<div>No events</div>';
  }
}

function appendMachineLog(state, event){
  state.logs = state.logs || [];
  const message = event.payload?.message || event.type;
  state.logs.unshift('[' + new Date(event.occurredAtUtc || Date.now()).toLocaleTimeString('zh-CN', {hour12:false}) + '] ' + event.type + ' ' + message);
  if(state.logs.length > 120) state.logs.length = 120;
}

function findSlideById(state, slideTaskId){
  for(const channel of state.channels || []){
    const slide = (channel.slides || []).find(x => x.id === slideTaskId);
    if(slide) return slide;
  }
  return null;
}

function applyMachineEvent(event){
  const state = window.machineStateSnapshot;
  if(!state || !event) return;
  const payload = event.payload || {};
  appendMachineLog(state, event);
  if(typeof window.invalidatePreflightView === 'function'
      && ['scanSession.changed','reagent.changed','reagentBottle.changed','channelBatch.changed','slideTask.created','machine.stateChanged','workflowStep.started','workflowStep.completed','alarm.raised','alarm.acknowledged','device.connectionChanged','qr.scanCompleted'].includes(event.type)){
    window.invalidatePreflightView('后端状态已变化。');
  }
  if(isReagentEvent(event)){
    refreshReagentRack();
    refreshReagentScanSession();
  }
  switch(event.type){
    case 'channelBatch.changed':
    case 'slideTask.created':
      loadHostState();
      if(typeof window.refreshRunView === 'function') window.refreshRunView();
      return;
    case 'machine.stateChanged':
      state.runId = payload.runId || event.runId || state.runId;
      state.status = String(payload.status || state.status || '').toLowerCase();
      break;
    case 'slideTask.stateChanged': {
      const slide = findSlideById(state, payload.slideTaskId || event.entityId);
      if(slide){
        slide.status = String(payload.status || slide.status || '').toLowerCase();
        slide.currentStep = payload.currentStep || slide.currentStep;
      }
      break;
    }
    case 'workflowStep.started':
    case 'workflowStep.completed': {
      const slide = findSlideById(state, payload.slideTaskId);
      if(slide){
        slide.currentStep = payload.stepName || payload.majorStepCode || slide.currentStep;
        slide.status = 'running';
        if(event.type === 'workflowStep.completed') slide.progress = Math.min(100, Number(slide.progress || 0) + 20);
      }
      break;
    }
    case 'temperature.changed':
      state.system = state.system || {};
      state.system.currentTemperatureC = Math.round(Number(payload.currentTemperatureDeciC || 0) / 10);
      break;
    case 'alarm.raised':
      state.alarms = state.alarms || [];
      state.alarms.unshift((payload.code || 'alarm') + ': ' + (payload.message || ''));
      break;
    case 'reagent.bottleDepleted':
    case 'dab.batchChanged':
    case 'device.connectionChanged':
    case 'qr.scanCompleted':
      break;
  }
  renderShellState(state);
  renderDashboard(state);
  renderAlerts(state);
  renderHistory(state);
  renderEngineer(state);
  renderRunPage(state);
  if(typeof window.refreshRunView === 'function'
      && ['machine.stateChanged','workflowStep.started','workflowStep.completed','alarm.raised','alarm.acknowledged','device.connectionChanged'].includes(event.type)){
    window.refreshRunView();
  }
}

function isReagentEvent(event){
  const text = `${event.type || ''} ${event.entityType || ''}`.toLowerCase();
  return text.includes('reagent') || text.includes('scansession') || event.type === 'qr.scanCompleted';
}

let machineSocket = null;
let machineReconnectTimer = null;
let machineReconnectDelayMs = 1000;
let machineEverConnected = false;

function machineHubUrl(){
  const scheme = location.protocol === 'https:' ? 'wss:' : 'ws:';
  return scheme + '//' + location.host + '/hubs/machine';
}

function connectMachineHub(){
  if(!('WebSocket' in window)) return;
  if(machineSocket && (machineSocket.readyState === WebSocket.OPEN || machineSocket.readyState === WebSocket.CONNECTING)) return;
  try{
    machineSocket = new WebSocket(machineHubUrl());
    machineSocket.onopen = async () => {
      machineSocket.send(JSON.stringify({protocol:'json', version:1}) + '\x1e');
      const wasReconnect = machineEverConnected;
      machineEverConnected = true;
      machineReconnectDelayMs = 1000;
      if(wasReconnect) await loadHostState();
    };
    machineSocket.onmessage = event => {
      String(event.data || '').split('\x1e').filter(Boolean).forEach(frame => {
        const message = JSON.parse(frame);
        if(message.type === 1 && message.target === 'machineEvent'){
          applyMachineEvent(message.arguments && message.arguments[0]);
        }
      });
    };
    machineSocket.onclose = scheduleMachineReconnect;
    machineSocket.onerror = () => {
      try{ machineSocket.close(); }catch(e){}
    };
  }catch(e){
    scheduleMachineReconnect();
  }
}

function scheduleMachineReconnect(){
  if(machineReconnectTimer) return;
  machineReconnectTimer = setTimeout(() => {
    machineReconnectTimer = null;
    connectMachineHub();
    machineReconnectDelayMs = Math.min(machineReconnectDelayMs * 2, 10000);
  }, machineReconnectDelayMs);
}

async function startReagentScanSession(){
  try{
    const result = await api('/api/reagents/scan-sessions/start', {
      method:'POST',
      body: JSON.stringify({commandId: commandId('reagent-scan-start')})
    });
    await Promise.all([refreshReagentScanSession(), refreshReagentRack()]);
    toast(result.message || '扫码会话已开始');
  }catch(e){
    toast(e.message || '开始扫码会话失败', true);
  }
}

async function completeReagentScanSession(){
  const active = window.reagentScanSessionOverview?.activeSession;
  if(!active?.scanSessionId){
    toast('当前没有进行中的扫码会话。', true);
    return;
  }
  const message = `完成当前扫码会话？\n已扫描：${Number(active.scannedCount || 0)} / ${Number(active.totalPositionCount || 40)}\n未扫描：${Number(active.unscannedCount || 0)}`;
  if(!confirm(message)) return;
  try{
    const result = await api(`/api/reagents/scan-sessions/${encodeURIComponent(active.scanSessionId)}/complete`, {
      method:'POST',
      body: JSON.stringify({commandId: commandId('reagent-scan-complete')})
    });
    await Promise.all([refreshReagentScanSession(), refreshReagentRack()]);
    toast(result.message || '扫码会话已完成');
  }catch(e){
    toast(e.message || '完成扫码会话失败', true);
  }
}

async function scanReagents(){
  await beginReagentScanGuide('全部试剂架', Array.from({length: 40}, (_, index) => 'R' + (index + 1)));
}

function mockColumnScan(col){
  const start = ((Number(col) || 1) - 1) * 8 + 1;
  const positions = Array.from({length: 8}, (_, index) => 'R' + (start + index));
  beginReagentScanGuide('ch' + col, positions);
}

async function beginReagentScanGuide(label, positions){
  await refreshReagentScanSession();
  if(!activeReagentScanSession()){
    toast('请先点击【开始扫码】创建正式扫码会话。', true);
    return;
  }
  window.reagentScanGuide = {label, positions, index: 0, cancelled: false};
  renderReagentScanSessionOverview(window.reagentScanSessionOverview || {});
  openReagentScanModal(positions[0], true);
}

function showReagentDetail(pos){
  if(activeReagentScanSession()){
    openReagentScanModal(pos, false);
    return;
  }
  const body = document.getElementById('reagentDetailBody');
  if(!body) return;
  const position = rackPositionByCode(pos);
  const bottle = position?.bottle;
  const state = scanStateOf(position);
  body.innerHTML = `<div><span>位置</span><b>${escapeHtml(pos)}</b></div><div><span>SCAN_STATE</span><b>${escapeHtml(scanStateLabel(state))}</b><small>${escapeHtml(position?.validationMessage || '--')}</small></div><div><span>完整条码</span><b>${escapeHtml(bottle?.fullBarcode || position?.rawBarcode || '--')}</b><small>${escapeHtml(bottle?.barcodeSummary || position?.barcodeSummary || '--')}</small></div><div><span>试剂名称</span><b>${escapeHtml(bottle?.name || scanStateTitle(state))}</b></div><div><span>试剂代码</span><b>${escapeHtml(bottle?.reagentCode || position?.parsedReagentCode || '--')}</b></div><div><span>剩余量</span><b>${escapeHtml(bottle ? formatVolume(bottle.remainingVolumeUl) : '--')}</b></div><div><span>批号 / 序列号</span><b>${escapeHtml([bottle?.lotNo, bottle?.serialNo].filter(Boolean).join(' / ') || '--')}</b></div><div><span>有效期</span><b>${escapeHtml(formatDate(bottle?.expirationDate))}</b></div><div><span>最后扫码时间</span><b>${escapeHtml(formatDateTime(position?.lastScannedAtUtc || bottle?.lastScannedAtUtc))}</b></div><div><span>扫码会话</span><b>${escapeHtml(position?.lastScanSessionCode || '--')}</b><small>${escapeHtml(position?.lastScanSessionStatus || '--')}</small></div>`;
  document.getElementById('reagentDetail')?.classList.remove('hidden');
}

function openReagentScanModal(pos, fromGuide=false){
  const active = activeReagentScanSession();
  if(!active?.scanSessionId){
    toast('请先点击【开始扫码】创建正式扫码会话。', true);
    return;
  }
  activeReagentScanPosition = pos;
  const position = rackPositionByCode(pos);
  const state = scanStateOf(position);
  const bottle = position?.bottle;
  setText('reagentScanTitle', `${pos} 扫码确认`);
  const context = document.getElementById('reagentScanContext');
  if(context){
    context.innerHTML = [
      `<div><span>当前扫码会话</span><b>${escapeHtml(active.sessionCode || active.scanSessionId)}</b><small>${escapeHtml(formatDateTime(active.startedAtUtc))}</small></div>`,
      `<div><span>当前位置</span><b>${escapeHtml(pos)}</b></div>`,
      `<div><span>所属扫描通道</span><b>${escapeHtml(position?.scannerChannelCode || ('ch' + (position?.scannerChannelNo || '--')))}</b></div>`,
      `<div><span>当前结果</span><b>${escapeHtml(scanStateLabel(state))}</b><small>${escapeHtml(position?.validationMessage || invalidOrScanMessage(position))}</small></div>`
    ].join('');
  }
  const result = document.getElementById('reagentScanResult');
  if(result){
    result.innerHTML = bottle
      ? `<div><span>试剂代码</span><b>${escapeHtml(bottle.reagentCode)}</b></div><div><span>试剂名称</span><b>${escapeHtml(bottle.name || '--')}</b></div><div><span>余量</span><b>${escapeHtml(formatVolume(bottle.remainingVolumeUl))}</b></div><div><span>批号 / 序列号</span><b>${escapeHtml([bottle.lotNo, bottle.serialNo].filter(Boolean).join(' / ') || '--')}</b></div><div><span>有效期</span><b>${escapeHtml(formatDate(bottle.expirationDate))}</b></div>`
      : `<div><span>后端结果</span><b>${escapeHtml(scanStateLabel(state))}</b><small>${escapeHtml(position?.validationMessage || '等待确认')}</small></div>`;
  }
  const mode = document.getElementById('reagentScanMode');
  const barcode = document.getElementById('reagentBarcodeInput');
  const expiration = document.getElementById('reagentExpirationInput');
  if(mode) mode.value = 'barcode';
  if(barcode) barcode.value = '';
  if(expiration) expiration.value = '';
  setText('reagentScanHint', state === 'UNSCANNED'
    ? '前端只提交原始文本，条码解析和校验由后端完成。'
    : '重新扫码将以本次确认结果为准；后端会在事务内更新瓶位关系。');
  syncReagentScanMode();
  document.getElementById('reagentScanModal')?.classList.remove('hidden');
}

function syncReagentScanMode(){
  const mode = document.getElementById('reagentScanMode')?.value || 'barcode';
  document.getElementById('reagentBarcodeLabel')?.classList.toggle('hidden', mode === 'empty');
  document.getElementById('reagentExpirationLabel')?.classList.toggle('hidden', mode === 'empty');
  setText('reagentScanHint', mode === 'empty'
    ? `确认将 ${activeReagentScanPosition || ''} 标记为空位；后端不会创建试剂瓶。`
    : '前端只提交原始文本，条码解析和校验由后端完成。');
}

function cancelReagentScanModal(){
  document.getElementById('reagentScanModal')?.classList.add('hidden');
  const guide = window.reagentScanGuide;
  if(guide && guide.positions?.[guide.index] === activeReagentScanPosition){
    guide.cancelled = true;
    renderReagentScanSessionOverview(window.reagentScanSessionOverview || {});
    toast('扫码引导已取消；已确认的 R 位保留，未扫描位置保持未扫描。');
  }
  activeReagentScanPosition = null;
}

async function confirmReagentPositionScan(){
  const active = activeReagentScanSession();
  const pos = activeReagentScanPosition;
  if(!active?.scanSessionId || !pos){
    toast('当前没有进行中的扫码会话或目标 R 位。', true);
    return;
  }
  const mode = document.getElementById('reagentScanMode')?.value || 'barcode';
  const rawBarcode = document.getElementById('reagentBarcodeInput')?.value?.trim() || '';
  const expirationDate = document.getElementById('reagentExpirationInput')?.value || null;
  if(mode === 'empty' && !confirm(`确认将 ${pos} 标记为空位？`)) return;
  if(mode !== 'empty' && !rawBarcode){
    toast('请输入 Mock 条码原始文本，或选择空位确认。', true);
    return;
  }
  try{
    const response = await api('/api/reagents/scan-confirm', {
      method:'POST',
      body: JSON.stringify({
        commandId: commandId('reagent-scan-confirm'),
        scanSessionId: active.scanSessionId,
        items: [{
          position: pos,
          scanResult: mode === 'empty' ? 'EMPTY' : 'VALID',
          rawBarcode: mode === 'empty' ? null : rawBarcode,
          locatorCode: pos,
          expirationDate
        }]
      })
    });
    await Promise.all([refreshReagentRack(), refreshReagentScanSession()]);
    const refreshed = rackPositionByCode(pos);
    renderReagentPositionResult(refreshed, response);
    toast(response.message || 'R 位扫码确认已保存');
    advanceReagentScanGuide(pos);
  }catch(e){
    setText('reagentScanHint', e.message || '扫码确认失败');
  }
}

function renderReagentPositionResult(position, response){
  const result = document.getElementById('reagentScanResult');
  if(!result) return;
  const bottle = position?.bottle;
  const state = scanStateOf(position);
  result.innerHTML = bottle
    ? `<div><span>后端状态</span><b>${escapeHtml(scanStateLabel(state))}</b><small>${escapeHtml(position?.validationMessage || response?.validationMessage || '--')}</small></div><div><span>试剂代码</span><b>${escapeHtml(bottle.reagentCode)}</b></div><div><span>试剂名称</span><b>${escapeHtml(bottle.name || '--')}</b></div><div><span>余量</span><b>${escapeHtml(formatVolume(bottle.remainingVolumeUl))}</b></div><div><span>批号 / 序列号</span><b>${escapeHtml([bottle.lotNo, bottle.serialNo].filter(Boolean).join(' / ') || '--')}</b></div><div><span>有效期</span><b>${escapeHtml(formatDate(bottle.expirationDate))}</b></div>`
    : `<div><span>后端状态</span><b>${escapeHtml(scanStateLabel(state))}</b><small>${escapeHtml(position?.validationMessage || response?.validationMessage || '--')}</small></div>`;
}

function advanceReagentScanGuide(pos){
  const guide = window.reagentScanGuide;
  if(!guide || guide.cancelled || guide.positions?.[guide.index] !== pos) return;
  guide.index += 1;
  if(guide.index >= guide.positions.length){
    window.reagentScanGuide = null;
    renderReagentScanSessionOverview(window.reagentScanSessionOverview || {});
    toast('扫码引导已完成。');
    return;
  }
  renderReagentScanSessionOverview(window.reagentScanSessionOverview || {});
  setTimeout(() => openReagentScanModal(guide.positions[guide.index], true), 250);
}

function commandId(prefix){
  return prefix + '-' + (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + '-' + Math.random().toString(16).slice(2));
}

async function createWorkflowDraft(){
  const code = prompt('流程代码', 'CUSTOM-' + new Date().toISOString().slice(0, 10).replaceAll('-', ''));
  if(!code) return;
  const name = prompt('流程名称', code + ' 草稿') || code;
  const workflowType = (prompt('流程类型：HE 或 IHC', 'IHC') || 'IHC').trim().toUpperCase();
  if(!['HE', 'IHC'].includes(workflowType)){
    toast('流程类型必须是 HE 或 IHC', true);
    return;
  }
  const versionLabel = prompt('草稿版本标签', '0.1') || '0.1';
  const description = prompt('流程说明', '管理员新建流程草稿。') || '管理员新建流程草稿。';
  const changeNote = prompt('变更说明', '创建空白草稿。') || '创建空白草稿。';
  const result = await api('/api/workflows/drafts', {
    method:'POST',
    body: JSON.stringify({
      commandId: commandId('workflow-draft'),
      code,
      name,
      workflowType,
      description,
      versionLabel,
      changeNote
    })
  });
  toast(`已创建草稿：${result.code} v${result.versionLabel}`);
  await renderConfigure();
}

async function copyWorkflowDraft(sourceWorkflowId){
  let workflowId = sourceWorkflowId;
  let workflow = null;
  const workflows = await api('/api/workflows');
  if(workflowId){
    workflow = workflows.find(x => x.id === workflowId);
  }else{
    const code = prompt('输入要复制的流程代码');
    if(!code) return;
    workflow = workflows.find(x => String(x.code).toUpperCase() === code.trim().toUpperCase());
    workflowId = workflow?.id;
  }
  if(!workflowId || !workflow){
    toast('未找到要复制的流程', true);
    return;
  }
  const versions = (workflow.versions || []).slice().sort((a, b) => (a.versionNo || 0) - (b.versionNo || 0));
  const latest = versions[versions.length - 1];
  const defaultLabel = latest ? String(Number.parseInt(String(latest.versionLabel).split('.')[0], 10) + 1 || (latest.versionNo + 1)) + '.0' : '0.1';
  const versionLabel = prompt('新草稿版本标签', defaultLabel);
  if(versionLabel === null) return;
  const changeNote = prompt('变更说明', `复制 ${workflow.code} ${latest ? 'v' + latest.versionLabel : ''} 为新草稿。`) || '复制现有流程为新草稿。';
  const result = await api('/api/workflows/drafts', {
    method:'POST',
    body: JSON.stringify({
      commandId: commandId('workflow-copy'),
      sourceWorkflowId: workflowId,
      versionLabel,
      changeNote
    })
  });
  toast(`已复制为草稿：${result.code} v${result.versionLabel}`);
  await renderConfigure();
}

async function renderAdmin(state){
  if(!document.getElementById('userTable')) return;
  const users = await api('/api/users');
  setText('adminUserCount', users.length);
  setText('adminReagentCount', (state.reagents || []).length);
  setText('adminLogCount', (state.logs || []).length);
  setText('adminAlarmCount', (state.alarms || []).length);
  wireAdminToolbar(users);
  document.getElementById('userTable').innerHTML = '<div class="table-row head"><span>用户</span><span>显示名</span><span>角色</span><span>启用</span><span>操作</span></div>' + users.map(u => `<div class="table-row"><span>${escapeHtml(u.username)}</span><span>${escapeHtml(u.displayName)}</span><span class="badge-soft">${escapeHtml((u.roles || [u.role]).join(','))}</span><span>${u.enabled ? '是' : '否'}</span><span class="button-row"><button class="btn btn-soft" onclick="adminRenameUser('${u.id}', '${escapeHtml(u.displayName)}')">改名</button><button class="btn btn-soft" onclick="adminToggleUser('${u.id}', ${!u.enabled})">${u.enabled ? '禁用' : '启用'}</button><button class="btn btn-soft" onclick="adminResetPassword('${u.id}')">重置</button><button class="btn btn-soft" onclick="adminSetRoles('${u.id}', '${escapeHtml((u.roles || []).join(','))}')">角色</button><button class="btn btn-soft" onclick="adminDeleteUser('${u.id}')">删除</button></span></div>`).join('');
  renderTimeline('adminLogs', state.logs || [], 50);
}

function wireAdminToolbar(users){
  const buttons = document.querySelectorAll('.section-title .button-row .btn');
  if(buttons[0]) buttons[0].onclick = adminCreateUser;
  if(buttons[1]) buttons[1].onclick = () => {
    const username = prompt('输入要重置密码的用户名');
    const user = users.find(x => x.username === username);
    if(user) adminResetPassword(user.id); else if(username) toast('用户不存在', true);
  };
}

async function adminCreateUser(){
  const username = prompt('用户名');
  if(!username) return;
  const displayName = prompt('显示名', username) || username;
  const password = prompt('初始密码', '123456');
  if(!password) return;
  const rolesText = prompt('角色，逗号分隔', 'operator') || 'operator';
  await api('/api/users', {method:'POST', body: JSON.stringify({commandId:commandId('user-create'), username, displayName, password, roles:rolesText.split(',').map(x=>x.trim()).filter(Boolean)})});
  toast('用户已创建');
  await loadHostState();
}

async function adminRenameUser(id, currentName){
  const displayName = prompt('新的显示名', currentName);
  if(!displayName) return;
  await api(`/api/users/${id}/display-name`, {method:'PUT', body: JSON.stringify({commandId:commandId('user-rename'), displayName})});
  toast('显示名已更新');
  await loadHostState();
}

async function adminToggleUser(id, enabled){
  if(!confirm(enabled ? '确认启用账号？' : '确认禁用账号？')) return;
  await api(`/api/users/${id}/enabled`, {method:'PUT', body: JSON.stringify({commandId:commandId('user-enabled'), enabled})});
  toast('账号状态已更新');
  await loadHostState();
}

async function adminResetPassword(id){
  const newPassword = prompt('新密码', '123456');
  if(!newPassword) return;
  await api(`/api/users/${id}/password`, {method:'PUT', body: JSON.stringify({commandId:commandId('user-password'), newPassword})});
  toast('密码已重置');
}

async function adminSetRoles(id, currentRoles){
  const rolesText = prompt('角色，逗号分隔', currentRoles || 'operator');
  if(!rolesText) return;
  await api(`/api/users/${id}/roles`, {method:'PUT', body: JSON.stringify({commandId:commandId('user-roles'), roles:rolesText.split(',').map(x=>x.trim()).filter(Boolean)})});
  toast('角色已更新');
  await loadHostState();
}

async function adminDeleteUser(id){
  if(!confirm('确认删除账号？存在审计记录的用户会被拒绝删除。')) return;
  await api(`/api/users/${id}?commandId=${encodeURIComponent(commandId('user-delete'))}`, {method:'DELETE'});
  toast('用户已删除');
  await loadHostState();
}

function channelByLetter(letter){
  const normalized = String(letter || '').trim().toUpperCase();
  return (window.machineStateSnapshot?.channels || []).find((channel, index) => {
    const channelLetter = String(channel.drawerCode || ['A','B','C','D'][index] || channel.id).trim().toUpperCase();
    return channelLetter === normalized;
  });
}

function slotCode(letter, slot){
  return `${letter}-${String(slot).padStart(2, '0')}`;
}

function parseSlotCode(value){
  const match = String(value || '').trim().toUpperCase().match(/^([A-D])-(\d{1,2})$/);
  return match ? {letter: match[1], slot: Number(match[2])} : {letter: '', slot: 0};
}

function channelWorkflowInfo(channel){
  if(!channel || !channel.workflowVersionId){
    return {
      selected: false,
      label: '未选择',
      detail: '请先选择通道实验脚本',
      version: '--',
      status: channel?.workflowSelectionStatus || 'Unselected'
    };
  }
  const name = channel.workflowName || channel.workflowCode || channel.experimentType || '已选择流程';
  const version = channel.workflowVersionLabel ? `v${channel.workflowVersionLabel}` : channel.workflowVersionId;
  return {
    selected: true,
    label: name,
    detail: `${channel.experimentType || '--'} / ${version}`,
    version,
    status: channel.workflowSelectionStatus || 'Selected'
  };
}

function workflowStatusLabel(channel){
  if(channel?.workflowLocked || channel?.workflowSelectionStatus === 'Locked') return '已锁定';
  if(channel?.workflowSelectionStatus === 'NeedsManualResolution') return '需人工处理';
  return channel?.workflowSelectionStatus === 'Selected' ? '未启动' : '未选择';
}

function renderSampleSlot(channel, letter, slot){
  const slide = (channel.slides || []).find(x => Number(x.slot) === slot);
  const code = slotCode(letter, slot);
  if(!slide){
    const disabled = !channel.workflowVersionId || channel.workflowLocked ? ' disabled' : '';
    const hint = channel.workflowVersionId ? '空闲 / 可上样' : '未选脚本，禁止添加';
    return `<div class="sample-slot empty"><div class="slot-no">${escapeHtml(code)}</div><div><b>空闲</b><span>${escapeHtml(hint)}</span><button class="btn btn-soft full"${disabled} onclick="openConfirmModalForSlot('${escapeHtml(letter)}', ${slot})">添加样本</button></div><em>--</em></div>`;
  }
  const inherited = [
    slide.workflowName || channel.workflowName || slide.protocolCode || channel.workflowCode,
    slide.workflowVersionLabel || channel.workflowVersionLabel
  ].filter(Boolean).join(' / ');
  return `<div class="sample-slot occupied"><div class="slot-no">${escapeHtml(code)}</div><div><b>${escapeHtml(slide.sampleIdentifier || slide.barcode || slide.id)}</b><span>${escapeHtml(statusText(slide.status || 'loaded'))} / ${escapeHtml(slide.currentStep || '')}</span><small>继承通道脚本：${escapeHtml(inherited || '--')}</small></div><em>${escapeHtml(slide.protocolCode || channel.workflowCode || channel.experimentType || '--')}</em></div>`;
}

function renderChannelWorkflowPicker(channel, letter, workflow){
  const locked = channel.workflowLocked || channel.workflowSelectionStatus === 'Locked';
  const canSelect = !workflow.selected && channel.canSelectWorkflow !== false && !locked;
  const canChange = workflow.selected && channel.canChangeWorkflow && !locked;
  const action = canSelect
    ? `<button class="btn btn-soft full" onclick="openChannelScriptModal('${escapeHtml(letter)}', 'select')">选择实验脚本</button>`
    : canChange
      ? `<button class="btn btn-soft full" onclick="openChannelScriptModal('${escapeHtml(letter)}', 'change')">更换实验脚本</button>`
      : locked
        ? '<button class="btn btn-soft full" disabled>已锁定</button>'
        : '';
  const detail = workflow.selected
    ? `实验类型：${channel.experimentType || '--'}；流程版本：${workflow.version}；脚本状态：${workflowStatusLabel(channel)}`
    : '先选择通道脚本，再添加样本';
  return `<div class="channel-script-picker ${workflow.selected ? 'selected' : 'unselected'}"><div><span>当前实验脚本</span><b>${escapeHtml(workflow.label)}</b><small>${escapeHtml(detail)}</small></div>${action}</div>`;
}

function refreshConfirmSlotOptions(){
  const select = document.getElementById('confirmSlot');
  if(!select) return;
  const selected = select.value;
  const channels = window.machineStateSnapshot?.channels || [];
  select.innerHTML = (channels.length ? channels : ['A','B','C','D'].map((letter, index) => ({drawerCode: letter, id: index + 1, slides: []})))
    .flatMap((channel, index) => {
      const letter = channel.drawerCode || ['A','B','C','D'][index] || channel.id;
      return [1,2,3,4].map(slot => {
        const occupied = (channel.slides || []).some(x => Number(x.slot) === slot);
        const code = slotCode(letter, slot);
        return `<option value="${escapeHtml(code)}"${occupied ? ' disabled' : ''}>${escapeHtml(code)}${occupied ? ' 已占用' : ''}</option>`;
      });
    }).join('');
  if(selected && Array.from(select.options).some(option => option.value === selected && !option.disabled)){
    select.value = selected;
  }
}

function renderSamples(state){
  if(!document.getElementById('sampleCabinet')) return;
  const count = slideCount(state);
  setText('sampleBadge', `${count}/16 已占用`);
  const root = document.getElementById('sampleCabinet');
  root.innerHTML = (state.channels || []).map((channel, index) => {
    const letter = channel.drawerCode || ['A','B','C','D'][index] || channel.id;
    const slides = channel.slides || [];
    const workflow = channelWorkflowInfo(channel);
    const slots = [4,3,2,1].map(slot => renderSampleSlot(channel, letter, slot)).join('');
    return `<div class="sample-column status-${escapeHtml(channel.status || 'empty')}"><div class="column-handle"><span>${escapeHtml(letter)} 通道</span><b>${slides.length}/4</b></div>${slots}${renderChannelWorkflowPicker(channel, letter, workflow)}<small class="slot-order-note">显示顺序：4 在上，1 在下</small></div>`;
  }).join('');
  refreshConfirmSlotOptions();
}

async function scanSamples(){
  await loadHostState();
  toast('样本状态已从后端刷新');
}

async function ensureActiveChannelBatch(letter){
  const existing = channelByLetter(letter);
  if(existing?.channelBatchId) return existing;
  await api('/api/channel-batches/active', {
    method: 'POST',
    body: JSON.stringify({
      commandId: commandId('channel-active'),
      drawerCode: letter
    })
  });
  await loadHostState();
  const refreshed = channelByLetter(letter);
  if(!refreshed?.channelBatchId){
    throw new Error('后端未返回活动通道批次，请刷新后重试');
  }
  return refreshed;
}

async function loadSampleWorkflowOptions(forceRefresh){
  if(!forceRefresh && window.sampleWorkflowOptions) return window.sampleWorkflowOptions;
  const workflows = await api('/api/workflows');
  window.sampleWorkflowOptions = (workflows || []).flatMap(workflow => {
    return (workflow.versions || [])
      .filter(version => String(version.status || '').toLowerCase() === 'published')
      .map(version => ({
        workflowId: workflow.id,
        workflowVersionId: version.id,
        code: workflow.code,
        name: workflow.name,
        workflowType: String(workflow.workflowType || '').toUpperCase(),
        versionNo: version.versionNo,
        versionLabel: version.versionLabel
      }));
  }).sort((a, b) => `${a.workflowType}-${a.code}-${a.versionNo}`.localeCompare(`${b.workflowType}-${b.code}-${b.versionNo}`));
  return window.sampleWorkflowOptions;
}

function refreshChannelScriptOptions(){
  const type = document.getElementById('channelScriptExperimentType')?.value || 'HE';
  const select = document.getElementById('channelScriptSelect');
  if(!select) return;
  const options = (window.sampleWorkflowOptions || []).filter(option => option.workflowType === type);
  select.disabled = options.length === 0;
  select.innerHTML = options.length
    ? options.map(option => {
      const label = `${option.workflowType} / ${option.code} v${option.versionLabel} / ${option.name}`;
      return `<option value="${escapeHtml(option.workflowVersionId)}">${escapeHtml(label)}</option>`;
    }).join('')
    : '<option value="">暂无 Published 流程</option>';
}

async function openChannelScriptModal(letter, mode='select'){
  activeChannelScriptLetter = letter;
  const modal = document.getElementById('channelScriptModal');
  const title = document.getElementById('channelScriptTitle');
  const select = document.getElementById('channelScriptSelect');
  const typeSelect = document.getElementById('channelScriptExperimentType');
  const reasonLabel = document.getElementById('channelScriptReasonLabel');
  const reasonInput = document.getElementById('channelScriptReason');
  if(!modal || !select || !typeSelect) return;
  const isChange = mode === 'change';
  if(title) title.textContent = `${letter} 通道${isChange ? '更换' : '选择'}实验脚本`;
  if(reasonLabel) reasonLabel.classList.toggle('hidden', !isChange);
  if(reasonInput) reasonInput.value = '';
  select.disabled = true;
  select.innerHTML = '<option value="">正在加载已发布流程...</option>';
  setText('channelScriptHint', isChange ? '将影响该通道全部未运行玻片；请填写变更原因。' : '该选择会保存到后端 ChannelBatch，并应用于该通道 1-4 号 Slot。');
  modal.classList.remove('hidden');
  try{
    const channel = await ensureActiveChannelBatch(letter);
    await loadSampleWorkflowOptions(true);
    typeSelect.value = channel.experimentType || 'HE';
    refreshChannelScriptOptions();
    if(channel.workflowVersionId && !isChange){
      select.disabled = true;
      setText('channelScriptHint', '该通道已经选择脚本；本界面不会覆盖已选脚本。');
    }
  }catch(e){
    select.innerHTML = '<option value="">流程加载失败</option>';
    setText('channelScriptHint', e.message || '请检查服务和流程配置接口。');
    toast(e.message || '流程脚本加载失败', true);
  }
}

function closeChannelScriptModal(){
  document.getElementById('channelScriptModal')?.classList.add('hidden');
}

async function applyChannelScriptSelection(){
  const type = document.getElementById('channelScriptExperimentType')?.value || 'HE';
  const workflowVersionId = document.getElementById('channelScriptSelect')?.value;
  const reason = document.getElementById('channelScriptReason')?.value?.trim();
  const channel = activeChannelScriptLetter ? channelByLetter(activeChannelScriptLetter) : null;
  if(!activeChannelScriptLetter || !workflowVersionId){
    toast('请选择已发布流程脚本', true);
    return;
  }
  if(channel?.workflowVersionId){
    toast('后端当前未开放覆盖已选脚本接口，请按预启动变更规则处理', true);
    return;
  }
  const reasonVisible = !document.getElementById('channelScriptReasonLabel')?.classList.contains('hidden');
  if(reasonVisible && !reason){
    toast('更换脚本必须填写原因', true);
    return;
  }
  try{
    const batch = await ensureActiveChannelBatch(activeChannelScriptLetter);
    await api('/api/channel-batches/workflow-selection', {
      method: 'POST',
      body: JSON.stringify({
        commandId: commandId('channel-workflow'),
        channelBatchId: batch.channelBatchId,
        drawerCode: activeChannelScriptLetter,
        experimentType: type,
        workflowVersionId
      })
    });
    closeChannelScriptModal();
    await loadHostState();
    toast(`${activeChannelScriptLetter} 通道实验脚本已保存`);
  }catch(e){
    setText('channelScriptHint', e.message || '脚本选择失败');
    toast(e.message || '脚本选择失败', true);
  }
}

function clearChannelScriptSelection(){
  closeChannelScriptModal();
  toast('通道脚本以后端 ChannelBatch 为准，不能在浏览器本地清除', true);
}

function showSampleTaskError(message){
  const error = document.getElementById('sampleTaskError');
  if(error){
    error.textContent = message || '';
    error.classList.toggle('hidden', !message);
  }
}

function updateConfirmModalFromSlot(){
  const slot = document.getElementById('confirmSlot')?.value;
  const path = document.getElementById('confirmPath')?.value || 'ihc-tl';
  const parsed = parseSlotCode(slot);
  const channel = channelByLetter(parsed.letter);
  const workflow = channelWorkflowInfo(channel);
  const rawCodeLabel = document.getElementById('rawCodeLabel');
  const primaryLabel = document.getElementById('primaryAntibodyLabel');
  const rawCode = document.getElementById('rawCode');
  const button = document.getElementById('confirmTaskButton');
  if(rawCodeLabel) rawCodeLabel.classList.toggle('hidden', path === 'he');
  if(primaryLabel) primaryLabel.classList.toggle('hidden', pendingPrimaryAntibodyCandidates.length === 0);
  if(rawCode && path === 'he') rawCode.value = '';
  setText('confirmChannelScript', workflow.selected
    ? `当前通道脚本：${channel.experimentType} / ${workflow.label} / ${workflow.version}`
    : '当前通道脚本：未选择。请先在通道卡片中选择实验脚本。');
  const expected = path === 'he' ? 'HE' : 'IHC';
  const valid = workflow.selected && !channel?.workflowLocked && channel?.experimentType === expected;
  if(button) button.disabled = !valid;
  showSampleTaskError(valid ? '' : `该 Slot 所在通道需要先选择 ${expected} 脚本，且启动后不能追加样本。`);
}

function openConfirmModalForSlot(letter, slot){
  openConfirmModal(null, slotCode(letter, slot));
}

function openConfirmModal(mode, preferredSlotCode){
  activeConfirmMode = mode || null;
  pendingPrimaryAntibodyCandidates = [];
  refreshConfirmSlotOptions();
  const modal = document.getElementById('sampleConfirmModal');
  const title = document.getElementById('confirmTitle');
  const path = document.getElementById('confirmPath');
  const slot = document.getElementById('confirmSlot');
  const rawCode = document.getElementById('rawCode');
  const primary = document.getElementById('primaryAntibodySelect');
  if(title) title.textContent = mode === 'he' ? 'HE 添加样本' : (mode === 'ihc-hospital' ? '医院码 / LIS 添加样本' : 'IHC 添加样本');
  if(path && mode) path.value = mode;
  if(slot && preferredSlotCode) slot.value = preferredSlotCode;
  if(rawCode) rawCode.value = path?.value === 'he' ? '' : (path?.value === 'ihc-hospital' ? 'HOSP-001' : 'PA1');
  if(primary) primary.innerHTML = '';
  modal?.classList.remove('hidden');
  updateConfirmModalFromSlot();
}

function closeConfirmModal(){
  document.getElementById('sampleConfirmModal')?.classList.add('hidden');
}

function populatePrimaryAntibodyCandidates(candidates){
  pendingPrimaryAntibodyCandidates = candidates || [];
  const primary = document.getElementById('primaryAntibodySelect');
  if(primary){
    primary.innerHTML = pendingPrimaryAntibodyCandidates.map(code => `<option value="${escapeHtml(code)}">${escapeHtml(code)}</option>`).join('');
  }
  updateConfirmModalFromSlot();
}

async function confirmTask(){
  const slot = document.getElementById('confirmSlot')?.value;
  const path = document.getElementById('confirmPath')?.value || 'ihc-tl';
  const rawCode = document.getElementById('rawCode')?.value?.trim();
  const parsed = parseSlotCode(slot);
  const channel = channelByLetter(parsed.letter);
  if(!channel?.workflowVersionId){
    showSampleTaskError('未选择通道实验脚本，不能创建任务。');
    return;
  }
  try{
    if(path === 'he'){
      await api('/api/tasks/he', {
        method: 'POST',
        body: JSON.stringify({
          commandId: commandId('task-he'),
          slotCode: slot,
          drawerCode: parsed.letter,
          channelBatchId: channel.channelBatchId
        })
      });
    }else{
      const selectedPrimaryAntibodyCode = document.getElementById('primaryAntibodySelect')?.value || null;
      await api('/api/tasks/ihc', {
        method: 'POST',
        body: JSON.stringify({
          commandId: commandId('task-ihc'),
          inputMode: path === 'ihc-hospital' ? 'HospitalBarcode' : 'PrimaryAntibody',
          rawCode: rawCode || '',
          slotCode: slot,
          drawerCode: parsed.letter,
          channelBatchId: channel.channelBatchId,
          selectedPrimaryAntibodyCode
        })
      });
    }
    closeConfirmModal();
    await loadHostState();
    toast('任务已创建，Slot 状态来自后端刷新');
  }catch(e){
    if(e.data?.requiresSelection){
      populatePrimaryAntibodyCandidates(e.data.candidatePrimaryAntibodyCodes || []);
      showSampleTaskError(e.data.message || '请选择最终一抗后再次确认创建。');
      return;
    }
    showSampleTaskError(e.message || '创建任务失败');
    toast(e.message || '创建任务失败', true);
  }
}

document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('confirmSlot')?.addEventListener('change', updateConfirmModalFromSlot);
  document.getElementById('confirmPath')?.addEventListener('change', () => {
    pendingPrimaryAntibodyCandidates = [];
    updateConfirmModalFromSlot();
  });
  document.getElementById('primaryAntibodySelect')?.addEventListener('change', updateConfirmModalFromSlot);
});

document.addEventListener('DOMContentLoaded', () => {
  loadHostState().then(() => {
    renderConfigure();
    connectMachineHub();
  });
});
