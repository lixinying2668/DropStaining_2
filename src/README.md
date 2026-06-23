# 全自动冰冻切片染色机上位机原型系统（FastAPI + Jinja2）

这是一个可运行的工程原型，用于把“实验流程、通道状态、试剂扫描、样本扫描、运行调度、工程师调试”先拆清楚，再进入正式硬件对接。

当前版本重点（现代触屏版）：

- 使用 **FastAPI** 提供后端 API。
- 使用 **Jinja2 + 原生 JavaScript + CSS** 实现现代化触屏 HMI，不依赖大型前端框架。
- UI 按 **13.3 寸 1920×1080 横屏** 优先设计，包含大按钮、大卡片、状态色、通道舱位图、试剂架地图、运行驾驶舱。
- 固定建模为 **4 个通道 × 每通道 4 张玻片**。
- 试剂区建模为 **5 列 × 8 位 = 40 个试剂位**。
- IHC 流程已按步骤表内置，包括阻断、一抗、二抗、DAB、苏木素、清洗和混匀。
- DAB 按 `A:B:纯水 = 1:1:18` 计算，默认每轮多配 0.4 mL。
- 提供模拟设备网关，后续可替换为机械臂 SDK、下位机串口协议、扫码器协议。

## 目录结构

```text
industrial_stainer_fastapi/
├── app/
│   ├── main.py                  # FastAPI 入口
│   ├── api/routes.py            # 页面路由 + API 路由
│   ├── models.py                # Pydantic 数据模型
│   ├── services/
│   │   ├── dab.py               # DAB 配制计算
│   │   ├── device_gateway.py    # 模拟设备控制网关
│   │   ├── protocol_engine.py   # 实验脚本解析/任务生成
│   │   ├── scheduler.py         # 运行状态机/模拟调度器
│   │   └── store.py             # JSON 文件状态存储
│   ├── templates/               # 页面模板
│   ├── static/                  # CSS/JS
│   └── data/
│       ├── protocols/ihc.json   # IHC 脚本
│       ├── protocols/he.json    # HE 脚本占位
│       ├── reagents.json        # 试剂扫描模拟数据
│       ├── liquid_classes.json  # 液体类型参数
│       ├── positions.json       # 坐标配置占位
│       ├── users.json           # 用户权限示例
│       └── runtime.json         # 运行状态
├── tests/
├── requirements.txt
└── run.bat
```

## 快速运行

```bash
cd industrial_stainer_fastapi
python -m venv .venv
.venv\Scripts\activate        # Windows
# source .venv/bin/activate    # Linux/macOS
pip install -r requirements.txt
uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
```

打开：

```text
http://127.0.0.1:8000
```

演示账号：

| 角色 | 用户名 | 密码 |
|---|---|---|
| 实验员 | operator | 123456 |
| 工程师 | engineer | 123456 |
| 管理员 | admin | 123456 |

## 页面说明

| 页面 | 功能 |
|---|---|
| `/` | 登录页 |
| `/dashboard` | 现代化主控台：流程步骤条、KPI卡片、通道总览、准备检查、液路状态 |
| `/samples` | 样本扫描，显示 4×4 玻片位 |
| `/reagents` | 试剂扫描，显示 5×8 试剂位 |
| `/configure` | 每张玻片配置脚本、一抗、温度、体积，并计算 DAB |
| `/run` | 运行驾驶舱：大触控命令、实时通道卡、执行单元状态、运行日志、中途加片 |
| `/engineer` | 工程师触屏调试：通讯、扫码器、泵、混匀、加热、移液、坐标/液体类型参数入口 |
| `/admin` | 用户、试剂、日志管理原型 |

## 与正式硬件对接的位置

正式对接时优先替换：

```text
app/services/device_gateway.py
```

建议接口边界：

- 上位机：任务调度、流程脚本、参数计算、UI、数据记录。
- 下位机：泵、电机、温控、限位、报警、实时保护。
- 机械臂：由 SDK 执行坐标运动和双针控制，上位机只做动作编排。

## 运行逻辑说明

当前 scheduler 是“演示级模拟调度器”，用于验证 UI、流程、状态机和 API。它会把真实实验秒数压缩成较短的演示时间。正式开发时建议保留 API 和状态模型，将 `Scheduler._run_loop` 改造成真实任务队列。

## 测试

```bash
pytest
```

## 后续建议

1. 先冻结 `models.py` 的状态结构和 `routes.py` 的 API 契约。
2. 再根据下位机协议补充 `device_gateway.py`。
3. 最后再做调度优化，例如双针并行、一抗聚类、通道级清洗同步、异常恢复断点。
