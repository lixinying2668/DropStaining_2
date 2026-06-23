async function initializeSystem(){
  await api('/api/system/initialize', {method:'POST'});
  toast('初始化完成：机械臂、制冷、扫码器、液位、洗针均通过');
  setTimeout(()=>location.reload(), 500);
}
