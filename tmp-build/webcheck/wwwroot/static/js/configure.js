function getSlideRoot(slideId){
  return document.querySelector(`[data-slide="${slideId}"]`);
}
async function saveSlide(slideId){
  const root = getSlideRoot(slideId);
  if(!root){
    toast('未找到玻片配置卡片', true);
    return;
  }
  const payload = {
    slide_id: slideId,
    protocol_code: root.querySelector('.protocol').value,
    antibody_code: root.querySelector('.antibody').value,
    primary_volume_ul: Number(root.querySelector('.volume').value),
    temperature_c: Number(root.querySelector('.temp').value)
  };
  await api('/api/slides/configure', {method:'POST', body: JSON.stringify(payload)});
  toast(`${slideId} 已保存：${payload.protocol_code} / ${payload.antibody_code} / ${payload.temperature_c}℃`);
}
function bulkFill(){
  document.querySelectorAll('.slide-config-card').forEach((card, idx)=>{
    card.querySelector('.protocol').value = 'IHC';
    card.querySelector('.antibody').value = idx % 2 ? 'AB-CK' : 'AB-DEFAULT';
    card.querySelector('.volume').value = idx % 3 === 0 ? 70 : 80;
    card.querySelector('.temp').value = idx % 4 === 0 ? 45 : 42;
  });
  toast('已填充默认 IHC 配置，需逐片保存');
}
