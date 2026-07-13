# 冰免染色机 2D 数字孪生 FastAPI 后端

## 启动

```bash
cd stainer_twin_fastapi
python -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -r requirements.txt
uvicorn stainer_twin_api.main:app --reload --host 0.0.0.0 --port 8000
```

打开：<http://localhost:8000/>

## 数据库

默认读取 `D:\Stainer\data\stainer.db`。也可以指定：

```bash
export STAINER_DB_PATH=/path/to/stainer.db
uvicorn stainer_twin_api.main:app --reload --port 8000
```

## 关键接口

- `GET /api/health`：健康检查和表数量。
- `GET /api/twin/snapshot`：前端一次性快照；所有缺失字段返回 `null`。
- `GET /api/twin/value/{control_id}`：单控件值读取。
- `GET /api/twin/mapping`：控件到数据库映射清单。
- `GET /api/twin/mapping.csv`：下载 CSV。

## null 规则

数据库没有对应表、没有对应行、字段为空、JSON 中没有该 key，统一返回 `null`。前端补丁中不再使用随机数作为真实数据替代。
