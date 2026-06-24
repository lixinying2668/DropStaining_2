async function engineerCommand(module, action){
  const payload = { module, action, payload: {} };
  if(module === 'serial') payload.payload = {port: serialPort.value, baud_rate: baudRate.value};
  if(module === 'mixer') { payload.channel = Number(mixChannel.value); payload.duration_s = 5; }
  if(module === 'heater') { payload.channel = Number(heaterChannel.value); payload.temperature_c = Number(targetTemp.value); payload.payload = {mode:'single_or_board_batch'}; }
  if(module === 'pump' && action === 'stop') { payload.channel = Number(pumpChannel.value); payload.duration_s = 0; payload.payload = {pwm:0, direction:'stop'}; }
  const res = await api('/api/engineer/command', {method:'POST', body: JSON.stringify(payload)});
  document.getElementById('engineerResult').textContent = JSON.stringify(res, null, 2);
  toast('调试命令完成：' + module + ' / ' + action);
}
async function testPump(){
  const pwm = Number(pumpPwm.value);
  const direction = pwm > 0 ? 'forward' : (pwm < 0 ? 'reverse' : 'stop');
  const payload = { module:'pump', action:'wash', channel:Number(pumpChannel.value), duration_s:Number(pumpDuration.value), payload:{pwm, direction} };
  const res = await api('/api/engineer/command', {method:'POST', body: JSON.stringify(payload)});
  engineerResult.textContent = JSON.stringify(res, null, 2);
  toast('清洗泵测试完成：PWM ' + pwm + ' / ' + direction);
}
async function testPipette(){
  const payload = { module:'needle', action:pipetteAction.value, position:wellPosition.value, volume_ul:Number(pipetteVolume.value), payload:{needle:needleSelect.value} };
  const res = await api('/api/engineer/command', {method:'POST', body: JSON.stringify(payload)});
  engineerResult.textContent = JSON.stringify(res, null, 2);
  toast('移液测试完成');
}
