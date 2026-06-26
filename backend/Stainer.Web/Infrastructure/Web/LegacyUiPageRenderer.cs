using System.Net;
using Microsoft.Extensions.Hosting;

namespace Stainer.Web.Infrastructure.Web;

public sealed class LegacyUiPageRenderer(IHostEnvironment environment)
{
    private const string AssetVersion = "20260626-r3";

    private static readonly IReadOnlyDictionary<string, PageDefinition> Pages = new Dictionary<string, PageDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["/dashboard"] = new("首页 / 运行总览", "首页 / 运行总览", "A-D 抽屉、16 Slot、初始化、试剂与运行状态总览。", DashboardContent(), "/static/js/dashboard.js"),
        ["/samples"] = new("样本确认", "样本装载、玻片识别与流程确认", "固定 A-D x 4 Slot，支持 Mock 扫描和任务确认。", SamplesContent(), null),
        ["/reagents"] = new("试剂扫描与管理", "试剂瓶条码、列扫码、目录与库存管理", "5x8 试剂架，ch1-ch5 按列扫码，R1-R40 位置管理。", ReagentsContent(), null),
        ["/run"] = new("运行详情", "运行详情、启动校验与故障恢复", "展示通道状态、启动校验、资源状态和事件日志。", RunContent(), "/static/js/run.js"),
        ["/alerts"] = new("告警中心", "告警中心", "显示告警、影响任务、处理建议和关联日志。", AlertsContent(), null),
        ["/history"] = new("历史与导出", "历史、审计与导出", "追踪任务、玻片、试剂、DAB、告警、用户操作和配置版本。", HistoryContent(), null),
        ["/configure"] = new("协议 / 脚本 / 目录配置", "协议、脚本、流程映射与版本管理", "维护已发布流程、试剂目录、液体类型和配置版本。", ConfigureContent(), "/static/js/configure.js"),
        ["/engineer"] = new("工程师模式", "工程师模式：设备调试、配置与安全边界", "管理员受控工程入口；真实危险动作默认 Mock。", EngineerContent(), "/static/js/engineer.js"),
        ["/admin"] = new("系统管理", "用户管理、审计与导出", "维护用户、角色、启停状态、审计查询和导出。", AdminContent(), null)
    };

    public IResult Render(string path)
    {
        if (path == "/" || path.Equals("/login", StringComparison.OrdinalIgnoreCase))
        {
            return Html(LoginPage());
        }

        PageDefinition page;
        if (!environment.IsProduction() && path.Equals("/mock-timeline", StringComparison.OrdinalIgnoreCase))
        {
            page = new PageDefinition(
                "Mock Timeline",
                "Timeline / 甘特图 Mock",
                "开发和测试专用页面，用于观察一次模拟运行的事件时间线、步骤跨度和资源占用；Production 环境隐藏。",
                MockTimelineContent(),
                "/static/js/mock-timeline.js");
        }
        else
        {
            page = Pages.TryGetValue(path, out var foundPage)
                ? foundPage
                : Pages["/dashboard"];
        }

        return Html(RenderShell(path, page));
    }

    private static IResult Html(string html)
    {
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private string RenderShell(string path, PageDefinition page)
    {
        var encodedTitle = WebUtility.HtmlEncode(page.Title);
        var encodedCrumb = WebUtility.HtmlEncode(page.Crumb);
        var encodedSubtitle = WebUtility.HtmlEncode(page.Subtitle);
        var pageScript = string.IsNullOrWhiteSpace(page.ScriptPath) ? string.Empty : $"""<script src="{VersionedAsset(page.ScriptPath)}"></script>""";
        var mockNavigation = environment.IsProduction()
            ? string.Empty
            : """
                <a href="/mock-timeline" class="nav-item admin-only" data-href="/mock-timeline"><i>T</i><span>Timeline</span><small>Mock</small></a>
""";
        var inlineScript = path switch
        {
            "/engineer" => """<script>function safeMock(name){ if(confirm(name + ' is a dangerous mock action. Continue?')) toast('Mock: ' + name + ' recorded'); }</script>""",
            _ => string.Empty
        };

        return $$"""
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
          <meta name="theme-color" content="#07111f">
          <title>{{encodedTitle}} - 全自动冰冻切片染色机</title>
          <link rel="stylesheet" href="{{VersionedAsset("/static/css/app.css")}}">
        </head>
        <body data-status="idle">
          <div class="app-shell">
            <aside class="side-rail">
              <div class="brand-card" onclick="location.href='/dashboard'">
                <div class="brand-orb">冰</div>
                <div class="brand-copy">
                  <strong>冰冻切片染色机</strong>
                  <span>V2.0 基线 / Mock</span>
                </div>
              </div>
              <nav class="nav-stack" aria-label="主导航">
                <a href="/dashboard" class="nav-item" data-href="/dashboard"><i>01</i><span>总览</span><small>运行</small></a>
                <a href="/samples" class="nav-item" data-href="/samples"><i>02</i><span>样本</span><small>确认</small></a>
                <a href="/reagents" class="nav-item" data-href="/reagents"><i>03</i><span>试剂</span><small>扫描</small></a>
                <a href="/run" class="nav-item" data-href="/run"><i>04</i><span>运行</span><small>详情</small></a>
                <a href="/alerts" class="nav-item" data-href="/alerts"><i>!</i><span>告警</span><small>处理</small></a>
                <a href="/history" class="nav-item" data-href="/history"><i>H</i><span>历史</span><small>导出</small></a>
                <a href="/configure" class="nav-item admin-only" data-href="/configure"><i>C</i><span>配置</span><small>协议</small></a>
                <a href="/engineer" class="nav-item admin-only" data-href="/engineer"><i>E</i><span>工程</span><small>调试</small></a>
                <a href="/admin" class="nav-item admin-only" data-href="/admin"><i>A</i><span>管理</span><small>用户</small></a>
                {{mockNavigation}}
              </nav>
              <div class="operator-card">
                <div class="avatar" id="operatorAvatar">访</div>
                <div>
                  <strong id="operatorName">未登录</strong>
                  <span id="operatorRole">guest</span>
                </div>
              </div>
            </aside>

            <main class="workbench">
              <header class="top-panel">
                <div>
                  <div class="crumb">全自动冰冻切片染色机 / {{encodedCrumb}}</div>
                  <h1>{{encodedTitle}}</h1>
                  <p>{{encodedSubtitle}}</p>
                </div>
                <div class="status-cluster">
                  <div class="status-chip status-idle" id="statusChip"><span></span><b data-status-label>idle</b></div>
                  <div class="mini-metric"><span>玻片</span><b id="metricSlides">0</b></div>
                  <div class="mini-metric"><span>试剂</span><b id="metricReagents">0</b></div>
                  <div class="mini-metric"><span>告警</span><b id="metricAlarms">0</b></div>
                  <div class="clock-card"><span id="clockTime">--:--</span><small id="clockDate">----</small></div>
                  <button class="icon-btn" onclick="location.href='/dashboard'" title="主控台">⌂</button>
                  <button class="icon-btn danger" onclick="logout()" title="退出">×</button>
                </div>
              </header>
              <section class="alert-banner hidden" id="alertBanner" onclick="location.href='/alerts'">
                <div class="pulse-dot"></div>
                <div><b>当前告警</b><span id="alertText"></span></div>
                <button class="btn btn-soft">查看处理</button>
              </section>
              {{page.Content}}
            </main>
          </div>
          <div id="toast" class="toast hidden"></div>
          <script src="{{VersionedAsset("/static/js/api.js")}}"></script>
          <script src="{{VersionedAsset("/static/js/stainer-host.js")}}"></script>
          {{pageScript}}
          {{inlineScript}}
        </body>
        </html>
        """;
    }

    private static string LoginPage()
    {
        return """
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
          <title>登录 - 全自动冰冻切片染色机</title>
          <link rel="stylesheet" href="/static/css/app.css?v=20260625-r4">
        </head>
        <body class="login-screen">
          <div class="login-grid">
            <section class="hero-panel glass-panel">
              <div class="machine-badge">HE / IHC · V2.0 基线 · Mock</div>
              <h1>全自动冰冻切片染色机</h1>
              <p>13.3 寸触屏优先设计，覆盖登录初始化、样本确认、试剂校验、运行监控、告警处理和受控工程调试。</p>
              <div class="hero-device">
                <div class="device-top"><span></span><span></span><span></span><span></span></div>
                <div class="device-screen"><div class="wave"></div><b>4 通道 × 4 玻片</b><small>通道批次 / 16 Slot 温控 / Mock Adapter</small></div>
                <div class="device-slots"><i></i><i></i><i></i><i></i></div>
              </div>
              <div class="hero-stats">
                <div><span>最大负载</span><b>16片</b></div>
                <div><span>试剂位</span><b>5×8</b></div>
                <div><span>温控映射</span><b>4×4</b></div>
              </div>
            </section>
            <section class="login-card modern-card">
              <div class="login-logo-row">
                <div class="brand-orb big">冰</div>
                <div><h2>用户登录</h2><p>选择角色后进入对应工作台</p></div>
              </div>
              <div class="role-picker" role="radiogroup">
                <label class="role-tile active"><input type="radio" name="role" value="operator" checked><i>OP</i><b>操作员</b><span>实验操作</span></label>
                <label class="role-tile"><input type="radio" name="role" value="admin"><i>AD</i><b>管理员</b><span>用户与配置</span></label>
                <label class="role-tile disabled" title="工程师账号待接入"><i>ENG</i><b>服务工程师</b><span>待账号接入</span></label>
              </div>
              <label class="field-label">用户名<input id="username" class="input input-xl" value="operator" placeholder="用户名"></label>
              <label class="field-label">密码<input id="password" class="input input-xl" type="password" value="123456" placeholder="密码"></label>
              <button class="btn btn-primary btn-xl full" onclick="login()">登录并初始化连接</button>
              <div class="quick-accounts">
                <button onclick="fillAccount('operator')">操作员演示</button>
                <button onclick="fillAccount('admin')">管理员演示</button>
              </div>
              <p class="hint center">演示账号：operator / admin，密码均为 123456</p>
            </section>
          </div>
          <div id="toast" class="toast hidden"></div>
          <script src="/static/js/api.js?v=20260625-r4"></script>
          <script>
            function selectedRole(){ return document.querySelector('input[name="role"]:checked').value; }
            function fillAccount(role){ username.value=role; password.value='123456'; document.querySelector(`input[value="${role}"]`).checked=true; syncRoles(); }
            function syncRoles(){ document.querySelectorAll('.role-tile').forEach(x=>{ const input=x.querySelector('input'); x.classList.toggle('active', !!input && input.checked); }); }
            document.querySelectorAll('input[name="role"]').forEach(x=>x.addEventListener('change', syncRoles));
            async function login(){
              const payload = {username: username.value, password: password.value, role: selectedRole()};
              const res = await api('/api/login', {method:'POST', body: JSON.stringify(payload)});
              if(res.ok){ location.href=res.redirect; }
            }
          </script>
        </body>
        </html>
        """;
    }

    private static string DashboardContent()
    {
        return """
        <section class="workflow-strip v18-flow">
          <article class="workflow-step active" id="stepInit" onclick="initializeSystem()"><i>01</i><b>登录与初始化</b><span>回零 / 通讯 / 液位 / 洗针</span></article>
          <article class="workflow-step" id="stepSamples" onclick="location.href='/samples'"><i>02</i><b>样本确认</b><span>通灵码 / 医院码 / HE 手动</span></article>
          <article class="workflow-step" id="stepReagents" onclick="location.href='/reagents'"><i>03</i><b>试剂扫描</b><span>ch1-ch5 / R1-R40</span></article>
          <article class="workflow-step" onclick="location.href='/run'"><i>04</i><b>启动校验</b><span>不允许绕过强制启动</span></article>
          <article class="workflow-step" id="stepRun" onclick="location.href='/run'"><i>05</i><b>运行详情</b><span>大步骤 / 原子动作 / 日志</span></article>
        </section>
        <section class="kpi-grid">
          <article class="kpi-card primary"><span>初始化状态</span><strong id="kpiInit">待初始化</strong><small>登录后需完成初始化与运行前预检</small><button class="btn btn-light full" onclick="initializeSystem()">执行 / 重试初始化</button></article>
          <article class="kpi-card"><span>通道批次</span><strong>A-D / 16 Slot</strong><small>启动后不得追加玻片</small><button class="btn btn-soft full" onclick="location.href='/samples'">创建/确认任务</button></article>
          <article class="kpi-card"><span>试剂校验</span><strong id="kpiReagents">待扫码</strong><small>缺失/无效/未知/过期/余量不足均拦截</small><button class="btn btn-soft full" onclick="location.href='/reagents'">进入试剂管理</button></article>
          <article class="kpi-card"><span>运行批号</span><strong id="kpiRunId">--</strong><small id="kpiRunIdFull">尚未开始实验</small><button class="btn btn-primary full" onclick="location.href='/run'">打开运行详情</button></article>
        </section>
        <section class="split-grid wide-left">
          <article class="modern-card">
            <div class="section-title"><div><h2>A-D 抽屉与 16 Slot</h2><p>每个抽屉固定 4 个 Slot，状态用于样本装载、任务确认、运行和故障判断。</p></div><div class="legend"><span class="legend-dot idle"></span>空闲 <span class="legend-dot running"></span>运行 <span class="legend-dot warn"></span>待确认/故障</div></div>
            <div class="drawer-board" id="drawerBoard"></div>
          </article>
          <aside class="stack-panel">
            <article class="modern-card compact-card"><h3>初始化 / 运行前预检</h3><div class="check-list" id="systemChecks"></div><button class="btn btn-primary full" onclick="initializeSystem()">授权重试预检</button></article>
            <article class="modern-card compact-card dab-mini"><h3>DAB 临时配液区</h3><div class="dab-mini-grid"><span>M1</span><span>M2</span><span>M3</span><span>M4</span><span>M5</span><span>M6</span><span>M7</span><span>M8</span></div><p class="hint-line">每次自动配制使用新试管，3 小时有效。</p></article>
          </aside>
        </section>
        <section class="modern-card"><div class="section-title"><h2>最近事件日志</h2><button class="btn btn-soft" onclick="location.href='/history'">历史与导出</button></div><div class="timeline" id="dashboardLogs"></div></section>
        """;
    }

    private static string SamplesContent()
    {
        return """
        <section class="control-strip modern-card v18-sample-actions">
          <div class="control-main">
            <label class="stepper-field"><span>Slot 总数</span><input id="sampleCount" type="number" min="1" max="16" value="16" disabled><em>位</em></label>
            <button class="btn btn-primary btn-xl" onclick="scanSamples()">刷新样本状态</button>
            <button class="btn btn-soft btn-xl" onclick="openConfirmModal('ihc-tl')">IHC 一抗码</button>
            <button class="btn btn-soft btn-xl" onclick="openConfirmModal('ihc-hospital')">医院码 / LIS</button>
            <button class="btn btn-soft btn-xl" onclick="openConfirmModal('he')">HE 手动确认</button>
          </div>
          <div class="scan-hint"><b>通道规则</b><span>先选择通道实验脚本，再在空 Slot 添加样本；启动后脚本和 Slot 永久锁定。</span></div>
        </section>
        <section class="split-grid wide-left"><article class="modern-card"><div class="section-title"><div><h2>A-D 样本区</h2><p>Slot 状态、通道脚本和任务信息均来自后端 ChannelBatch / SlideTask。</p></div><div class="badge-soft" id="sampleBadge">0/16 已占用</div></div><div class="sample-cabinet v18-slot-grid" id="sampleCabinet"></div></article><aside class="stack-panel"><article class="modern-card compact-card"><h3>识别路径</h3><div class="decision-list"><div><b>IHC 一抗码</b><span>直接输入或模拟扫码一抗代码，只做与当前通道脚本的兼容性校验。</span></div><div><b>医院码 / LIS</b><span>保留原始样本码，后端返回候选一抗时由操作员确认。</span></div><div><b>HE 玻片</b><span>不需要二维码，继承当前通道 HE 脚本创建任务。</span></div></div></article></aside></section>
        <div id="sampleConfirmModal" class="modal-mask hidden"><div class="modal-card sample-confirm-card"><header><h2 id="confirmTitle">添加样本</h2><button class="icon-btn" onclick="closeConfirmModal()">×</button></header><div class="form-grid-mini"><label>目标 Slot<select id="confirmSlot" class="input"></select></label><label>识别路径<select id="confirmPath" class="input"><option value="ihc-tl">IHC 一抗码</option><option value="ihc-hospital">医院码 / LIS</option><option value="he">HE 手动确认</option></select></label><label id="rawCodeLabel">样本码 / 一抗码<input class="input" id="rawCode" value="PA1"></label><label id="primaryAntibodyLabel" class="hidden">确认一抗<select id="primaryAntibodySelect" class="input"></select></label></div><div class="notice-box" id="confirmChannelScript">当前通道脚本：未选择</div><div class="notice-box hidden" id="sampleTaskError"></div><footer><button class="btn btn-soft" onclick="closeConfirmModal()">取消</button><button class="btn btn-primary" id="confirmTaskButton" onclick="confirmTask()">确认创建任务</button></footer></div></div>
        <div id="channelScriptModal" class="modal-mask hidden"><div class="modal-card sample-confirm-card"><header><h2 id="channelScriptTitle">选择通道实验脚本</h2><button class="icon-btn" onclick="closeChannelScriptModal()">×</button></header><div class="form-grid-mini"><label>实验类型<select id="channelScriptExperimentType" class="input" onchange="refreshChannelScriptOptions()"><option value="HE">HE</option><option value="IHC">IHC</option></select></label><label>已发布流程<select id="channelScriptSelect" class="input"></select></label></div><label class="field-label hidden" id="channelScriptReasonLabel">变更原因<input id="channelScriptReason" class="input" placeholder="请输入变更原因"></label><div class="notice-box" id="channelScriptHint">该选择会保存到后端 ChannelBatch，并应用于该通道 1-4 号 Slot。</div><footer><button class="btn btn-soft" onclick="closeChannelScriptModal()">取消</button><button class="btn btn-primary" onclick="applyChannelScriptSelection()">确认</button></footer></div></div>
        """;
    }

    private static string ReagentsContent()
    {
        return """
        <section class="control-strip modern-card"><div class="control-main reagent-scan-actions"><button class="btn btn-primary btn-xl" id="startReagentScanSessionBtn" onclick="startReagentScanSession()">开始扫码</button><button class="btn btn-soft btn-xl" id="completeReagentScanSessionBtn" onclick="completeReagentScanSession()" disabled>完成扫码</button><button class="btn btn-soft btn-xl" onclick="scanReagents()">扫描全部试剂架</button><button class="btn btn-soft btn-xl" onclick="mockColumnScan(1)">扫描 ch1</button><button class="btn btn-soft btn-xl" onclick="mockColumnScan(2)">扫描 ch2</button><button class="btn btn-soft btn-xl" onclick="mockColumnScan(3)">扫描 ch3</button><button class="btn btn-soft btn-xl" onclick="mockColumnScan(4)">扫描 ch4</button><button class="btn btn-soft btn-xl" onclick="mockColumnScan(5)">扫描 ch5</button></div><div class="scan-hint"><b>扫码会话</b><span id="reagentScanSessionSummary">正在读取后端扫码会话...</span></div></section>
        <section class="split-grid wide-left reagent-v18-layout"><article class="modern-card"><div class="section-title"><div><h2>5×8 试剂架 / R1-R40</h2><p>状态来自后端数据库：EMPTY / VALID / INVALID / 未扫码；点击瓶位查看解析结果。</p></div><div class="badge-soft" id="reagentBadge">0 个 VALID</div></div><div class="reagent-deck v18-reagent-deck" id="reagentDeck"></div></article><aside class="stack-panel"><article class="modern-card compact-card"><h3>列扫码状态</h3><div class="column-status-grid" id="columnStatus"></div></article><article class="modern-card compact-card dab-mini"><h3>DAB 临时配液区</h3><div class="dab-mini-grid"><span>M1</span><span>M2</span><span>M3</span><span>M4</span><span>M5</span><span>M6</span><span>M7</span><span>M8</span></div><button class="btn btn-soft full" onclick="toast('DAB 清洗确认写入尚未接入正式接口', true)">确认清洗完成</button></article></aside></section>
        <div id="reagentDetail" class="modal-mask hidden"><div class="modal-card"><header><h2>试剂瓶解析详情</h2><button class="icon-btn" onclick="reagentDetail.classList.add('hidden')">×</button></header><div id="reagentDetailBody" class="detail-grid"></div><footer><button class="btn btn-soft" onclick="reagentDetail.classList.add('hidden')">关闭</button><button class="btn btn-primary" onclick="toast('单个 R 位正式扫码确认尚未接入。', true)">重新扫码</button></footer></div></div>
        <div id="reagentScanModal" class="modal-mask hidden"><div class="modal-card"><header><h2 id="reagentScanTitle">R 位扫码确认</h2><button class="icon-btn" onclick="cancelReagentScanModal()">×</button></header><div class="detail-grid" id="reagentScanContext"></div><div class="form-grid-mini"><label>输入方式<select id="reagentScanMode" class="input" onchange="syncReagentScanMode()"><option value="barcode">Mock 扫描文本</option><option value="empty">空位确认</option></select></label><label id="reagentBarcodeLabel">Mock 条码文本<input id="reagentBarcodeInput" class="input" placeholder="例如 ABC05020270101001"></label><label id="reagentExpirationLabel">有效期<input id="reagentExpirationInput" class="input" type="date"></label></div><div class="notice-box" id="reagentScanHint">前端只提交原始文本，条码解析和校验由后端完成。</div><div id="reagentScanResult" class="detail-grid"></div><footer><button class="btn btn-soft" onclick="cancelReagentScanModal()">取消</button><button class="btn btn-primary" onclick="confirmReagentPositionScan()">确认扫码</button></footer></div></div>
        """;
    }

    private static string RunContent()
    {
        return """
        <section class="run-command-bar modern-card v18-runbar"><button class="command start" onclick="runAction('start')"><i>▶</i><b>启动</b><span>先执行强制校验</span></button><button class="command pause" onclick="runAction('pause')"><i>Ⅱ</i><b>暂停</b><span>当前原子动作后生效</span></button><button class="command resume" onclick="runAction('resume')"><i>↪</i><b>恢复</b><span>等待/孵育直接下一步</span></button><button class="command stop" onclick="confirmStop()"><i>■</i><b>普通整机停止</b><span>不支持单 Slot/通道终止</span></button><button class="command fault hidden" id="mockFaultButton" onclick="injectMockFault()"><i>!</i><b>Mock 故障注入</b><span>工程师 / 管理员</span></button><button class="command redo hidden" id="redoMajorStepButton" onclick="redoCurrentMajorStep()"><i>↺</i><b>大步骤重做</b><span>需填写原因</span></button><div class="run-badge"><span id="runStatus" class="status-chip status-idle"><i></i><b data-status-label>idle</b></span><small id="runIdSmall">未生成批号</small></div></section>
        <section class="modern-card validation-card"><div class="section-title"><div><h2>启动前校验</h2><p>对全部已确认、未完成任务统一汇总；失败时只允许返回处理。</p></div><button class="btn btn-soft" onclick="openValidationModal()">查看校验详情</button></div><div class="validation-grid"><div><b>样本任务</b><span id="validationSlides">0 张</span><em id="validationSlidesState">未创建</em></div><div><b>初始化</b><span id="validationInit">待完成</span><em id="validationInitState">BLOCK</em></div><div><b>试剂状态</b><span id="validationReagents">0 个有效瓶</span><em id="validationReagentsState">BLOCK</em></div><div><b>人工覆盖</b><span>管理员/操作员均不可绕过</span><em>禁止</em></div></div></section>
        <section class="split-grid wide-left"><article class="modern-card"><div class="section-title"><div><h2>实时通道状态</h2><p>状态推进以动作完成反馈或明确状态条件为准。</p></div><button class="btn btn-soft" onclick="refreshRun()">刷新</button></div><div id="runChannels" class="runtime-board"></div></article><aside class="stack-panel"><article class="modern-card compact-card"><h3>关键资源</h3><div class="module-grid"><div><b>双针机械臂</b><span>Mock 平台</span><em>READY</em></div><div><b>通道清洗泵</b><span>A-D 同步出液</span><em>READY</em></div><div><b>通道混匀</b><span>同轮试剂完成后混匀</span><em>READY</em></div><div><b>16 Slot 温控</b><span>目标/实测/异常</span><em>42℃</em></div></div></article><article class="modern-card compact-card"><h3>事件日志</h3><div id="logList" class="log-console"></div></article></aside></section>
        <div id="validationModal" class="modal-mask hidden"><div class="modal-card large-modal"><header><h2>启动校验结果</h2><button class="icon-btn" onclick="validationModal.classList.add('hidden')">×</button></header><div id="validationBody" class="validation-result"></div><footer><button class="btn btn-soft" onclick="validationModal.classList.add('hidden')">返回处理</button><button class="btn btn-primary" id="validationStartBtn" onclick="forceStartAfterValidation()">全部通过后启动</button></footer></div></div>
        """;
    }

    private static string AlertsContent()
    {
        return """<section class="modern-card"><div class="section-title"><div><h2>当前告警</h2><p>关键告警禁止启动或继续，需返回对应页面处理。</p></div><button class="btn btn-soft" onclick="toast('Mock: 告警已确认记录')">确认选中告警</button></div><div class="alarm-list" id="alarmList"></div></section><section class="modern-card"><div class="section-title"><h2>关联事件日志</h2><button class="btn btn-soft" onclick="location.href='/history'">历史与导出</button></div><div class="timeline large" id="alertLogs"></div></section>""";
    }

    private static string HistoryContent()
    {
        return """<section class="modern-card"><div class="section-title"><div><h2>筛选条件</h2><p>首版本本地审计，后续可扩展 LIS 状态回写。</p></div><button class="btn btn-primary" onclick="toast('Mock: CSV 已生成')">导出 CSV</button></div><div class="inline-form touch-form"><label>开始日期<input class="input" type="date"></label><label>结束日期<input class="input" type="date"></label><label>通道<select class="input"><option>全部</option><option>A</option><option>B</option><option>C</option><option>D</option></select></label><label>玻片/批号<input class="input" placeholder="输入玻片码或批号"></label></div></section><section class="split-grid"><article class="modern-card"><div class="section-title"><h2>任务/玻片记录</h2></div><div class="data-table history-table" id="historySlides"></div></article><article class="modern-card"><div class="section-title"><h2>试剂与消耗追溯</h2></div><div class="data-table history-table" id="historyReagents"></div></article></section><section class="modern-card"><div class="section-title"><h2>用户操作与配置版本日志</h2></div><div class="timeline large" id="historyLogs"></div></section>""";
    }

    private static string ConfigureContent()
    {
        return """<section class="kpi-grid"><article class="kpi-card"><span>流程生命周期</span><strong>草稿→发布</strong><small>发布后不可覆盖</small></article><article class="kpi-card"><span>首版流程</span><strong>HE / IHC</strong><small>特染、多重染色不在首版</small></article><article class="kpi-card"><span>映射关系</span><strong>一抗 1:N 脚本</strong><small>操作员从候选中选择</small></article><article class="kpi-card"><span>配置快照</span><strong>版本追溯</strong><small>流程/试剂/液体/坐标固化</small></article></section><section class="split-grid wide-left"><article class="modern-card"><div class="section-title"><div><h2>流程版本管理</h2><p>管理员可创建草稿、复制、发布、停用；已发布流程不允许直接覆盖。</p></div><div class="button-row"><button class="btn btn-primary" onclick="createWorkflowDraft()">新建草稿</button><button class="btn btn-soft" onclick="copyWorkflowDraft()">复制为新版本</button></div></div><div class="protocol-version-table" id="protocolTable"></div></article><aside class="stack-panel"><article class="modern-card compact-card"><h3>步骤类型</h3><div class="tag-cloud"><span>加液</span><span>等待/孵育</span><span>通道清洗</span><span>混匀</span><span>温控</span><span>DAB 自动配制</span><span>人工确认</span></div></article><article class="modern-card compact-card"><h3>DAB 计算预览</h3><div class="dab-numbers small-dab" id="dabPreview"></div></article></aside></section><section class="split-grid"><article class="modern-card"><div class="section-title"><h2>一抗代码 - 已发布 IHC 脚本映射</h2><button class="btn btn-primary">新增映射</button></div><div class="data-table mapping-table"><div class="table-row head"><span>一抗代码</span><span>脚本ID</span><span>脚本名称</span><span>版本</span><span>状态</span></div><div class="table-row"><span>010 / CK</span><span>IHC-CK</span><span>CK 快速染色</span><span>v1.0</span><span>已发布</span></div></div></article><article class="modern-card"><div class="section-title"><h2>试剂目录与液体参数引用</h2><button class="btn btn-soft">导入目录</button></div><div class="data-table reagent-catalog-table" id="catalogTable"></div></article></section>""";
    }

    private static string EngineerContent()
    {
        return """<section class="modern-card engineer-session"><div class="section-title"><div><h2>ENG-01 工程入口与会话</h2><p>二次认证、工程权限说明、设备是否可调试、剩余会话时间和工程操作日志。</p></div><span class="badge-soft">管理员受控入口</span></div><div class="validation-grid"><div><b>运行中任务</b><span id="engineerRunState">无活动任务</span><em>可模拟</em></div><div><b>二次确认</b><span>危险动作前再次确认</span><em>启用</em></div><div><b>会话剩余</b><span>30 分钟</span><em>Mock</em></div><div><b>真实设备</b><span>P1 未关闭前禁用危险动作</span><em>安全</em></div></div></section><section class="engineer-layout v18-engineer-layout"><article class="modern-card engineer-card"><div class="section-title"><h2>ENG-02 设备连接与通讯</h2><span class="badge-soft">COM / SDK</span></div><div class="form-grid-mini"><label>主控 COM<input id="serialPort" class="input" value="COM1"></label><label>波特率<input id="baudRate" class="input" value="115200"></label><label>SOCON SDK<input class="input" value="Mock Adapter"></label></div><div class="button-row"><button class="btn btn-primary" onclick="engineerCommand('serial','connect')">连接测试</button><button class="btn btn-soft" onclick="engineerCommand('serial','reconnect')">重连</button></div></article><article class="modern-card engineer-card"><div class="section-title"><h2>ENG-07 移液与洗针测试</h2><span class="badge-soft">Mock</span></div><div class="inline-form touch-form"><label>动作<select id="pipetteAction" class="input"><option value="aspirate">吸液</option><option value="dispense">加液</option><option value="detect_liquid">探液</option><option value="wash">洗针/冲洗</option></select></label><label>针<select id="needleSelect" class="input"><option>针1</option><option>针2</option><option>双针同步</option></select></label><label>位置<input id="wellPosition" class="input" value="R1"></label><label>体积 μL<input id="pipetteVolume" class="input" type="number" value="100"></label></div><button class="btn btn-primary full" onclick="testPipette()">执行模拟移液/洗针</button></article><article class="modern-card engineer-card"><div class="section-title"><h2>ENG-08 温控与制冷</h2><span class="badge-soft">4 板 × 4 点</span></div><div class="form-grid-mini"><label>测试通道<input id="heaterChannel" class="input" type="number" min="1" max="4" value="1"></label><label>目标温度 ℃<input id="targetTemp" class="input" type="number" value="42"></label></div><button class="btn btn-soft full" onclick="engineerCommand('heater','set_temperature')">设温测试</button></article><article class="modern-card engineer-card"><div class="section-title"><h2>ENG-09 清洗泵与混匀</h2><span class="badge-soft">PWM -100~+100</span></div><div class="form-grid-mini"><label>清洗泵通道<input id="pumpChannel" class="input" type="number" min="1" max="4" value="1"></label><label>混匀通道<input id="mixChannel" class="input" type="number" min="1" max="4" value="1"></label><label>PWM<input id="pumpPwm" class="input" type="number" min="-100" max="100" value="60"></label><label>时间 s<input id="pumpDuration" class="input" type="number" value="10"></label></div><div class="button-row"><button class="btn btn-primary" onclick="testPump()">通道清洗</button><button class="btn btn-soft" onclick="engineerCommand('pump','stop')">停止泵</button><button class="btn btn-soft" onclick="engineerCommand('mixer','start')">混匀测试</button></div></article></section><section class="modern-card"><div class="section-title"><h2>调试返回 / 原始通讯</h2><button class="btn btn-soft" onclick="engineerResult.textContent='等待命令...'">清空</button></div><pre id="engineerResult" class="terminal">等待命令...</pre></section>""";
    }

    private static string AdminContent()
    {
        return """<section class="kpi-grid admin-kpi"><article class="kpi-card"><span>用户账号</span><strong id="adminUserCount">0</strong><small>操作员 / 管理员；工程入口受控</small></article><article class="kpi-card"><span>试剂记录</span><strong id="adminReagentCount">0</strong><small>库存与条码审计</small></article><article class="kpi-card"><span>日志条数</span><strong id="adminLogCount">0</strong><small>动作与异常追溯</small></article><article class="kpi-card"><span>告警</span><strong id="adminAlarmCount">0</strong><small>未处理/历史告警</small></article></section><section class="split-grid"><article class="modern-card"><div class="section-title"><h2>用户管理</h2><div class="button-row"><button class="btn btn-primary">新增用户</button><button class="btn btn-soft">重置密码</button></div></div><div class="data-table user-table" id="userTable"></div></article><article class="modern-card"><div class="section-title"><h2>审计导出</h2><button class="btn btn-soft" onclick="location.href='/history'">打开历史与导出</button></div><div class="decision-list"><div><b>人工操作</b><span>登录、任务确认、流程选择、暂停、停止、故障重做。</span></div><div><b>配置操作</b><span>流程发布、坐标、液体类型、通信导入导出。</span></div><div><b>试剂/DAB</b><span>条码、来源瓶、用量、DAB 配制与清洗确认。</span></div></div></article></section><section class="modern-card"><div class="section-title"><h2>最近日志</h2><button class="btn btn-soft" onclick="toast('Mock: 日志导出成功')">导出审计</button></div><div class="timeline large" id="adminLogs"></div></section>""";
    }

    private static string MockTimelineContent()
    {
        return """
        <section class="modern-card mock-intro-card">
          <div class="section-title">
            <div>
              <h2>Mock 运行观察台</h2>
              <p>用于内部验证 Mock 执行器、事件顺序、耗材占用和异常恢复。Production 环境不会显示该入口。</p>
            </div>
            <span class="badge-soft">测试专用</span>
          </div>
          <div class="validation-grid mock-summary-grid" id="mockRunSummary"></div>
        </section>
        <section class="mock-timeline-layout">
          <article class="modern-card">
            <div class="section-title">
              <div>
                <h2>Timeline</h2>
                <p>按时间展示状态变化、步骤、告警、DAB 和试剂事件。</p>
              </div>
              <span class="badge-soft" id="mockEventCount">0 events</span>
            </div>
            <div class="mock-event-list" id="mockTimelineList"></div>
          </article>
          <article class="modern-card mock-gantt-card">
            <div class="section-title">
              <div>
                <h2>甘特图</h2>
                <p>按玻片任务展示大步骤跨度和暂停、故障、重做区间。</p>
              </div>
              <span class="badge-soft">08:00 - 09:40</span>
            </div>
            <div class="mock-gantt-shell" id="mockGanttBoard"></div>
          </article>
        </section>
        """;
    }

    private static string VersionedAsset(string path)
    {
        return $"{path}?v={AssetVersion}";
    }

    private sealed record PageDefinition(string Crumb, string Title, string Subtitle, string Content, string? ScriptPath);
}
