async function runAction(action){
  await api(`/api/run/${action}`, {method:'POST'});
  const label = {start:'开始', pause:'暂停', resume:'继续', stop:'终止'}[action] || action;
  toast('命令已下发：' + label);
  await refreshRun();
}
async function addSlide(){
  const payload = {
    channel: Number(addChannel.value), slot: Number(addSlot.value), barcode: addBarcode.value,
    protocol_code: (window.addProtocol ? addProtocol.value : 'IHC'), antibody_code: addAntibody.value, temperature_c: Number(addTemp.value)
  };
  await api('/api/run/add-slide', {method:'POST', body: JSON.stringify(payload)});
  toast('玻片已加入调度队列');
  await refreshRun();
}
function renderChannels(channels){
  const root = document.getElementById('runChannels');
  root.innerHTML = channels.map(ch => `
    <div class="runtime-channel ${ch.status}">
      <div class="runtime-head"><b>${ch.name}</b><span>${statusText(ch.status)}</span></div>
      <div class="progress-track"><span style="width:${ch.progress}%"></span></div>
      <p><b>${ch.progress}%</b> · ${ch.current_step || '空闲'}</p>
      <div class="runtime-slides">
        ${[1,2,3,4].map(slot => {
          const s = ch.slides.find(x=>x.slot===slot);
          return `<div class="runtime-slide ${s?'loaded':'empty'}"><b>${slot}</b><small>${s?s.barcode:'空位'}</small><em>${s?statusText(s.status):'EMPTY'}</em><small>${s?s.current_step:''}</small></div>`;
        }).join('')}
      </div>
    </div>`).join('');
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
