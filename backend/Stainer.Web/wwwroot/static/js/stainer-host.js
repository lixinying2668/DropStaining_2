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

async function loadHostState(){
  try{
    const state = await api('/api/state');
    renderShellState(state);
    renderDashboard(state);
    renderSamples(state);
    renderReagents(state);
    renderAlerts(state);
    renderHistory(state);
    renderAdmin(state);
    renderEngineer(state);
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

function renderSamples(state){
  if(!document.getElementById('sampleCabinet')) return;
  const count = slideCount(state);
  setText('sampleBadge', `${count}/16 已装载`);
  const root = document.getElementById('sampleCabinet');
  root.innerHTML = (state.channels || []).map((channel, index) => {
    const letter = ['A','B','C','D'][index] || channel.id;
    const slides = channel.slides || [];
    const slots = [4,3,2,1].map(slot => {
      const slide = slides.find(x => x.slot === slot);
      return `<div class="sample-slot ${slide ? 'occupied' : 'empty'}"><div class="slot-no">${letter}-${String(slot).padStart(2,'0')}</div><div><b>${escapeHtml(slide ? slide.barcode : '未装载')}</b><span>${escapeHtml(slide ? slide.currentStep : '可上样 / 等待确认')}</span></div><em>${escapeHtml(slide ? slide.protocolCode : '--')}</em></div>`;
    }).join('');
    return `<div class="sample-column status-${channel.status}"><div class="column-handle"><span>${letter} 通道</span><b>${slides.length}/4</b></div>${slots}<small class="slot-order-note">显示顺序：4 在上，1 在下</small></div>`;
  }).join('');

  const select = document.getElementById('confirmSlot');
  if(select && select.options.length === 0){
    select.innerHTML = ['A','B','C','D'].flatMap(letter => [1,2,3,4].map(slot => `<option>${letter}-${String(slot).padStart(2,'0')}</option>`)).join('');
  }
}

function renderReagents(state){
  if(!document.getElementById('reagentDeck')) return;
  const reagents = state.reagents || [];
  setText('reagentBadge', `${reagents.length} 个 VALID`);
  const byPosition = new Map(reagents.map(x => [x.position, x]));
  const deck = document.getElementById('reagentDeck');
  deck.innerHTML = [1,2,3,4,5].map(col => {
    const rows = [1,2,3,4,5,6,7,8].map(row => {
      const pos = 'R' + (((col - 1) * 8) + row);
      const reagent = byPosition.get(pos);
      const scanState = reagent ? 'VALID' : (pos === 'R18' || pos === 'R32' ? 'INVALID' : 'EMPTY');
      const args = [pos, scanState, reagent?.barcode || '', reagent?.name || '无瓶/未识别', reagent?.code || '', reagent?.volumeMl || '', reagent?.lotNo || '', reagent?.expireDate || ''].map(x => `'${String(x).replaceAll("'", "\\'")}'`).join(',');
      return `<button type="button" class="vial ${reagent ? 'filled' : 'empty'} scan-${scanState.toLowerCase()} ${escapeHtml(reagent?.reagentType || '')}" onclick="showReagentDetail(${args})"><b>${pos}</b><div><span>${escapeHtml(reagent ? reagent.name : scanState)}</span><small>${escapeHtml(reagent ? reagent.code : '扫码状态 ' + scanState)}</small></div><em>${reagent ? reagent.volumeMl + 'mL' : '--'}</em></button>`;
    }).join('');
    return `<div class="reagent-rack"><header><b>ch${col}</b><span>R${(col-1)*8+1}-R${col*8}</span></header>${rows}</div>`;
  }).join('');

  const columnStatus = document.getElementById('columnStatus');
  if(columnStatus){
    columnStatus.innerHTML = [1,2,3,4,5].map(col => `<div><b>ch${col}</b><span>${reagents.length ? '完成' : '待扫描'}</span><em>R${(col-1)*8+1}-R${col*8}</em></div>`).join('');
  }
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
  const protocols = await api('/api/protocols');
  const dab = await api('/api/dab');
  document.getElementById('protocolTable').innerHTML = protocols.map(p => `<div class="protocol-version-row"><b>${escapeHtml(p.code)}</b><span>${escapeHtml(p.name)}</span><em>v${escapeHtml(p.version)}</em><small>${escapeHtml(p.description)}</small><div><button class="btn btn-soft">查看步骤</button><button class="btn btn-soft">停用</button></div></div>`).join('');
  document.getElementById('dabPreview').innerHTML = `<div><span>IHC 张数</span><b>${dab.slideCount}</b></div><div><span>总量</span><b>${dab.totalMl}</b><em>mL</em></div><div><span>A/B/水</span><b>${dab.dabAMl}/${dab.dabBMl}/${dab.pureWaterMl}</b></div>`;
  document.getElementById('catalogTable').innerHTML = '<div class="table-row head"><span>代码</span><span>名称</span><span>类别</span><span>保质期</span><span>液体类型</span></div><div class="table-row"><span>BLOCK</span><span>Block</span><span>common</span><span>12月</span><span>Ab</span></div><div class="table-row"><span>WASH</span><span>Wash</span><span>wash</span><span>12月</span><span>Wash</span></div>';
}

function renderTimeline(id, items, limit){
  const root = document.getElementById(id);
  if(!root) return;
  root.innerHTML = (items || []).slice(0, limit).map(item => `<div><i></i><span>${escapeHtml(item)}</span></div>`).join('') || '<div><i></i><span>暂无日志</span></div>';
}

async function scanSamples(){
  const count = document.getElementById('sampleCount')?.value || 8;
  await api('/api/samples/scan?count=' + count, {method:'POST'});
  toast('样本扫描完成，待操作员确认任务');
  await loadHostState();
}

function openConfirmModal(mode){
  const modal = document.getElementById('sampleConfirmModal');
  const title = document.getElementById('confirmTitle');
  if(title){
    title.textContent = mode === 'he' ? 'HE 玻片手动确认' : (mode === 'ihc-hospital' ? '医院码 / LIS 查询确认' : '通灵 IHC 一抗码确认');
  }
  modal?.classList.remove('hidden');
}

function closeConfirmModal(){
  document.getElementById('sampleConfirmModal')?.classList.add('hidden');
}

function confirmMockTask(){
  toast('Mock: 任务已确认并生成流程快照');
  closeConfirmModal();
}

async function scanReagents(){
  await api('/api/reagents/scan', {method:'POST'});
  toast('试剂扫描完成，已进入目录/有效期/余量校验');
  await loadHostState();
}

function mockColumnScan(col){
  toast('Mock: ch' + col + ' 列扫码完成，返回 8 个位置状态');
}

function showReagentDetail(pos,state,barcode,name,code,volume,lot,expire){
  const body = document.getElementById('reagentDetailBody');
  if(!body) return;
  const validFormat = barcode && barcode.length === 17 ? '17位格式有效' : 'Mock条码/待替换为17位Code128';
  body.innerHTML = `<div><span>位置</span><b>${escapeHtml(pos)}</b></div><div><span>SCAN_STATE</span><b>${escapeHtml(state)}</b></div><div><span>完整条码</span><b>${escapeHtml(barcode || '--')}</b><small>${validFormat}</small></div><div><span>试剂名称</span><b>${escapeHtml(name)}</b></div><div><span>试剂代码</span><b>${escapeHtml(code || '--')}</b></div><div><span>理论剩余量</span><b>${volume ? escapeHtml(volume) + ' mL' : '--'}</b></div><div><span>批号</span><b>${escapeHtml(lot || '--')}</b></div><div><span>有效期</span><b>${escapeHtml(expire || '--')}</b></div>`;
  document.getElementById('reagentDetail')?.classList.remove('hidden');
}

document.addEventListener('DOMContentLoaded', () => {
  loadHostState().then(renderConfigure);
  setInterval(loadHostState, 2500);
});
