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
document.addEventListener('DOMContentLoaded',()=>{ refreshRun(); setInterval(refreshRun, 1400); });
