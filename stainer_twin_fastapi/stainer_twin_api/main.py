
from __future__ import annotations

import os
from pathlib import Path
from typing import Any

from fastapi import FastAPI, HTTPException, Query
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles

from .database import connect, many, one, table_exists
from .snapshot import build_snapshot, mapping_rows

PACKAGE_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DB_PATH = Path(r"D:\Stainer\data\stainer.db")
REGISTRY_PATH = PACKAGE_ROOT / "data" / "frontend_registry.json"
MAPPING_CSV_PATH = PACKAGE_ROOT / "data" / "frontend_db_mapping.csv"
STATIC_DIR = PACKAGE_ROOT / "static"


def get_db_path() -> Path:
    return Path(os.environ.get("STAINER_DB_PATH", str(DEFAULT_DB_PATH))).resolve()

app = FastAPI(title="冰免染色机 2D 数字孪生 API", version="0.1.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=os.environ.get("STAINER_CORS_ORIGINS", "*").split(","),
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
app.mount("/static", StaticFiles(directory=str(STATIC_DIR)), name="static")


@app.get("/")
def index() -> FileResponse:
    html = STATIC_DIR / "index_db_bound.html"
    if not html.exists():
        html = STATIC_DIR / "index_original_modified.html"
    return FileResponse(str(html), media_type="text/html; charset=utf-8")


@app.get("/api/health")
def health() -> dict[str, Any]:
    db_path = get_db_path()
    try:
        with connect(db_path) as conn:
            return {"ok": True, "db_path": str(db_path), "tables": len(many(conn, "SELECT name FROM sqlite_master WHERE type='table'"))}
    except Exception as exc:
        return {"ok": False, "db_path": str(db_path), "error": str(exc)}


@app.get("/api/twin/snapshot")
def twin_snapshot() -> dict[str, Any]:
    try:
        with connect(get_db_path()) as conn:
            return build_snapshot(conn, REGISTRY_PATH)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc


@app.get("/api/twin/value/{control_id}")
def twin_value(control_id: str) -> dict[str, Any]:
    try:
        with connect(get_db_path()) as conn:
            snap = build_snapshot(conn, REGISTRY_PATH)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc
    return {"control_id": control_id, "value": snap.get("control_values", {}).get(control_id, None)}


@app.get("/api/twin/mapping")
def twin_mapping(status: str | None = Query(default=None, description="linked / partial / unlinked")) -> list[dict[str, str]]:
    rows = mapping_rows(MAPPING_CSV_PATH)
    if status:
        rows = [r for r in rows if r.get("link_status") == status]
    return rows


@app.get("/api/twin/mapping.csv")
def twin_mapping_csv() -> FileResponse:
    return FileResponse(str(MAPPING_CSV_PATH), media_type="text/csv; charset=utf-8", filename="frontend_db_mapping.csv")


@app.get("/api/db/tables")
def db_tables() -> list[dict[str, Any]]:
    with connect(get_db_path()) as conn:
        tables = many(conn, "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")
        out = []
        for t in tables:
            name = t["name"]
            count = one(conn, f'SELECT COUNT(*) AS count FROM "{name}"') if table_exists(conn, name) else {"count": None}
            out.append({"table": name, "count": count.get("count") if count else None})
        return out
