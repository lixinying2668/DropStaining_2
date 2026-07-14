let engineeringCoordinateProfiles = [];
let engineeringLiquidClasses = [];

function engineeringReason(){ return document.getElementById('engineeringReason')?.value?.trim() || '工程配置维护'; }
function engineeringWriteBody(target, extra={}){ return {commandId:commandId('engineering'), reason:engineeringReason(), target, dangerousOperationConfirmed:false, ...extra}; }

async function loadEngineeringSession(){
  const badge = document.getElementById('engineeringSessionBadge');
  try{
    const session = await api('/api/engineering/session');
    if(badge) badge.textContent = session.status === 'Active' ? '已认证' : displayStatusText(session.status);
    setText('engineeringSessionExpiry', formatDateTime(session.expiresAtUtc));
  }catch(e){
    if(badge) badge.textContent = e.status === 404 ? '未认证' : '读取失败';
    setText('engineeringSessionExpiry', '--');
  }
}

async function startEngineeringSession(){
  const password = document.getElementById('engineeringPassword')?.value || '';
  const target = document.getElementById('engineeringTarget')?.value?.trim() || 'engineering-console';
  const durationMinutes = Number(document.getElementById('engineeringDuration')?.value || 15);
  await api('/api/engineering/session', {method:'POST', body:JSON.stringify({commandId:commandId('engineering-session'), password, reason:engineeringReason(), target, durationMinutes})});
  document.getElementById('engineeringPassword').value = '';
  toast('工程二次认证已生效。');
  await loadEngineeringSession();
}

async function revokeEngineeringSession(){
  await api('/api/engineering/session/revoke', {method:'POST', body:JSON.stringify(engineeringWriteBody('engineering-console'))});
  toast('工程会话已结束。');
  await loadEngineeringSession();
}

async function runEngineeringAdapterTest(){
  const mode = String(document.getElementById('engineeringDeviceMode')?.textContent || '');
  if(mode.toLowerCase() === 'real' && !confirm('Real 模式会保持 fail-closed；确认发起只读初始化检查？')) return;
  const result = await api('/api/device-initialization', {method:'POST', body:JSON.stringify({commandId:commandId('engineering-adapter-test')})});
  document.getElementById('engineeringDeviceState').textContent = JSON.stringify(result, null, 2);
  toast(result.ok ? '适配器测试完成。' : '适配器测试未通过。', !result.ok);
  await loadEngineeringDiagnostics();
}

function engineeringTable(rootId, head, rows, empty){
  const root = document.getElementById(rootId);
  if(!root) return;
  root.innerHTML = `<div class="table-row head">${head.map(x=>`<span>${escapeHtml(x)}</span>`).join('')}</div>` + (rows.join('') || `<div class="empty-state"><b>${escapeHtml(empty)}</b><span>正式数据源暂无记录。</span></div>`);
}

async function loadEngineeringDiagnostics(){
  const [device, commands, errors, communications] = await Promise.all([
    api('/api/engineering/diagnostics/device-state'),
    api('/api/engineering/diagnostics/command-log?pageSize=50'),
    api('/api/engineering/diagnostics/errors?pageSize=50'),
    api('/api/engineering/diagnostics/mock-communications?pageSize=50')
  ]);
  const terminal = document.getElementById('engineeringDeviceState');
  if(terminal) terminal.textContent = JSON.stringify(device, null, 2);
  engineeringTable('engineeringCommandLog', ['时间','命令','状态','运行','版本'], (commands.items || []).map(x => `<div class="table-row"><span>${escapeHtml(formatDateTime(x.createdAtUtc))}</span><span>${escapeHtml(commandTypeText(x.commandType))}</span><span>${escapeHtml(displayStatusText(x.status))}</span><span>${escapeHtml(x.machineRunId || '--')}</span><span>${escapeHtml(x.liquidClassVersionId || '--')}</span></div>`), '暂无命令记录');
  engineeringTable('engineeringErrors', ['时间','来源','代码','状态','运行'], (errors.items || []).map(x => `<div class="table-row"><span>${escapeHtml(formatDateTime(x.createdAtUtc))}</span><span>${escapeHtml(entityTypeText(x.sourceType))}</span><span title="${escapeHtml(displayLogText(x.message))}">${escapeHtml(displayLogText(x.code))}</span><span>${escapeHtml(displayStatusText(x.status))}</span><span>${escapeHtml(x.machineRunId || '--')}</span></div>`), '暂无错误码');
  engineeringTable('engineeringCommunications', ['时间','模块/动作','设备状态','持久化','报文'], (communications.items || []).map(x => `<div class="table-row"><span>${escapeHtml(formatDateTime(x.createdAtUtc))}</span><span>${escapeHtml(moduleText(x.moduleCode))} / ${escapeHtml(actionText(x.action))}<small>${escapeHtml(x.adapterName)}</small></span><span>${escapeHtml(displayStatusText(x.status))}<small>${escapeHtml(displayLogText(x.errorCode || ''))}</small></span><span>${escapeHtml(displayStatusText(x.persistenceStatus))}<small>${escapeHtml(displayLogText(x.persistenceFailureReason || ''))}</small></span><span><button class="btn btn-soft" data-communication-id="${escapeHtml(x.id)}">查看技术报文</button></span></div>`), '暂无 Mock 通讯记录');
  window.engineeringCommunications = communications.items || [];
  document.querySelectorAll('[data-communication-id]').forEach(button => button.onclick = () => showEngineeringCommunication(button.dataset.communicationId));
}

function showEngineeringCommunication(id){
  const value = (window.engineeringCommunications || []).find(x => x.id === id);
  document.getElementById('engineeringDeviceState').textContent = JSON.stringify(value || {}, null, 2);
}

function renderCoordinateVersionsLegacy(){
  const rows = engineeringCoordinateProfiles.flatMap(profile => (profile.versions || []).map(version => `<div class="table-row"><span><b>${escapeHtml(profile.code)}</b><small>${escapeHtml(profile.name)}</small></span><span>${escapeHtml(version.versionLabel)}</span><span>${escapeHtml(displayStatusText(version.status))}${version.isActive ? '<small>已启用</small>' : ''}</span><span>${escapeHtml(displayStatusText(version.verificationStatus || '--'))}</span><span class="button-row"><button class="btn btn-soft" onclick="showCoordinateDiff('${version.id}')">差异</button>${version.status === 'Draft' ? `<button class="btn btn-soft" onclick="coordinateVersionAction('${version.id}','publish')">发布</button>` : ''}${version.status === 'Published' && !version.isActive ? `<button class="btn btn-soft" onclick="coordinateVersionAction('${version.id}','activate')">启用</button>` : ''}${version.isActive ? `<button class="btn btn-soft" onclick="coordinateVersionAction('${version.id}','deactivate')">停用</button>` : ''}</span></div>`));
  engineeringTable('coordinateVersionTable', ['配置','版本','状态','验证','操作'], rows, '暂无坐标版本');
}

function parseEngineeringJson(value){
  try{ return JSON.parse(value || '{}'); }catch(_){ return {}; }
}

function coordinateDigitalTwinSummary(version){
  const summary = parseEngineeringJson(version.changeSummaryJson);
  const validation = parseEngineeringJson(version.validationResultJson);
  const source = summary.importKind === 'DigitalTwinCoordinateImport' ? summary.source || {} : null;
  const targetPoints = version.targetPoints || [];
  const executable = targetPoints.filter(x => x.isEnabled && x.pointType !== 'ReferenceOnly');
  const ready = executable.filter(x => x.calibratedXUm != null && x.calibratedYUm != null && x.calibratedZUm != null && x.safeZUm != null && x.aspirateZUm != null && x.dispenseZUm != null && x.actionOffsetXUm != null && x.actionOffsetYUm != null && x.actionOffsetZUm != null && !x.requiresCalibration && x.validationStatus === 'Validated');
  const realReady = validation.xyImported === true && validation.requiredHeightsComplete === true && validation.calibrationVerified === true && validation.safetyParametersComplete === true && validation.speedLimitsConfigured === true && validation.accelerationLimitsConfigured === true && validation.softLimitsConfigured === true;
  const sample = (summary.rowDispositions || []).slice(0, 4).map(x => `${x.label || x.csvName || x.row}->${x.target || x.disposition}`).join(', ');
  return {
    sourceText: source ? `${source.mappingVersion || 'DigitalTwinXY'} / ${source.fileName || '--'} / ${String(source.sha256 || '').slice(0, 12)}` : 'Legacy / manual',
    directionText: source ? 'X+ left, Y+ down, CSV mm -> DB um' : '--',
    completionText: `${ready.length}/${executable.length || targetPoints.length} height+safety complete`,
    realText: realReady ? 'Real ready' : 'Mock only / Real blocked',
    mappingText: sample || '--'
  };
}

function renderCoordinateVersions(){
  const rows = engineeringCoordinateProfiles.flatMap(profile => (profile.versions || []).map(version => {
    const twin = coordinateDigitalTwinSummary(version);
    return `<div class="table-row"><span><b>${escapeHtml(profile.code)}</b><small>${escapeHtml(profile.name)}</small><small>${escapeHtml(twin.sourceText)}</small></span><span>${escapeHtml(version.versionLabel)}<small>${escapeHtml(twin.directionText)}</small><small>${escapeHtml(twin.mappingText)}</small></span><span>${escapeHtml(displayStatusText(version.status))}${version.isActive ? '<small>Active</small>' : ''}<small>${escapeHtml(twin.completionText)}</small></span><span>${escapeHtml(displayStatusText(version.verificationStatus || '--'))}<small>${escapeHtml(twin.realText)}</small></span><span class="button-row"><button class="btn btn-soft" onclick="showCoordinateDiff('${version.id}')">Diff</button>${version.status === 'Draft' ? `<button class="btn btn-soft" onclick="coordinateVersionAction('${version.id}','publish')">Publish</button>` : ''}${version.status === 'Published' && !version.isActive ? `<button class="btn btn-soft" onclick="coordinateVersionAction('${version.id}','activate')">Activate</button>` : ''}${version.isActive ? `<button class="btn btn-soft" onclick="coordinateVersionAction('${version.id}','deactivate')">Deactivate</button>` : ''}</span></div>`;
  }));
  engineeringTable('coordinateVersionTable', ['Profile','Version','Status','Real Gate','Actions'], rows, 'No coordinate versions');
}

function renderLiquidClassVersions(){
  const rows = engineeringLiquidClasses.flatMap(profile => (profile.versions || []).map(version => `<div class="table-row"><span><b>${escapeHtml(profile.code)}</b><small>${escapeHtml(version.name || profile.name)}</small></span><span>${escapeHtml(version.versionLabel)}</span><span>${escapeHtml(displayStatusText(version.status))}</span><span>${version.aspirateSpeedUlPerSecond} / ${version.dispenseSpeedUlPerSecond}<small>μL/s</small></span><span class="button-row"><button class="btn btn-soft" onclick="showLiquidClassDiff('${version.id}')">差异</button>${version.status === 'Draft' ? `<button class="btn btn-soft" onclick="liquidClassVersionAction('${version.id}','publish')">发布</button>` : ''}${version.status === 'Published' ? `<button class="btn btn-soft" onclick="liquidClassVersionAction('${version.id}','enable')">启用</button>` : ''}${version.status === 'Enabled' ? `<button class="btn btn-soft" onclick="liquidClassVersionAction('${version.id}','disable')">停用</button>` : ''}</span></div>`));
  engineeringTable('liquidClassVersionTable', ['类型','版本','状态','吸/排速度','操作'], rows, '暂无 Liquid Class 版本');
}

async function loadEngineeringConfig(){
  [engineeringCoordinateProfiles, engineeringLiquidClasses] = await Promise.all([api('/api/engineering/coordinate-profiles'), api('/api/engineering/liquid-classes')]);
  renderCoordinateVersions(); renderLiquidClassVersions();
}

async function showCoordinateDiff(id){ document.getElementById('engineeringConfigResult').textContent = JSON.stringify(await api(`/api/engineering/coordinate-profile-versions/${encodeURIComponent(id)}/diff`), null, 2); }
async function showLiquidClassDiff(id){ document.getElementById('engineeringConfigResult').textContent = JSON.stringify(await api(`/api/engineering/liquid-class-versions/${encodeURIComponent(id)}/diff`), null, 2); }

async function coordinateVersionAction(id, action){
  const extra = action === 'publish' ? {validationResultJson:JSON.stringify({status:'Passed', source:'engineering-page'})} : {};
  await api(`/api/engineering/coordinate-profile-versions/${encodeURIComponent(id)}/${action}`, {method:'POST', body:JSON.stringify(engineeringWriteBody(`coordinate-version:${id}`, extra))});
  toast(`坐标版本已${({publish:'发布',activate:'启用',deactivate:'停用'})[action]}。`); await loadEngineeringConfig();
}

async function liquidClassVersionAction(id, action){
  await api(`/api/engineering/liquid-class-versions/${encodeURIComponent(id)}/${action}`, {method:'POST', body:JSON.stringify(engineeringWriteBody(`liquid-class-version:${id}`))});
  toast(`Liquid Class 版本已${({publish:'发布',enable:'启用',disable:'停用'})[action]}。`); await loadEngineeringConfig();
}

async function createCoordinateVersion(){
  const sourceId = prompt('输入要复制的坐标版本 ID'); if(!sourceId) return;
  const source = await api(`/api/engineering/coordinate-profile-versions/${encodeURIComponent(sourceId)}`);
  const profile = engineeringCoordinateProfiles.find(x => (x.versions || []).some(v => v.id === sourceId));
  if(!profile) throw new Error('未找到坐标配置。');
  const versionLabel = prompt('新版本标签', `draft-${Date.now()}`); if(!versionLabel) return;
  const targetPoints = (source.targetPoints || []).map(x => ({pointCode:x.pointCode, pointType:x.pointType, xUm:x.calibratedXUm ?? x.presetXUm, yUm:x.calibratedYUm ?? x.presetYUm, zUm:x.calibratedZUm, safeZUm:x.safeZUm, liquidDetectZUm:x.aspirateZUm, dispenseZUm:x.dispenseZUm, actionOffsetXUm:x.actionOffsetXUm, actionOffsetYUm:x.actionOffsetYUm, actionOffsetZUm:x.actionOffsetZUm, isEnabled:x.isEnabled}));
  await api('/api/engineering/coordinate-profile-versions', {method:'POST', body:JSON.stringify(engineeringWriteBody(`coordinate-profile:${profile.code}`, {profileCode:profile.code, sourceVersionId:sourceId, versionLabel, targetPoints, validationResultJson:'{}'}))});
  toast('坐标 Draft 已创建。'); await loadEngineeringConfig();
}

async function createLiquidClassVersion(){
  const sourceId = prompt('输入要复制的 Liquid Class 版本 ID'); if(!sourceId) return;
  const profile = engineeringLiquidClasses.find(x => (x.versions || []).some(v => v.id === sourceId));
  const source = profile?.versions?.find(v => v.id === sourceId); if(!profile || !source) throw new Error('未找到 Liquid Class 版本。');
  const versionLabel = prompt('新版本标签', `draft-${Date.now()}`); if(!versionLabel) return;
  const fields = {code:profile.code, name:source.name || profile.name, sourceVersionId:sourceId, versionLabel, aspirateSpeedUlPerSecond:source.aspirateSpeedUlPerSecond, dispenseSpeedUlPerSecond:source.dispenseSpeedUlPerSecond, leadingAirGapUl:source.leadingAirGapUl, trailingAirGapUl:source.trailingAirGapUl, excessVolumeUl:source.volumeAdjustmentUl, preWetCycles:source.preWetCycles, mixCycles:source.mixCycles, isEnabled:false, liquidDetectionEnabled:source.liquidDetectionEnabled, liquidDetectionSensitivityPercent:source.liquidDetectionSensitivityPercent, liquidDetectionSpeedUmPerSecond:source.liquidDetectionSpeedUmPerSecond, aspirateDelayMs:source.aspirateDelayMs, dispenseDelayMs:source.dispenseDelayMs, blowoutVolumeUl:source.blowoutVolumeUl, blowoutDelayMs:source.blowoutDelayMs, volumeAdjustmentUl:source.volumeAdjustmentUl};
  await api('/api/engineering/liquid-classes', {method:'POST', body:JSON.stringify(engineeringWriteBody(`liquid-class:${profile.code}`, fields))});
  toast('Liquid Class Draft 已创建。'); await loadEngineeringConfig();
}

function exportEngineeringCsv(type){ location.href = type === 'command' ? '/api/engineering/diagnostics/command-log.csv' : '/api/engineering/diagnostics/mock-communications.csv'; }
async function exportEngineeringConfig(){
  const data = await api('/api/engineering/config/export');
  const link = document.createElement('a'); link.href = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], {type:'application/json'})); link.download = `engineering-config-${new Date().toISOString().replace(/[:.]/g,'-')}.json`; link.click(); URL.revokeObjectURL(link.href);
}

function engineeringImportRequest(){ return {configType:document.getElementById('engineeringImportType').value, targetCode:document.getElementById('engineeringImportTarget').value.trim(), payload:JSON.parse(document.getElementById('engineeringImportPayload').value || '{}')}; }
async function previewEngineeringImport(){ document.getElementById('engineeringConfigResult').textContent = JSON.stringify(await api('/api/engineering/config/import/preview', {method:'POST', body:JSON.stringify(engineeringImportRequest())}), null, 2); }
async function applyEngineeringImport(){
  const request = engineeringImportRequest();
  const result = await api('/api/engineering/config/import', {method:'POST', body:JSON.stringify({...request, ...engineeringWriteBody(`config-import:${request.configType}:${request.targetCode}`)})});
  document.getElementById('engineeringConfigResult').textContent = JSON.stringify(result, null, 2); toast('工程配置已导入为受控版本。'); await loadEngineeringConfig();
}

async function loadEngineerPage(){
  try{
    const [info, currentRun] = await Promise.all([api('/api/system/info'), api('/api/runs/current').catch(()=>null)]);
    const mode = info.deviceMode?.currentMode || info.deviceMode?.mode || '--';
    setText('engineeringDeviceMode', mode); setText('engineeringMockBadge', String(mode).toLowerCase() === 'mock' ? 'Mock 测试入口' : 'Real fail-closed');
    setText('engineerRunState', currentRun ? `${currentRun.runCode || currentRun.id} / ${displayStatusText(currentRun.status)}` : '无活动任务');
    await Promise.all([loadEngineeringSession(), loadEngineeringDiagnostics(), loadEngineeringConfig()]);
  }catch(e){ toast(e.message || '工程页正式数据加载失败。', true); }
}

document.addEventListener('DOMContentLoaded', loadEngineerPage);
