# -*- coding: utf-8 -*-
"""
stainer.db 测试数据填充脚本（一次性工具，不属于应用代码）。

设计原则：
  * 不修改任何应用代码，不改变任何表结构 —— 只 INSERT 行。
  * 单事务执行，PRAGMA foreign_keys=ON；任何约束违例自动 ROLLBACK，数据库保持原样。
  * 所有外键引用都从现有数据自查（users / drawers / slots / reagents / workflow_versions /
    coordinate / liquid_class 等），绝不凭空捏造 FK。
  * 状态枚举严格使用 Domain\Entities 下 C# 枚举定义的合法值。
  * 时间戳沿用库内既有格式 "YYYY-MM-DD HH:MM:SS.ffffff+00:00"。
  * 已有备份 data/stainer.db.bak.* 可随时回滚。

用法:
    python data/_seed_test_data.py            # 默认 dry-run，只打印计划，不写入
    python data/_seed_test_data.py --commit   # 实际写入
"""
from __future__ import annotations
import sqlite3, sys, json, uuid
from datetime import datetime, timedelta

DB = "data/stainer.db"
BASE = datetime(2026, 7, 12, 8, 0, 0, 0)  # 固定基线，不依赖系统时钟


def ts(minute=0, second=0):
    d = BASE + timedelta(minutes=minute, seconds=second)
    return d.strftime("%Y-%m-%d %H:%M:%S.%f") + "+00:00"


def uid():
    return str(uuid.uuid4())


def hex10():
    return uuid.uuid4().hex[:10]


# ---------------------------------------------------------------------------
# 连接 + 引用自查
# ---------------------------------------------------------------------------
con = sqlite3.connect(DB)
con.row_factory = sqlite3.Row
con.execute("PRAGMA foreign_keys=ON")
cur = con.cursor()


def one(q, p=()):
    return cur.execute(q, p).fetchone()


def col(q, p=()):
    return [r[0] for r in cur.execute(q, p).fetchall()]


def all_rows(q, p=()):
    return [dict(r) for r in cur.execute(q, p).fetchall()]


# 关键引用 ID（均来自现有数据）
OP = one("SELECT id FROM users WHERE username='operator'")["id"]
AD = one("SELECT id FROM users WHERE username='admin'")["id"]
EG = one("SELECT id FROM users WHERE username='engineer'")["id"]

DRAWER = {r["code"]: r["id"] for r in cur.execute("SELECT code,id FROM drawers")}
# slot_code -> (id, drawer_code)
SLOT = {r["code"]: (r["id"], r["drawer_id"]) for r in cur.execute("SELECT code,id,drawer_id FROM physical_slots")}
# reagent_code -> definition_id
RDEF = {r["reagent_code"]: r["id"] for r in cur.execute("SELECT reagent_code,id FROM reagent_definitions")}
# reagent_code -> [bottle_id,...]
RBOT = {}
for r in cur.execute("SELECT reagent_code,id FROM reagent_bottles"):
    RBOT.setdefault(r["reagent_code"], []).append(r["id"])
REAGENT_CODES = list(RDEF.keys())

WV_HE = one("SELECT id FROM workflow_versions WHERE default_experiment_type='HE'")["id"]
WV_IHC = one("SELECT id FROM workflow_versions WHERE default_experiment_type='IHC'")["id"]
WD_HE = one("SELECT id FROM workflow_definitions WHERE workflow_type='HE'")["id"]
WD_IHC = one("SELECT id FROM workflow_definitions WHERE workflow_type='IHC'")["id"]

CPV = one("SELECT id FROM coordinate_profile_versions WHERE is_active=1")["id"]
LCV = one("SELECT id FROM liquid_class_versions WHERE status='Enabled'")["id"]
LCV_NO = one("SELECT version_no FROM liquid_class_versions WHERE id=?", (LCV,))[0]
LCV_PARAMS = one("SELECT change_summary_json FROM liquid_class_versions WHERE id=?", (LCV,))[0]

DAB_MIX = all_rows("SELECT id,code,position_no FROM dab_mix_positions WHERE is_enabled=1 ORDER BY position_no")
WASH_POS = col("SELECT code FROM wash_positions WHERE is_enabled=1")
RACK_POS = all_rows("SELECT id,code,scanner_channel_no,scanner_channel_code FROM reagent_rack_positions WHERE is_enabled=1 ORDER BY position_no")
COORD_POINTS = col("SELECT id FROM coordinate_points LIMIT 8")

print(f"[refs] operator/admin/engineer users OK")
print(f"[refs] drawers={list(DRAWER)} slots={len(SLOT)} reagents={REAGENT_CODES}")
print(f"[refs] WV_HE={WV_HE[:8]} WV_IHC={WV_IHC[:8]} CPV={CPV[:8]} LCV={LCV[:8]}")
print(f"[refs] dab_mix={len(DAB_MIX)} rack_pos={len(RACK_POS)} coord_points={len(COORD_POINTS)}")


def slot_code(drawer_letter, n):
    return f"{drawer_letter}-0{n}"


# ---------------------------------------------------------------------------
# 收集器：每张表挂一个 list[dict]
# ---------------------------------------------------------------------------
T = {k: [] for k in [
    "machine_runs", "channel_batches", "staining_tasks", "slide_tasks",
    "workflow_executions", "workflow_step_executions", "device_command_executions",
    "dispense_executions", "reagent_reservations", "reagent_consumptions",
    "machine_resource_leases", "alarms", "alarm_actions",
    "dab_batches", "dab_batch_tasks", "dab_batch_usages", "dab_repreparation_plans",
    "system_liquid_usages", "pipetting_operations", "fluidics_telemetry",
    "temperature_telemetry", "coordinate_calibration_history", "engineering_sessions",
    "sample_scan_sessions", "sample_scan_items", "lis_query_logs", "mock_lis_entries",
    "hospital_barcode_mappings", "legacy_import_runs", "legacy_import_issues",
    "legacy_runtime_snapshots", "liquid_class_version_differences",
    "liquid_class_validation_records", "mock_demo_data_tags",
    "primary_antibody_workflow_mappings", "reagent_scan_sessions", "reagent_scan_items",
    "device_initialization_runs", "device_initialization_checks",
    "device_communication_records", "command_receipts", "audit_logs",
]}


def run_code(tag):
    return f"RUN-{BASE.strftime('%Y%m%d%H%M%S')}-{hex10()}-{tag}"


# ===========================================================================
# 业务主线：3 个染色运行
# ===========================================================================

# --- Run 1：IHC / Running / Drawer C（2 张玻片） -----------------------------
RUN1 = uid()
T["machine_runs"].append(dict(
    id=RUN1, run_code=run_code("r1"), status="Running", pause_requested=0, stop_requested=0,
    created_at_utc=ts(0), started_at_utc=ts(4), completed_at_utc=None,
    coordinate_profile_version_id=CPV, current_major_step_code="PRIMARY_ANTIBODY",
    fault_message=None, requested_by_user_id=OP,
    coordinate_snapshot_json=json.dumps({"coordinateProfileVersionId": CPV}),
    liquid_class_snapshot_json=json.dumps({"schemaVersion": 1, "items": []}),
    liquid_class_selection_status="Frozen",
))
CB1 = uid()
T["channel_batches"].append(dict(
    id=CB1, drawer_id=DRAWER["C"], drawer_code="C", status="Running", experiment_type="IHC",
    machine_run_id=RUN1, selected_workflow_version_id=WV_IHC, coordinate_profile_version_id=CPV,
    workflow_selection_status="Locked", coordinate_selection_status="Frozen",
    liquid_class_selection_status="Frozen", needs_manual_resolution=0,
    manual_resolution_reason="", workflow_selected_by_user_id=OP,
    workflow_selected_at_utc=ts(1), workflow_locked_at_utc=ts(3), started_at_utc=ts(4),
    completed_at_utc=None, created_at_utc=ts(0),
    coordinate_snapshot_json=json.dumps({"coordinateProfileVersionId": CPV}),
    workflow_snapshot_json=json.dumps({"workflowVersionId": WV_IHC}),
    liquid_class_snapshot_json=json.dumps({"schemaVersion": 1, "items": []}),
))

# --- Run 2：HE / Failed / Drawer D（1 张玻片，清洗阶段泵故障） ---------------
RUN2 = uid()
T["machine_runs"].append(dict(
    id=RUN2, run_code=run_code("r2"), status="Failed", pause_requested=0, stop_requested=0,
    created_at_utc=ts(-60), started_at_utc=ts(-55), completed_at_utc=ts(-40),
    coordinate_profile_version_id=CPV, current_major_step_code="TERMINAL_WASH",
    fault_message="Wash pump PWM2 reported SensorFailure during terminal wash.",
    requested_by_user_id=OP,
    coordinate_snapshot_json=json.dumps({"coordinateProfileVersionId": CPV}),
    liquid_class_snapshot_json=json.dumps({"schemaVersion": 1, "items": []}),
    liquid_class_selection_status="Frozen",
))
CB2 = uid()
T["channel_batches"].append(dict(
    id=CB2, drawer_id=DRAWER["D"], drawer_code="D", status="Faulted", experiment_type="HE",
    machine_run_id=RUN2, selected_workflow_version_id=WV_HE, coordinate_profile_version_id=CPV,
    workflow_selection_status="Locked", coordinate_selection_status="Frozen",
    liquid_class_selection_status="Frozen", needs_manual_resolution=0,
    manual_resolution_reason="", workflow_selected_by_user_id=OP,
    workflow_selected_at_utc=ts(-59), workflow_locked_at_utc=ts(-57), started_at_utc=ts(-55),
    completed_at_utc=ts(-40), created_at_utc=ts(-60),
    coordinate_snapshot_json=json.dumps({"coordinateProfileVersionId": CPV}),
    workflow_snapshot_json=json.dumps({"workflowVersionId": WV_HE}),
    liquid_class_snapshot_json=json.dumps({"schemaVersion": 1, "items": []}),
))

# --- Run 3：IHC / Completed / Drawer A（1 张玻片，含 DAB） -------------------
RUN3 = uid()
T["machine_runs"].append(dict(
    id=RUN3, run_code=run_code("r3"), status="Completed", pause_requested=0, stop_requested=0,
    created_at_utc=ts(-120), started_at_utc=ts(-115), completed_at_utc=ts(-80),
    coordinate_profile_version_id=CPV, current_major_step_code="TERMINAL_WASH",
    fault_message=None, requested_by_user_id=OP,
    coordinate_snapshot_json=json.dumps({"coordinateProfileVersionId": CPV}),
    liquid_class_snapshot_json=json.dumps({"schemaVersion": 1, "items": []}),
    liquid_class_selection_status="Frozen",
))
CB3 = uid()
T["channel_batches"].append(dict(
    id=CB3, drawer_id=DRAWER["A"], drawer_code="A", status="Completed", experiment_type="IHC",
    machine_run_id=RUN3, selected_workflow_version_id=WV_IHC, coordinate_profile_version_id=CPV,
    workflow_selection_status="Locked", coordinate_selection_status="Frozen",
    liquid_class_selection_status="Frozen", needs_manual_resolution=0,
    manual_resolution_reason="", workflow_selected_by_user_id=OP,
    workflow_selected_at_utc=ts(-119), workflow_locked_at_utc=ts(-117), started_at_utc=ts(-115),
    completed_at_utc=ts(-80), created_at_utc=ts(-120),
    coordinate_snapshot_json=json.dumps({"coordinateProfileVersionId": CPV}),
    workflow_snapshot_json=json.dumps({"workflowVersionId": WV_IHC}),
    liquid_class_snapshot_json=json.dumps({"schemaVersion": 1, "items": []}),
))

# ---------------------------------------------------------------------------
# staining_tasks + slide_tasks + workflow_executions + step_exec + device_commands
# ---------------------------------------------------------------------------
# (task_id, channel_batch, slot_code, task_type, primary_ab, workflow_version, workflow_def, run, run_status, slide_status, exec_status, steps)
# steps: list of (major_step_code, step_name, action_type, reagent_code, volume_ul, step_status, device_command_type, device_status)
plans = [
    (uid(), CB1, "C-01", "IHC", "P01", WV_IHC, WD_IHC, RUN1, "Running", "Running", "Running", [
        ("PRIMARY_ANTIBODY", "Primary antibody P01", "Dispense", "P01", 80, "Completed", "Dispense", "Completed"),
        ("WASH_AFTER_PRIMARY", "Wash after primary", "Wash", "WAS", 100, "Completed", "Wash", "Completed"),
        ("PRIMARY_DAB", "DAB development", "Dab", "DAB", 100, "Running", "Dab", "CommandSent"),
    ]),
    (uid(), CB1, "C-02", "IHC", "P01", WV_IHC, WD_IHC, RUN1, "Running", "Running", "Running", [
        ("PRIMARY_ANTIBODY", "Primary antibody P01", "Dispense", "P01", 80, "Completed", "Dispense", "Completed"),
        ("WASH_AFTER_PRIMARY", "Wash after primary", "Wash", "WAS", 100, "Running", "Wash", "DeviceAcknowledged"),
    ]),
    (uid(), CB2, "D-01", "HE", None, WV_HE, WD_HE, RUN2, "Failed", "Faulted", "Failed", [
        ("HEMATOXYLIN", "Hematoxylin", "Dispense", "HEM", 100, "Completed", "Dispense", "Completed"),
        ("TERMINAL_WASH", "Terminal wash", "Wash", "WAS", 100, "Failed", "Wash", "Failed"),
    ]),
    (uid(), CB3, "A-02", "IHC", "P01", WV_IHC, WD_IHC, RUN3, "Completed", "Completed", "Completed", [
        ("PRIMARY_ANTIBODY", "Primary antibody P01", "Dispense", "P01", 80, "Completed", "Dispense", "Completed"),
        ("WASH_AFTER_PRIMARY", "Wash after primary", "Wash", "WAS", 100, "Completed", "Wash", "Completed"),
        ("PRIMARY_DAB", "DAB development", "Dab", "DAB", 100, "Completed", "Dab", "Completed"),
        ("TERMINAL_WASH", "Terminal wash", "Wash", "WAS", 100, "Completed", "Wash", "Completed"),
    ]),
]

staining_by_run = {}   # run_id -> [staining_task_id, ...]
step_ids = {}          # (run_id, major_step_code) -> (workflow_step_execution_id, device_command_execution_id)
for (stid, cb, scode, ttype, ab, wv, wd, run, run_status, slide_status, exec_status, steps) in plans:
    slot_id, _ = SLOT[scode]
    staining_by_run.setdefault(run, []).append(stid)
    T["staining_tasks"].append(dict(
        id=stid, task_code=f"{ttype}-{BASE.strftime('%Y%m%d%H%M%S')}-{hex10()}",
        task_type=ttype, status="Confirmed", physical_slot_id=slot_id,
        workflow_definition_id=wd, workflow_version_id=wv,
        workflow_snapshot_json=json.dumps({"workflowVersionId": wv}),
        input_mode=("ManualHE" if ttype == "HE" else "ManualIHC"),
        raw_code=None, normalized_code=None, primary_antibody_code=ab,
        candidate_results_json="[]", created_by_user_id=OP, created_at_utc=ts(-130),
        updated_at_utc=None, compatibility_validation_message=None,
        compatibility_validation_status=("Passed" if ttype == "IHC" else None),
        confirmed_primary_antibody_code=ab, lis_candidate_primary_antibody_codes_json=None,
        lis_query_log_id=None, normalized_sample_code=None, raw_sample_code=None,
    ))
    slid = uid()
    T["slide_tasks"].append(dict(
        id=slid, channel_batch_id=cb, staining_task_id=stid, physical_slot_id=slot_id,
        slot_code=scode, task_type=ttype, status=slide_status, created_at_utc=ts(-128),
    ))
    we = uid()
    T["workflow_executions"].append(dict(
        id=we, machine_run_id=run, slide_task_id=slid, workflow_version_id=wv,
        status=exec_status, created_at_utc=ts(-127), started_at_utc=ts(-126),
        completed_at_utc=(ts(-80) if exec_status == "Completed" else (ts(-40) if exec_status == "Failed" else None)),
    ))
    for idx, (maj, name, act, rcode, vol, s_status, cmd_type, cmd_status) in enumerate(steps, start=1):
        wse = uid()
        T["workflow_step_executions"].append(dict(
            id=wse, workflow_execution_id=we, step_no=idx, major_step_code=maj,
            step_name=name, action_type=act, reagent_code=rcode, volume_ul=vol,
            target_temperature_deci_c=420, status=s_status, redo_count=0,
            created_at_utc=ts(-126 + idx), started_at_utc=ts(-125 + idx),
            completed_at_utc=(ts(-124 + idx) if s_status in ("Completed", "Failed") else None),
        ))
        # device command execution per step
        dce = uid()
        T["device_command_executions"].append(dict(
            id=dce, machine_run_id=run, workflow_step_execution_id=wse,
            command_type=cmd_type, status=cmd_status,
            payload_json=json.dumps({"reagentCode": rcode, "volumeUl": vol}),
            liquid_class_version_id=LCV, liquid_class_version_no=LCV_NO,
            liquid_class_parameters_json=LCV_PARAMS,
            liquid_class_selection_status="Frozen",
            result_json=json.dumps({"ok": cmd_status == "Completed"}),
            created_at_utc=ts(-125 + idx), command_sent_at_utc=ts(-125 + idx),
            acknowledged_at_utc=(ts(-124 + idx) if cmd_status != "Planned" else None),
            completed_at_utc=(ts(-123 + idx) if cmd_status in ("Completed", "Failed") else None),
        ))
        step_ids[(run, maj)] = (wse, dce)
        # dispense execution（仅 Dispense / Dab）
        if cmd_type in ("Dispense", "Dab"):
            bottle = (RBOT.get(rcode) or [None])[0]
            T["dispense_executions"].append(dict(
                id=uid(), device_command_execution_id=dce, reagent_bottle_id=bottle,
                reagent_code=rcode, volume_ul=vol, source_position_code=("R1" if bottle else None),
                target_slot_code=None, status=cmd_status, created_at_utc=ts(-124 + idx),
            ))
            # 试剂消耗 + 预留
            if bottle:
                T["reagent_consumptions"].append(dict(
                    id=uid(), machine_run_id=run, workflow_step_execution_id=wse,
                    device_command_execution_id=dce, dab_batch_id=None,
                    reagent_bottle_id=bottle, reagent_code=rcode, source_role="Primary",
                    volume_ul=vol, created_at_utc=ts(-123 + idx),
                ))
        # 资源租约（每步都占机械臂 + 对应资源）
        leases = [("Platform:RobotArm", "Platform")]
        if cmd_type == "Wash":
            leases.append(("WashStation:NeedleWash", "WashStation"))
            leases.append(("Pump:PWM2", "Pump"))
        if cmd_type in ("Dispense", "Dab"):
            leases.append((f"Source:{rcode}", "Source"))
            leases.append(("Needle:Needle1", "Needle"))
        for (rc, rt) in leases:
            T["machine_resource_leases"].append(dict(
                id=uid(), resource_code=rc, resource_type=rt, status="Released",
                machine_run_id=run, workflow_step_execution_id=wse, device_command_execution_id=dce,
                command_type=cmd_type, wait_reason=None, created_at_utc=ts(-125 + idx),
                acquired_at_utc=ts(-125 + idx),
                released_at_utc=(ts(-123 + idx) if cmd_status in ("Completed", "Failed") else None),
            ))
        # 移液操作记录
        T["pipetting_operations"].append(dict(
            id=uid(), operation_type=("Dispense" if cmd_type == "Dispense" else
                                      ("Aspirate" if cmd_type == "Dab" else "Blowout")),
            status=cmd_status, needle_code="Needle1", execution_mode="Single",
            target_point_code=None, secondary_target_point_code=None,
            coordinate_profile_version_id=CPV, liquid_class_version_id=LCV,
            liquid_class_version_no=LCV_NO, liquid_class_parameters_json=LCV_PARAMS,
            source_type=("ReagentBottle" if cmd_type in ("Dispense", "Dab") else "Empty"),
            reagent_code=rcode, reagent_bottle_id=(RBOT.get(rcode) or [None])[0],
            dab_batch_id=None, system_liquid_source_type=None, source_position_code=None,
            volume_ul=vol, machine_run_id=run, workflow_step_execution_id=wse,
            device_command_execution_id=dce, error_code=(None if cmd_status != "Failed" else "Pump.SensorFailure"),
            error_message=(None if cmd_status != "Failed" else "Wash pump sensor failure."),
            created_at_utc=ts(-125 + idx), completed_at_utc=(ts(-123 + idx) if cmd_status in ("Completed", "Failed") else None),
        ))

# 试剂预留（按 run 汇总，每个 run 一条主预留）
for run, rcode, vol in [(RUN1, "P01", 160), (RUN1, "WAS", 400), (RUN2, "HEM", 100),
                        (RUN3, "P01", 80), (RUN3, "WAS", 400)]:
    bottle = (RBOT.get(rcode) or [None])[0]
    T["reagent_reservations"].append(dict(
        id=uid(), machine_run_id=run, reagent_bottle_id=bottle, reagent_code=rcode,
        reservation_kind="MachineRun", source_role="Primary",
        status=("Consumed" if rcode != "P01" or run == RUN3 else "Reserved"),
        command_id=None, created_by_user_id=OP, required_volume_ul=vol, reserved_volume_ul=vol,
        created_at_utc=ts(-126), updated_at_utc=ts(-80),
    ))

# ---------------------------------------------------------------------------
# DAB 批次链（run3 完成 IHC 用到 DAB）
# ---------------------------------------------------------------------------
DAB_BOTTLE = (RBOT.get("DAB") or [None])[0]
DAB1 = uid()
# 2 张玻片：total = 2*200 + 400 = 800 ; 1 part = 40 ; A=40 B=40 Water=720
T["dab_batches"].append(dict(
    id=DAB1, dab_mix_position_id=DAB_MIX[0]["id"], position_code=DAB_MIX[0]["code"],
    dab_a_reagent_bottle_id=DAB_BOTTLE, dab_b_reagent_bottle_id=DAB_BOTTLE,
    created_by_user_id=OP, status="Depleted", cleaning_status="Confirmed",
    slide_count=2, volume_per_slide_ul=200, line_reserve_volume_ul=400,
    dab_a_ratio_parts=1, dab_b_ratio_parts=1, water_ratio_parts=18,
    total_required_volume_ul=800, actual_prepared_volume_ul=800,
    dab_a_volume_ul=40, dab_b_volume_ul=40, water_volume_ul=720,
    used_volume_ul=800, remaining_volume_ul=0, prepared_at_utc=ts(-118),
    expires_at_utc=ts(-115 + 180), cleaning_confirmed_at_utc=ts(-79),
    created_at_utc=ts(-119), updated_at_utc=ts(-79),
))
DAB2 = uid()
T["dab_batches"].append(dict(
    id=DAB2, dab_mix_position_id=DAB_MIX[1]["id"], position_code=DAB_MIX[1]["code"],
    dab_a_reagent_bottle_id=DAB_BOTTLE, dab_b_reagent_bottle_id=DAB_BOTTLE,
    created_by_user_id=OP, status="Available", cleaning_status="NotRequired",
    slide_count=1, volume_per_slide_ul=200, line_reserve_volume_ul=400,
    dab_a_ratio_parts=1, dab_b_ratio_parts=1, water_ratio_parts=18,
    total_required_volume_ul=600, actual_prepared_volume_ul=600,
    dab_a_volume_ul=30, dab_b_volume_ul=30, water_volume_ul=540,
    used_volume_ul=0, remaining_volume_ul=600, prepared_at_utc=ts(-10),
    expires_at_utc=ts(-10 + 180), cleaning_confirmed_at_utc=None,
    created_at_utc=ts(-11), updated_at_utc=ts(-10),
))
# 把 run3 的第3步（DAB）关联到 DAB1（用内存中记录的 id，避免依赖尚未写入的 DB 行）
run3_dab_step, run3_dab_dce = step_ids[(RUN3, "PRIMARY_DAB")]
run3_staining = staining_by_run[RUN3][0]
T["dab_batch_tasks"].append(dict(id=uid(), dab_batch_id=DAB1, staining_task_id=run3_staining,
                                 required_volume_ul=200, created_at_utc=ts(-117)))
T["dab_batch_usages"].append(dict(
    id=uid(), dab_batch_id=DAB1, machine_run_id=RUN3, workflow_step_execution_id=run3_dab_step,
    staining_task_id=run3_staining, command_id=run3_dab_dce, created_by_user_id=OP,
    volume_ul=200, created_at_utc=ts(-95),
))
# 注：run3 的 DAB 步试剂消耗已在上方 plans 循环中随 device_command 一并创建，
# 此处不再重复插入，以免与 IX_reagent_consumptions_device_command_execution_id_reagent_bottle_id 唯一索引冲突。
# DAB 重新制备计划（DAB1 已耗尽，在 run3 之后规划 DAB2 替换）
T["dab_repreparation_plans"].append(dict(
    id=uid(), expired_dab_batch_id=DAB1, replacement_dab_batch_id=DAB2, machine_run_id=RUN3,
    status="Planned", reason="DAB batch depleted after run3 completion; replacement prepared.",
    created_at_utc=ts(-78), updated_at_utc=ts(-10),
))
# 系统液（水）用量 —— run3 的清洗步（用内存中记录的 id）
run3_wash_step, run3_wash_dce = step_ids[(RUN3, "TERMINAL_WASH")]
T["system_liquid_usages"].append(dict(
    id=uid(), machine_run_id=RUN3, workflow_step_execution_id=run3_wash_step,
    device_command_execution_id=run3_wash_dce, dab_batch_id=DAB1, source_type="SystemWater",
    volume_ul=500, level_snapshot_json=json.dumps({"SystemWater": 849500, "Waste": 100500}),
    created_at_utc=ts(-90),
))

# ---------------------------------------------------------------------------
# 告警 + 处置（run2 泵故障）
# ---------------------------------------------------------------------------
AL1 = uid()
T["alarms"].append(dict(
    id=AL1, machine_run_id=RUN2, code="Pump.SensorFailure", severity="Error",
    message="Wash pump PWM2 sensor failure during terminal wash of run2.",
    status="Active", created_at_utc=ts(-41), cleared_at_utc=None,
))
AL2 = uid()
T["alarms"].append(dict(
    id=AL2, machine_run_id=RUN1, code="Reagent.LowVolume", severity="Warning",
    message="Reagent P01 bottle approaching minimum alarm volume.",
    status="Active", created_at_utc=ts(-2), cleared_at_utc=None,
))
AL3 = uid()
T["alarms"].append(dict(
    id=AL3, machine_run_id=None, code="Cooling.Deviation", severity="Warning",
    message="Cooling unit temperature deviation detected.",
    status="Cleared", created_at_utc=ts(-200), cleared_at_utc=ts(-195),
))
for (aid, actor, action, msg, off) in [
    (AL1, OP, "Acknowledged", "Operator acknowledged pump failure; pausing for inspection.", -39),
    (AL1, EG, "Commented", "Engineer dispatched to inspect PWM2 wiring.", -35),
    (AL2, OP, "Acknowledged", "Operator acknowledged low reagent warning.", -1),
    (AL3, OP, "Cleared", "Cooling recovered to target automatically.", -195),
    (AL3, OP, "Commented", "Transient deviation, no action required.", -198),
]:
    T["alarm_actions"].append(dict(id=uid(), alarm_id=aid, actor_user_id=actor,
                                   action=action, message=msg, created_at_utc=ts(off)))

# ---------------------------------------------------------------------------
# 工程会话 + 样本扫描 + LIS
# ---------------------------------------------------------------------------
T["engineering_sessions"].append(dict(
    id=uid(), command_id=f"engineering-{hex10()}", user_id=EG, username="engineer",
    status="Expired", reason="Inspect PWM2 pump sensor after run2 failure.",
    target="pump", dangerous_operation_confirmed=1, authenticated_at_utc=ts(-30),
    expires_at_utc=ts(-15), revoked_at_utc=None, created_at_utc=ts(-31),
))
T["engineering_sessions"].append(dict(
    id=uid(), command_id=f"engineering-{hex10()}", user_id=EG, username="engineer",
    status="Active", reason="Calibrate coordinate points on Drawer C.",
    target="arm", dangerous_operation_confirmed=1, authenticated_at_utc=ts(-5),
    expires_at_utc=ts(25), revoked_at_utc=None, created_at_utc=ts(-6),
))

SS1 = uid()
T["sample_scan_sessions"].append(dict(
    id=SS1, session_code=f"SSCAN-{BASE.strftime('%Y%m%d%H%M%S')}-{hex10()}",
    status="Completed", started_at_utc=ts(-140), completed_at_utc=ts(-135),
    created_by_user_id=OP,
))
SS2 = uid()
T["sample_scan_sessions"].append(dict(
    id=SS2, session_code=f"SSCAN-{BASE.strftime('%Y%m%d%H%M%S')}-{hex10()}-2",
    status="Running", started_at_utc=ts(-3), completed_at_utc=None,
    created_by_user_id=OP,
))
sample_items = [
    (SS1, "C-01", "HospitalQr", "VALID", "HOSP-P01-001", "P01", None, "Connected"),
    (SS1, "C-02", "HospitalQr", "VALID", "HOSP-P01-002", "P01", None, "Connected"),
    (SS1, "A-02", "TonglingPrimaryAntibody", "VALID", "TL-P01-003", "P01", None, "Connected"),
    (SS1, "D-01", "Empty", "EMPTY", None, None, "Empty slot scanned.", "Connected"),
    (SS1, None, "Damaged", "INVALID", "???-damaged", None, "Barcode unreadable.", "Connected"),
    (SS2, "C-03", "HospitalQr", "VALID", "HOSP-P01-004", "P01", None, "Connected"),
]
for (ssid, slot, kind, status, raw, ab, err, dev) in sample_items:
    norm = (raw.replace("HOSP-", "").replace("TL-", "") if raw and status == "VALID" else None)
    T["sample_scan_items"].append(dict(
        id=uid(), sample_scan_session_id=ssid, slot_code=slot, scan_kind=kind,
        scan_status=status, raw_code=raw, normalized_code=norm, primary_antibody_code=ab,
        error_reason=err, device_status=dev, scanned_at_utc=ts(-138), created_at_utc=ts(-138),
    ))

# LIS 查询日志 + mock 条目
for off, raw, status, cand, sel in [
    (-137, "HOSP-P01-001", "Selected", ["P01"], "P01"),
    (-137, "HOSP-P01-002", "MultipleCandidates", ["P01", "P02"], None),
    (-136, "UNKNOWN-999", "NoResult", [], None),
    (-2, "HOSP-P01-004", "SingleCandidate", ["P01"], None),
]:
    T["lis_query_logs"].append(dict(
        id=uid(), source="MockLIS", status=status, raw_code=raw,
        normalized_code=raw.replace("HOSP-", ""), candidate_primary_antibody_codes_json=json.dumps(cand),
        selected_primary_antibody_code=sel, selected_at_utc=(ts(off + 1) if sel else None),
        selected_by_user_id=(OP if sel else None), error_code=None, error_message=None,
        exception_json="{}", started_at_utc=ts(off), completed_at_utc=(ts(off + 1) if status != "TimedOut" else None),
        created_at_utc=ts(off), updated_at_utc=None,
    ))
for code, ab, scenario in [("HOSP-P01-001", "P01", "Candidate"),
                           ("HOSP-P01-002", "P01", "Candidate"),
                           ("UNKNOWN-999", None, "NoResult"),
                           ("TIMEOUT-888", None, "Timeout")]:
    T["mock_lis_entries"].append(dict(
        id=uid(), normalized_code=code.replace("HOSP-", ""), primary_antibody_code=ab,
        scenario=scenario, is_enabled=1, metadata_json=json.dumps({"note": "seeded test entry"}),
        created_at_utc=ts(-200), updated_at_utc=None,
    ))
# 医院条码 → 一抗 映射
for hosp, ab in [("HOSP001", "P01"), ("HOSP002", "P01"), ("HOSP003", "P01")]:
    T["hospital_barcode_mappings"].append(dict(
        id=uid(), hospital_code=hosp, primary_antibody_code=ab, is_enabled=1, created_at_utc=ts(-200),
    ))

# ---------------------------------------------------------------------------
# 遗留数据导入
# ---------------------------------------------------------------------------
for off, dry, result, stats in [(-300, 0, "Succeeded", json.dumps({"users": 3, "slides": 12, "skipped": 0})),
                                 (-290, 1, "DryRun", json.dumps({"users": 3, "slides": 12, "wouldImport": True}))]:
    LIR = uid()
    T["legacy_import_runs"].append(dict(
        id=LIR, imported_at_utc=ts(off), source_path=f"D:/legacy/runtime_{abs(off)}.json",
        source_hash_json=json.dumps({"sha256": hex10() + hex10()}), is_dry_run=dry,
        result=result, statistics_json=stats,
        report_path=(f"D:/legacy/report_{abs(off)}.md" if not dry else None),
    ))
    for fp, itype, msg in [("users.json", "DuplicateUsername", "Username 'operator' already exists; skipped."),
                            ("slides.json", "InvalidVolume", "Slide S-09 volume 'abc' is not numeric.")]:
        T["legacy_import_issues"].append(dict(
            id=uid(), legacy_import_run_id=LIR, file_path=fp,
            record_identifier=None, field_name=None, issue_type=itype, message=msg,
            raw_fragment='{"v":"abc"}', created_at_utc=ts(off + 1),
        ))
    T["legacy_runtime_snapshots"].append(dict(
        id=uid(), legacy_import_run_id=LIR, source_file_path=f"D:/legacy/runtime_{abs(off)}.json",
        source_file_hash=hex10() + hex10(), run_id=None, status="Captured",
        captured_at_utc=ts(off), snapshot_json=json.dumps({"schemaVersion": 1, "capturedAt": ts(off)}),
    ))

# ---------------------------------------------------------------------------
# 液相类版本差异 + 校验记录 + 一抗工作流映射
# ---------------------------------------------------------------------------
for pname, prev, new, unit in [("AspirateSpeedUlPerSecond", "100", "120", "uL/s"),
                                ("LeadingAirGapUl", "5", "8", "uL")]:
    T["liquid_class_version_differences"].append(dict(
        id=uid(), liquid_class_version_id=LCV, parameter_name=pname,
        previous_value=prev, new_value=new, unit=unit, created_at_utc=ts(-250),
    ))
for stage, valid in [("Draft", 1), ("Publish", 1)]:
    T["liquid_class_validation_records"].append(dict(
        id=uid(), liquid_class_version_id=LCV, stage=stage, is_valid=valid,
        result_json=json.dumps({"checkedAt": ts(-255), "ok": valid == 1}),
        validated_by_user_id=EG, created_at_utc=ts(-255),
    ))
T["primary_antibody_workflow_mappings"].append(dict(
    id=uid(), primary_antibody_code="002", workflow_version_id=WV_IHC, is_enabled=1, created_at_utc=ts(-260)))
T["primary_antibody_workflow_mappings"].append(dict(
    id=uid(), primary_antibody_code="003", workflow_version_id=WV_IHC, is_enabled=1, created_at_utc=ts(-260)))

# ---------------------------------------------------------------------------
# 坐标校准历史
# ---------------------------------------------------------------------------
for idx, cpid in enumerate(COORD_POINTS[:3]):
    T["coordinate_calibration_history"].append(dict(
        id=uid(), action_offset_x_um=idx * 10, action_offset_y_um=0, action_offset_z_um=0,
        calibrated_by_user_id=EG, change_summary_json=json.dumps({"field": "preset_x_um", "delta": idx * 10}),
        coordinate_point_id=cpid, coordinate_profile_version_id=CPV, created_at_utc=ts(-6 + idx),
        dispense_z_um=None, liquid_detect_z_um=None,
        new_x_um=1000 + idx * 10, new_y_um=2000, new_z_um=3000,
        previous_x_um=1000, previous_y_um=2000,
        reason="Manual calibration during engineering session.", safe_z_um=5000,
        source_coordinate_profile_version_id=CPV, validation_result_json=json.dumps({"status": "Ok"}),
    ))

# ---------------------------------------------------------------------------
# 试剂扫描会话/条目（补充）
# ---------------------------------------------------------------------------
RSS1 = uid()
T["reagent_scan_sessions"].append(dict(
    id=RSS1, session_code=f"RSCAN-{BASE.strftime('%Y%m%d%H%M%S')}-{hex10()}-seed",
    status="Completed", started_at_utc=ts(-160), completed_at_utc=ts(-155),
    created_by_user_id=OP,
))
for idx in range(6):
    rp = RACK_POS[idx]
    rcode = REAGENT_CODES[idx % len(REAGENT_CODES)]
    T["reagent_scan_items"].append(dict(
        id=uid(), reagent_scan_session_id=RSS1, reagent_rack_position_id=rp["id"],
        scanner_channel_no=rp["scanner_channel_no"], scanner_channel_code=rp["scanner_channel_code"],
        locator_code=rp["code"], scan_result="VALID",
        raw_barcode=f"{rcode}0802{BASE.strftime('%Y%m%d')}0{idx+1:02d}",
        parsed_reagent_code=rcode, parsed_quantity_ul=8000,
        parsed_batch_no=BASE.strftime("%Y%m%d"), parsed_serial_no=f"0{idx+1:02d}",
        is_validation_passed=1, validation_message="OK", created_at_utc=ts(-158 + idx),
    ))

# ---------------------------------------------------------------------------
# 遥测（流体 / 温度）
# ---------------------------------------------------------------------------
for off, src_id, evt, status, speed, direction, liq, vol, cap, mrun in [
    (-124, "PWM2", "PumpChanged", "Completed", 80, "Forward", "PBS", None, None, RUN1),
    (-123, "PWM2", "PumpChanged", "Running", 60, "Reverse", "Waste", None, None, RUN1),
    (-42, "PWM2", "FaultConfigured", "Faulted", 0, "Stopped", "Waste", None, None, RUN2),
    (-42, "LiquidLevel", "LiquidLevelChanged", "Completed", None, None, "Waste", 120000, 1000000, RUN2),
    (-90, "LiquidLevel", "LiquidLevelChanged", "Completed", None, None, "SystemWater", 849500, 1000000, RUN3),
    (-95, "PWM0", "PumpChanged", "Completed", 100, "Forward", "SystemWater", None, None, RUN3),
    (-30, "PWM3", "PumpChanged", "Idle", 0, "Stopped", None, None, None, None),
    (-20, "LiquidLevel", "LiquidLevelChanged", "Completed", None, None, "ToxicWaste", 50000, 1000000, None),
]:
    T["fluidics_telemetry"].append(dict(
        id=uid(), source_type=("PumpChannel" if src_id.startswith("PWM") else "LiquidLevel"),
        source_id=src_id, event_type=evt, status=status, pwm_channel_code=(src_id if src_id.startswith("PWM") else None),
        drawer_code=None, liquid_source_type=liq, speed_percent=speed, direction=direction,
        current_volume_ul=vol, capacity_ul=cap, target_point_code=None, command_id=None,
        machine_run_id=mrun, workflow_step_execution_id=None, device_command_execution_id=None,
        fault_code=("Fluidics.SensorFailure" if status == "Faulted" else None),
        recorded_at_utc=ts(off),
    ))
# 温度遥测：每个 drawer 一个采样
for off, drawer, board, slot, point, curc, tgt, status in [
    (-126, "A", 0, 1, 0, 415, 420, "Heating"),
    (-126, "C", 2, 1, 8, 420, 420, "Stable"),
    (-90, "A", 0, 1, 0, 420, 420, "Stable"),
    (-5, "C", 2, 1, 8, 422, 420, "Returning"),
    (-200, None, None, None, None, 80, 80, "Stable"),  # CoolingUnit
]:
    T["temperature_telemetry"].append(dict(
        id=uid(), source_type=("CoolingUnit" if drawer is None else "ThermalPoint"),
        source_id=("cooling" if drawer is None else f"{drawer}-s{slot}-p{point}"),
        drawer_code=drawer, board_no=board, slot_no=slot, point_no=point,
        current_temperature_deci_c=curc, target_temperature_deci_c=tgt,
        is_enabled=1, is_connected=1, status=status, fault_code=None, recorded_at_utc=ts(off),
    ))

# ---------------------------------------------------------------------------
# 设备初始化（补 2 个 run + 每个 6 步 check + 通信记录）
# ---------------------------------------------------------------------------
for off, status, user in [(-500, "Ready", OP), (-50, "Failed", EG)]:
    DIR = uid()
    cmd = f"device-initialization-{hex10()}"
    T["device_initialization_runs"].append(dict(
        id=DIR, command_id=cmd, status=status, device_mode="Mock", adapter_name="MockDeviceAdapter",
        attempt_no=1, retry_of_run_id=None, requested_by_user_id=user,
        failure_code=(None if status == "Ready" else "Temperature.CheckFailed"),
        message=("Device initialization completed successfully." if status == "Ready"
                 else "Temperature 16-point check failed."),
        started_at_utc=ts(off), completed_at_utc=(ts(off + 1) if status == "Ready" else ts(off + 1)),
        created_at_utc=ts(off),
    ))
    for step_no, (mod, act, ok) in enumerate([
        ("controller", "check-connection", status == "Ready"),
        ("temperature", "check-16-points", status == "Ready"),
        ("reagent-scanner", "check-connection", True),
        ("arm", "home", True),
        ("pump", "check-connection", True),
        ("needle", "check-connection", True),
    ], start=1):
        s = "Succeeded" if ok else ("Failed" if mod == "temperature" and status != "Ready" else "Succeeded")
        T["device_initialization_checks"].append(dict(
            id=uid(), device_initialization_run_id=DIR, step_no=step_no, module_code=mod,
            status=s, error_code=(None if s == "Succeeded" else "CheckFailed"),
            message=f"Mock {mod}/{act} {'completed' if s == 'Succeeded' else 'failed'}.",
            result_json=json.dumps({"connected": ok}), started_at_utc=ts(off),
            completed_at_utc=ts(off + 1),
        ))
        T["device_communication_records"].append(dict(
            id=uid(), device_mode="Mock", adapter_name="MockDeviceAdapter", module_code=mod,
            action=act, command_id=f"{cmd}:{mod}", correlation_id=cmd, actor="operator",
            source="DeviceInitializationService", status=("Succeeded" if s == "Succeeded" else "Failed"),
            ok=(1 if s == "Succeeded" else 0), acknowledged=1,
            error_code=(None if s == "Succeeded" else "CheckFailed"),
            message=f"Mock {mod}/{act} {'completed' if s == 'Succeeded' else 'failed'}.",
            request_json="{}", response_json=json.dumps({"connected": ok}),
            started_at_utc=ts(off), completed_at_utc=ts(off + 1), created_at_utc=ts(off),
        ))

# ---------------------------------------------------------------------------
# 命令回执 + 审计日志 + demo 标签
# ---------------------------------------------------------------------------
for off, opn, ent_t, ent_id in [
    (-125, "staining.confirm", "StainingTask", None),
    (-118, "dab.prepare", "DabBatch", DAB1),
    (-95, "run.progress", "MachineRun", RUN3),
    (-41, "alarm.raise", "Alarm", AL1),
    (-6, "coordinate.calibrate", "CoordinateProfileVersion", CPV),
    (-1, "reagent.scan", "ReagentScanSession", RSS1),
]:
    T["command_receipts"].append(dict(
        id=uid(), command_id=f"cmd-{hex10()}", operation=opn, request_hash=hex10() + hex10(),
        status="Completed", response_json=json.dumps({"ok": True}), actor_user_id=OP,
        entity_type=ent_t, entity_id=ent_id, error_message=None,
        created_at_utc=ts(off), completed_at_utc=ts(off + 1),
    ))
for off, actor, action, et, eid, msg in [
    (-130, OP, "staining_task.create", "StainingTask", None, "Operator created staining tasks for run."),
    (-126, OP, "machine_run.start", "MachineRun", RUN1, "Operator started machine run."),
    (-118, OP, "dab_batch.prepare", "DabBatch", DAB1, "Operator prepared DAB batch."),
    (-90, OP, "workflow.complete", "WorkflowExecution", None, "Workflow execution completed."),
    (-41, None, "alarm.raised", "Alarm", AL1, "Pump sensor failure alarm raised."),
    (-6, EG, "coordinate.calibrate", "CoordinateProfileVersion", CPV, "Engineer calibrated coordinate points."),
    (-3, OP, "sample_scan.start", "SampleScanSession", SS2, "Operator started sample scan session."),
    (-1, OP, "auth.login", "User", OP, "User login as operator."),
]:
    T["audit_logs"].append(dict(
        id=uid(), actor_user_id=actor, action=action, entity_type=et,
        entity_id=eid, message=msg, created_at_utc=ts(off),
    ))
# demo 标签：标注本次注入的测试实体
for et, eid, key in [("MachineRun", RUN1, "seed-2026-07-12"),
                     ("MachineRun", RUN2, "seed-2026-07-12"),
                     ("DabBatch", DAB1, "seed-2026-07-12"),
                     ("Alarm", AL1, "seed-2026-07-12"),
                     ("StainingTask", run3_staining, "seed-2026-07-12")]:
    T["mock_demo_data_tags"].append(dict(
        id=uid(), entity_type=et, entity_id=eid, demo_key=key, created_at_utc=ts(0),
    ))


# ===========================================================================
# 写入
# ===========================================================================
REAL_COLS = {}
for t in T:
    REAL_COLS[t] = {r[1] for r in cur.execute(f'PRAGMA table_info("{t}")')}


def insert_table(table, rows):
    cols = REAL_COLS[table]
    n = 0
    for row in rows:
        bad = set(row) - cols
        if bad:
            raise SystemExit(f"[FATAL] table '{table}' has unknown columns: {sorted(bad)}")
        # 用表的实际列与 row 的交集插入；保证 NOT NULL 列都被覆盖由数据保证
        use_cols = [c for c in cols if c in row]
        placeholders = ",".join("?" for _ in use_cols)
        sql = f'INSERT INTO "{table}" ({",".join(use_cols)}) VALUES ({placeholders})'
        cur.execute(sql, [row[c] for c in use_cols])
        n += 1
    return n


# 按外键拓扑顺序
ORDER = [
    "machine_runs", "channel_batches", "staining_tasks", "slide_tasks",
    "workflow_executions", "workflow_step_executions", "device_command_executions",
    "dispense_executions", "reagent_reservations", "reagent_consumptions",
    "machine_resource_leases", "alarms", "alarm_actions",
    "dab_batches", "dab_batch_tasks", "dab_batch_usages", "dab_repreparation_plans",
    "system_liquid_usages", "pipetting_operations", "fluidics_telemetry",
    "temperature_telemetry", "coordinate_calibration_history", "engineering_sessions",
    "sample_scan_sessions", "sample_scan_items", "lis_query_logs", "mock_lis_entries",
    "hospital_barcode_mappings", "legacy_import_runs", "legacy_import_issues",
    "legacy_runtime_snapshots", "liquid_class_version_differences",
    "liquid_class_validation_records", "primary_antibody_workflow_mappings",
    "reagent_scan_sessions", "reagent_scan_items",
    "device_initialization_runs", "device_initialization_checks",
    "device_communication_records", "command_receipts", "audit_logs", "mock_demo_data_tags",
]

commit = "--commit" in sys.argv
total = 0
print("\n=== INSERT PLAN (dry-run, no write)" + ("  [will COMMIT]" if commit else "") + " ===")
try:
    cur.execute("BEGIN")
    for t in ORDER:
        if not T[t]:
            continue
        n = insert_table(t, T[t])
        total += n
        print(f"  {t:<38} +{n}")
    # 外键完整性检查
    fk_problems = cur.execute("PRAGMA foreign_key_check").fetchall()
    if fk_problems:
        raise SystemExit(f"[FATAL] foreign_key_check reported problems: {fk_problems}")
    if commit:
        con.commit()
        print(f"\n[COMMITTED] {total} rows inserted into {DB}")
    else:
        con.rollback()
        print(f"\n[DRY-RUN] would insert {total} rows; re-run with --commit to write. (rolled back)")
except Exception as e:
    con.rollback()
    print(f"\n[ERROR rolled back] {type(e).__name__}: {e}")
    raise
finally:
    con.close()
