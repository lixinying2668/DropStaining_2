const mockTimelineEvents = [
  { time: '08:00', type: 'machine.stateChanged', title: '创建 Mock 运行', detail: 'RUN-MOCK-001，A 抽屉内 2 张 IHC + 1 张 HE 已锁定批次。', level: 'info' },
  { time: '08:02', type: 'device.connectionChanged', title: 'Mock 设备就绪', detail: '温控、加液、清洗泵、混匀和 DAB 模块均返回 READY。', level: 'done' },
  { time: '08:05', type: 'qr.scanCompleted', title: '试剂扫码完成', detail: 'R1-R40 扫码结果写入，EMPTY / VALID / INVALID 均可追溯。', level: 'done' },
  { time: '08:08', type: 'workflowStep.started', title: 'IHC 前处理开始', detail: 'A-01、A-02 进入前处理；每个原子动作先保存 Planned。', level: 'running' },
  { time: '08:18', type: 'temperature.changed', title: '温控到达目标', detail: 'HeatBoard A：420 deciC，Slot 1-3 达到稳定范围。', level: 'done' },
  { time: '08:25', type: 'workflowStep.completed', title: 'HE 苏木素完成', detail: 'A-03 从苏木素/末端清洗流程开始，进入清洗。', level: 'done' },
  { time: '08:32', type: 'dab.batchChanged', title: 'DAB M2 配制完成', detail: 'DAB-BATCH-M2 有效期至 11:32，后续用量将写入 DabBatchUsage。', level: 'dab' },
  { time: '08:39', type: 'reagent.bottleDepleted', title: '一抗瓶 R6 理论耗尽', detail: '自动切换到同代码 R7，产生耗尽告警但不中断当前动作。', level: 'warn' },
  { time: '08:48', type: 'machine.stateChanged', title: '暂停请求已接收', detail: '当前加液原子动作完成后暂停，不重新计时、不重复加液。', level: 'paused' },
  { time: '08:55', type: 'alarm.raised', title: '注入 Mock 故障', detail: 'A-02 当前玻片状态 Unknown，禁止自动继续，等待大步骤重做。', level: 'fault' },
  { time: '09:04', type: 'workflowStep.started', title: '从当前大步骤重做', detail: '重做前完成试剂余量、DAB 有效期和设备状态校验。', level: 'running' },
  { time: '09:26', type: 'alarm.acknowledged', title: '故障已处理', detail: '工程师确认原因和恢复动作，额外消耗已写入审计。', level: 'done' },
  { time: '09:40', type: 'slideTask.stateChanged', title: '运行完成，等待卸载', detail: 'A-01、A-02、A-03 全部进入 Completed / PendingUnload。', level: 'done' }
];

const mockGanttRows = [
  {
    lane: 'A-01 IHC',
    caption: '直接一抗码：PAB-010',
    bars: [
      { label: '前处理', start: 8, duration: 17, status: 'done' },
      { label: '一抗', start: 27, duration: 15, status: 'done' },
      { label: '暂停', start: 48, duration: 7, status: 'paused' },
      { label: '二抗', start: 58, duration: 13, status: 'done' },
      { label: 'DAB', start: 73, duration: 9, status: 'dab' },
      { label: '苏木素', start: 85, duration: 10, status: 'done' }
    ]
  },
  {
    lane: 'A-02 IHC',
    caption: '医院码：HOSP-7788',
    bars: [
      { label: '前处理', start: 10, duration: 15, status: 'done' },
      { label: '一抗', start: 28, duration: 12, status: 'done' },
      { label: '故障', start: 55, duration: 8, status: 'fault' },
      { label: '重做一抗', start: 64, duration: 14, status: 'running' },
      { label: 'DAB', start: 80, duration: 8, status: 'dab' },
      { label: '苏木素', start: 90, duration: 8, status: 'done' }
    ]
  },
  {
    lane: 'A-03 HE',
    caption: '手动确认 HE 流程',
    bars: [
      { label: '苏木素', start: 18, duration: 10, status: 'done' },
      { label: '分化/蓝化', start: 31, duration: 10, status: 'done' },
      { label: '末端清洗', start: 45, duration: 12, status: 'done' },
      { label: '待卸载', start: 59, duration: 17, status: 'pending' }
    ]
  },
  {
    lane: '资源',
    caption: '试剂、DAB、温控',
    bars: [
      { label: '扫码确认', start: 5, duration: 7, status: 'done' },
      { label: '温控稳定', start: 18, duration: 8, status: 'done' },
      { label: '跨瓶取液', start: 39, duration: 8, status: 'warn' },
      { label: 'M2 有效期', start: 32, duration: 68, status: 'dab' }
    ]
  }
];

const mockSummary = [
  ['运行批次', 'RUN-MOCK-001', 'A 抽屉，3 张玻片'],
  ['状态', 'PendingUnload', '已完成闭环，等待卸载'],
  ['DAB', 'M2 有效', '有效期至 11:32'],
  ['告警', '1 已确认', '耗尽告警与故障处理已审计']
];

function renderMockSummary(){
  const root = document.getElementById('mockRunSummary');
  if(!root){ return; }
  root.innerHTML = mockSummary.map(([label, value, note]) =>
    `<div><b>${escapeHtml(displayLogText(label))}</b><span>${escapeHtml(displayLogText(value))}</span><em>${escapeHtml(displayLogText(note))}</em></div>`
  ).join('');
}

function renderMockTimeline(){
  const root = document.getElementById('mockTimelineList');
  const counter = document.getElementById('mockEventCount');
  if(!root){ return; }
  if(counter){ counter.textContent = `${mockTimelineEvents.length} 条事件`; }
  root.innerHTML = mockTimelineEvents.map(event => `
    <article class="mock-event level-${escapeHtml(event.level)}">
      <time>${escapeHtml(event.time)}</time>
      <div>
        <span>${escapeHtml(displayLogText(event.type))}</span>
        <b>${escapeHtml(displayLogText(event.title))}</b>
        <p>${escapeHtml(displayLogText(event.detail))}</p>
      </div>
    </article>
  `).join('');
}

function renderMockGantt(){
  const root = document.getElementById('mockGanttBoard');
  if(!root){ return; }
  const ticks = ['08:00', '08:20', '08:40', '09:00', '09:20', '09:40'];
  const scale = `
    <div class="mock-gantt-scale">
      <span>任务</span>
      <div>${ticks.map(tick => `<i>${escapeHtml(tick)}</i>`).join('')}</div>
    </div>`;
  const rows = mockGanttRows.map(row => `
    <div class="mock-gantt-row">
      <div class="mock-gantt-lane"><b>${escapeHtml(row.lane)}</b><span>${escapeHtml(row.caption)}</span></div>
      <div class="mock-gantt-track">
        ${row.bars.map(bar => `
          <span class="mock-gantt-bar status-${escapeHtml(bar.status)}" style="left:${bar.start}%;width:${bar.duration}%">
            ${escapeHtml(displayLogText(bar.label))}
          </span>
        `).join('')}
      </div>
    </div>
  `).join('');
  root.innerHTML = scale + rows;
}

document.addEventListener('DOMContentLoaded', () => {
  renderMockSummary();
  renderMockTimeline();
  renderMockGantt();
});
