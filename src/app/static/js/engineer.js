async function engineerCommand(module, action){
  const payload = { module, action, payload: {} };
  if(module === 'serial') payload.payload = {port: serialPort.value, baud_rate: baudRate.value};
  if(module === 'mixer') { payload.channel = Number(mixChannel.value); payload.duration_s = 5; }
  if(module === 'heater') { payload.channel = Number(mixChannel.value); payload.temperature_c = Number(targetTemp.value); }
  const res = await api('/api/engineer/command', {method:'POST', body: JSON.stringify(payload)});
  document.getElementById('engineerResult').textContent = JSON.stringify(res, null, 2);
  toast('调试命令完成：' + module + ' / ' + action);
}
async function testPump(){
  const payload = { module:'pump', action:'wash', channel:Number(pumpChannel.value), duration_s:Number(pumpDuration.value) };
  const res = await api('/api/engineer/command', {method:'POST', body: JSON.stringify(payload)});
  engineerResult.textContent = JSON.stringify(res, null, 2);
  toast('清洗泵测试完成');
}
async function testPipette(){
  const payload = { module:'needle', action:pipetteAction.value, position:wellPosition.value, volume_ul:Number(pipetteVolume.value) };
  const res = await api('/api/engineer/command', {method:'POST', body: JSON.stringify(payload)});
  engineerResult.textContent = JSON.stringify(res, null, 2);
  toast('移液测试完成');
}
