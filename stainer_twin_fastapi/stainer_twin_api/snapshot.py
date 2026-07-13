
from __future__ import annotations

import csv
import json
import math
import sqlite3
from datetime import datetime, timezone, date
from pathlib import Path
from typing import Any

from .database import has_columns, many, one, table_exists

DRAWER_TO_CHANNEL = {"A": 1, "B": 2, "C": 3, "D": 4}
CHANNEL_TO_DRAWER = {v: k for k, v in DRAWER_TO_CHANNEL.items()}


def null() -> None:
    return None


def deci_c(value: Any) -> float | None:
    try:
        if value is None:
            return None
        return round(float(value) / 10.0, 1)
    except (TypeError, ValueError):
        return None


def pct(current: Any, capacity: Any) -> float | None:
    try:
        if current is None or capacity in (None, 0):
            return None
        return round(float(current) / float(capacity) * 100.0, 1)
    except (TypeError, ValueError, ZeroDivisionError):
        return None


def status_to_frontend(status: Any) -> str | None:
    if status is None:
        return None
    s = str(status).lower()
    if s in {"completed", "complete", "succeeded", "success", "available", "stable", "confirmed", "waitingunload"}:
        return "complete"
    if s in {"running", "active", "inprogress", "processing"}:
        return "running"
    if s in {"failed", "error", "faulted", "invalid"}:
        return "error"
    if s in {"idle", "off", "ready", "normal"}:
        return "idle"
    return str(status)


def reagent_frontend_name_from_db_position(position_no: int | None = None, column_no: int | None = None, row_no: int | None = None) -> str | None:
    if column_no and row_no:
        return f"试剂_S{int(column_no)}{int(row_no)}"
    if position_no:
        pos = int(position_no)
        col = (pos - 1) // 8 + 1
        row = (pos - 1) % 8 + 1
        return f"试剂_S{col}{row}"
    return None


def slide_frontend_name(drawer_code: str | None, slot_no: int | None) -> str | None:
    if not drawer_code or slot_no is None:
        return None
    ch = DRAWER_TO_CHANNEL.get(str(drawer_code).upper())
    if not ch:
        return None
    return f"R{ch}{int(slot_no)}"


def mix_frontend_name(position_no: int | None) -> str | None:
    if position_no is None:
        return None
    pos = int(position_no)
    row = (pos - 1) // 2 + 1
    col = (pos - 1) % 2 + 1
    return f"配液_R{row}_C{col}"


def load_registry(registry_path: Path | None) -> list[dict[str, Any]]:
    if registry_path and registry_path.exists():
        return json.loads(registry_path.read_text(encoding="utf-8"))
    return []


def latest_reagent_placements(conn: sqlite3.Connection) -> dict[str, dict[str, Any]]:
    if not (has_columns(conn, "reagent_rack_placements", ["reagent_bottle_id", "reagent_rack_position_id", "removed_at_utc"]) and
            has_columns(conn, "reagent_rack_positions", ["id", "code", "position_no", "column_no", "row_no"]) and
            has_columns(conn, "reagent_bottles", ["id", "remaining_volume_ul", "initial_volume_ul", "status", "reagent_code", "full_barcode"])):
        return {}
    rows = many(conn, """
        SELECT p.code, p.position_no, p.column_no, p.row_no,
               b.reagent_code, b.full_barcode, b.remaining_volume_ul, b.initial_volume_ul, b.status
        FROM reagent_rack_placements rp
        JOIN reagent_rack_positions p ON p.id = rp.reagent_rack_position_id
        JOIN reagent_bottles b ON b.id = rp.reagent_bottle_id
        WHERE rp.removed_at_utc IS NULL
        ORDER BY rp.placed_at_utc DESC
    """)
    out: dict[str, dict[str, Any]] = {}
    for r in rows:
        name = reagent_frontend_name_from_db_position(r.get("position_no"), r.get("column_no"), r.get("row_no"))
        if name and name not in out:
            out[name] = r
    return out


def build_items(conn: sqlite3.Connection, registry: list[dict[str, Any]]) -> list[dict[str, Any]]:
    items: list[dict[str, Any]] = []
    placements = latest_reagent_placements(conn)
    # Reagents: include every frontend reagent; unplaced positions return null level/status.
    for row in registry:
        if row.get("category") != "试剂区":
            continue
        name = row.get("name")
        p = placements.get(name)
        if p:
            items.append({"name": name, "state": status_to_frontend(p.get("status")), "level": pct(p.get("remaining_volume_ul"), p.get("initial_volume_ul")), "reagentCode": p.get("reagent_code"), "barcode": p.get("full_barcode")})
        else:
            items.append({"name": name, "state": None, "level": None, "reagentCode": None, "barcode": None})
    # DAB mix positions
    if has_columns(conn, "dab_mix_positions", ["position_no", "status", "active_dab_batch_id"]):
        active_batches: dict[str, dict[str, Any]] = {}
        if has_columns(conn, "dab_batches", ["id", "remaining_volume_ul", "total_volume_ul", "expires_at_utc", "status"]):
            active_batches = {r["id"]: r for r in many(conn, "SELECT * FROM dab_batches") if r.get("id")}
        for r in many(conn, "SELECT * FROM dab_mix_positions ORDER BY position_no"):
            name = mix_frontend_name(r.get("position_no"))
            if not name:
                continue
            batch = active_batches.get(r.get("active_dab_batch_id"))
            items.append({
                "name": name,
                "state": status_to_frontend(r.get("status")),
                "level": pct(batch.get("remaining_volume_ul"), batch.get("total_volume_ul")) if batch else None,
                "validUntilUtc": batch.get("expires_at_utc") if batch else None,
            })
    # A/B source bottles only have coordinate points in sample DB -> values null
    for name in ["A液", "B液"]:
        if any(r.get("name") == name for r in registry):
            items.append({"name": name, "state": None, "level": None})
    return items


def build_slide_temps(conn: sqlite3.Connection) -> list[dict[str, Any]]:
    if not has_columns(conn, "thermal_point_states", ["drawer_code", "slot_no", "current_temperature_deci_c", "target_temperature_deci_c", "status"]):
        return []
    out = []
    for r in many(conn, "SELECT * FROM thermal_point_states"):
        name = slide_frontend_name(r.get("drawer_code"), r.get("slot_no"))
        if name:
            out.append({"name": name, "temp": deci_c(r.get("current_temperature_deci_c")), "targetTemp": deci_c(r.get("target_temperature_deci_c")), "state": status_to_frontend(r.get("status"))})
    return out


def build_slide_ops(conn: sqlite3.Connection) -> list[dict[str, Any]]:
    if not (has_columns(conn, "slide_tasks", ["physical_slot_id", "slot_code", "status"]) and has_columns(conn, "physical_slots", ["id", "code", "slot_no", "drawer_id"]) and has_columns(conn, "drawers", ["id", "code"])):
        return []
    rows = many(conn, """
        SELECT st.status, ps.slot_no, d.code AS drawer_code
        FROM slide_tasks st
        JOIN physical_slots ps ON ps.id = st.physical_slot_id
        JOIN drawers d ON d.id = ps.drawer_id
    """)
    out = []
    for r in rows:
        name = slide_frontend_name(r.get("drawer_code"), r.get("slot_no"))
        if name:
            done = status_to_frontend(r.get("status")) == "complete"
            out.append({"name": name, "steps": [done]})
    return out


def build_channels(conn: sqlite3.Connection) -> list[dict[str, Any]]:
    channels = [{"id": i, "state": None, "progress": None, "pulled": None, "configProfileId": None} for i in range(1, 5)]
    if has_columns(conn, "channel_batches", ["drawer_code", "status", "selected_workflow_version_id"]):
        for r in many(conn, "SELECT * FROM channel_batches"):
            ch = DRAWER_TO_CHANNEL.get(str(r.get("drawer_code") or "").upper())
            if not ch:
                continue
            channels[ch - 1].update({
                "state": status_to_frontend(r.get("status")),
                "progress": 100 if status_to_frontend(r.get("status")) == "complete" else None,
                "configProfileId": r.get("selected_workflow_version_id"),
                "experimentType": r.get("experiment_type"),
            })
    return channels


def build_liquids(conn: sqlite3.Connection) -> dict[str, float | None]:
    out = {"pure": None, "pbs": None, "waste": None, "toxic": None}
    if not has_columns(conn, "liquid_container_states", ["source_type", "current_volume_ul", "capacity_ul"]):
        return out
    map_key = {"SystemWater": "pure", "PBS": "pbs", "Waste": "waste", "ToxicWaste": "toxic"}
    for r in many(conn, "SELECT * FROM liquid_container_states"):
        key = map_key.get(r.get("source_type"))
        if key:
            out[key] = pct(r.get("current_volume_ul"), r.get("capacity_ul"))
    return out


def build_scalars(conn: sqlite3.Connection) -> dict[str, Any]:
    scalars: dict[str, Any] = {}
    cooling = one(conn, "SELECT * FROM cooling_unit_states LIMIT 1") if table_exists(conn, "cooling_unit_states") else None
    scalars["reagent_current_temperature_c"] = deci_c(cooling.get("current_temperature_deci_c")) if cooling else None
    scalars["reagent_target_temperature_c"] = deci_c(cooling.get("target_temperature_deci_c")) if cooling else None
    scalars["reagent_cooling_status"] = cooling.get("status") if cooling else None
    scalars["reagent_cooling_connected"] = bool(cooling.get("is_connected")) if cooling and cooling.get("is_connected") is not None else None
    arm = one(conn, "SELECT * FROM robot_arm_states LIMIT 1") if table_exists(conn, "robot_arm_states") else None
    for axis in ["x", "y", "z"]:
        v = arm.get(f"current_{axis}_um") if arm else None
        scalars[f"arm_current_{axis}_mm"] = round(v / 1000.0, 3) if isinstance(v, (int, float)) else None
    scalars["arm_status"] = arm.get("status") if arm else None
    # Liquid container raw values and thresholds
    if table_exists(conn, "liquid_container_states"):
        for r in many(conn, "SELECT * FROM liquid_container_states"):
            key = {"SystemWater": "pure", "PBS": "pbs", "Waste": "waste", "ToxicWaste": "toxic"}.get(r.get("source_type"))
            if not key:
                continue
            scalars[f"{key}_current_volume_ul"] = r.get("current_volume_ul")
            scalars[f"{key}_capacity_ul"] = r.get("capacity_ul")
            scalars[f"{key}_low_threshold_ul"] = r.get("low_threshold_ul")
            scalars[f"{key}_full_threshold_ul"] = r.get("full_threshold_ul")
            scalars[f"{key}_level_status"] = r.get("level_status")
    # 染色区目标温度：取 thermal_point_states 的代表值（当前 DB 16 行均为 250 deci_c = 25.0℃）
    if table_exists(conn, "thermal_point_states"):
        row = one(conn, "SELECT MAX(target_temperature_deci_c) AS v FROM thermal_point_states")
        scalars["work_target_temperature_c"] = deci_c(row.get("v")) if row and row.get("v") is not None else None
    else:
        scalars["work_target_temperature_c"] = None
    # 试剂瓶默认容量：取 reagent_bottles 最大 initial_volume_ul（当前 DB 6 瓶均为 8000 → 8.0 ml）
    if table_exists(conn, "reagent_bottles"):
        row = one(conn, "SELECT MAX(initial_volume_ul) AS v FROM reagent_bottles")
        scalars["reagent_bottle_capacity_ml"] = round(row.get("v") / 1000.0, 1) if row and row.get("v") is not None else None
    else:
        scalars["reagent_bottle_capacity_ml"] = None
    return scalars


def build_metrics(conn: sqlite3.Connection) -> dict[str, int | None]:
    metrics = {"total": None, "today": None, "active": None}
    if table_exists(conn, "staining_tasks"):
        total = one(conn, "SELECT COUNT(*) AS c FROM staining_tasks")
        metrics["total"] = total.get("c") if total else None
        today = one(conn, "SELECT COUNT(*) AS c FROM staining_tasks WHERE substr(created_at_utc,1,10)=date('now')")
        metrics["today"] = today.get("c") if today else None
        active = one(conn, "SELECT COUNT(*) AS c FROM staining_tasks WHERE status NOT IN ('Completed','Cancelled','Failed')")
        metrics["active"] = active.get("c") if active else None
    return metrics


def build_precheck_results(conn: sqlite3.Connection) -> dict[str, bool | None]:
    labels = {
        "controller": "主控连接",
        "robot-arm": "机械臂回零",
        "cooling": "制冷连接",
        "sample-scanner": "样本扫码器在线",
        "reagent-scanner": "试剂扫码器在线",
        "sensors": "液位/传感器读取",
        "needle-wash": "洗针准备",
        "system-water": "纯水可用",
        "pbs": "PBS 可用",
        "waste": "废液未满",
        "toxic-waste": "排毒桶未满",
        "pump": "液位/传感器读取",
    }
    out = {v: None for v in set(labels.values())}
    if not table_exists(conn, "device_initialization_checks"):
        return out
    rows = many(conn, """
        SELECT c.* FROM device_initialization_checks c
        JOIN device_initialization_runs r ON r.id = c.device_initialization_run_id
        WHERE r.started_at_utc = (SELECT MAX(started_at_utc) FROM device_initialization_runs)
        ORDER BY c.step_no
    """) if table_exists(conn, "device_initialization_runs") else many(conn, "SELECT * FROM device_initialization_checks ORDER BY started_at_utc DESC")
    for r in rows:
        label = labels.get(r.get("module_code"))
        if label:
            out[label] = str(r.get("status")).lower() in {"succeeded", "completed", "success"}
    return out


def build_logs(conn: sqlite3.Connection) -> dict[str, list[dict[str, Any]]]:
    logs: list[dict[str, Any]] = []
    warnings: list[dict[str, Any]] = []
    if table_exists(conn, "audit_logs"):
        for r in many(conn, "SELECT created_at_utc, action, message FROM audit_logs ORDER BY created_at_utc DESC LIMIT 30"):
            logs.append({"time": r.get("created_at_utc"), "type": "audit", "message": f"{r.get('action')}: {r.get('message')}"})
    if table_exists(conn, "command_receipts"):
        for r in many(conn, "SELECT created_at_utc, operation, status, error_message FROM command_receipts ORDER BY created_at_utc DESC LIMIT 30"):
            msg = f"{r.get('operation')} -> {r.get('status')}"
            if r.get("error_message"):
                msg += f": {r.get('error_message')}"
                warnings.append({"time": r.get("created_at_utc"), "type": "command", "message": msg})
            else:
                logs.append({"time": r.get("created_at_utc"), "type": "command", "message": msg})
    if table_exists(conn, "alarms"):
        for r in many(conn, "SELECT * FROM alarms ORDER BY created_at_utc DESC LIMIT 30"):
            warnings.append({"time": r.get("created_at_utc"), "type": "alarm", "message": r.get("message") or r.get("alarm_code") or "alarm"})
    return {"logs": logs[:50], "warnings": warnings[:50]}


def build_arm_payload(conn: sqlite3.Connection) -> dict[str, Any] | None:
    if not table_exists(conn, "robot_arm_states"):
        return None
    arm = one(conn, "SELECT * FROM robot_arm_states LIMIT 1")
    if not arm:
        return None
    payload: dict[str, Any] = {}
    if arm.get("current_x_um") is not None: payload["x"] = round(arm["current_x_um"] / 1000.0, 3)
    if arm.get("current_y_um") is not None: payload["y"] = round(arm["current_y_um"] / 1000.0, 3)
    if arm.get("current_z_um") is not None:
        payload["z1"] = round(arm["current_z_um"] / 1000.0, 3)
        payload["z2"] = round(arm["current_z_um"] / 1000.0, 3)
    return payload or None


def build_cameras(conn: sqlite3.Connection) -> dict[str, str | None]:
    cameras = {"reagent": None, "arm": None}
    if table_exists(conn, "reagent_scan_sessions"):
        r = one(conn, "SELECT status FROM reagent_scan_sessions ORDER BY started_at_utc DESC LIMIT 1")
        cameras["reagent"] = status_to_frontend(r.get("status")) if r else None
    if table_exists(conn, "sample_scan_sessions"):
        r = one(conn, "SELECT status FROM sample_scan_sessions ORDER BY started_at_utc DESC LIMIT 1")
        cameras["arm"] = status_to_frontend(r.get("status")) if r else None
    return cameras


def build_workflow_profiles(conn: sqlite3.Connection) -> list[dict[str, Any]]:
    if not (table_exists(conn, "workflow_versions") and table_exists(conn, "workflow_definitions") and table_exists(conn, "workflow_steps")):
        return []
    versions = many(conn, """
        SELECT v.*, d.name AS definition_name, d.workflow_type, d.description, d.code AS definition_code
        FROM workflow_versions v JOIN workflow_definitions d ON d.id = v.workflow_definition_id
        ORDER BY v.status='Published' DESC, v.version_no DESC
    """)
    profiles = []
    for v in versions:
        steps = many(conn, "SELECT * FROM workflow_steps WHERE workflow_version_id=? ORDER BY step_no", (v["id"],))
        profiles.append({
            "id": v.get("id"),
            "name": v.get("definition_name"),
            "stainType": v.get("workflow_type"),
            "version": str(v.get("version_label") or v.get("version_no") or ""),
            "description": v.get("description") or v.get("change_note") or "",
            "steps": [{
                "id": s.get("id"),
                "label": s.get("step_name") or s.get("major_step_code") or s.get("action_type"),
                "opKey": str(s.get("action_type") or "").lower() or "custom",
                "durationSec": s.get("duration_seconds"),
                "toleranceSec": 0,
                "immediateAfterPrev": False,
                "requiresTemp": s.get("target_temperature_deci_c") is not None,
                "targetTempC": deci_c(s.get("target_temperature_deci_c")),
                "reagentRole": s.get("reagent_code") or "",
                "notes": s.get("failure_strategy") or "",
            } for s in steps]
        })
    return profiles


def build_control_values(conn: sqlite3.Connection, registry: list[dict[str, Any]], scalars: dict[str, Any]) -> dict[str, Any]:
    values = {row.get("controlId"): None for row in registry if row.get("controlId")}
    # Explicit known controls. Missing controls in current lazy DOM remain in snapshot for later rendering.
    explicit = {
        "reagentTempText": scalars.get("reagent_current_temperature_c"),
        "settingsReagentTargetInput": scalars.get("reagent_target_temperature_c"),
        "reagentCoolingCurrentInput": scalars.get("reagent_current_temperature_c"),
        "reagentCoolingTargetInput": scalars.get("reagent_target_temperature_c"),
        "settingsPureThresholdInput": pct(scalars.get("pure_low_threshold_ul"), scalars.get("pure_capacity_ul")),
        "settingsPbsThresholdInput": pct(scalars.get("pbs_low_threshold_ul"), scalars.get("pbs_capacity_ul")),
        "settingsWasteThresholdInput": pct(scalars.get("waste_full_threshold_ul"), scalars.get("waste_capacity_ul")),
        "settingsToxicThresholdInput": pct(scalars.get("toxic_full_threshold_ul"), scalars.get("toxic_capacity_ul")),
        "settingsWorkTargetInput": scalars.get("work_target_temperature_c"),
        "settingsReagentCapacityInput": scalars.get("reagent_bottle_capacity_ml"),
        "settingsNeedleGapInput": None,
    }
    for k, v in explicit.items():
        values[k] = v
    # Coordinate editor dynamic fields can be resolved by frontend object code later; all uncalibrated fields stay null.
    return values


def build_snapshot(conn: sqlite3.Connection, registry_path: Path | None = None) -> dict[str, Any]:
    registry = load_registry(registry_path)
    scalars = build_scalars(conn)
    payload = {
        "items": build_items(conn, registry),
        "slideTemps": build_slide_temps(conn),
        "slideOps": build_slide_ops(conn),
        "channels": build_channels(conn),
        "liquids": build_liquids(conn),
        "metrics": build_metrics(conn),
        "cameras": build_cameras(conn),
    }
    arm = build_arm_payload(conn)
    if arm:
        payload["arm"] = arm
    profiles = build_workflow_profiles(conn)
    if profiles:
        payload["configProfiles"] = profiles
    precheck = build_precheck_results(conn)
    logs = build_logs(conn)
    return {
        "schema_version": 1,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "null_policy": "数据库表/行/字段/JSON key 不存在时返回 null；前端不得再用随机数补值。",
        "digitalTwinPayload": payload,
        "scalars": scalars,
        "precheckResults": precheck,
        "control_values": build_control_values(conn, registry, scalars),
        "logs": logs["logs"],
        "warnings": logs["warnings"],
    }


def mapping_rows(mapping_csv: Path) -> list[dict[str, str]]:
    with mapping_csv.open("r", encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))
