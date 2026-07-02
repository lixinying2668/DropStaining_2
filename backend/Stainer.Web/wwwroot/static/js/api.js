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
      const msg = data.detail || data.message || ('请求失败：' + res.status);
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
  el.textContent = message;
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
  const map = {idle:'空闲',initialized:'已初始化',ready:'就绪',running:'运行中',paused:'暂停',stopped:'已停止/待处理',completed:'完成',error:'故障',unknown:'Unknown / 待人工处理',empty:'可上样',loaded:'待确认',configured:'待启动',waiting:'等待/待卸载',dispensing:'加液',incubating:'孵育',washing:'通道清洗',mixing:'通道混匀'};
  return map[s] || s;
}

function syncStatusLabels(root=document){
  root.querySelectorAll('[data-status-label]').forEach(el=>{ el.textContent=statusText(el.textContent.trim()); });
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
