async function api(url, options={}){
  options.headers = Object.assign({'Content-Type':'application/json'}, options.headers || {});
  const res = await fetch(url, options);
  let data = null;
  const text = await res.text();
  try { data = text ? JSON.parse(text) : {}; } catch(e) { data = {raw:text}; }
  if(!res.ok){
    const msg = data.detail || data.message || ('请求失败：' + res.status);
    toast(msg, true);
    throw new Error(msg);
  }
  return data;
}
function toast(message, danger=false){
  const el = document.getElementById('toast');
  if(!el) return alert(message);
  el.textContent = message;
  el.classList.remove('hidden');
  el.style.background = danger ? '#991b1b' : '#07111f';
  setTimeout(()=> el.classList.add('hidden'), 2800);
}
async function logout(){
  try{ await api('/api/logout', {method:'POST'}); }catch(e){}
  location.href='/';
}
function statusText(s){
  const map = {idle:'空闲',initialized:'已初始化',ready:'就绪',running:'运行中',paused:'暂停',stopped:'已停止/待处理',completed:'完成',error:'故障',empty:'可上样',loaded:'待确认',configured:'待启动',waiting:'等待/待卸载',dispensing:'加液',incubating:'孵育',washing:'通道清洗',mixing:'通道混匀'};
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
  const path = location.pathname || '/dashboard';
  document.querySelectorAll('.nav-item').forEach(a=>a.classList.toggle('active', a.dataset.href === path));
}
document.addEventListener('DOMContentLoaded',()=>{ syncStatusLabels(); updateClock(); setInterval(updateClock,1000); markActiveNav(); });
