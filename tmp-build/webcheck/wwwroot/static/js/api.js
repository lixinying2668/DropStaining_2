async function api(url, options={}){
  const request = Object.assign({}, options);
  request.headers = Object.assign({'Content-Type':'application/json'}, options.headers || {});
  const method = String(request.method || 'GET').toUpperCase();
  const active = options.busyElement || (method !== 'GET' && document.activeElement instanceof HTMLButtonElement ? document.activeElement : null);
  delete request.busyElement;
  const priorDisabled = active?.disabled;
  if(active){
    active.disabled = true;
    active.setAttribute('aria-busy', 'true');
  }
  try{
    const res = await fetch(url, request);
    let data = null;
    const text = await res.text();
    try { data = text ? JSON.parse(text) : {}; } catch(e) { data = {raw:text}; }
    if(!res.ok){
      const msg = displayLogText(data.detail || data.message || ('请求失败：' + res.status));
      toast(msg, true);
      const error = new Error(msg);
      error.status = res.status;
      error.data = data;
      throw error;
    }
    return data;
  }finally{
    if(active){
      active.disabled = !!priorDisabled;
      active.removeAttribute('aria-busy');
    }
  }
}
function setButtonDisabledReason(button, disabled, reason=''){
  if(!button) return;
  button.disabled = !!disabled;
  button.title = disabled ? reason : '';
  button.setAttribute('aria-disabled', disabled ? 'true' : 'false');
}
function toast(message, danger=false){
  const el = document.getElementById('toast');
  if(!el){
    console[danger ? 'error' : 'log'](message);
    return;
  }
  el.textContent = displayLogText(message);
  el.classList.remove('hidden');
  el.style.background = danger ? '#991b1b' : '#07111f';
  setTimeout(()=> el.classList.add('hidden'), 2800);
}
function ensureOperatorDialog(){
  let modal = document.getElementById('operatorDialogModal');
  if(modal) return modal;
  modal = document.createElement('div');
  modal.id = 'operatorDialogModal';
  modal.className = 'modal-mask operator-dialog hidden';
  modal.innerHTML = `
    <div class="modal-card operator-dialog-card" role="dialog" aria-modal="true" aria-labelledby="operatorDialogTitle">
      <header><h2 id="operatorDialogTitle">确认操作</h2><button class="icon-btn" id="operatorDialogClose" type="button">x</button></header>
      <div class="operator-dialog-body">
        <p id="operatorDialogMessage"></p>
        <div class="notice-box hidden" id="operatorDialogWarning"></div>
        <label class="field-label hidden" id="operatorDialogInputWrap"><span id="operatorDialogInputLabel">原因</span><textarea class="input" id="operatorDialogInput" rows="3"></textarea></label>
      </div>
      <footer><button class="btn btn-soft" id="operatorDialogCancel" type="button">取消</button><button class="btn btn-primary" id="operatorDialogConfirm" type="button">确认</button></footer>
    </div>`;
  document.body.appendChild(modal);
  return modal;
}
function operatorConfirm(options){
  const config = typeof options === 'string' ? {message: options} : (options || {});
  const modal = ensureOperatorDialog();
  const title = document.getElementById('operatorDialogTitle');
  const message = document.getElementById('operatorDialogMessage');
  const warning = document.getElementById('operatorDialogWarning');
  const inputWrap = document.getElementById('operatorDialogInputWrap');
  const inputLabel = document.getElementById('operatorDialogInputLabel');
  const input = document.getElementById('operatorDialogInput');
  const cancel = document.getElementById('operatorDialogCancel');
  const confirmButton = document.getElementById('operatorDialogConfirm');
  const close = document.getElementById('operatorDialogClose');
  title.textContent = config.title || '确认操作';
  message.textContent = config.message || '';
  warning.textContent = config.warning || '';
  warning.classList.toggle('hidden', !config.warning);
  inputWrap.classList.toggle('hidden', !config.input && !config.reasonRequired && !config.reasonLabel);
  inputLabel.textContent = config.inputLabel || config.reasonLabel || '原因';
  input.value = config.defaultValue || '';
  input.placeholder = config.placeholder || '';
  cancel.textContent = config.cancelText || '取消';
  confirmButton.textContent = config.confirmText || '确认';
  confirmButton.classList.toggle('danger-action', !!config.danger);
  modal.classList.remove('hidden');
  if(config.input || config.reasonRequired || config.reasonLabel) setTimeout(()=>input.focus(), 0);
  return new Promise(resolve => {
    const cleanup = result => {
      modal.classList.add('hidden');
      cancel.onclick = confirmButton.onclick = close.onclick = null;
      document.removeEventListener('keydown', onKey);
      resolve(result);
    };
    const onKey = event => {
      if(event.key === 'Escape') cleanup({confirmed:false, value:null, reason:null});
      if(event.key === 'Enter' && (event.ctrlKey || event.metaKey)) confirmButton.click();
    };
    cancel.onclick = () => cleanup({confirmed:false, value:null, reason:null});
    close.onclick = cancel.onclick;
    confirmButton.onclick = () => {
      const value = input.value.trim();
      if((config.reasonRequired || config.inputRequired) && !value){
        toast(config.requiredMessage || '请填写原因。', true);
        input.focus();
        return;
      }
      cleanup({confirmed:true, value, reason:value});
    };
    document.addEventListener('keydown', onKey);
  });
}
async function operatorPrompt(message, defaultValue='', options={}){
  const result = await operatorConfirm(Object.assign({}, options, {
    title: options.title || '填写信息',
    message,
    input: true,
    defaultValue,
    confirmText: options.confirmText || '确认'
  }));
  return result.confirmed ? result.value : null;
}
async function logout(){
  try{ await api('/api/logout', {method:'POST'}); }catch(e){}
  location.href='/';
}
function initializeUserMenu(){
  const card = document.getElementById('operatorCard');
  const menu = document.getElementById('userMenu');
  const logoutButton = document.getElementById('logoutButton');
  if(!card || !menu || !logoutButton) return;

  const setOpen = open => {
    menu.classList.toggle('hidden', !open);
    card.setAttribute('aria-expanded', open ? 'true' : 'false');
  };
  const toggle = event => {
    event.preventDefault();
    event.stopPropagation();
    setOpen(menu.classList.contains('hidden'));
  };

  card.addEventListener('click', event => {
    if(event.target instanceof Element && event.target.closest('#logoutButton')) return;
    toggle(event);
  });
  card.addEventListener('keydown', event => {
    if(event.key === 'Enter' || event.key === ' ') toggle(event);
  });
  logoutButton.addEventListener('click', async event => {
    event.preventDefault();
    event.stopPropagation();
    logoutButton.disabled = true;
    await logout();
  });
  document.addEventListener('click', () => setOpen(false));
  document.addEventListener('keydown', event => {
    if(event.key === 'Escape') setOpen(false);
  });
}
function statusText(s){
  const map = {idle:'空闲',initialized:'已初始化',ready:'就绪',running:'运行中',paused:'暂停',stopped:'已停止/待处理',completed:'完成',error:'故障',unknown:'待人工处理',empty:'可上样',loaded:'待确认',configured:'待启动',waiting:'等待/待卸载',dispensing:'加液',incubating:'孵育',washing:'通道清洗',mixing:'通道混匀'};
  return map[s] || s;
}

function textToken(value){
  return String(value ?? '').trim();
}

function normalizedToken(value){
  return textToken(value).replace(/[\s_-]+/g, '').toLowerCase();
}

const uiStatusTextMap = {
  active:'活动中',
  acknowledged:'已确认',
  resolved:'已处理',
  cleared:'已清除',
  created:'已创建',
  pending:'等待中',
  planned:'已计划',
  commandsent:'命令已下发',
  deviceacknowledged:'设备已确认',
  running:'运行中',
  paused:'已暂停',
  stopped:'已停止',
  completed:'已完成',
  succeeded:'成功',
  failed:'失败',
  faulted:'故障',
  error:'错误',
  unknown:'待人工确认',
  timedout:'超时',
  timeout:'超时',
  waiting:'等待中',
  waitingunload:'等待卸载',
  pendingunload:'等待卸载',
  needsmanualresolution:'需要人工处理',
  locked:'已锁定',
  selected:'已选择',
  unselected:'未选择',
  frozen:'已冻结',
  ready:'就绪',
  initialized:'已初始化',
  idle:'空闲',
  empty:'空位',
  loaded:'已装载',
  configured:'已配置',
  available:'可用',
  depleted:'已耗尽',
  expired:'已过期',
  low:'偏低',
  full:'已满',
  draft:'草稿',
  published:'已发布',
  retired:'已停用',
  enabled:'已启用',
  disabled:'已禁用',
  preparing:'制备中',
  pendingpreparation:'等待制备',
  awaitingcleaning:'等待清洗',
  cleaned:'已清洗',
  legacyunverified:'旧数据未验证',
  pass:'通过',
  warning:'警告',
  fail:'失败',
  critical:'严重',
  information:'信息',
  info:'信息',
  ok:'正常',
  block:'阻断'
};

function displayStatusText(value){
  const raw = textToken(value);
  if(!raw) return '--';
  const mapped = uiStatusTextMap[normalizedToken(raw)];
  if(mapped) return mapped;
  const lower = raw.toLowerCase();
  const existing = typeof statusText === 'function' ? statusText(lower) : lower;
  return existing && existing !== lower ? existing : raw;
}

function severityText(value){
  return displayStatusText(value);
}

const uiActionTextMap = {
  'alarm.acknowledge':'确认告警',
  acknowledged:'已确认',
  resolved:'已处理',
  'auth.login':'用户登录',
  'auth.logout':'用户退出',
  'channel.workflow.select':'选择通道流程',
  'channel.experiment_type.select':'选择实验类型',
  'channel.experiment_type.change':'变更实验类型',
  'channel_batch.ensure_active':'建立通道批次',
  'task.create_he':'创建 HE 任务',
  'task.create_ihc':'创建 IHC 任务',
  'run.create':'创建运行',
  'run.start':'启动运行',
  'run.pause':'暂停运行',
  'run.resume':'恢复运行',
  'run.stop':'停止运行',
  'run.fault':'记录运行故障',
  'run.redo_current_major_step':'大步骤重做',
  'workflow.create':'创建流程',
  'workflow.version.create':'创建流程版本',
  'workflow.version.copy_draft':'复制草稿版本',
  'workflow.version.update':'更新流程版本',
  'workflow.version.publish':'发布流程版本',
  'workflow.version.retire':'停用流程版本',
  'workflow.default.set':'设置默认流程',
  'workflow.default.unset':'取消默认流程',
  'workflow.step.create':'新增流程步骤',
  'workflow.step.update':'更新流程步骤',
  'workflow.step.delete':'删除流程步骤',
  'workflow.step.move_up':'上移流程步骤',
  'workflow.step.move_down':'下移流程步骤',
  'workflow.reagent_requirement.create':'新增试剂需求',
  'workflow.reagent_requirement.update':'更新试剂需求',
  'workflow.reagent_requirement.delete':'删除试剂需求',
  'workflow.reagent_requirement.recalculate':'重算试剂需求',
  'primary_antibody_mapping.create':'新增一抗映射',
  'primary_antibody_mapping.enable':'启用一抗映射',
  'primary_antibody_mapping.disable':'停用一抗映射',
  'reagent.scan_session.start':'开始试剂扫码',
  'reagent.scan_session.complete':'完成试剂扫码',
  'reagent.scan_confirm':'确认试剂扫码',
  'sample.mock_scan':'样本扫码',
  'database.backup':'数据库备份',
  'database.backup_degraded':'数据库降级备份',
  'database.backup_attempt_cleanup_failed':'数据库备份临时目录清理待维护',
  'database.restore_request':'数据库恢复请求',
  'device.initialization':'设备初始化',
  'device.fault.configure':'配置 Mock 故障',
  'device.fault.clear':'清除 Mock 故障',
  'engineering.session.start':'工程二次认证',
  'engineering.session.revoke':'撤销工程二次认证',
  'engineering.coordinate.create_version':'创建坐标版本',
  'engineering.coordinate.publish':'发布坐标版本',
  'engineering.coordinate.activate':'启用坐标版本',
  'engineering.coordinate.deactivate':'停用坐标版本',
  'engineering.liquid_class.create_version':'创建 Liquid Class 版本',
  'engineering.liquid_class.publish':'发布 Liquid Class 版本',
  'engineering.liquid_class.enable':'启用 Liquid Class 版本',
  'engineering.liquid_class.disable':'停用 Liquid Class 版本',
  'engineering.config.import':'导入工程配置',
  'export.csv':'导出 CSV',
  'mock_demo.seed':'生成 Mock 演示数据',
  'mock_demo.reset':'重置 Mock 演示数据',
  scan:'扫码',
  dispense:'加液',
  wash:'清洗',
  mix:'混匀',
  heat:'加热',
  dab:'DAB 显色',
  initialize:'初始化',
  start:'启动',
  pause:'暂停',
  resume:'恢复',
  stop:'停止',
  publish:'发布',
  activate:'启用',
  deactivate:'停用',
  enable:'启用',
  disable:'停用'
};

function actionText(value){
  const raw = textToken(value);
  if(!raw) return '--';
  const mapped = uiActionTextMap[raw] || uiActionTextMap[raw.toLowerCase()] || uiActionTextMap[normalizedToken(raw)];
  if(mapped) return mapped;
  return raw.replace(/[._-]+/g, ' ');
}

const uiEntityTypeTextMap = {
  AuditLog:'审计记录',
  Alarm:'告警',
  AlarmAction:'告警处理',
  ChannelBatch:'通道批次',
  StainingTask:'染色任务',
  SlideTask:'玻片任务',
  MachineRun:'运行批次',
  DeviceCommandExecution:'设备命令',
  DeviceCommunicationRecord:'通讯记录',
  WorkflowVersion:'流程版本',
  WorkflowStep:'流程步骤',
  WorkflowReagentRequirement:'流程试剂需求',
  PrimaryAntibodyWorkflowMapping:'一抗流程映射',
  ReagentBottle:'试剂瓶',
  ReagentScanSession:'试剂扫码会话',
  DatabaseBackup:'数据库备份',
  TraceabilityExport:'追溯导出',
  User:'用户',
  MockDemoData:'Mock 演示数据'
};

function entityTypeText(value){
  const raw = textToken(value);
  return uiEntityTypeTextMap[raw] || raw || '--';
}

const uiFieldTextMap = {
  commandId:'命令号',
  correlationId:'关联号',
  reason:'原因',
  target:'目标',
  runId:'运行',
  machineRunId:'运行',
  channelBatchId:'通道批次',
  drawerCode:'通道',
  drawer:'通道',
  channel:'通道',
  slotCode:'位置',
  slot:'位置',
  taskId:'任务',
  slideTaskId:'玻片任务',
  workflowVersionId:'流程版本',
  workflowVersion:'流程版本',
  workflowVersionNo:'流程版本号',
  workflowVersionLabel:'流程版本标签',
  experimentType:'实验类型',
  status:'状态',
  before:'变更前',
  after:'变更后',
  actor:'操作人',
  username:'用户名',
  displayName:'显示名',
  roles:'角色',
  enabled:'启用',
  action:'操作',
  entityType:'对象类型',
  entityId:'对象',
  backupPath:'备份文件',
  backupMethod:'备份方式',
  backup:'备份',
  attemptDirectory:'临时目录',
  rowCount:'行数',
  exportType:'导出类型',
  filters:'筛选条件',
  reagentCode:'试剂代码',
  primaryAntibodyCode:'一抗代码',
  selectedPrimaryAntibodyCode:'选择的一抗',
  rawCode:'原始码',
  normalizedCode:'规范码',
  message:'消息',
  errorCode:'错误码',
  severity:'级别',
  source:'来源',
  module:'模块',
  moduleCode:'模块',
  deviceStatus:'设备状态',
  persistenceStatus:'持久化状态',
  persistenceFailureReason:'持久化失败原因'
};

function fieldText(key){
  return uiFieldTextMap[key] || key;
}

function commandTypeText(value){
  const raw = textToken(value);
  const map = {
    Dispense:'加液',
    Wash:'清洗',
    Mix:'混匀',
    Heat:'加热',
    Dab:'DAB 显色',
    Scan:'扫码',
    Initialize:'初始化',
    Move:'移动',
    Pipette:'移液',
    Cool:'制冷',
    Mock:'Mock 命令'
  };
  return map[raw] || actionText(raw);
}

function moduleText(value){
  const raw = textToken(value);
  const key = raw.toLowerCase();
  const map = {
    controller:'主控',
    cooling:'制冷',
    'sample-scanner':'样本扫码器',
    samplescanner:'样本扫码器',
    'reagent-scanner':'试剂扫码器',
    reagentscanner:'试剂扫码器',
    'robot-arm':'机械臂',
    robotarm:'机械臂',
    'liquid-level':'液位',
    liquidlevel:'液位',
    'needle-wash':'洗针',
    needlewash:'洗针',
    fluidics:'液路',
    motion:'运动',
    thermal:'温控',
    dab:'DAB'
  };
  return map[key] || raw || '--';
}

function displayJsonValue(value, key=''){
  if(value === null || value === undefined || value === '') return '--';
  if(typeof value === 'boolean') return value ? '是' : '否';
  if(typeof value === 'number') return String(value);
  if(Array.isArray(value)) return value.length ? value.map(x => displayJsonValue(x)).join('、') : '无';
  if(typeof value === 'object') return displayJsonSummary(value);
  const text = String(value);
  if(key.toLowerCase().includes('status') || key === 'severity') return displayStatusText(text);
  if(key.toLowerCase().includes('action')) return actionText(text);
  if(key === 'entityType') return entityTypeText(text);
  return displayLogText(text);
}

function displayJsonSummary(value, context={}){
  const source = value && typeof value === 'object' ? value : {};
  const keys = Object.keys(source).filter(key => {
    const item = source[key];
    return item !== null && item !== undefined && typeof item !== 'object';
  });
  const preferred = [
    'reason','commandId','correlationId','status','drawerCode','slotCode','experimentType',
    'workflowVersionId','primaryAntibodyCode','selectedPrimaryAntibodyCode','reagentCode',
    'runId','machineRunId','taskId','slideTaskId','backupPath','rowCount','exportType'
  ];
  const selected = preferred.filter(key => keys.includes(key)).concat(keys.filter(key => !preferred.includes(key))).slice(0, 8);
  const details = selected.map(key => `${fieldText(key)}：${displayJsonValue(source[key], key)}`);
  const prefix = context.action ? actionText(context.action) : '';
  if(prefix && details.length) return `${prefix}；${details.join('；')}`;
  if(prefix) return prefix;
  return details.length ? details.join('；') : '日志明细已记录';
}

function tryJsonSummary(text, context={}){
  const raw = textToken(text);
  if(!raw || !/^[\[{]/.test(raw)) return null;
  try{
    const value = JSON.parse(raw);
    return Array.isArray(value)
      ? (value.length ? value.map(item => displayJsonSummary(item, context)).join('；') : '空列表')
      : displayJsonSummary(value, context);
  }catch(e){
    return null;
  }
}

const uiLogPhrasePairs = [
  ['ASP.NET Core web host started', 'Web 服务已启动'],
  ['User login', '用户登录'],
  ['User logout', '用户退出'],
  ['System initialized: arm, cooling, scanner, liquid sensor and needle wash are ready', '系统已初始化：机械臂、制冷、扫码器、液位传感器和洗针均就绪'],
  ['Runtime state reset', '运行状态已重置'],
  ['Sample area scan completed', '样本区扫码完成'],
  ['Reagent rack scan completed', '试剂架扫码完成'],
  ['Slide configured', '玻片已配置'],
  ['Run command', '运行命令'],
  ['Mock command executed', 'Mock 命令已执行'],
  ['Mock running', 'Mock 运行中'],
  ['Waiting for confirmation', '等待确认'],
  ['Waiting for slides', '等待放入玻片'],
  ['Waiting unload', '等待卸载'],
  ['No active batch', '无活动批次'],
  ['No events', '暂无事件'],
  ['Database backup completed.', '数据库备份已完成。'],
  ['Database backup was created but failed integrity check.', '数据库备份已创建，但完整性检查未通过。'],
  ['Database backup failed integrity check.', '数据库备份完整性检查未通过。'],
  ['Database restore requested. Stop the service and restore offline before restart.', '数据库恢复请求已记录；请停止服务后离线恢复，再重新启动。'],
  ['Restore request was audited. Stop the service and restore the verified backup offline before restart.', '恢复请求已审计；请停止服务后离线恢复已验证备份，再重新启动。'],
  ['Alarm acknowledged.', '告警已确认。'],
  ['HE task created.', 'HE 任务已创建。'],
  ['IHC task created.', 'IHC 任务已创建。'],
  ['Run created.', '运行批次已创建。'],
  ['Run started.', '运行已启动。'],
  ['Run paused.', '运行已暂停。'],
  ['Run resumed.', '运行已恢复。'],
  ['Run stopped.', '运行已停止。'],
  ['Workflow draft created.', '流程草稿已创建。'],
  ['Workflow draft version created.', '流程草稿版本已创建。'],
  ['Workflow version copied as draft.', '流程版本已复制为草稿。'],
  ['Workflow version published.', '流程版本已发布。'],
  ['Workflow version retired.', '流程版本已停用。'],
  ['Workflow version is already the current default.', '该流程版本已经是当前默认流程。'],
  ['Primary antibody mapping enabled.', '一抗映射已启用。'],
  ['Primary antibody mapping disabled.', '一抗映射已停用。'],
  ['Primary antibody mapping created.', '一抗映射已创建。'],
  ['Mock demo data seeded.', 'Mock 演示数据已生成。'],
  ['Mock demo data reset completed.', 'Mock 演示数据已重置。'],
  ['Sample scanner Mock result recorded.', '样本扫码 Mock 结果已记录。'],
  ['Reagent scan session started.', '试剂扫码会话已开始。'],
  ['Reagent scan session completed.', '试剂扫码会话已完成。'],
  ['Reagent scan confirmation saved.', '试剂扫码确认已保存。'],
  ['LIS lookup returned no primary antibody code.', 'LIS 未返回可用的一抗代码。'],
  ['Multiple primary antibody codes were found. Operator selection is required.', 'LIS 返回多个一抗代码，需要操作员选择。'],
  ['Select the final primary antibody, then confirm again.', '请选择最终一抗后再次确认。'],
  ['LIS query did not return a usable candidate.', 'LIS 查询未返回可用候选。'],
  ['Device initialization completed.', '设备初始化已完成。'],
  ['Device initialization failed.', '设备初始化失败。'],
  ['Real fail-closed', 'Real 模式保持关闭保护'],
  ['Planned → CommandSent → DeviceAcknowledged → Completed / Failed / Unknown', '已计划 → 命令已下发 → 设备已确认 → 已完成 / 失败 / 待人工确认']
];

function displayLogText(value, context={}){
  const raw = textToken(value);
  if(!raw) return '--';
  const jsonSummary = tryJsonSummary(raw, context);
  if(jsonSummary) return jsonSummary;
  let text = raw;
  for(const [from, to] of uiLogPhrasePairs){
    text = text.replaceAll(from, to);
  }
  text = text.replace(/\[([A-Za-z]+)\]/g, (_, severity) => `【${severityText(severity)}】`);
  text = text.replace(/\b(Active|Acknowledged|Resolved|Cleared|Created|Pending|Planned|CommandSent|DeviceAcknowledged|Running|Paused|Stopped|Completed|Succeeded|Failed|Faulted|Unknown|TimedOut|Waiting|WaitingUnload|PendingUnload|NeedsManualResolution|Locked|Selected|Unselected|Frozen|Ready|Initialized|Idle|Empty|Loaded|Configured|Available|Depleted|Expired|Low|Full|Draft|Published|Retired|Enabled|Disabled|Preparing|PendingPreparation|AwaitingCleaning|Cleaned|LegacyUnverified|Pass|Fail|Warning|Critical|Information|Info|Error)\b/g, match => displayStatusText(match));
  text = text.replace(/\b(Dispense|Wash|Mix|Heat|Dab|Scan|Initialize)\b/g, match => commandTypeText(match));
  text = text.replace(/Initialization\/([^\]\s]+)/g, (_, status) => `初始化/${displayStatusText(status)}`);
  return text;
}

function auditLogText(item){
  if(!item) return '--';
  const pieces = [
    actionText(item.action),
    entityTypeText(item.entityType)
  ];
  if(item.channel) pieces.push(`通道 ${item.channel}`);
  if(item.slot) pieces.push(`位置 ${item.slot}`);
  if(item.reason) pieces.push(`原因：${displayLogText(item.reason)}`);
  if(item.commandId) pieces.push(`命令号：${item.commandId}`);
  const summary = displayLogText(item.summary || item.message || '', {action:item.action});
  if(summary && summary !== '--' && !pieces.includes(summary)) pieces.push(summary);
  return pieces.filter(Boolean).join(' · ');
}

function alarmDisplayText(alarm){
  if(!alarm) return '--';
  return `${severityText(alarm.severity || '')} · ${displayLogText(alarm.code || '--')} · ${displayLogText(alarm.message || '')}`;
}

function alarmActionLogText(entry){
  if(!entry) return '--';
  const alarm = entry.alarm || {};
  const action = entry.action || {};
  return `${displayLogText(alarm.code || '--')} · ${actionText(action.action)} · ${displayLogText(action.actor || '--')} · ${displayLogText(action.message || '--')}`;
}

function eventDisplayText(event){
  if(!event) return '--';
  return [event.title, event.detail, event.status || event.type]
    .filter(Boolean)
    .map(x => displayLogText(x))
    .join(' · ');
}

function syncStatusLabels(root=document){
  root.querySelectorAll('[data-status-label]').forEach(el=>{ el.textContent=displayStatusText(el.textContent.trim()); });
}
function updateClock(){
  const t = document.getElementById('clockTime');
  const d = document.getElementById('clockDate');
  if(!t || !d) return;
  const now = new Date();
  t.textContent = now.toLocaleTimeString('zh-CN',{hour12:false,hour:'2-digit',minute:'2-digit'});
  d.textContent = now.toLocaleDateString('zh-CN',{month:'2-digit',day:'2-digit',weekday:'short'});
}
function markActiveNav(){
  const path = location.pathname || '/control-console';
  document.querySelectorAll('.nav-item').forEach(a=>a.classList.toggle('active', a.dataset.href === path));
}
document.addEventListener('DOMContentLoaded',()=>{ syncStatusLabels(); updateClock(); setInterval(updateClock,1000); markActiveNav(); initializeUserMenu(); });
