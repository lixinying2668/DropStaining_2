using System.Net;
using Microsoft.Extensions.Hosting;

namespace Stainer.Web.Infrastructure.Web;

public sealed class LegacyUiPageRenderer(IHostEnvironment environment)
{
    private const string AssetVersion = "20260702-r19-patch1";

    private static readonly IReadOnlyDictionary<string, PageDefinition> Pages = new Dictionary<string, PageDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["/control-console"] = new("主控台", "主控台", "设备数字孪生、演示控制与快速入口。", ControlConsoleContent(), null),
        ["/dashboard"] = new("首页 / 运行总览", "首页 / 运行总览", "A-D 抽屉、16 Slot、初始化、试剂与运行状态总览。", DashboardContent(), "/static/js/dashboard.js"),
        ["/samples"] = new("样本确认", "样本装载、玻片识别与流程确认", "固定 A-D x 4 Slot，支持 Mock 扫描和任务确认。", SamplesContent(), null),
        ["/reagents"] = new("试剂扫描与管理", "试剂瓶条码、列扫码、目录与库存管理", "5x8 试剂架，ch1-ch5 按列扫码，R1-R40 位置管理。", ReagentsContent(), null),
        ["/run"] = new("运行详情", "运行详情、启动校验与故障恢复", "展示通道状态、启动校验、资源状态和事件日志。", RunContent(), "/static/js/run.js"),
        ["/alerts"] = new("告警中心", "告警中心", "显示告警、影响任务、处理建议和关联日志。", AlertsContent(), null),
        ["/alarms"] = new("告警中心", "告警中心", "显示告警、影响任务、处理建议和关联日志。", AlertsContent(), null),
        ["/history"] = new("历史与导出", "历史、审计与导出", "追踪任务、玻片、试剂、DAB、告警、用户操作和配置版本。", HistoryContent(), null),
        ["/configure"] = new("协议 / 脚本 / 目录配置", "协议、脚本、流程映射与版本管理", "维护已发布流程、试剂目录、液体类型和配置版本。", ConfigureContent(), "/static/js/configure.js"),
        ["/engineer"] = new("工程师模式", "工程师模式：设备调试、配置与安全边界", "管理员受控工程入口；真实危险动作默认 Mock。", EngineerContent(), "/static/js/engineer.js"),
        ["/admin"] = new("系统管理", "用户管理、审计与导出", "维护用户、角色、启停状态、审计查询和导出。", AdminContent(), null),
        ["/management"] = new("系统管理", "用户管理、审计与导出", "维护用户、角色、启停状态、审计查询和导出。", AdminContent(), null)
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

    private static string PageLabel(string path)
    {
        return path.ToLowerInvariant() switch
        {
            "/control-console" => "主控",
            "/dashboard" => "检查",
            "/samples" => "样本",
            "/reagents" => "试剂",
            "/run" => "运行",
            "/alerts" or "/alarms" => "告警",
            "/history" => "历史",
            "/configure" => "配置",
            "/engineer" => "工程",
            "/admin" or "/management" => "管理",
            "/mock-timeline" => "Timeline",
            _ => "主控"
        };
    }

    private string RenderShell(string path, PageDefinition page)
    {
        var encodedTitle = WebUtility.HtmlEncode(page.Title);
        var encodedPageLabel = WebUtility.HtmlEncode(PageLabel(path));
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
              <div class="brand-card current-page-card" onclick="location.href='/control-console'" title="返回主控台">
                <div class="brand-orb">冰</div>
                <div class="brand-copy">
                  <strong id="currentPageLabel">{{encodedPageLabel}}</strong>
                  <span class="page-mode">模式：<b id="metricDeviceMode">Mock</b></span>
                </div>
              </div>
              <nav class="nav-stack" aria-label="主导航">
                <a href="/control-console" class="nav-item" data-href="/control-console"><i>01</i><span>主控</span><small>总台</small></a>
                <a href="/dashboard" class="nav-item" data-href="/dashboard"><i>02</i><span>检查</span><small>运行</small></a>
                <a href="/samples" class="nav-item" data-href="/samples"><i>03</i><span>样本</span><small>确认</small></a>
                <a href="/reagents" class="nav-item" data-href="/reagents"><i>04</i><span>试剂</span><small>扫描</small></a>
                <a href="/run" class="nav-item" data-href="/run"><i>05</i><span>运行</span><small>详情</small></a>
                <a href="/alerts" class="nav-item" data-href="/alerts"><i>06</i><span>告警</span><small>处理</small></a>
                <a href="/history" class="nav-item" data-href="/history"><i>07</i><span>历史</span><small>导出</small></a>
                <a href="/configure" class="nav-item admin-only" data-href="/configure"><i>C</i><span>配置</span><small>协议</small></a>
                <a href="/engineer" class="nav-item admin-only" data-href="/engineer"><i>E</i><span>工程</span><small>调试</small></a>
                <a href="/admin" class="nav-item admin-only" data-href="/admin"><i>A</i><span>管理</span><small>用户</small></a>
                {{mockNavigation}}
              </nav>
              <div class="operator-card" id="operatorCard" role="button" tabindex="0" aria-label="用户菜单" aria-controls="userMenu" aria-expanded="false">
                <div class="avatar" id="operatorAvatar">访</div>
                <div>
                  <strong id="operatorName">未登录</strong>
                  <span id="operatorRole">guest</span>
                </div>
                <div class="user-menu hidden" id="userMenu" role="menu">
                  <button type="button" id="logoutButton" role="menuitem">退出登录</button>
                </div>
              </div>
            </aside>

            <main class="workbench">
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
          <link rel="stylesheet" href="/static/css/app.css?v=20260626-r3">
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
              <div class="role-picker two-roles" role="radiogroup">
                <label class="role-tile active"><input type="radio" name="role" value="operator" checked><i>OP</i><b>操作员</b><span>实验操作</span></label>
                <label class="role-tile"><input type="radio" name="role" value="admin"><i>AD</i><b>管理员</b><span>用户与配置</span></label>
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
          <script src="/static/js/api.js?v=20260626-r3"></script>
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

    private static string ControlConsoleContent()
    {
        return $$"""
        <section class="control-console-shell" aria-label="主控台数字孪生" style="width:100%;height:calc(100vh - 34px);min-height:0;margin-bottom:0;border:1px solid rgba(219,231,245,.88);border-radius:20px;overflow:hidden;background:#f5f7fb;box-shadow:0 20px 55px rgba(15,23,42,.12)">
          <iframe id="controlConsoleFrame" title="主控台数字孪生" src="{{VersionedAsset("/static/control-console/index.html")}}" style="width:100%;height:100%;border:0;display:block;background:#f5f7fb"></iframe>
        </section>
        """;
    }

    private static string DashboardContent()
    {
        return """
        <section class="modern-card dashboard-channel-panel">
          <div class="section-title"><div><h2>A-D 通道与 16 Slot</h2><p id="dashboardRunSummary">正在读取正式运行数据...</p></div><div class="legend"><span class="legend-dot idle"></span>空闲 <span class="legend-dot running"></span>运行 <span class="legend-dot warn"></span>待处理</div></div>
          <div class="drawer-board" id="drawerBoard"></div>
        </section>
        <section class="dashboard-ops-grid">
          <article class="modern-card compact-card dashboard-checks"><div class="section-title"><h2>初始化与预检</h2><button class="btn btn-primary" onclick="initializeSystem()">执行 / 重试</button></div><div class="check-list" id="systemChecks"></div></article>
          <article class="modern-card compact-card dashboard-events-card"><div class="section-title"><h2>最近事件</h2><button class="btn btn-soft" onclick="openDashboardEventList()">查看事件</button></div><div class="operator-event-list" id="dashboardEvents"></div></article>
        </section>
        <div id="dashboardEventModal" class="modal-mask hidden"><div class="modal-card"><header><h2 id="dashboardEventTitle">事件详情</h2><button class="icon-btn" onclick="dashboardEventModal.classList.add('hidden')">×</button></header><div class="detail-grid" id="dashboardEventBody"></div><footer><button class="btn btn-soft" onclick="dashboardEventModal.classList.add('hidden')">关闭</button><button class="btn btn-primary" id="dashboardEventLink">打开关联页面</button></footer></div></div>
        """;
    }

    private static string SamplesContent()
    {
        return """
        <section class="control-strip modern-card v18-sample-actions compact-toolbar">
          <div class="control-main">
            <b>A-D 样本区</b>
            <span class="badge-soft" id="sampleBadge">0/16 已占用</span>
            <button class="btn btn-primary" onclick="scanSamples()">刷新</button>
            <button class="btn btn-soft" onclick="openConfirmModal('ihc-tl')">通灵码 / 一抗码</button>
            <button class="btn btn-soft" onclick="openConfirmModal('ihc-hospital')">医院码 / LIS</button>
            <button class="btn btn-soft" onclick="openConfirmModal('he')">HE</button>
          </div>
          <div class="scan-hint one-line"><span>先选 HE / IHC 实验类型，再添加样本；默认流程由后端自动绑定，启动后锁定。</span></div>
        </section>
        <section class="sample-screen modern-card"><div class="sample-cabinet v18-slot-grid" id="sampleCabinet"></div></section>
        <div id="sampleConfirmModal" class="modal-mask hidden"><div class="modal-card sample-confirm-card"><header><h2 id="confirmTitle">添加样本</h2><button class="icon-btn" onclick="closeConfirmModal()">×</button></header><div class="form-grid-mini"><label>目标 Slot<select id="confirmSlot" class="input"></select></label><label>识别路径<select id="confirmPath" class="input"><option value="ihc-tl">通灵码 / 一抗码</option><option value="ihc-hospital">医院码 / LIS</option><option value="he">HE 手动确认</option></select></label><label id="rawCodeLabel">样本码 / 一抗码<input class="input" id="rawCode" value="PA1"></label><label id="primaryAntibodyLabel" class="hidden">确认一抗<select id="primaryAntibodySelect" class="input"></select></label></div><div class="notice-box" id="confirmChannelScript">实验类型：未选择</div><div class="notice-box hidden" id="sampleTaskError"></div><footer><button class="btn btn-soft" onclick="closeConfirmModal()">取消</button><button class="btn btn-primary" id="confirmTaskButton" onclick="confirmTask()">确认创建任务</button></footer></div></div>
        <div id="sampleDetailModal" class="modal-mask hidden"><div class="modal-card sample-confirm-card"><header><h2>样本详情</h2><button class="icon-btn" onclick="closeSampleDetailModal()">×</button></header><div id="sampleDetailBody" class="detail-grid"></div><footer><button class="btn btn-soft" onclick="closeSampleDetailModal()">关闭</button><button class="btn btn-primary" onclick="location.href='/history'">查看追溯</button></footer></div></div>
        <div id="channelScriptModal" class="modal-mask hidden"><div class="modal-card sample-confirm-card"><header><h2 id="channelScriptTitle">选择实验类型</h2><button class="icon-btn" onclick="closeChannelScriptModal()">×</button></header><div class="experiment-type-options"><button type="button" onclick="applyChannelExperimentTypeSelection('HE')"><b>HE</b><span>使用当前默认 HE 流程</span></button><button type="button" onclick="applyChannelExperimentTypeSelection('IHC')"><b>IHC</b><span>使用当前默认 IHC 流程</span></button></div><label class="field-label hidden" id="channelScriptReasonLabel">变更原因<input id="channelScriptReason" class="input" placeholder="请输入变更原因"></label><div class="notice-box" id="channelScriptHint">后端将自动绑定并冻结当前默认已发布流程。</div><footer><button class="btn btn-soft" onclick="closeChannelScriptModal()">取消</button></footer></div></div>
        """;
    }

    private static string ReagentsContent()
    {
        return """
        <section class="control-strip modern-card compact-toolbar reagent-toolbar"><div class="control-main reagent-scan-actions"><b>5×8 试剂架</b><span class="badge-soft" id="reagentBadge">0/40 VALID</span><button class="btn btn-primary" id="startReagentScanSessionBtn" onclick="startReagentScanSession()">开始扫码</button><button class="btn btn-soft" id="completeReagentScanSessionBtn" onclick="completeReagentScanSession()" disabled>完成扫码</button><button class="btn btn-soft" onclick="scanReagents()">扫描全部</button><button class="btn btn-soft" onclick="mockColumnScan(1)">ch1</button><button class="btn btn-soft" onclick="mockColumnScan(2)">ch2</button><button class="btn btn-soft" onclick="mockColumnScan(3)">ch3</button><button class="btn btn-soft" onclick="mockColumnScan(4)">ch4</button><button class="btn btn-soft" onclick="mockColumnScan(5)">ch5</button></div><div class="scan-hint one-line"><span id="reagentScanSessionSummary">扫码会话：读取中...</span></div></section>
        <section class="split-grid wide-left reagent-v18-layout"><article class="modern-card reagent-rack-card"><div class="reagent-deck v18-reagent-deck" id="reagentDeck"></div></article><aside class="stack-panel reagent-side-panel"><article class="modern-card compact-card"><h3>列扫码状态</h3><div class="column-status-grid" id="columnStatus"></div></article><article class="modern-card compact-card dab-formal-panel"><div class="section-title"><h3>DAB M1-M8</h3><button class="btn btn-soft" onclick="refreshDabPositions()">刷新</button></div><div class="dab-position-grid" id="dabPositionGrid"></div></article></aside></section>
        <div id="reagentDetail" class="modal-mask hidden"><div class="modal-card"><header><h2>试剂瓶解析详情</h2><button class="icon-btn" onclick="reagentDetail.classList.add('hidden')">×</button></header><div id="reagentDetailBody" class="detail-grid"></div><footer><button class="btn btn-soft" onclick="reagentDetail.classList.add('hidden')">关闭</button><button class="btn btn-primary" onclick="reagentDetail.classList.add('hidden');openReagentScanModal(window.reagentDetailPosition)">重新扫码</button></footer></div></div>
        <div id="reagentScanModal" class="modal-mask hidden"><div class="modal-card"><header><h2 id="reagentScanTitle">R 位扫码确认</h2><button class="icon-btn" onclick="cancelReagentScanModal()">×</button></header><div class="detail-grid" id="reagentScanContext"></div><div class="form-grid-mini"><label>输入方式<select id="reagentScanMode" class="input" onchange="syncReagentScanMode()"><option value="barcode">Mock 扫描文本</option><option value="empty">空位确认</option></select></label><label id="reagentBarcodeLabel">Mock 条码文本<input id="reagentBarcodeInput" class="input" placeholder="例如 ABC05020270101001"></label><label id="reagentExpirationLabel">有效期<input id="reagentExpirationInput" class="input" type="date"></label></div><div class="notice-box" id="reagentScanHint">前端只提交原始文本，条码解析和校验由后端完成。</div><div id="reagentScanResult" class="detail-grid"></div><footer><button class="btn btn-soft" onclick="cancelReagentScanModal()">取消</button><button class="btn btn-primary" onclick="confirmReagentPositionScan()">确认扫码</button></footer></div></div>
        <div id="dabDetailModal" class="modal-mask hidden"><div class="modal-card"><header><h2>DAB 批次详情</h2><button class="icon-btn" onclick="dabDetailModal.classList.add('hidden')">×</button></header><div id="dabDetailBody" class="detail-grid"></div><footer><button class="btn btn-soft" onclick="dabDetailModal.classList.add('hidden')">关闭</button><button class="btn btn-soft" id="dabCleaningStartButton">启动清洗</button><button class="btn btn-primary" id="dabCleaningConfirmButton">确认清洗完成</button></footer></div></div>
        """;
    }

    private static string RunContent()
    {
        return """
        <section class="run-command-bar modern-card v18-runbar"><button class="command start" id="runStartButton" onclick="runAction('start')"><i>▶</i><b>启动前预检</b><span>通过后再启动运行</span></button><button class="command pause" id="runPauseButton" onclick="runAction('pause')"><i>Ⅱ</i><b>暂停</b><span>当前原子动作后生效</span></button><button class="command resume" id="runResumeButton" onclick="runAction('resume')"><i>↪</i><b>恢复</b><span>等待/孵育直接下一步</span></button><button class="command stop" id="runStopButton" onclick="confirmStop()"><i>■</i><b>整机停止</b><span>当前动作后停止</span></button><button class="command fault hidden" id="mockFaultButton" onclick="injectMockFault()"><i>!</i><b>Mock 故障</b><span>工程师 / 管理员</span></button><button class="command redo hidden" id="redoMajorStepButton" onclick="redoCurrentMajorStep()"><i>↺</i><b>大步骤重做</b><span>需填写原因</span></button><div class="run-badge"><span id="runStatus" class="status-chip status-idle"><i></i><b data-status-label>idle</b></span><small id="runIdSmall">未生成批号</small></div></section>
        <section class="run-grid">
          <article class="modern-card run-channels-card">
            <div class="section-title"><div><h2>实时通道状态</h2><p>状态推进以动作完成反馈或明确状态条件为准。</p></div><button class="btn btn-soft" onclick="refreshRun()">刷新</button></div>
            <div id="runChannels" class="runtime-board"></div>
          </article>
          <aside class="run-side-stack">
            <article class="modern-card validation-card run-side-card"><div class="section-title"><div><h2>启动前校验</h2><p>失败时只允许返回处理。</p></div><button class="btn btn-soft" onclick="openValidationModal()">查看详情</button></div><div class="validation-grid"><div><b>样本任务</b><span id="validationSlides">0 张</span><em id="validationSlidesState">未创建</em></div><div><b>初始化</b><span id="validationInit">待完成</span><em id="validationInitState">BLOCK</em></div><div><b>试剂状态</b><span id="validationReagents">0 个有效瓶</span><em id="validationReagentsState">BLOCK</em></div><div><b>人工覆盖</b><span>不可绕过</span><em>禁止</em></div></div></article>
            <article class="modern-card compact-card run-side-card run-resource-card"><h3>正式运行快照</h3><div class="module-grid operator-run-resources" id="runResourceGrid"></div></article>
            <article class="modern-card compact-card run-side-card run-device-card"><div class="run-device-columns"><div><h3>双针状态</h3><div class="operator-device-list" id="runNeedles"></div></div><div><h3>命令阶段</h3><div class="operator-device-list command-stage-list" id="runCommandStages"></div></div></div></article>
            <article class="modern-card compact-card run-side-card run-log-card"><h3>事件日志</h3><div id="logList" class="log-console"></div></article>
          </aside>
        </section>
        <div id="validationModal" class="modal-mask hidden"><div class="modal-card large-modal"><header><h2>启动校验结果</h2><button class="icon-btn" onclick="validationModal.classList.add('hidden')">×</button></header><div id="validationBody" class="validation-result"></div><footer><button class="btn btn-soft" onclick="validationModal.classList.add('hidden')">返回处理</button><button class="btn btn-primary" id="validationStartBtn" onclick="forceStartAfterValidation()">全部通过后启动</button></footer></div></div>
        """;
    }

    private static string AlertsContent()
    {
        return """<section class="modern-card"><div class="section-title"><div><h2>告警查询</h2><p>Active / Acknowledged / Resolved 告警均来自正式数据库。</p></div><div class="button-row"><button class="btn btn-soft" onclick="loadTraceAlarms()">查询</button><button class="btn btn-soft" onclick="exportTraceCsv('alarms')">导出告警 CSV</button></div></div><div class="inline-form touch-form"><label>状态<select class="input" id="alarmStatusFilter"><option value="">全部</option><option>Active</option><option>Acknowledged</option><option>Resolved</option></select></label><label>级别<select class="input" id="alarmSeverityFilter"><option value="">全部</option><option>Info</option><option>Warning</option><option>Error</option><option>Critical</option></select></label><label>通道<select class="input" id="alarmChannelFilter"><option value="">全部</option><option>A</option><option>B</option><option>C</option><option>D</option></select></label><label>告警代码<input class="input" id="alarmCodeFilter" placeholder="reagent_insufficient"></label></div><div class="alarm-list" id="alarmList"></div></section><section class="modern-card"><div class="section-title"><h2>告警处理记录</h2><button class="btn btn-soft" onclick="location.href='/history'">历史与导出</button></div><div class="timeline large" id="alertLogs"></div></section>""";
    }

    private static string HistoryContent()
    {
        return """<section class="modern-card"><div class="section-title"><div><h2>筛选条件</h2><p>历史运行、玻片、试剂消耗、DAB、告警和审计均来自正式数据库。</p></div><div class="button-row"><button class="btn btn-primary" onclick="loadTraceHistory()">查询</button><button class="btn btn-soft" onclick="exportTraceCsv('history-runs')">导出运行 CSV</button><button class="btn btn-soft" onclick="exportTraceCsv('reagents')">导出试剂消耗 CSV</button></div></div><div class="inline-form touch-form"><label>开始时间<input class="input" id="historyFromFilter" type="datetime-local"></label><label>结束时间<input class="input" id="historyToFilter" type="datetime-local"></label><label>运行状态<input class="input" id="historyStatusFilter" placeholder="Completed"></label><label>通道<select class="input" id="historyChannelFilter"><option value="">全部</option><option>A</option><option>B</option><option>C</option><option>D</option></select></label><label>Slot<input class="input" id="historySlotFilter" placeholder="A-01"></label><label>HE/IHC<select class="input" id="historyExperimentFilter"><option value="">全部</option><option>HE</option><option>IHC</option></select></label><label>流程<input class="input" id="historyWorkflowFilter" placeholder="流程名称/版本"></label><label>样本码<input class="input" id="historySampleFilter" placeholder="样本码"></label><label>一抗<input class="input" id="historyPrimaryFilter" placeholder="001"></label><label>试剂<input class="input" id="historyReagentFilter" placeholder="HEM"></label><label>批号<input class="input" id="historyBatchFilter" placeholder="20260101"></label><label>操作人<input class="input" id="historyOperatorFilter" placeholder="operator"></label></div></section><section class="split-grid"><article class="modern-card"><div class="section-title"><h2>运行 / 通道 / 玻片记录</h2></div><div class="data-table history-table" id="historySlides"></div></article><article class="modern-card"><div class="section-title"><h2>试剂与消耗追溯</h2></div><div class="data-table history-table" id="historyReagents"></div></article></section><section class="modern-card"><div class="section-title"><h2>用户操作与配置版本日志</h2><div class="button-row"><button class="btn btn-soft" onclick="loadTraceAudit()">刷新审计</button><button class="btn btn-soft" onclick="exportTraceCsv('audit')">导出审计 CSV</button></div></div><div class="timeline large" id="historyLogs"></div></section>""";
    }

    private static string ConfigureContent()
    {
        return """<section class="kpi-grid"><article class="kpi-card"><span>流程生命周期</span><strong>Draft → Published → Retired</strong><small>发布后只读，需复制后修改</small></article><article class="kpi-card"><span>流程类型</span><strong>HE / IHC</strong><small>发布校验按实验类型执行</small></article><article class="kpi-card"><span>一抗映射</span><strong>001 → IHC</strong><small>兼容性校验实时读取映射</small></article><article class="kpi-card"><span>数据来源</span><strong>SQLite</strong><small>页面刷新后仍读取正式后端</small></article></section><section class="split-grid wide-left"><article class="modern-card"><div class="section-title"><div><h2>流程版本管理</h2><p>管理员可创建 Draft、复制版本、发布、停用；Published / Retired 不可直接编辑。</p></div><div class="button-row"><button class="btn btn-primary" onclick="createWorkflowDraft()">新建流程 Draft</button><button class="btn btn-soft" onclick="copyWorkflowDraft()">复制为 Draft</button></div></div><div class="protocol-version-table" id="protocolTable"></div></article><aside class="stack-panel"><article class="modern-card compact-card"><h3>步骤类型</h3><div class="tag-cloud"><span>Dispense</span><span>Incubate</span><span>Wash</span><span>Mix</span><span>Heat</span><span>Dab</span><span>Manual</span></div></article><article class="modern-card compact-card"><h3>DAB 计算预览</h3><div class="dab-numbers small-dab" id="dabPreview"></div></article></aside></section><section class="modern-card" id="workflowVersionDetail"><div class="section-title"><div><h2>版本详情</h2><p>选择一个版本后查看步骤、试剂需求、发布校验和可用操作。</p></div><span class="badge-soft">未选择</span></div><div class="empty-state"><b>请选择流程版本</b><span>从上方版本列表点击“详情”。</span></div></section><section class="split-grid"><article class="modern-card"><div class="section-title"><h2>一抗代码 - Published IHC 脚本映射</h2><button class="btn btn-primary" onclick="createPrimaryAntibodyMapping()">新增映射</button></div><div class="data-table mapping-table" id="primaryAntibodyMappingTable"></div></article><article class="modern-card"><div class="section-title"><h2>试剂目录与液体参数引用</h2><button class="btn btn-soft" onclick="renderConfigure()">刷新</button></div><div class="data-table reagent-catalog-table" id="catalogTable"></div></article></section>""";
    }

    private static string EngineerContent()
    {
        return """<section class="modern-card engineer-session"><div class="section-title"><div><h2>ENG-01 工程入口与会话</h2><p>二次认证、工程权限说明、设备是否可调试、剩余会话时间和工程操作日志。</p></div><span class="badge-soft">管理员受控入口</span></div><div class="validation-grid"><div><b>运行中任务</b><span id="engineerRunState">无活动任务</span><em>可模拟</em></div><div><b>二次确认</b><span>危险动作前再次确认</span><em>启用</em></div><div><b>会话剩余</b><span>30 分钟</span><em>Mock</em></div><div><b>真实设备</b><span>P1 未关闭前禁用危险动作</span><em>安全</em></div></div></section><section class="engineer-layout v18-engineer-layout"><article class="modern-card engineer-card"><div class="section-title"><h2>ENG-02 设备连接与通讯</h2><span class="badge-soft">COM / SDK</span></div><div class="form-grid-mini"><label>主控 COM<input id="serialPort" class="input" value="COM1"></label><label>波特率<input id="baudRate" class="input" value="115200"></label><label>SOCON SDK<input class="input" value="Mock Adapter"></label></div><div class="button-row"><button class="btn btn-primary" onclick="engineerCommand('serial','connect')">连接测试</button><button class="btn btn-soft" onclick="engineerCommand('serial','reconnect')">重连</button></div></article><article class="modern-card engineer-card"><div class="section-title"><h2>ENG-07 移液与洗针测试</h2><span class="badge-soft">Mock</span></div><div class="inline-form touch-form"><label>动作<select id="pipetteAction" class="input"><option value="aspirate">吸液</option><option value="dispense">加液</option><option value="detect_liquid">探液</option><option value="wash">洗针/冲洗</option></select></label><label>针<select id="needleSelect" class="input"><option>针1</option><option>针2</option><option>双针同步</option></select></label><label>位置<input id="wellPosition" class="input" value="R1"></label><label>体积 μL<input id="pipetteVolume" class="input" type="number" value="100"></label></div><button class="btn btn-primary full" onclick="testPipette()">执行模拟移液/洗针</button></article><article class="modern-card engineer-card"><div class="section-title"><h2>ENG-08 温控与制冷</h2><span class="badge-soft">4 板 × 4 点</span></div><div class="form-grid-mini"><label>测试通道<input id="heaterChannel" class="input" type="number" min="1" max="4" value="1"></label><label>目标温度 ℃<input id="targetTemp" class="input" type="number" value="42"></label></div><button class="btn btn-soft full" onclick="engineerCommand('heater','set_temperature')">设温测试</button></article><article class="modern-card engineer-card"><div class="section-title"><h2>ENG-09 清洗泵与混匀</h2><span class="badge-soft">PWM -100~+100</span></div><div class="form-grid-mini"><label>清洗泵通道<input id="pumpChannel" class="input" type="number" min="1" max="4" value="1"></label><label>混匀通道<input id="mixChannel" class="input" type="number" min="1" max="4" value="1"></label><label>PWM<input id="pumpPwm" class="input" type="number" min="-100" max="100" value="60"></label><label>时间 s<input id="pumpDuration" class="input" type="number" value="10"></label></div><div class="button-row"><button class="btn btn-primary" onclick="testPump()">通道清洗</button><button class="btn btn-soft" onclick="engineerCommand('pump','stop')">停止泵</button><button class="btn btn-soft" onclick="engineerCommand('mixer','start')">混匀测试</button></div></article></section><section class="modern-card"><div class="section-title"><h2>调试返回 / 原始通讯</h2><button class="btn btn-soft" onclick="engineerResult.textContent='等待命令...'">清空</button></div><pre id="engineerResult" class="terminal">等待命令...</pre></section>""";
    }

    private static string AdminContent()
    {
        return """<section class="kpi-grid admin-kpi"><article class="kpi-card"><span>用户账号</span><strong id="adminUserCount">0</strong><small>操作员 / 管理员；工程入口受控</small></article><article class="kpi-card"><span>试剂记录</span><strong id="adminReagentCount">0</strong><small>库存与条码审计</small></article><article class="kpi-card"><span>日志条数</span><strong id="adminLogCount">0</strong><small>动作与异常追溯</small></article><article class="kpi-card"><span>告警</span><strong id="adminAlarmCount">0</strong><small>未处理/历史告警</small></article></section><section class="split-grid"><article class="modern-card"><div class="section-title"><h2>用户管理</h2><div class="button-row"><button class="btn btn-primary">新增用户</button><button class="btn btn-soft">重置密码</button></div></div><div class="data-table user-table" id="userTable"></div></article><article class="modern-card"><div class="section-title"><h2>审计筛选</h2><button class="btn btn-soft" onclick="exportTraceCsv('audit')">导出审计 CSV</button></div><div class="inline-form touch-form"><label>用户<input class="input" id="auditUserFilter" placeholder="admin"></label><label>动作<input class="input" id="auditActionFilter" placeholder="workflow"></label><label>对象<input class="input" id="auditEntityFilter" placeholder="MachineRun"></label><label>通道<input class="input" id="auditChannelFilter" placeholder="A"></label><label>Slot<input class="input" id="auditSlotFilter" placeholder="A-01"></label><label>RunId<input class="input" id="auditRunFilter"></label><label>TaskId<input class="input" id="auditTaskFilter"></label><label>CommandId<input class="input" id="auditCommandFilter"></label><label>CorrelationId<input class="input" id="auditCorrelationFilter"></label></div><button class="btn btn-primary full" onclick="loadTraceAudit()">查询审计</button></article></section><section class="modern-card"><div class="section-title"><h2>审计日志</h2><button class="btn btn-soft" onclick="loadTraceAudit()">刷新</button></div><div class="timeline large" id="adminLogs"></div></section>""";
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
