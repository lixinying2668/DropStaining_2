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
window.reagentScanSessionOverview = null;
window.reagentScanGuide = null;
let activeReagentScanPosition = null;
let activeChannelScriptLetter = null;
let activeConfirmMode = null;
let pendingPrimaryAntibodyCandidates = [];
let pendingLisQueryLogId = null;
let pendingLisRawCode = null;

async function loadHostState(){
  try{
    const state = await api('/api/operator/snapshot');
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
  setText('metricDeviceMode', state.deviceMode || 'Mock');

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
  document.querySelectorAll('.engineer-only').forEach(el => {
    el.style.display = user && ['engineer','admin'].includes(user.role) ? '' : 'none';
  });

  const banner = document.getElementById('alertBanner');
  if(banner){
    const hasAlarm = (state.alarms || []).length > 0;
    banner.classList.toggle('hidden', !hasAlarm);
    setText('alertText', hasAlarm ? displayLogText(state.alarms[0]) : '');
  }
}

function renderDashboard(state){
  if(!document.getElementById('drawerBoard')) return;
  renderDrawerBoard('drawerBoard', state.channels || []);
  renderSystemChecks(state.system || {});
  renderDashboardEvents(state.recentEvents || []);
  setText('dashboardRunSummary', state.runId
    ? `${state.runCode || state.runId} · ${state.currentMajorStepCode || statusText(state.status)}`
    : '当前没有正式运行');
}

function renderDashboardEvents(events){
  const root = document.getElementById('dashboardEvents');
  if(!root) return;
  const list = (events || []).slice(0, 20);
  const latest = list[0];
  root.innerHTML = `<button type="button" class="operator-event-entry" onclick="openDashboardEventList()"><b>查看事件</b><span>${latest ? escapeHtml(displayLogText(latest.title || latest.type || '最近事件')) : '暂无正式事件'}</span><em>${list.length} 条</em></button>`;
}

function openDashboardEventList(){
  const events = (window.machineStateSnapshot?.recentEvents || []).slice(0, 20);
  setText('dashboardEventTitle', '最近事件');
  const body = document.getElementById('dashboardEventBody');
  if(body){
    body.classList.remove('detail-grid');
    body.classList.add('operator-event-list');
    body.innerHTML = events.length
      ? events.map(event => `<button type="button" class="operator-event-row" onclick="openDashboardEvent('${escapeHtml(event.id)}')"><span>${escapeHtml(formatDateTime(event.occurredAtUtc))}</span><b>${escapeHtml(displayLogText(event.title))}</b><em>${escapeHtml(displayStatusText(event.status || event.type))}</em></button>`).join('')
      : '<div class="empty-state"><b>暂无正式事件</b><span>初始化、命令和运行事件会在这里显示。</span></div>';
  }
  const link = document.getElementById('dashboardEventLink');
  if(link) link.onclick = () => { location.href = '/history'; };
  document.getElementById('dashboardEventModal')?.classList.remove('hidden');
}

function openDashboardEvent(eventId){
  const event = (window.machineStateSnapshot?.recentEvents || []).find(x => x.id === eventId);
  if(!event) return;
  setText('dashboardEventTitle', event.title || '事件详情');
  const body = document.getElementById('dashboardEventBody');
  if(body){
    body.classList.add('detail-grid');
    body.classList.remove('operator-event-list');
    body.innerHTML = `<div><span>时间</span><b>${escapeHtml(formatDateTime(event.occurredAtUtc))}</b></div><div><span>类型</span><b>${escapeHtml(displayLogText(event.type || '--'))}</b></div><div><span>状态</span><b>${escapeHtml(displayStatusText(event.status || '--'))}</b></div><div><span>详情</span><b>${escapeHtml(displayLogText(event.detail || '--'))}</b></div>`;
  }
  const link = document.getElementById('dashboardEventLink');
  if(link) link.onclick = () => { location.href = event.href || '/history'; };
  document.getElementById('dashboardEventModal')?.classList.remove('hidden');
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
    ['controllerOnline', '主控连接'],
    ['roboticArmHome', '机械臂回零'],
    ['reagentCooling', '制冷连接 ' + (system.reagentTemperatureC ?? 8) + '℃'],
    ['sampleScannerOnline', '样本扫码器在线'],
    ['reagentScannerOnline', '试剂扫码器在线'],
    ['liquidSensor', '液位/传感器读取'],
    ['needleWash', '洗针准备'],
    ['pureWaterOk', '纯水可用'],
    ['pbsOk', 'PBS 可用'],
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
  await Promise.all([refreshReagentRack(), refreshReagentScanSession(), refreshDabPositions()]);
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
  const warning = session.hasWarning ? ` · 警告：${escapeHtml(displayLogText(session.message || ''))}` : '';
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

function renderAlertsLegacySnapshot(state){
  if(!document.getElementById('alarmList')) return;
  const alarms = state.alarms || [];
  const root = document.getElementById('alarmList');
  root.innerHTML = alarms.length
    ? alarms.map(alarm => `<div class="alarm-row level-high"><b>高</b><span>${escapeHtml(alarm)}</span><em>影响：当前任务 / 启动校验</em><small>建议：查看关联日志并处理对应试剂、设备或通道状态。</small></div>`).join('')
    : '<div class="empty-state"><b>暂无告警</b><span>系统当前没有未处理告警。</span></div>';
  renderTimeline('alertLogs', state.logs || [], 40);
}

function renderHistoryLegacySnapshot(state){
  if(!document.getElementById('historySlides')) return;
  const slideRows = (state.channels || []).flatMap((channel, index) => {
    const letter = ['A','B','C','D'][index] || channel.id;
    return (channel.slides || []).map(slide => `<div class="table-row"><span>${escapeHtml(slide.barcode)}</span><span>${letter}-${String(slide.slot).padStart(2,'0')}</span><span>${escapeHtml(slide.protocolCode)}</span><span>${escapeHtml(displayStatusText(slide.status))}</span><span>${escapeHtml(displayLogText(slide.currentStep))}</span></div>`);
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
  const [workflows, catalog, mappings] = await Promise.all([
    api('/api/workflows'),
    api('/api/reagents/catalog'),
    api('/api/primary-antibody-mappings')
  ]);
  window.configureWorkflows = workflows || [];
  window.configureReagentCatalog = catalog || [];
  window.configurePrimaryAntibodyMappings = mappings || [];
  renderWorkflowVersionTable(window.configureWorkflows);
  renderPrimaryAntibodyMappings(window.configurePrimaryAntibodyMappings);
  const dabPreview = document.getElementById('dabPreview');
  if(dabPreview){
    const publishedIhc = (workflows || []).flatMap(x => (x.versions || []).filter(v => x.workflowType === 'IHC' && v.status === 'Published'));
    dabPreview.innerHTML = `<div><span>Published IHC</span><b>${publishedIhc.length}</b></div><div><span>一抗映射</span><b>${(mappings || []).filter(x => x.isEnabled).length}</b></div><div><span>计算来源</span><b>运行快照</b></div>`;
  }
  const catalogTable = document.getElementById('catalogTable');
  if(catalogTable){
    catalogTable.innerHTML = '<div class="table-row head"><span>代码</span><span>名称</span><span>类别</span><span>报警余量</span><span>液体类型</span></div>'
      + ((catalog || []).map(item => `<div class="table-row"><span>${escapeHtml(item.reagentCode)}</span><span>${escapeHtml(item.name)}</span><span>${escapeHtml(item.reagentType || '--')}</span><span>${item.minimumAlarmVolumeUl ?? '--'} uL</span><span>${escapeHtml(item.liquidClassCode || '--')}</span></div>`).join('')
      || '<div class="empty-state"><b>暂无数据库试剂目录</b><span>请先维护试剂目录。</span></div>');
  }
}

async function refreshDabPositions(){
  const root = document.getElementById('dabPositionGrid');
  if(!root) return;
  try{
    const snapshot = window.machineStateSnapshot || await api('/api/operator/snapshot');
    window.dabPositionSnapshot = snapshot.dabPositions || [];
    renderDabPositions(window.dabPositionSnapshot);
  }catch(e){
    root.innerHTML = `<div class="empty-state"><b>DAB 状态读取失败</b><span>${escapeHtml(e.message || '请检查正式只读接口')}</span></div>`;
  }
}

function renderDabPositions(positions){
  const root = document.getElementById('dabPositionGrid');
  if(!root) return;
  const byCode = new Map((positions || []).map(x => [String(x.code || '').toUpperCase(), x]));
  root.innerHTML = Array.from({length:8}, (_, index) => {
    const code = `M${index + 1}`;
    const position = byCode.get(code);
    const batchState = position?.batchStatus || position?.status || 'Available';
    const expiry = position?.expiresAtUtc ? formatDateTime(position.expiresAtUtc) : '--';
    const volume = position?.remainingVolumeUl == null ? '--' : formatVolume(position.remainingVolumeUl);
    return `<button type="button" class="dab-position status-${escapeHtml(String(batchState).toLowerCase())}" onclick="showDabPosition('${code}')"><b>${code}</b><span>${escapeHtml(batchState)}</span><small>${escapeHtml(volume)} · ${escapeHtml(expiry)}</small></button>`;
  }).join('');
}

function showDabPosition(code){
  const position = (window.dabPositionSnapshot || []).find(x => String(x.code).toUpperCase() === String(code).toUpperCase());
  if(!position){
    toast(`${code} 暂无正式位置记录`, true);
    return;
  }
  const body = document.getElementById('dabDetailBody');
  if(body){
    const rows = [
      ['位置', position.code],
      ['位置状态', position.status],
      ['批次状态', position.batchStatus || '--'],
      ['批次编号', position.activeDabBatchId || '--'],
      ['DAB A 来源', position.dabASource || '--'],
      ['DAB B 来源', position.dabBSource || '--'],
      ['玻片数', position.slideCount ?? '--'],
      ['剩余量', position.remainingVolumeUl == null ? '--' : formatVolume(position.remainingVolumeUl)],
      ['配制时间', formatDateTime(position.preparedAtUtc)],
      ['到期时间', formatDateTime(position.expiresAtUtc)],
      ['清洗状态', position.cleaningStatus || '--']
    ];
    body.innerHTML = rows.map(([label, value]) => `<div><span>${escapeHtml(label)}</span><b>${escapeHtml(value)}</b></div>`).join('');
  }
  const role = String(window.machineStateSnapshot?.activeUser?.role || '').toLowerCase();
  const canManage = ['engineer','admin'].includes(role);
  const hasBatch = !!position.activeDabBatchId;
  const start = document.getElementById('dabCleaningStartButton');
  const confirmButton = document.getElementById('dabCleaningConfirmButton');
  if(start){
    start.onclick = () => runDabCleaning(position.activeDabBatchId, 'start');
    setButtonDisabledReason(start, !canManage || !hasBatch, !canManage ? '仅工程师或管理员可启动清洗' : '该位置没有活动 DAB 批次');
  }
  if(confirmButton){
    confirmButton.onclick = () => runDabCleaning(position.activeDabBatchId, 'confirm');
    const ready = position.cleaningStatus === 'Required' || position.batchStatus === 'AwaitingCleaning';
    setButtonDisabledReason(confirmButton, !canManage || !hasBatch || !ready, !canManage ? '仅工程师或管理员可确认清洗' : !hasBatch ? '该位置没有活动 DAB 批次' : '当前批次无需清洗确认');
  }
  document.getElementById('dabDetailModal')?.classList.remove('hidden');
}

async function runDabCleaning(batchId, action){
  if(!batchId) return;
  const label = action === 'start' ? '启动清洗' : '确认清洗完成';
  const decision = await operatorConfirm({
    title:label,
    message:`${label}将写入正式 DAB 批次和审计。`,
    warning:'请确认目标批次与清洗状态无误。',
    confirmText:label
  });
  if(!decision.confirmed) return;
  await api(`/api/dab/batches/${encodeURIComponent(batchId)}/cleaning/${action}`, {
    method:'POST',
    body:JSON.stringify({commandId:commandId(`dab-cleaning-${action}`)})
  });
  document.getElementById('dabDetailModal')?.classList.add('hidden');
  await loadHostState();
  toast(`${label}已记录`);
}

function workflowStatusText(status){
  return ({Draft:'草稿', Published:'已发布', Retired:'已停用'}[status] || status || '--');
}

function renderWorkflowVersionTable(workflows){
  const root = document.getElementById('protocolTable');
  if(!root) return;
  const rows = (workflows || []).flatMap(workflow => {
    const versions = (workflow.versions || []).slice().sort((a, b) => (b.versionNo || 0) - (a.versionNo || 0));
    return versions.map(version => ({workflow, version}));
  });
  const canManageDefaults = window.machineStateSnapshot?.activeUser?.role === 'admin';
  root.innerHTML = rows.map(({workflow, version}) => {
    const status = workflowStatusText(version.status);
    const isDefault = version.defaultExperimentType === workflow.workflowType;
    const defaultAction = isDefault
      ? `<span class="badge-soft">默认 ${escapeHtml(workflow.workflowType)} 流程</span>`
      : canManageDefaults && version.status === 'Published' && workflow.isEnabled
        ? `<button class="btn btn-soft" onclick="setDefaultWorkflowVersion('${escapeHtml(version.id)}','${escapeHtml(workflow.workflowType)}')">设为默认 ${escapeHtml(workflow.workflowType)} 流程</button>`
        : '';
    const retireAction = version.status === 'Published'
      ? isDefault
        ? '<button class="btn btn-soft" disabled title="请先设置另一个默认流程">默认流程不可停用</button>'
        : `<button class="btn btn-soft" onclick="retireWorkflowVersion('${escapeHtml(version.id)}')">停用</button>`
      : '';
    return `<div class="protocol-version-row workflow-version-${escapeHtml(String(version.status || '').toLowerCase())}"><b>${escapeHtml(workflow.code)}</b><span>${escapeHtml(workflow.name)}<small>${escapeHtml(workflow.workflowType)} · 步骤 ${Number(version.stepCount || 0)} · 试剂 ${Number(version.reagentRequirementCount || 0)}</small></span><em>v${escapeHtml(version.versionLabel)} · ${escapeHtml(status)}</em><small>${escapeHtml(workflow.description || '--')}</small><div class="button-row"><button class="btn btn-soft" onclick="openWorkflowVersionDetail('${escapeHtml(version.id)}')">详情</button><button class="btn btn-soft" onclick="copyWorkflowVersionDraft('${escapeHtml(version.id)}')">复制 Draft</button>${defaultAction}${retireAction}</div></div>`;
  }).join('') || '<div class="empty-state"><b>暂无流程版本</b><span>请先新建流程 Draft。</span></div>';
}

async function openWorkflowVersionDetail(workflowVersionId){
  const detail = await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}`);
  window.activeWorkflowVersionDetail = detail;
  renderWorkflowVersionDetail(detail);
}

function renderWorkflowVersionDetail(detail){
  const root = document.getElementById('workflowVersionDetail');
  if(!root) return;
  const editable = detail.status === 'Draft';
  const isDefault = detail.defaultExperimentType === detail.workflowType;
  const canManageDefaults = window.machineStateSnapshot?.activeUser?.role === 'admin';
  const defaultAction = isDefault
    ? `<span class="badge-soft">默认 ${escapeHtml(detail.workflowType)} 流程</span>`
    : canManageDefaults && detail.status === 'Published' && detail.isEnabled
      ? `<button class="btn btn-soft" onclick="setDefaultWorkflowVersion('${escapeHtml(detail.workflowVersionId)}','${escapeHtml(detail.workflowType)}')">设为默认 ${escapeHtml(detail.workflowType)} 流程</button>`
      : '';
  const readonlyHint = editable ? '' : '<div class="notice-box">该版本不可以直接修改，请复制为 Draft 后编辑。</div>';
  root.innerHTML = `<div class="section-title"><div><h2>${escapeHtml(detail.code)} v${escapeHtml(detail.versionLabel)}</h2><p>${escapeHtml(detail.name)} · ${escapeHtml(detail.workflowType)} · ${escapeHtml(workflowStatusText(detail.status))}</p></div><div class="button-row">${editable ? '<button class="btn btn-soft" onclick="updateWorkflowVersionMeta()">保存基本信息</button><button class="btn btn-primary" onclick="publishWorkflowVersion()">发布 Draft</button>' : ''}${defaultAction}<button class="btn btn-soft" onclick="copyWorkflowVersionDraft(activeWorkflowVersionDetail.workflowVersionId)">复制 Draft</button></div></div>${readonlyHint}<div class="version-grid"><div><span>流程代码</span><b>${escapeHtml(detail.code)}</b></div><div><span>实验类型</span><b>${escapeHtml(detail.workflowType)}</b></div><div><span>版本状态</span><b>${escapeHtml(workflowStatusText(detail.status))}</b></div><div><span>默认状态</span><b>${isDefault ? `默认 ${escapeHtml(detail.workflowType)} 流程` : '非默认'}</b></div></div>${editable ? workflowMetaEditor(detail) : ''}<div class="section-title compact-title"><h2>步骤编辑</h2>${editable ? '<button class="btn btn-primary" onclick="addWorkflowStep()">新增步骤</button>' : ''}</div><div class="data-table workflow-step-table">${workflowStepRows(detail.steps || [], editable)}</div><div class="section-title compact-title"><h2>试剂需求</h2>${editable ? '<div class="button-row"><button class="btn btn-primary" onclick="addWorkflowRequirement()">新增需求</button><button class="btn btn-soft" onclick="recalculateWorkflowRequirements()">从步骤重算</button></div>' : ''}</div><div class="data-table workflow-requirement-table">${workflowRequirementRows(detail.reagentRequirements || [], editable)}</div><div class="section-title compact-title"><h2>发布前校验</h2><button class="btn btn-soft" onclick="validateWorkflowPublish()">运行校验</button></div><div class="validation-result" id="workflowPublishValidation"></div>`;
}

function workflowMetaEditor(detail){
  return `<div class="inline-form touch-form"><label>流程名称<input class="input" id="workflowMetaName" value="${escapeHtml(detail.name)}"></label><label>版本号<input class="input" id="workflowMetaVersionLabel" value="${escapeHtml(detail.versionLabel)}"></label><label>变更说明<input class="input" id="workflowMetaChangeNote" value="${escapeHtml(detail.changeNote || '')}"></label><label>流程说明<input class="input" id="workflowMetaDescription" value="${escapeHtml(detail.description || '')}"></label></div>`;
}

function workflowStepRows(steps, editable){
  return '<div class="table-row head"><span>顺序</span><span>名称</span><span>动作</span><span>试剂/体积/时长</span><span>操作</span></div>' + (steps.map(step => `<div class="table-row"><span>${step.stepNo}</span><span>${escapeHtml(step.stepName)}<small>${escapeHtml(step.majorStepCode || '--')}</small></span><span>${escapeHtml(step.actionType)}</span><span>${escapeHtml(step.reagentCode || '--')} / ${step.volumeUl ?? '--'} uL / ${step.durationSeconds ?? '--'} s</span><span class="button-row">${editable ? `<button class="btn btn-soft" onclick="editWorkflowStep('${escapeHtml(step.id)}')">编辑</button><button class="btn btn-soft" onclick="moveWorkflowStep('${escapeHtml(step.id)}','up')">上移</button><button class="btn btn-soft" onclick="moveWorkflowStep('${escapeHtml(step.id)}','down')">下移</button><button class="btn btn-soft" onclick="deleteWorkflowStep('${escapeHtml(step.id)}')">删除</button>` : '只读'}</span></div>`).join('') || '<div class="empty-state"><b>暂无步骤</b><span>Draft 至少需要一个步骤才能发布。</span></div>');
}

function workflowRequirementRows(requirements, editable){
  return '<div class="table-row head"><span>试剂代码</span><span>名称</span><span>需求量</span><span>必需</span><span>操作</span></div>' + (requirements.map(item => `<div class="table-row"><span>${escapeHtml(item.reagentCode)}</span><span>${escapeHtml(item.reagentName || '--')}</span><span>${item.requiredVolumeUl ?? '--'} uL</span><span>${item.isRequired ? '是' : '否'}</span><span class="button-row">${editable ? `<button class="btn btn-soft" onclick="editWorkflowRequirement('${escapeHtml(item.id)}')">编辑</button><button class="btn btn-soft" onclick="deleteWorkflowRequirement('${escapeHtml(item.id)}')">删除</button>` : '只读'}</span></div>`).join('') || '<div class="empty-state"><b>暂无试剂需求</b><span>可从步骤引用自动重算。</span></div>');
}

function renderTimeline(id, items, limit){
  const root = document.getElementById(id);
  if(!root) return;
  root.innerHTML = (items || []).slice(0, limit).map(item => `<div><i></i><span>${escapeHtml(displayLogText(item))}</span></div>`).join('') || '<div><i></i><span>暂无日志</span></div>';
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
    logRoot.innerHTML = (state.logs || []).slice(0,30).map(x => `<div>${escapeHtml(displayLogText(x))}</div>`).join('') || '<div>暂无事件</div>';
  }
}

function appendMachineLog(state, event){
  state.logs = state.logs || [];
  const message = {
    'alarm.raised':'系统告警已更新，请查看告警摘要。',
    'alarm.acknowledged':'告警处理状态已更新。',
    'device.connectionChanged':'设备连接状态已更新。',
    'device.stateChanged':'设备状态已更新。',
    'machine.stateChanged':'运行状态已更新。',
    'workflowStep.started':'染色步骤已开始。',
    'workflowStep.completed':'染色步骤已完成。'
  }[event.type] || '系统状态已更新。';
  state.logs.unshift('[' + new Date(event.occurredAtUtc || Date.now()).toLocaleTimeString('zh-CN', {hour12:false}) + '] ' + message);
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
  scheduleFormalSnapshotRefresh();
  const payload = event.payload || {};
  appendMachineLog(state, event);
  if(typeof window.invalidatePreflightView === 'function'
      && ['scanSession.changed','reagent.changed','reagentBottle.changed','channelBatch.changed','slideTask.created','machine.stateChanged','workflowStep.started','workflowStep.completed','alarm.raised','alarm.acknowledged','device.connectionChanged','device.stateChanged','device.initializationChanged','qr.scanCompleted'].includes(event.type)){
    window.invalidatePreflightView('后端状态已变化。');
  }
  if(isReagentEvent(event)){
    refreshReagentRack();
    refreshReagentScanSession();
  }
  if(['workflow.changed','workflowVersion.changed','workflowStep.changed','workflowReagentRequirement.changed','primaryAntibodyMapping.changed'].includes(event.type)){
    renderConfigure();
  }
  switch(event.type){
    case 'channelBatch.changed':
    case 'slideTask.created':
    case 'device.stateChanged':
    case 'device.initializationChanged':
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
      state.alarms.unshift('系统提示需要处理，请按界面指引检查。');
      loadHostState();
      return;
    case 'alarm.acknowledged':
      loadHostState();
      return;
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
      && ['machine.stateChanged','workflowStep.started','workflowStep.completed','alarm.raised','alarm.acknowledged','device.connectionChanged','device.stateChanged','device.initializationChanged'].includes(event.type)){
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
let formalSnapshotTimer = null;

function scheduleFormalSnapshotRefresh(){
  if(formalSnapshotTimer) clearTimeout(formalSnapshotTimer);
  formalSnapshotTimer = setTimeout(async () => {
    formalSnapshotTimer = null;
    await loadHostState();
    if(typeof window.refreshRunView === 'function') await window.refreshRunView();
  }, 180);
}

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
      if(wasReconnect){
        await loadHostState();
        if(typeof window.refreshRunView === 'function') await window.refreshRunView();
      }
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
  const decision = await operatorConfirm({
    title:'完成扫码会话',
    message,
    warning:'完成后将以正式扫码结果参与启动前预检。',
    confirmText:'确认完成'
  });
  if(!decision.confirmed) return;
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
  window.reagentDetailPosition = pos;
  const body = document.getElementById('reagentDetailBody');
  if(!body) return;
  const position = rackPositionByCode(pos);
  const bottle = position?.bottle;
  const state = scanStateOf(position);
  body.innerHTML = `<div><span>位置</span><b>${escapeHtml(pos)}</b></div><div><span>状态</span><b>${escapeHtml(displayStatusText(bottle?.status || scanStateLabel(state)))}</b></div><div><span>错误</span><b>${escapeHtml(state === 'INVALID' ? displayLogText(position?.validationMessage || '扫码校验失败') : '--')}</b></div><div><span>完整条码</span><b>${escapeHtml(bottle?.fullBarcode || position?.rawBarcode || '--')}</b><small>${escapeHtml(bottle?.barcodeSummary || position?.barcodeSummary || '--')}</small></div><div><span>试剂名称</span><b>${escapeHtml(bottle?.name || scanStateTitle(state))}</b></div><div><span>试剂代码</span><b>${escapeHtml(bottle?.reagentCode || position?.parsedReagentCode || '--')}</b></div><div><span>剩余量</span><b>${escapeHtml(bottle ? formatVolume(bottle.remainingVolumeUl) : '--')}</b></div><div><span>批号 / 序列号</span><b>${escapeHtml([bottle?.lotNo, bottle?.serialNo].filter(Boolean).join(' / ') || '--')}</b></div><div><span>有效期</span><b>${escapeHtml(formatDate(bottle?.expirationDate))}</b></div><div><span>最后扫码时间</span><b>${escapeHtml(formatDateTime(position?.lastScannedAtUtc || bottle?.lastScannedAtUtc))}</b></div><div><span>扫码会话</span><b>${escapeHtml(position?.lastScanSessionCode || '--')}</b><small>${escapeHtml(displayStatusText(position?.lastScanSessionStatus || '--'))}</small></div>`;
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
      `<div><span>当前结果</span><b>${escapeHtml(scanStateLabel(state))}</b><small>${escapeHtml(displayLogText(position?.validationMessage || invalidOrScanMessage(position)))}</small></div>`
    ].join('');
  }
  const result = document.getElementById('reagentScanResult');
  if(result){
    result.innerHTML = bottle
      ? `<div><span>试剂代码</span><b>${escapeHtml(bottle.reagentCode)}</b></div><div><span>试剂名称</span><b>${escapeHtml(bottle.name || '--')}</b></div><div><span>余量</span><b>${escapeHtml(formatVolume(bottle.remainingVolumeUl))}</b></div><div><span>批号 / 序列号</span><b>${escapeHtml([bottle.lotNo, bottle.serialNo].filter(Boolean).join(' / ') || '--')}</b></div><div><span>有效期</span><b>${escapeHtml(formatDate(bottle.expirationDate))}</b></div>`
      : `<div><span>后端结果</span><b>${escapeHtml(scanStateLabel(state))}</b><small>${escapeHtml(displayLogText(position?.validationMessage || '等待确认'))}</small></div>`;
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
  if(mode === 'empty'){
    const decision = await operatorConfirm({
      title:'确认空位',
      message:`确认将 ${pos} 标记为空位？`,
      warning:'空位确认会写入正式扫码会话。',
      confirmText:'标记为空位'
    });
    if(!decision.confirmed) return;
  }
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
    ? `<div><span>后端状态</span><b>${escapeHtml(scanStateLabel(state))}</b><small>${escapeHtml(displayLogText(position?.validationMessage || response?.validationMessage || '--'))}</small></div><div><span>试剂代码</span><b>${escapeHtml(bottle.reagentCode)}</b></div><div><span>试剂名称</span><b>${escapeHtml(bottle.name || '--')}</b></div><div><span>余量</span><b>${escapeHtml(formatVolume(bottle.remainingVolumeUl))}</b></div><div><span>批号 / 序列号</span><b>${escapeHtml([bottle.lotNo, bottle.serialNo].filter(Boolean).join(' / ') || '--')}</b></div><div><span>有效期</span><b>${escapeHtml(formatDate(bottle.expirationDate))}</b></div>`
    : `<div><span>后端状态</span><b>${escapeHtml(scanStateLabel(state))}</b><small>${escapeHtml(displayLogText(position?.validationMessage || response?.validationMessage || '--'))}</small></div>`;
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

async function renderAdminLegacyInitial(state){
  if(!document.getElementById('userTable')) return;
  const [users, roles, workflows, mappings, catalog, alarms] = await Promise.all([
    api('/api/users'), api('/api/roles'), api('/api/workflows'), api('/api/primary-antibody-mappings'),
    api('/api/reagents/catalog'), api('/api/alarms?status=Active&pageSize=100')
  ]);
  setText('adminUserCount', users.length);
  setText('adminRoleSummary', `${roles.length} 个角色：${roles.map(x => x.code).join(' / ')}`);
  setText('adminReagentCount', catalog.length);
  setText('adminWorkflowCount', workflows.reduce((n, x) => n + (x.versions || []).length, 0));
  setText('adminAlarmCount', alarms.totalCount || 0);
  wireAdminToolbar(users);
  document.getElementById('userTable').innerHTML = '<div class="table-row head"><span>用户</span><span>显示名</span><span>角色</span><span>启用</span><span>操作</span></div>' + users.map(u => `<div class="table-row"><span>${escapeHtml(u.username)}</span><span>${escapeHtml(u.displayName)}</span><span class="badge-soft">${escapeHtml((u.roles || [u.role]).join(','))}</span><span>${u.enabled ? '是' : '否'}</span><span class="button-row"><button class="btn btn-soft" onclick="adminRenameUser('${u.id}', '${escapeHtml(u.displayName)}')">改名</button><button class="btn btn-soft" onclick="adminToggleUser('${u.id}', ${!u.enabled})">${u.enabled ? '禁用' : '启用'}</button><button class="btn btn-soft" onclick="adminResetPassword('${u.id}')">重置</button><button class="btn btn-soft" onclick="adminSetRoles('${u.id}', '${escapeHtml((u.roles || []).join(','))}')">角色</button><button class="btn btn-soft" onclick="adminDeleteUser('${u.id}')">删除</button></span></div>`).join('');
  const versions = workflows.flatMap(workflow => (workflow.versions || []).map(version => ({workflow, version})));
  document.getElementById('adminWorkflowTable').innerHTML = '<div class="table-row head"><span>类型</span><span>流程</span><span>版本</span><span>状态</span><span>默认</span></div>' + versions.map(x => `<div class="table-row"><span>${escapeHtml(x.workflow.workflowType)}</span><span>${escapeHtml(x.workflow.code)}<small>${escapeHtml(x.workflow.name)}</small></span><span>${escapeHtml(x.version.versionLabel)}</span><span>${escapeHtml(displayStatusText(x.version.status))}</span><span>${escapeHtml(x.version.defaultExperimentType || '--')}</span></div>`).join('');
  document.getElementById('adminMappingTable').innerHTML = '<div class="table-row head"><span>一抗</span><span>流程</span><span>版本</span><span>状态</span><span>启用</span></div>' + mappings.map(x => `<div class="table-row"><span>${escapeHtml(x.primaryAntibodyCode)}</span><span>${escapeHtml(x.workflowCode)}</span><span>${escapeHtml(x.versionLabel)}</span><span>${escapeHtml(x.workflowStatus)}</span><span>${x.isEnabled ? '是' : '否'}</span></div>`).join('');
  document.getElementById('adminCatalogTable').innerHTML = '<div class="table-row head"><span>代码</span><span>名称</span><span>类别</span><span>Liquid Class</span><span>启用</span></div>' + catalog.map(x => `<div class="table-row"><span>${escapeHtml(x.reagentCode)}</span><span>${escapeHtml(x.name)}</span><span>${escapeHtml(x.reagentType)}</span><span>${escapeHtml(x.liquidClassCode || '--')}</span><span>${x.isEnabled ? '是' : '否'}</span></div>`).join('');
  await loadTraceAudit({silentForbidden:false});
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
      detail: '请先选择实验类型',
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
    const hint = channel.workflowVersionId ? '空闲 / 可上样' : '未选实验类型，禁止添加';
    return `<div class="sample-slot empty"><div class="slot-no">${escapeHtml(code)}</div><div><b>空闲</b><span>${escapeHtml(hint)}</span><button class="btn btn-soft full"${disabled} onclick="openConfirmModalForSlot('${escapeHtml(letter)}', ${slot})">添加样本</button></div><em>--</em></div>`;
  }
  const inherited = [
    slide.workflowName || channel.workflowName || slide.protocolCode || channel.workflowCode,
    slide.workflowVersionLabel || channel.workflowVersionLabel
  ].filter(Boolean).join(' / ');
  return `<div class="sample-slot occupied"><div class="slot-no">${escapeHtml(code)}</div><div><b>${escapeHtml(slide.sampleIdentifier || slide.barcode || slide.id)}</b><span>${escapeHtml(displayStatusText(slide.status || 'loaded'))} / ${escapeHtml(displayLogText(slide.currentStep || ''))}</span><small>继承通道脚本：${escapeHtml(inherited || '--')}</small></div><em>${escapeHtml(slide.protocolCode || channel.workflowCode || channel.experimentType || '--')}</em></div>`;
}

function renderChannelWorkflowPicker(channel, letter, workflow){
  const locked = channel.workflowLocked || channel.workflowSelectionStatus === 'Locked';
  const canSelect = !workflow.selected && channel.canSelectWorkflow !== false && !locked;
  const canChange = workflow.selected && channel.canChangeWorkflow && !locked;
  const action = canSelect
    ? `<button class="btn btn-soft full" onclick="openChannelScriptModal('${escapeHtml(letter)}', 'select')">选择实验类型</button>`
    : canChange
      ? `<button class="btn btn-soft full" onclick="openChannelScriptModal('${escapeHtml(letter)}', 'change')">更换实验类型</button>`
      : locked
        ? '<button class="btn btn-soft full" disabled>已锁定</button>'
        : '';
  const detail = workflow.selected ? workflowStatusLabel(channel) : '先选择实验类型，再添加样本';
  return `<div class="channel-script-picker ${workflow.selected ? 'selected' : 'unselected'}"><div><span>实验类型</span><b>${escapeHtml(channel.experimentType || '未选择')}</b><small>${escapeHtml(detail)}</small></div>${action}</div>`;
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

async function openChannelScriptModal(letter, mode='select'){
  activeChannelScriptLetter = letter;
  const modal = document.getElementById('channelScriptModal');
  const title = document.getElementById('channelScriptTitle');
  const reasonLabel = document.getElementById('channelScriptReasonLabel');
  const reasonInput = document.getElementById('channelScriptReason');
  if(!modal) return;
  const isChange = mode === 'change';
  if(title) title.textContent = `${letter} 通道${isChange ? '更换' : '选择'}实验类型`;
  if(reasonLabel) reasonLabel.classList.toggle('hidden', !isChange);
  if(reasonInput) reasonInput.value = '';
  setText('channelScriptHint', isChange
    ? '将影响该通道全部未运行玻片。请输入变更原因，再选择 HE 或 IHC。'
    : '只需选择 HE 或 IHC，后端会自动绑定当前默认已发布流程。');
  modal.classList.remove('hidden');
  try{
    await ensureActiveChannelBatch(letter);
  }catch(e){
    setText('channelScriptHint', e.message || '请检查通道批次服务。');
    toast(e.message || '通道加载失败', true);
  }
}

function closeChannelScriptModal(){
  document.getElementById('channelScriptModal')?.classList.add('hidden');
}

async function applyChannelExperimentTypeSelection(type){
  const experimentType = String(type || '').trim().toUpperCase();
  const reason = document.getElementById('channelScriptReason')?.value?.trim();
  const channel = activeChannelScriptLetter ? channelByLetter(activeChannelScriptLetter) : null;
  if(!activeChannelScriptLetter || !['HE','IHC'].includes(experimentType)){
    toast('请选择 HE 或 IHC', true);
    return;
  }
  const reasonVisible = !document.getElementById('channelScriptReasonLabel')?.classList.contains('hidden');
  if(reasonVisible && !reason){
    toast('更换实验类型必须填写原因', true);
    return;
  }
  if(reasonVisible){
    const decision = await operatorConfirm({
      title:'确认更换实验类型',
      message:`确认将 ${activeChannelScriptLetter} 通道重新绑定为当前默认 ${experimentType} 流程？`,
      warning:'该操作只允许在未启动批次上执行，并会写入审计。',
      confirmText:'确认更换'
    });
    if(!decision.confirmed) return;
  }
  try{
    const batch = await ensureActiveChannelBatch(activeChannelScriptLetter);
    const result = await api('/api/channel-batches/experiment-type-selection', {
      method: 'POST',
      body: JSON.stringify({
        commandId: commandId('channel-experiment-type'),
        channelBatchId: batch.channelBatchId,
        drawerCode: activeChannelScriptLetter,
        experimentType,
        reason: reason || null
      })
    });
    closeChannelScriptModal();
    await loadHostState();
    toast(`${activeChannelScriptLetter} 通道已选择 ${experimentType}，默认流程 ${result.workflowName || ''} v${result.workflowVersionLabel || ''} 已冻结`);
  }catch(e){
    setText('channelScriptHint', e.message || '实验类型选择失败');
    toast(e.message || '实验类型选择失败', true);
  }
}

function clearChannelScriptSelection(){
  closeChannelScriptModal();
  toast('通道实验类型以后端 ChannelBatch 为准，不能在浏览器本地清除', true);
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
  const expected = path === 'he' ? 'HE' : 'IHC';
  const valid = workflow.selected && !channel?.workflowLocked && channel?.experimentType === expected;
  setText('confirmChannelScript', workflow.selected
    ? `通道继承：${channel.experimentType} · ${workflow.label} / ${workflow.version}；当前识别路径要求 ${expected}，${valid ? '流程兼容' : '流程不兼容'}。`
    : '实验类型：未选择。请先在通道卡片中选择 HE 或 IHC。');
  if(button) button.disabled = !valid;
  showSampleTaskError(valid ? '' : `该 Slot 所在通道需要先选择 ${expected} 实验类型，且启动后不能追加样本。`);
}

function openConfirmModalForSlot(letter, slot){
  openConfirmModal(null, slotCode(letter, slot));
}

function openConfirmModal(mode, preferredSlotCode){
  activeConfirmMode = mode || null;
  pendingPrimaryAntibodyCandidates = [];
  pendingLisQueryLogId = null;
  pendingLisRawCode = null;
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
    showSampleTaskError('未选择通道实验类型，不能创建任务。');
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
      if(path === 'ihc-hospital' && (!pendingLisQueryLogId || pendingLisRawCode !== rawCode)){
        const lisResult = await api('/api/lis/mock-query', {
          method: 'POST',
          body: JSON.stringify({
            commandId: commandId('lis-query'),
            rawCode: rawCode || ''
          })
        });
        pendingLisQueryLogId = lisResult.lisQueryLogId;
        pendingLisRawCode = rawCode;
        const candidates = lisResult.candidatePrimaryAntibodyCodes || [];
        populatePrimaryAntibodyCandidates(candidates);
        if(lisResult.status === 'MultipleCandidates'){
          showSampleTaskError(lisResult.message || 'Select the final primary antibody, then confirm again.');
          return;
        }
        if(lisResult.status !== 'SingleCandidate'){
          showSampleTaskError(lisResult.errorMessage || lisResult.message || 'LIS query did not return a usable candidate.');
          return;
        }
      }
      const selectedPrimaryAntibodyCode = document.getElementById('primaryAntibodySelect')?.value
        || pendingPrimaryAntibodyCandidates[0]
        || null;
      await api('/api/tasks/ihc', {
        method: 'POST',
        body: JSON.stringify({
          commandId: commandId('task-ihc'),
          inputMode: path === 'ihc-hospital' ? 'HospitalBarcode' : 'PrimaryAntibody',
          rawCode: rawCode || '',
          slotCode: slot,
          drawerCode: parsed.letter,
          channelBatchId: channel.channelBatchId,
          selectedPrimaryAntibodyCode,
          lisQueryLogId: path === 'ihc-hospital' ? pendingLisQueryLogId : null
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

function currentWorkflowVersionId(){
  return window.activeWorkflowVersionDetail?.workflowVersionId || null;
}

function workflowDetailStep(stepId){
  return (window.activeWorkflowVersionDetail?.steps || []).find(x => x.id === stepId) || null;
}

function workflowDetailRequirement(id){
  return (window.activeWorkflowVersionDetail?.reagentRequirements || []).find(x => x.id === id) || null;
}

function parseOptionalInteger(value, fieldName){
  const normalized = String(value ?? '').trim();
  if(!normalized) return null;
  const parsed = Number.parseInt(normalized, 10);
  if(Number.isNaN(parsed)){
    toast(`${fieldName} 必须是整数`, true);
    return undefined;
  }
  return parsed;
}

function workflowCatalogCodes(){
  return (window.configureReagentCatalog || []).map(x => x.reagentCode).filter(Boolean).join(', ');
}

async function createWorkflowDraft(){
  const code = prompt('流程代码', 'CUSTOM-' + new Date().toISOString().slice(0, 10).replaceAll('-', ''));
  if(!code) return;
  const name = prompt('流程名称', code + ' Draft') || code;
  const workflowType = (prompt('实验类型：HE 或 IHC', 'IHC') || 'IHC').trim().toUpperCase();
  if(!['HE', 'IHC'].includes(workflowType)){
    toast('实验类型必须是 HE 或 IHC', true);
    return;
  }
  const versionLabel = prompt('Draft 版本号', '0.1') || '0.1';
  const description = prompt('流程说明', '配置页面创建的流程 Draft。') || '';
  const changeNote = prompt('变更说明', '创建空白 Draft。') || '创建空白 Draft。';
  const result = await api('/api/workflows', {
    method:'POST',
    body: JSON.stringify({
      commandId: commandId('workflow-create'),
      code,
      name,
      workflowType,
      description,
      versionLabel,
      changeNote
    })
  });
  toast(`已创建 Draft：${result.code} v${result.versionLabel}`);
  await renderConfigure();
  await openWorkflowVersionDetail(result.workflowVersionId);
}

async function copyWorkflowDraft(sourceWorkflowId){
  const workflows = window.configureWorkflows?.length ? window.configureWorkflows : await api('/api/workflows');
  let workflow = sourceWorkflowId ? workflows.find(x => x.id === sourceWorkflowId) : null;
  if(!workflow){
    const code = prompt('输入要复制的流程代码');
    if(!code) return;
    workflow = workflows.find(x => String(x.code).toUpperCase() === code.trim().toUpperCase());
  }
  const latest = (workflow?.versions || []).slice().sort((a, b) => (b.versionNo || 0) - (a.versionNo || 0))[0];
  if(!latest){
    toast('未找到可复制的流程版本', true);
    return;
  }
  await copyWorkflowVersionDraft(latest.id);
}

async function copyWorkflowVersionDraft(sourceWorkflowVersionId){
  const versionLabel = prompt('新 Draft 版本号', '0.1');
  if(versionLabel === null) return;
  const changeNote = prompt('变更说明', '复制现有版本为 Draft。') || '复制现有版本为 Draft。';
  const result = await api(`/api/workflow-versions/${encodeURIComponent(sourceWorkflowVersionId)}/copy-draft`, {
    method:'POST',
    body: JSON.stringify({
      commandId: commandId('workflow-copy'),
      versionLabel: versionLabel || null,
      changeNote
    })
  });
  toast(`已复制为 Draft：${result.code} v${result.versionLabel}`);
  await renderConfigure();
  await openWorkflowVersionDetail(result.workflowVersionId);
}

async function updateWorkflowVersionMeta(){
  const workflowVersionId = currentWorkflowVersionId();
  if(!workflowVersionId) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}`, {
    method:'PUT',
    body: JSON.stringify({
      commandId: commandId('workflow-meta'),
      name: document.getElementById('workflowMetaName')?.value || null,
      versionLabel: document.getElementById('workflowMetaVersionLabel')?.value || null,
      changeNote: document.getElementById('workflowMetaChangeNote')?.value || null,
      description: document.getElementById('workflowMetaDescription')?.value || null,
      isEnabled: true
    })
  });
  toast('流程基本信息已保存');
  await renderConfigure();
  await openWorkflowVersionDetail(workflowVersionId);
}

function collectWorkflowStepInput(existing){
  const defaultStepNo = existing?.stepNo || ((window.activeWorkflowVersionDetail?.steps || []).length + 1);
  const stepNoRaw = prompt('步骤顺序', String(defaultStepNo));
  if(stepNoRaw === null) return null;
  const stepNo = parseOptionalInteger(stepNoRaw, '步骤顺序');
  if(stepNo === undefined) return null;
  const stepName = prompt('步骤名称', existing?.stepName || 'New Step');
  if(!stepName) return null;
  const actionType = prompt('动作类型，例如 Dispense / Incubate / Wash / Dab', existing?.actionType || 'Dispense');
  if(!actionType) return null;
  const majorStepCode = prompt('大步骤代码，例如 PRIMARY / WASH / DAB / HEMATOXYLIN', existing?.majorStepCode || actionType.toUpperCase());
  const reagentHint = workflowCatalogCodes();
  const reagentCode = prompt(`试剂代码，可留空。当前目录：${reagentHint || '暂无'}`, existing?.reagentCode || '');
  const volume = parseOptionalInteger(prompt('体积 uL，可留空', existing?.volumeUl ?? ''), '体积');
  if(volume === undefined) return null;
  const duration = parseOptionalInteger(prompt('Mock 时长秒，可留空', existing?.durationSeconds ?? '3'), '时长');
  if(duration === undefined) return null;
  const temp = parseOptionalInteger(prompt('目标温度 deciC，例如 250 表示 25.0C，可留空', existing?.targetTemperatureDeciC ?? ''), '目标温度');
  if(temp === undefined) return null;
  return {
    commandId: commandId(existing ? 'workflow-step-update' : 'workflow-step-add'),
    stepNo,
    majorStepCode,
    stepName,
    actionType,
    reagentCode: reagentCode || null,
    volumeUl: volume,
    durationSeconds: duration,
    targetTemperatureDeciC: temp,
    mixParametersJson: '{}',
    washParametersJson: '{}',
    legacyParametersJson: '{}',
    failureStrategy: 'Stop'
  };
}

async function addWorkflowStep(){
  const workflowVersionId = currentWorkflowVersionId();
  if(!workflowVersionId) return;
  const payload = collectWorkflowStepInput(null);
  if(!payload) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/steps`, {
    method:'POST',
    body: JSON.stringify(payload)
  });
  toast('步骤已新增');
  await openWorkflowVersionDetail(workflowVersionId);
  await renderConfigure();
}

async function editWorkflowStep(stepId){
  const workflowVersionId = currentWorkflowVersionId();
  const existing = workflowDetailStep(stepId);
  if(!workflowVersionId || !existing) return;
  const payload = collectWorkflowStepInput(existing);
  if(!payload) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/steps/${encodeURIComponent(stepId)}`, {
    method:'PUT',
    body: JSON.stringify(payload)
  });
  toast('步骤已保存');
  await openWorkflowVersionDetail(workflowVersionId);
  await renderConfigure();
}

async function deleteWorkflowStep(stepId){
  const workflowVersionId = currentWorkflowVersionId();
  if(!workflowVersionId || !confirm('确认删除该步骤？')) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/steps/${encodeURIComponent(stepId)}?commandId=${encodeURIComponent(commandId('workflow-step-delete'))}`, {
    method:'DELETE'
  });
  toast('步骤已删除');
  await openWorkflowVersionDetail(workflowVersionId);
  await renderConfigure();
}

async function moveWorkflowStep(stepId, direction){
  const workflowVersionId = currentWorkflowVersionId();
  if(!workflowVersionId) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/steps/${encodeURIComponent(stepId)}/${direction === 'up' ? 'move-up' : 'move-down'}`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('workflow-step-move')})
  });
  await openWorkflowVersionDetail(workflowVersionId);
  await renderConfigure();
}

function collectWorkflowRequirementInput(existing){
  const reagentHint = workflowCatalogCodes();
  const reagentCode = prompt(`试剂代码。当前目录：${reagentHint || '暂无'}`, existing?.reagentCode || '');
  if(!reagentCode) return null;
  const requiredVolumeUl = parseOptionalInteger(prompt('需求量 uL', existing?.requiredVolumeUl ?? '0'), '需求量');
  if(requiredVolumeUl === undefined) return null;
  const isRequired = confirm('是否为必需试剂？点击“确定”=必需，“取消”=可选。');
  return {
    commandId: commandId(existing ? 'workflow-req-update' : 'workflow-req-add'),
    reagentCode,
    requiredVolumeUl,
    isRequired
  };
}

async function addWorkflowRequirement(){
  const workflowVersionId = currentWorkflowVersionId();
  if(!workflowVersionId) return;
  const payload = collectWorkflowRequirementInput(null);
  if(!payload) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/reagent-requirements`, {
    method:'POST',
    body: JSON.stringify(payload)
  });
  toast('试剂需求已新增');
  await openWorkflowVersionDetail(workflowVersionId);
  await renderConfigure();
}

async function editWorkflowRequirement(id){
  const workflowVersionId = currentWorkflowVersionId();
  const existing = workflowDetailRequirement(id);
  if(!workflowVersionId || !existing) return;
  const payload = collectWorkflowRequirementInput(existing);
  if(!payload) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/reagent-requirements/${encodeURIComponent(id)}`, {
    method:'PUT',
    body: JSON.stringify(payload)
  });
  toast('试剂需求已保存');
  await openWorkflowVersionDetail(workflowVersionId);
  await renderConfigure();
}

async function deleteWorkflowRequirement(id){
  const workflowVersionId = currentWorkflowVersionId();
  if(!workflowVersionId || !confirm('确认删除该试剂需求？')) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/reagent-requirements/${encodeURIComponent(id)}?commandId=${encodeURIComponent(commandId('workflow-req-delete'))}`, {
    method:'DELETE'
  });
  toast('试剂需求已删除');
  await openWorkflowVersionDetail(workflowVersionId);
  await renderConfigure();
}

async function recalculateWorkflowRequirements(){
  const workflowVersionId = currentWorkflowVersionId();
  if(!workflowVersionId) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/reagent-requirements/recalculate`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('workflow-req-recalc')})
  });
  toast('已从步骤重算试剂需求');
  await openWorkflowVersionDetail(workflowVersionId);
  await renderConfigure();
}

function renderPublishValidation(validation){
  const root = document.getElementById('workflowPublishValidation');
  if(!root) return;
  if(!validation){
    root.innerHTML = '';
    return;
  }
  const issues = validation.issues || [];
  if(!issues.length){
    root.innerHTML = '<div class="validation-ok"><b>校验通过</b><span>该 Draft 可发布。</span></div>';
    return;
  }
  root.innerHTML = `<div class="validation-ok"><b>${escapeHtml(displayStatusText(validation.result))}</b><span>失败 ${validation.failCount} / 警告 ${validation.warningCount}</span></div>` + issues.map(issue => `<div class="validation-issue"><b>${escapeHtml(severityText(issue.severity))}</b><em>${escapeHtml(displayLogText(issue.area))} · ${escapeHtml(displayLogText(issue.code))}</em><span>${escapeHtml(displayLogText(issue.message))}</span></div>`).join('');
}

async function validateWorkflowPublish(){
  const workflowVersionId = currentWorkflowVersionId();
  if(!workflowVersionId) return null;
  const validation = await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/publish-validation`);
  renderPublishValidation(validation);
  return validation;
}

async function publishWorkflowVersion(){
  const workflowVersionId = currentWorkflowVersionId();
  if(!workflowVersionId) return;
  const validation = await validateWorkflowPublish();
  if(!validation || validation.result === 'Fail'){
    toast('发布校验未通过，不能发布。', true);
    return;
  }
  if(validation.result === 'Warning' && !confirm('存在警告，仍然发布？')) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/publish`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('workflow-publish')})
  });
  toast('流程版本已发布，后续不可直接编辑');
  await renderConfigure();
  await openWorkflowVersionDetail(workflowVersionId);
}

async function retireWorkflowVersion(workflowVersionId){
  const reason = prompt('停用原因');
  if(!reason) return;
  if(!confirm('停用后该版本不能再被新通道选择，确认停用？')) return;
  await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/retire`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('workflow-retire'), reason})
  });
  toast('流程版本已停用');
  await renderConfigure();
  if(currentWorkflowVersionId() === workflowVersionId) await openWorkflowVersionDetail(workflowVersionId);
}

async function setDefaultWorkflowVersion(workflowVersionId, experimentType){
  if(!confirm(`确认将该版本设为默认 ${experimentType} 流程？现有通道不会自动切换。`)) return;
  try{
    await api(`/api/workflow-versions/${encodeURIComponent(workflowVersionId)}/set-default`, {
      method:'POST',
      body: JSON.stringify({commandId: commandId('workflow-default'), experimentType})
    });
    toast(`默认 ${experimentType} 流程已更新`);
    await renderConfigure();
    if(currentWorkflowVersionId() === workflowVersionId) await openWorkflowVersionDetail(workflowVersionId);
  }catch(e){
    toast(e.message || '设置默认流程失败', true);
  }
}

function renderPrimaryAntibodyMappings(mappings){
  const root = document.getElementById('primaryAntibodyMappingTable');
  if(!root) return;
  root.innerHTML = '<div class="table-row head"><span>一抗代码</span><span>流程</span><span>版本</span><span>流程状态</span><span>映射状态</span><span>操作</span></div>'
    + ((mappings || []).map(item => `<div class="table-row"><span>${escapeHtml(item.primaryAntibodyCode)}</span><span>${escapeHtml(item.workflowCode)}<small>${escapeHtml(item.workflowName)}</small></span><span>v${escapeHtml(item.versionLabel)}</span><span>${escapeHtml(workflowStatusText(item.workflowStatus))}</span><span>${item.isEnabled ? 'Enabled' : 'Disabled'}</span><span class="button-row">${item.isEnabled ? `<button class="btn btn-soft" onclick="disablePrimaryAntibodyMapping('${escapeHtml(item.id)}')">停用</button>` : `<button class="btn btn-soft" onclick="enablePrimaryAntibodyMapping('${escapeHtml(item.id)}')">启用</button>`}</span></div>`).join('')
    || '<div class="empty-state"><b>暂无一抗映射</b><span>新增映射后 IHC 兼容性校验会实时读取。</span></div>');
}

function pickPublishedIhcWorkflowVersion(){
  const candidates = (window.configureWorkflows || []).flatMap(workflow => (workflow.versions || [])
    .filter(version => workflow.workflowType === 'IHC' && version.status === 'Published')
    .map(version => ({workflow, version})));
  if(!candidates.length){
    toast('当前没有 Published IHC 流程版本', true);
    return null;
  }
  const menu = candidates.map((item, index) => `${index + 1}. ${item.workflow.code} v${item.version.versionLabel} - ${item.workflow.name}`).join('\n');
  const selected = Number.parseInt(prompt(`选择 Published IHC 流程：\n${menu}`, '1') || '', 10) - 1;
  if(Number.isNaN(selected) || !candidates[selected]){
    toast('未选择有效流程版本', true);
    return null;
  }
  return candidates[selected].version.id;
}

async function createPrimaryAntibodyMapping(){
  const primaryAntibodyCode = prompt('一抗代码，例如 001');
  if(!primaryAntibodyCode) return;
  const workflowVersionId = pickPublishedIhcWorkflowVersion();
  if(!workflowVersionId) return;
  await api('/api/primary-antibody-mappings', {
    method:'POST',
    body: JSON.stringify({
      commandId: commandId('primary-map-create'),
      primaryAntibodyCode,
      workflowVersionId
    })
  });
  toast('一抗映射已保存');
  await renderConfigure();
}

async function enablePrimaryAntibodyMapping(id){
  await api(`/api/primary-antibody-mappings/${encodeURIComponent(id)}/enable`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('primary-map-enable'), reason: '启用映射'})
  });
  toast('一抗映射已启用');
  await renderConfigure();
}

async function disablePrimaryAntibodyMapping(id){
  const reason = prompt('停用映射原因');
  if(!reason) return;
  await api(`/api/primary-antibody-mappings/${encodeURIComponent(id)}/disable`, {
    method:'POST',
    body: JSON.stringify({commandId: commandId('primary-map-disable'), reason})
  });
  toast('一抗映射已停用');
  await renderConfigure();
}

async function renderAlerts(){
  if(!document.getElementById('alarmList')) return;
  await loadTraceAlarms();
}

async function renderHistory(){
  if(!document.getElementById('historySlides')) return;
  await loadTraceHistory();
  await loadTraceAudit({silentForbidden:true});
}

async function renderAdminLegacySnapshot(state){
  if(!document.getElementById('userTable')) return;
  const users = await api('/api/users');
  setText('adminUserCount', users.length);
  setText('adminReagentCount', (state?.reagents || []).length);
  setText('adminAlarmCount', (state?.alarms || []).length);
  wireAdminToolbar(users);
  document.getElementById('userTable').innerHTML = '<div class="table-row head"><span>用户</span><span>显示名</span><span>角色</span><span>启用</span><span>操作</span></div>' + users.map(u => `<div class="table-row"><span>${escapeHtml(u.username)}</span><span>${escapeHtml(u.displayName)}</span><span class="badge-soft">${escapeHtml((u.roles || [u.role]).join(','))}</span><span>${u.enabled ? '是' : '否'}</span><span class="button-row"><button class="btn btn-soft" onclick="adminRenameUser('${u.id}', '${escapeHtml(u.displayName)}')">改名</button><button class="btn btn-soft" onclick="adminToggleUser('${u.id}', ${!u.enabled})">${u.enabled ? '禁用' : '启用'}</button><button class="btn btn-soft" onclick="adminResetPassword('${u.id}')">重置</button><button class="btn btn-soft" onclick="adminSetRoles('${u.id}', '${escapeHtml((u.roles || []).join(','))}')">角色</button><button class="btn btn-soft" onclick="adminDeleteUser('${u.id}')">删除</button></span></div>`).join('');
  await loadTraceAudit({silentForbidden:true});
}

function traceInputValue(id){
  return (document.getElementById(id)?.value || '').trim();
}

function traceUtcQueryValue(id){
  const value = traceInputValue(id);
  if(!value) return '';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toISOString();
}

function appendTraceQuery(params, key, value){
  if(value !== null && value !== undefined && String(value).trim() !== '') params.set(key, String(value).trim());
}

function historyQueryString(){
  const params = new URLSearchParams();
  appendTraceQuery(params, 'fromUtc', traceUtcQueryValue('historyFromFilter'));
  appendTraceQuery(params, 'toUtc', traceUtcQueryValue('historyToFilter'));
  appendTraceQuery(params, 'status', traceInputValue('historyStatusFilter'));
  appendTraceQuery(params, 'channel', traceInputValue('historyChannelFilter'));
  appendTraceQuery(params, 'slot', traceInputValue('historySlotFilter'));
  appendTraceQuery(params, 'experimentType', traceInputValue('historyExperimentFilter'));
  appendTraceQuery(params, 'workflow', traceInputValue('historyWorkflowFilter'));
  appendTraceQuery(params, 'sampleCode', traceInputValue('historySampleFilter'));
  appendTraceQuery(params, 'primaryAntibodyCode', traceInputValue('historyPrimaryFilter'));
  appendTraceQuery(params, 'reagentCode', traceInputValue('historyReagentFilter'));
  appendTraceQuery(params, 'reagentBatchNo', traceInputValue('historyBatchFilter'));
  appendTraceQuery(params, 'operator', traceInputValue('historyOperatorFilter'));
  params.set('pageSize', '80');
  return params.toString();
}

function alarmQueryString(){
  const params = new URLSearchParams();
  appendTraceQuery(params, 'alarmStatus', traceInputValue('alarmStatusFilter'));
  appendTraceQuery(params, 'severity', traceInputValue('alarmSeverityFilter'));
  appendTraceQuery(params, 'channel', traceInputValue('alarmChannelFilter'));
  params.set('pageSize', '80');
  return params.toString();
}

function auditQueryString(){
  const params = new URLSearchParams();
  appendTraceQuery(params, 'fromUtc', traceUtcQueryValue('historyFromFilter'));
  appendTraceQuery(params, 'toUtc', traceUtcQueryValue('historyToFilter'));
  appendTraceQuery(params, 'user', traceInputValue('auditUserFilter'));
  appendTraceQuery(params, 'action', traceInputValue('auditActionFilter'));
  appendTraceQuery(params, 'entityType', traceInputValue('auditEntityFilter'));
  appendTraceQuery(params, 'channel', traceInputValue('auditChannelFilter'));
  appendTraceQuery(params, 'slot', traceInputValue('auditSlotFilter'));
  appendTraceQuery(params, 'machineRunId', traceInputValue('auditRunFilter'));
  appendTraceQuery(params, 'taskId', traceInputValue('auditTaskFilter'));
  appendTraceQuery(params, 'commandId', traceInputValue('auditCommandFilter'));
  appendTraceQuery(params, 'correlationId', traceInputValue('auditCorrelationFilter'));
  params.set('pageSize', '80');
  return params.toString();
}

async function loadTraceHistory(){
  const slidesRoot = document.getElementById('historySlides');
  const reagentsRoot = document.getElementById('historyReagents');
  if(!slidesRoot && !reagentsRoot) return;
  try{
    const query = historyQueryString();
    const suffix = query ? '?' + query : '';
    const [runs, consumptions] = await Promise.all([
      api('/api/history/runs' + suffix),
      api('/api/history/reagent-consumptions' + suffix)
    ]);
    if(slidesRoot) renderTraceHistoryRuns(slidesRoot, runs.items || []);
    if(reagentsRoot) renderTraceReagentConsumptions(reagentsRoot, consumptions.items || []);
  }catch(e){
    if(slidesRoot) slidesRoot.innerHTML = `<div class="empty-state"><b>历史查询失败</b><span>${escapeHtml(e.message || '请检查后端服务。')}</span></div>`;
    if(reagentsRoot) reagentsRoot.innerHTML = `<div class="empty-state"><b>试剂消耗查询失败</b><span>${escapeHtml(e.message || '请检查后端服务。')}</span></div>`;
  }
}

function renderTraceHistoryRuns(root, runs){
  root.innerHTML = '<div class="table-row head"><span>运行</span><span>通道/玻片</span><span>流程</span><span>状态</span><span>时间</span></div>' + ((runs || []).map(run =>
    `<div class="table-row" role="button" tabindex="0" onclick="loadTraceRunDetail('${escapeHtml(run.machineRunId)}')"><span><b>${escapeHtml(run.runCode || run.machineRunId)}</b><small>${escapeHtml(run.machineRunId)}</small></span><span>${escapeHtml(run.channels || '--')} / ${Number(run.slideTaskCount || 0)} 张</span><span>${escapeHtml(run.workflowNames || '--')}</span><span>${escapeHtml(displayStatusText(run.status || '--'))}<small>告警 ${Number(run.alarmCount || 0)}</small></span><span>${escapeHtml(formatDateTime(run.createdAtUtc))}<small>${escapeHtml(run.requestedBy || '--')} · 查看链路</small></span></div>`
  ).join('') || '<div class="empty-state"><b>暂无正式历史运行</b><span>当前筛选条件下没有 MachineRun / ChannelBatch 记录。</span></div>');
}

async function loadTraceRunDetail(machineRunId){
  let root = document.getElementById('historyRunDetail');
  if(!root){
    const section = document.createElement('section');
    section.className = 'modern-card';
    section.innerHTML = '<div class="section-title"><div><h2>运行全链路详情</h2><p>操作员只显示业务摘要；技术报文、路径和持久化异常仅工程页可见。</p></div><span class="badge-soft">正式追溯</span></div><div id="historyRunDetail" class="detail-grid"></div>';
    document.getElementById('historySlides')?.closest('.split-grid')?.after(section);
    root = document.getElementById('historyRunDetail');
  }
  if(!root) return;
  root.innerHTML = '<div><span>读取中</span><b>正在加载正式追溯链路...</b></div>';
  try{
    const detail = await api(`/api/history/runs/${encodeURIComponent(machineRunId)}`);
    const channels = detail.channels || detail.channelBatches || [];
    const slides = channels.flatMap(x => x.slides || []);
    const steps = detail.steps || (detail.workflowExecutions || []).flatMap(x => x.steps || []);
    const reagents = detail.reagentConsumptions || [];
    const dab = detail.dabUsages || [];
    const alarms = detail.alarms || [];
    root.innerHTML = [
      ['运行', `${detail.runCode || detail.machineRunId} / ${displayStatusText(detail.status || '--')}`],
      ['通道与玻片', `${channels.map(x => x.drawerCode).filter(Boolean).join(' / ') || '--'} · ${slides.length} 张`],
      ['步骤链路', steps.map(x => `${x.stepNo ?? ''} ${x.stepName || x.majorStepCode || ''}(${displayStatusText(x.status || '--')})`).join(' → ') || '--'],
      ['试剂消耗', reagents.map(x => `${x.reagentCode || '--'} ${x.volumeUl || 0}μL`).join(' / ') || '--'],
      ['DAB', dab.map(x => `${x.positionCode || '--'} ${x.volumeUl || 0}μL`).join(' / ') || '--'],
      ['告警摘要', alarms.map(x => alarmDisplayText(x)).join(' / ') || '无'],
      ['时间', `${formatDateTime(detail.startedAtUtc)} → ${formatDateTime(detail.completedAtUtc)}`]
    ].map(([label,value]) => `<div><span>${escapeHtml(label)}</span><b>${escapeHtml(value)}</b></div>`).join('');
  }catch(e){ root.innerHTML = `<div><span>加载失败</span><b>${escapeHtml(e.message || '无法读取追溯详情')}</b></div>`; }
}

function renderTraceReagentConsumptions(root, rows){
  root.innerHTML = '<div class="table-row head"><span>试剂</span><span>批号/序列</span><span>用量</span><span>运行</span><span>时间</span></div>' + ((rows || []).map(item =>
    `<div class="table-row"><span><b>${escapeHtml(item.reagentCode || '--')}</b><small>${escapeHtml(item.reagentName || '--')}</small></span><span>${escapeHtml(item.productionBatchNo || '--')}<small>${escapeHtml(item.serialNo || '--')}</small></span><span>${Number(item.volumeUl || 0)} uL</span><span>${escapeHtml(item.machineRunId || '--')}<small>${escapeHtml(item.workflowStepExecutionId || '--')}</small></span><span>${escapeHtml(formatDateTime(item.createdAtUtc))}</span></div>`
  ).join('') || '<div class="empty-state"><b>暂无试剂消耗</b><span>当前筛选条件下没有 ReagentConsumption 记录。</span></div>');
}

async function loadTraceAlarms(){
  const root = document.getElementById('alarmList');
  if(!root) return;
  try{
    const query = alarmQueryString();
    const result = await api('/api/alarms' + (query ? '?' + query : ''));
    const alarms = result.items || [];
    root.innerHTML = alarms.length ? alarms.map(alarm => {
      const level = String(alarm.severity || '').toLowerCase();
      const canAck = alarm.status === 'Active';
      const ackButton = canAck ? `<button class="btn btn-soft" onclick="acknowledgeTraceAlarm('${escapeHtml(alarm.alarmId)}','${escapeHtml(alarm.severity)}')">确认</button>` : '';
      return `<div class="alarm-row level-${escapeHtml(level || 'warning')}"><b>${escapeHtml(severityText(alarm.severity || '--'))}</b><span>${escapeHtml(displayLogText(alarm.code || '--'))} · ${escapeHtml(displayLogText(alarm.message || ''))}</span><em>${escapeHtml(displayStatusText(alarm.status || '--'))} · 通道 ${escapeHtml(alarm.sourceChannels || '--')} · ${escapeHtml(formatDateTime(alarm.createdAtUtc))}</em><small>${alarm.ackBy ? `确认：${escapeHtml(alarm.ackBy)} / ${escapeHtml(formatDateTime(alarm.ackAtUtc))} / ${escapeHtml(displayLogText(alarm.ackReason || '--'))}` : '未确认'} ${ackButton}</small></div>`;
    }).join('') : '<div class="empty-state"><b>暂无告警</b><span>当前筛选条件下没有告警记录。</span></div>';
    renderTraceAlarmActions(alarms);
  }catch(e){
    root.innerHTML = `<div class="empty-state"><b>告警查询失败</b><span>${escapeHtml(e.message || '请检查后端服务。')}</span></div>`;
  }
}

function renderTraceAlarmActions(alarms){
  const root = document.getElementById('alertLogs');
  if(!root) return;
  const actions = (alarms || []).flatMap(alarm => (alarm.actions || []).map(action => ({alarm, action})))
    .sort((a, b) => String(b.action.createdAtUtc || '').localeCompare(String(a.action.createdAtUtc || '')));
  root.innerHTML = actions.length ? actions.map(x =>
    `<div><i></i><span>${escapeHtml(formatDateTime(x.action.createdAtUtc))} · ${escapeHtml(alarmActionLogText(x))}</span></div>`
  ).join('') : '<div><i></i><span>暂无告警处理记录</span></div>';
}

async function acknowledgeTraceAlarm(alarmId, severity){
  let reason = '';
  if(['Error','Critical'].includes(severity)){
    reason = await operatorPrompt('确认 Error/Critical 告警必须填写处理原因。', '', {
      title:'确认告警',
      inputRequired:true,
      requiredMessage:'请填写处理原因。'
    }) || '';
    if(!reason.trim()){
      toast('请填写处理原因。', true);
      return;
    }
  }else{
    reason = await operatorPrompt('处理原因（可选）。', '', {
      title:'确认告警',
      confirmText:'确认告警'
    }) || '';
  }
  try{
    await api(`/api/alarms/${encodeURIComponent(alarmId)}/acknowledge`, {
      method:'POST',
      body: JSON.stringify({commandId: commandId('alarm-ack'), reason})
    });
    toast('告警已确认。');
    await loadTraceAlarms();
    await loadTraceAudit({silentForbidden:true});
  }catch(e){
    toast(e.message || '告警确认失败。', true);
  }
}

async function loadTraceAudit(options={}){
  const roots = [document.getElementById('historyLogs'), document.getElementById('adminLogs')].filter(Boolean);
  if(!roots.length) return;
  try{
    const query = auditQueryString();
    const result = await api('/api/audit/logs' + (query ? '?' + query : ''));
    const rows = result.items || [];
    const html = rows.length ? rows.map(item =>
      `<div><i></i><span>${escapeHtml(formatDateTime(item.createdAtUtc))} · ${escapeHtml(item.actor || '--')} · ${escapeHtml(actionText(item.action || '--'))} · ${escapeHtml(entityTypeText(item.entityType || '--'))} ${escapeHtml(item.entityId || '')}<small>${escapeHtml(auditLogText(item))}</small></span></div>`
    ).join('') : '<div><i></i><span>暂无审计记录</span></div>';
    roots.forEach(root => { root.innerHTML = html; });
    setText('adminLogCount', rows.length);
  }catch(e){
    if(options.silentForbidden && (e.status === 401 || e.status === 403)){
      roots.forEach(root => { root.innerHTML = '<div><i></i><span>当前账号无权查看审计日志。</span></div>'; });
      return;
    }
    roots.forEach(root => { root.innerHTML = `<div><i></i><span>审计查询失败：${escapeHtml(e.message || '请检查后端服务。')}</span></div>`; });
  }
}

function exportTraceCsv(type){
  const map = {
    'history-runs': ['/api/history/export/runs', historyQueryString],
    reagents: ['/api/history/export/reagent-consumptions', historyQueryString],
    alarms: ['/api/alarms/export', alarmQueryString],
    audit: ['/api/audit/export', auditQueryString]
  };
  const entry = map[type];
  if(!entry){
    toast('未知导出类型。', true);
    return;
  }
  const query = entry[1]();
  location.href = entry[0] + (query ? '?' + query : '');
}

function sampleShortCode(slide){
  const value = String(slide?.sampleIdentifier || slide?.normalizedSampleCode || slide?.rawSampleCode || slide?.barcode || slide?.id || '--');
  return value.length > 18 ? value.slice(0, 15) + '...' : value;
}

function sampleWorkflowText(channel, slide){
  return [
    slide?.workflowName || channel?.workflowName || slide?.protocolCode || channel?.workflowCode,
    slide?.workflowVersionLabel || channel?.workflowVersionLabel
  ].filter(Boolean).join(' / ');
}

function renderSampleSlotLegacyColumn(channel, letter, slot){
  const slide = (channel.slides || []).find(x => Number(x.slot) === slot);
  const code = slotCode(letter, slot);
  const safeLetter = escapeHtml(letter);
  if(!slide){
    const disabled = !channel.workflowVersionId || channel.workflowLocked ? ' disabled' : '';
    const label = channel.workflowVersionId && !channel.workflowLocked ? '添加样本' : '请先选类型';
    return `<div class="sample-slot empty compact-slot"><div class="slot-no">${escapeHtml(code)}</div><div class="slot-main"><b>空闲</b><span>${channel.workflowLocked ? '已锁定' : '可用 Slot'}</span></div><button class="btn btn-soft"${disabled} onclick="openConfirmModalForSlot('${safeLetter}', ${slot})">${label}</button></div>`;
  }
  const status = statusText(slide.status || 'loaded');
  return `<div class="sample-slot occupied compact-slot" onclick="showSampleDetail('${safeLetter}', ${slot})"><div class="slot-no">${escapeHtml(code)}</div><div class="slot-main"><b>${escapeHtml(sampleShortCode(slide))}</b><span>${escapeHtml(status)}</span></div><button class="btn btn-soft" onclick="event.stopPropagation();showSampleDetail('${safeLetter}', ${slot})">查看</button></div>`;
}

function renderChannelWorkflowPickerLegacyColumn(channel, letter, workflow){
  const locked = channel.workflowLocked || channel.workflowSelectionStatus === 'Locked';
  const canSelect = !workflow.selected && channel.canSelectWorkflow !== false && !locked;
  const canChange = workflow.selected && channel.canChangeWorkflow && !locked;
  const action = canSelect
    ? `<button class="btn btn-soft" onclick="openChannelScriptModal('${escapeHtml(letter)}', 'select')">选择实验类型</button>`
    : canChange
      ? `<button class="btn btn-soft" onclick="openChannelScriptModal('${escapeHtml(letter)}', 'change')">更换实验类型</button>`
      : locked
        ? '<button class="btn btn-soft" disabled>已锁定</button>'
        : '';
  const label = workflow.selected ? channel.experimentType || '--' : '未选择';
  const detail = workflow.selected ? workflowStatusLabel(channel) : '先选实验类型';
  return `<div class="channel-script-line ${workflow.selected ? 'selected' : 'unselected'}"><span>实验类型：<b>${escapeHtml(label)}</b><em>${escapeHtml(detail)}</em></span>${action}</div>`;
}

function renderSamplesLegacyColumn(state){
  if(!document.getElementById('sampleCabinet')) return;
  const count = slideCount(state);
  setText('sampleBadge', `${count}/16 已占用`);
  const root = document.getElementById('sampleCabinet');
  const channels = (state.channels && state.channels.length)
    ? state.channels
    : ['A','B','C','D'].map((letter, index) => ({drawerCode: letter, id: index + 1, slides: [], status: 'empty'}));
  root.innerHTML = channels.map((channel, index) => {
    const letter = channel.drawerCode || ['A','B','C','D'][index] || channel.id;
    const slides = channel.slides || [];
    const workflow = channelWorkflowInfo(channel);
    const slots = [4,3,2,1].map(slot => renderSampleSlot(channel, letter, slot)).join('');
    return `<div class="sample-column status-${escapeHtml(channel.status || 'empty')}"><div class="column-handle"><span>${escapeHtml(letter)} 通道</span><b>${slides.length}/4</b></div>${renderChannelWorkflowPicker(channel, letter, workflow)}<div class="slot-stack">${slots}</div></div>`;
  }).join('');
  refreshConfirmSlotOptions();
}

function showSampleDetail(letter, slot){
  const channel = channelByLetter(letter);
  const slide = (channel?.slides || []).find(x => Number(x.slot) === Number(slot));
  if(!channel || !slide){
    toast('未找到该 Slot 的样本详情。', true);
    return;
  }
  const body = document.getElementById('sampleDetailBody');
  if(body){
    const inherited = sampleWorkflowText(channel, slide) || '--';
    const fields = [
      ['Slot', slotCode(letter, slot)],
      ['状态', statusText(slide.status || 'loaded')],
      ['样本标识', slide.sampleIdentifier || slide.normalizedSampleCode || slide.rawSampleCode || slide.barcode || slide.id || '--'],
      ['实验类型', channel.experimentType || slide.experimentType || '--'],
      ['继承通道脚本', inherited],
      ['一抗代码', slide.confirmedPrimaryAntibodyCode || slide.primaryAntibodyCode || '--'],
      ['识别路径', slide.inputMode || '--'],
      ['兼容性', slide.compatibilityValidationStatus || '--'],
      ['兼容性说明', slide.compatibilityValidationMessage || '--'],
      ['当前步骤', slide.currentStep || '--'],
      ['任务编号', slide.id || '--']
    ];
    body.innerHTML = fields.map(([label, value]) => `<div><span>${escapeHtml(label)}</span><b>${escapeHtml(value)}</b></div>`).join('');
  }
  document.getElementById('sampleDetailModal')?.classList.remove('hidden');
}

function closeSampleDetailModal(){
  document.getElementById('sampleDetailModal')?.classList.add('hidden');
}

function reagentShortStateLabel(scanState){
  return ({VALID:'VALID', INVALID:'INVALID', EMPTY:'EMPTY', UNSCANNED:'未扫码'}[scanState] || scanState || '未扫码');
}

function renderReagentRackFromDatabaseLegacyInitial(rack){
  const positions = Array.isArray(rack) ? rack : [];
  const validCount = positions.filter(x => scanStateOf(x) === 'VALID').length;
  const invalidCount = positions.filter(x => scanStateOf(x) === 'INVALID').length;
  setText('reagentBadge', `${validCount}/40 VALID${invalidCount ? ` · ${invalidCount} INVALID` : ''}`);
  const byPosition = new Map(positions.map(x => [String(x.position || '').toUpperCase(), x]));
  const deck = document.getElementById('reagentDeck');
  if(!deck) return;
  deck.innerHTML = [1,2,3,4,5].map(col => {
    const rows = [1,2,3,4,5,6,7,8].map(row => {
      const pos = 'R' + (((col - 1) * 8) + row);
      const position = byPosition.get(pos);
      const bottle = position?.bottle;
      const scanState = scanStateOf(position);
      const code = bottle?.reagentCode || position?.reagentCode || position?.parsedReagentCode || '';
      const title = scanState === 'VALID' ? (bottle?.name || code || '有效试剂') : reagentShortStateLabel(scanState);
      const subtitle = scanState === 'VALID'
        ? [bottle?.fullBarcode || position?.rawBarcode, bottle?.lotNo, formatDate(bottle?.expirationDate)].filter(Boolean).join(' / ')
        : (scanState === 'INVALID' ? (invalidOrScanMessage(position) || '点击查看原因') : '');
      const end = scanState === 'VALID' && bottle ? formatVolume(bottle.remainingVolumeUl) : '';
      const stateText = bottle?.status || reagentShortStateLabel(scanState);
      const errorText = scanState === 'INVALID' ? invalidOrScanMessage(position) : '';
      return `<button type="button" class="vial ${bottle ? 'filled' : 'empty'} scan-${scanState.toLowerCase()} ${escapeHtml(bottle?.reagentType || '')}" onclick="showReagentDetail('${pos}')" title="${escapeHtml([pos, title, subtitle, stateText, errorText].filter(Boolean).join(' · '))}"><b>${pos}</b><div class="vial-main"><span>${escapeHtml(title)}</span><small>${escapeHtml(subtitle || errorText)}</small></div><em>${escapeHtml(end)}<small>${escapeHtml(stateText)}</small></em></button>`;
    }).join('');
    return `<div class="reagent-rack"><header><b>ch${col}</b><span>R${(col-1)*8+1}-R${col*8}</span></header>${rows}</div>`;
  }).join('');

  const columnStatus = document.getElementById('columnStatus');
  if(columnStatus){
    columnStatus.innerHTML = [1,2,3,4,5].map(col => {
      const column = positions.filter(x => x.columnNo === col);
      const valid = column.filter(x => scanStateOf(x) === 'VALID').length;
      const invalid = column.filter(x => scanStateOf(x) === 'INVALID').length;
      const empty = column.filter(x => scanStateOf(x) === 'EMPTY').length;
      const stateClass = invalid ? 'invalid' : valid ? 'has-data' : 'empty';
      const label = invalid ? `${invalid} INVALID` : `${valid}/8 VALID`;
      return `<div class="${stateClass}"><b>ch${col}</b><span>${escapeHtml(label)}</span><em>${escapeHtml(empty ? `${empty} EMPTY` : `R${(col-1)*8+1}-R${col*8}`)}</em></div>`;
    }).join('');
  }
}

document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('confirmSlot')?.addEventListener('change', updateConfirmModalFromSlot);
  document.getElementById('confirmPath')?.addEventListener('change', () => {
    pendingPrimaryAntibodyCandidates = [];
    pendingLisQueryLogId = null;
    pendingLisRawCode = null;
    updateConfirmModalFromSlot();
  });
  document.getElementById('rawCode')?.addEventListener('input', () => {
    pendingPrimaryAntibodyCandidates = [];
    pendingLisQueryLogId = null;
    pendingLisRawCode = null;
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
