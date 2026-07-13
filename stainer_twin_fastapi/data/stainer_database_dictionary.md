# Stainer 数据库结构说明

> 基于 `stainer(1).db` SQLite 数据库结构整理。

- 数据表数量：73 张

# __EFMigrationsHistory

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| MigrationId | TEXT | 是 | 否 | 保存 MigrationId 对应业务属性。 |
| ProductVersion | TEXT | 否 | 否 | 保存 ProductVersion 对应业务属性。 |

# __EFMigrationsLock

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| Id | INTEGER | 是 | 否 | 记录唯一标识。 |
| Timestamp | TEXT | 否 | 否 | 保存 Timestamp 对应业务属性。 |

# alarm_actions

## 表作用

存储报警定义、报警记录及处理信息。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| alarm_id | TEXT | 否 | 否 | 保存 alarm_id 对应业务属性。 |
| actor_user_id | TEXT | 否 | 是 | 保存 actor_user_id 对应业务属性。 |
| action | TEXT | 否 | 否 | 保存 action 对应业务属性。 |
| message | TEXT | 否 | 否 | 保存 message 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# alarms

## 表作用

存储报警定义、报警记录及处理信息。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| severity | TEXT | 否 | 否 | 保存 severity 对应业务属性。 |
| message | TEXT | 否 | 否 | 保存 message 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| cleared_at_utc | TEXT | 否 | 是 | 保存 cleared_at_utc 对应业务属性。 |

# audit_logs

## 表作用

存储系统日志和操作记录。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| actor_user_id | TEXT | 否 | 是 | 保存 actor_user_id 对应业务属性。 |
| action | TEXT | 否 | 否 | 保存 action 对应业务属性。 |
| entity_type | TEXT | 否 | 否 | 保存 entity_type 对应业务属性。 |
| entity_id | TEXT | 否 | 是 | 保存 entity_id 对应业务属性。 |
| message | TEXT | 否 | 否 | 保存 message 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# channel_batches

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |
| coordinate_profile_version_id | TEXT | 否 | 是 | 保存 coordinate_profile_version_id 对应业务属性。 |
| coordinate_selection_status | TEXT | 否 | 否 | 保存 coordinate_selection_status 对应业务属性。 |
| coordinate_snapshot_json | TEXT | 否 | 否 | 保存 coordinate_snapshot_json 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| drawer_code | TEXT | 否 | 否 | 保存 drawer_code 对应业务属性。 |
| drawer_id | TEXT | 否 | 否 | 保存 drawer_id 对应业务属性。 |
| experiment_type | TEXT | 否 | 是 | 保存 experiment_type 对应业务属性。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| manual_resolution_reason | TEXT | 否 | 否 | 保存 manual_resolution_reason 对应业务属性。 |
| needs_manual_resolution | INTEGER | 否 | 否 | 保存 needs_manual_resolution 对应业务属性。 |
| selected_workflow_version_id | TEXT | 否 | 是 | 保存 selected_workflow_version_id 对应业务属性。 |
| started_at_utc | TEXT | 否 | 是 | 保存 started_at_utc 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| workflow_locked_at_utc | TEXT | 否 | 是 | 保存 workflow_locked_at_utc 对应业务属性。 |
| workflow_selected_at_utc | TEXT | 否 | 是 | 保存 workflow_selected_at_utc 对应业务属性。 |
| workflow_selected_by_user_id | TEXT | 否 | 是 | 保存 workflow_selected_by_user_id 对应业务属性。 |
| workflow_selection_status | TEXT | 否 | 否 | 保存 workflow_selection_status 对应业务属性。 |
| workflow_snapshot_json | TEXT | 否 | 否 | 保存 workflow_snapshot_json 对应业务属性。 |
| liquid_class_selection_status | TEXT | 否 | 否 | 保存 liquid_class_selection_status 对应业务属性。 |
| liquid_class_snapshot_json | TEXT | 否 | 否 | 保存 liquid_class_snapshot_json 对应业务属性。 |

# command_receipts

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| command_id | TEXT | 否 | 否 | 保存 command_id 对应业务属性。 |
| operation | TEXT | 否 | 否 | 保存 operation 对应业务属性。 |
| request_hash | TEXT | 否 | 否 | 保存 request_hash 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| response_json | TEXT | 否 | 否 | 保存 response_json 对应业务属性。 |
| actor_user_id | TEXT | 否 | 是 | 保存 actor_user_id 对应业务属性。 |
| entity_type | TEXT | 否 | 是 | 保存 entity_type 对应业务属性。 |
| entity_id | TEXT | 否 | 是 | 保存 entity_id 对应业务属性。 |
| error_message | TEXT | 否 | 是 | 保存 error_message 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |

# cooling_unit_states

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| current_temperature_deci_c | INTEGER | 否 | 否 | 保存 current_temperature_deci_c 对应业务属性。 |
| target_temperature_deci_c | INTEGER | 否 | 否 | 保存 target_temperature_deci_c 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| is_connected | INTEGER | 否 | 否 | 保存 is_connected 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| fault_code | TEXT | 否 | 是 | 保存 fault_code 对应业务属性。 |
| fault_message | TEXT | 否 | 是 | 保存 fault_message 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 否 | 保存 updated_at_utc 对应业务属性。 |

# coordinate_calibration_history

## 表作用

存储坐标点、校准参数及位置配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| action_offset_x_um | INTEGER | 否 | 是 | 保存 action_offset_x_um 对应业务属性。 |
| action_offset_y_um | INTEGER | 否 | 是 | 保存 action_offset_y_um 对应业务属性。 |
| action_offset_z_um | INTEGER | 否 | 是 | 保存 action_offset_z_um 对应业务属性。 |
| calibrated_by_user_id | TEXT | 否 | 是 | 保存 calibrated_by_user_id 对应业务属性。 |
| change_summary_json | TEXT | 否 | 否 | 保存 change_summary_json 对应业务属性。 |
| coordinate_point_id | TEXT | 否 | 否 | 保存 coordinate_point_id 对应业务属性。 |
| coordinate_profile_version_id | TEXT | 否 | 是 | 保存 coordinate_profile_version_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| dispense_z_um | INTEGER | 否 | 是 | 保存 dispense_z_um 对应业务属性。 |
| liquid_detect_z_um | INTEGER | 否 | 是 | 保存 liquid_detect_z_um 对应业务属性。 |
| new_x_um | INTEGER | 否 | 是 | 保存 new_x_um 对应业务属性。 |
| new_y_um | INTEGER | 否 | 是 | 保存 new_y_um 对应业务属性。 |
| new_z_um | INTEGER | 否 | 是 | 保存 new_z_um 对应业务属性。 |
| previous_x_um | INTEGER | 否 | 是 | 保存 previous_x_um 对应业务属性。 |
| previous_y_um | INTEGER | 否 | 是 | 保存 previous_y_um 对应业务属性。 |
| reason | TEXT | 否 | 否 | 保存 reason 对应业务属性。 |
| safe_z_um | INTEGER | 否 | 是 | 保存 safe_z_um 对应业务属性。 |
| source_coordinate_profile_version_id | TEXT | 否 | 是 | 保存 source_coordinate_profile_version_id 对应业务属性。 |
| validation_result_json | TEXT | 否 | 否 | 保存 validation_result_json 对应业务属性。 |

# coordinate_points

## 表作用

存储坐标点、校准参数及位置配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| action_offset_x_um | INTEGER | 否 | 是 | 保存 action_offset_x_um 对应业务属性。 |
| action_offset_y_um | INTEGER | 否 | 是 | 保存 action_offset_y_um 对应业务属性。 |
| action_offset_z_um | INTEGER | 否 | 是 | 保存 action_offset_z_um 对应业务属性。 |
| calibrated_x_um | INTEGER | 否 | 是 | 保存 calibrated_x_um 对应业务属性。 |
| calibrated_y_um | INTEGER | 否 | 是 | 保存 calibrated_y_um 对应业务属性。 |
| calibrated_z_um | INTEGER | 否 | 是 | 保存 calibrated_z_um 对应业务属性。 |
| coordinate_profile_id | TEXT | 否 | 否 | 保存 coordinate_profile_id 对应业务属性。 |
| coordinate_profile_version_id | TEXT | 否 | 否 | 保存 coordinate_profile_version_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| dispense_z_um | INTEGER | 否 | 是 | 保存 dispense_z_um 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| liquid_detect_z_um | INTEGER | 否 | 是 | 保存 liquid_detect_z_um 对应业务属性。 |
| point_code | TEXT | 否 | 否 | 保存 point_code 对应业务属性。 |
| point_type | TEXT | 否 | 否 | 保存 point_type 对应业务属性。 |
| preset_x_um | INTEGER | 否 | 是 | 保存 preset_x_um 对应业务属性。 |
| preset_y_um | INTEGER | 否 | 是 | 保存 preset_y_um 对应业务属性。 |
| requires_calibration | INTEGER | 否 | 否 | 保存 requires_calibration 对应业务属性。 |
| safe_z_um | INTEGER | 否 | 是 | 保存 safe_z_um 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |
| validation_message | TEXT | 否 | 否 | 保存 validation_message 对应业务属性。 |
| validation_status | TEXT | 否 | 否 | 保存 validation_status 对应业务属性。 |

# coordinate_profile_versions

## 表作用

存储坐标点、校准参数及位置配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| coordinate_profile_id | TEXT | 否 | 否 | 保存 coordinate_profile_id 对应业务属性。 |
| version_no | INTEGER | 否 | 否 | 保存 version_no 对应业务属性。 |
| version_label | TEXT | 否 | 否 | 保存 version_label 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| is_active | INTEGER | 否 | 否 | 是否有效。 |
| source_version_id | TEXT | 否 | 是 | 保存 source_version_id 对应业务属性。 |
| change_reason | TEXT | 否 | 否 | 保存 change_reason 对应业务属性。 |
| change_summary_json | TEXT | 否 | 否 | 保存 change_summary_json 对应业务属性。 |
| validation_result_json | TEXT | 否 | 否 | 保存 validation_result_json 对应业务属性。 |
| created_by_user_id | TEXT | 否 | 是 | 保存 created_by_user_id 对应业务属性。 |
| published_by_user_id | TEXT | 否 | 是 | 保存 published_by_user_id 对应业务属性。 |
| activated_by_user_id | TEXT | 否 | 是 | 保存 activated_by_user_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| published_at_utc | TEXT | 否 | 是 | 保存 published_at_utc 对应业务属性。 |
| activated_at_utc | TEXT | 否 | 是 | 保存 activated_at_utc 对应业务属性。 |
| retired_at_utc | TEXT | 否 | 是 | 保存 retired_at_utc 对应业务属性。 |
| usage_scope | TEXT | 否 | 否 | 保存 usage_scope 对应业务属性。 |
| verification_status | TEXT | 否 | 否 | 保存 verification_status 对应业务属性。 |

# coordinate_profiles

## 表作用

存储坐标点、校准参数及位置配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| active_version_id | TEXT | 否 | 是 | 保存 active_version_id 对应业务属性。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| is_active | INTEGER | 否 | 否 | 是否有效。 |
| name | TEXT | 否 | 否 | 名称。 |
| origin_definition | TEXT | 否 | 否 | 保存 origin_definition 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |

# dab_batch_tasks

## 表作用

存储任务定义、任务状态及执行数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| dab_batch_id | TEXT | 否 | 否 | 保存 dab_batch_id 对应业务属性。 |
| staining_task_id | TEXT | 否 | 否 | 保存 staining_task_id 对应业务属性。 |
| required_volume_ul | INTEGER | 否 | 否 | 保存 required_volume_ul 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# dab_batch_usages

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| command_id | TEXT | 否 | 是 | 保存 command_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| created_by_user_id | TEXT | 否 | 是 | 保存 created_by_user_id 对应业务属性。 |
| dab_batch_id | TEXT | 否 | 否 | 保存 dab_batch_id 对应业务属性。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| staining_task_id | TEXT | 否 | 是 | 保存 staining_task_id 对应业务属性。 |
| volume_ul | INTEGER | 否 | 否 | 保存 volume_ul 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 是 | 保存 workflow_step_execution_id 对应业务属性。 |

# dab_batches

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| actual_prepared_volume_ul | INTEGER | 否 | 否 | 保存 actual_prepared_volume_ul 对应业务属性。 |
| cleaning_confirmed_at_utc | TEXT | 否 | 是 | 保存 cleaning_confirmed_at_utc 对应业务属性。 |
| cleaning_status | TEXT | 否 | 否 | 保存 cleaning_status 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| created_by_user_id | TEXT | 否 | 是 | 保存 created_by_user_id 对应业务属性。 |
| dab_a_ratio_parts | INTEGER | 否 | 否 | 保存 dab_a_ratio_parts 对应业务属性。 |
| dab_a_reagent_bottle_id | TEXT | 否 | 是 | 保存 dab_a_reagent_bottle_id 对应业务属性。 |
| dab_a_volume_ul | INTEGER | 否 | 否 | 保存 dab_a_volume_ul 对应业务属性。 |
| dab_b_ratio_parts | INTEGER | 否 | 否 | 保存 dab_b_ratio_parts 对应业务属性。 |
| dab_b_reagent_bottle_id | TEXT | 否 | 是 | 保存 dab_b_reagent_bottle_id 对应业务属性。 |
| dab_b_volume_ul | INTEGER | 否 | 否 | 保存 dab_b_volume_ul 对应业务属性。 |
| dab_mix_position_id | TEXT | 否 | 否 | 保存 dab_mix_position_id 对应业务属性。 |
| expires_at_utc | TEXT | 否 | 是 | 保存 expires_at_utc 对应业务属性。 |
| line_reserve_volume_ul | INTEGER | 否 | 否 | 保存 line_reserve_volume_ul 对应业务属性。 |
| position_code | TEXT | 否 | 否 | 保存 position_code 对应业务属性。 |
| prepared_at_utc | TEXT | 否 | 是 | 保存 prepared_at_utc 对应业务属性。 |
| remaining_volume_ul | INTEGER | 否 | 否 | 保存 remaining_volume_ul 对应业务属性。 |
| slide_count | INTEGER | 否 | 否 | 保存 slide_count 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| total_required_volume_ul | INTEGER | 否 | 否 | 保存 total_required_volume_ul 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |
| used_volume_ul | INTEGER | 否 | 否 | 保存 used_volume_ul 对应业务属性。 |
| volume_per_slide_ul | INTEGER | 否 | 否 | 保存 volume_per_slide_ul 对应业务属性。 |
| water_ratio_parts | INTEGER | 否 | 否 | 保存 water_ratio_parts 对应业务属性。 |
| water_volume_ul | INTEGER | 否 | 否 | 保存 water_volume_ul 对应业务属性。 |

# dab_mix_positions

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| position_no | INTEGER | 否 | 否 | 保存 position_no 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| active_dab_batch_id | TEXT | 否 | 是 | 保存 active_dab_batch_id 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |

# dab_repreparation_plans

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| expired_dab_batch_id | TEXT | 否 | 否 | 保存 expired_dab_batch_id 对应业务属性。 |
| replacement_dab_batch_id | TEXT | 否 | 是 | 保存 replacement_dab_batch_id 对应业务属性。 |
| machine_run_id | TEXT | 否 | 否 | 保存 machine_run_id 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| reason | TEXT | 否 | 否 | 保存 reason 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |

# device_command_executions

## 表作用

存储设备配置、状态及设备相关信息。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| acknowledged_at_utc | TEXT | 否 | 是 | 保存 acknowledged_at_utc 对应业务属性。 |
| command_sent_at_utc | TEXT | 否 | 是 | 保存 command_sent_at_utc 对应业务属性。 |
| command_type | TEXT | 否 | 否 | 保存 command_type 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| liquid_class_parameters_json | TEXT | 否 | 否 | 保存 liquid_class_parameters_json 对应业务属性。 |
| liquid_class_selection_status | TEXT | 否 | 否 | 保存 liquid_class_selection_status 对应业务属性。 |
| liquid_class_version_id | TEXT | 否 | 是 | 保存 liquid_class_version_id 对应业务属性。 |
| liquid_class_version_no | INTEGER | 否 | 是 | 保存 liquid_class_version_no 对应业务属性。 |
| machine_run_id | TEXT | 否 | 否 | 保存 machine_run_id 对应业务属性。 |
| payload_json | TEXT | 否 | 否 | 保存 payload_json 对应业务属性。 |
| result_json | TEXT | 否 | 否 | 保存 result_json 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| workflow_step_execution_id | TEXT | 否 | 是 | 保存 workflow_step_execution_id 对应业务属性。 |

# device_communication_records

## 表作用

存储设备配置、状态及设备相关信息。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| device_mode | TEXT | 否 | 否 | 保存 device_mode 对应业务属性。 |
| adapter_name | TEXT | 否 | 否 | 保存 adapter_name 对应业务属性。 |
| module_code | TEXT | 否 | 否 | 保存 module_code 对应业务属性。 |
| action | TEXT | 否 | 否 | 保存 action 对应业务属性。 |
| command_id | TEXT | 否 | 否 | 保存 command_id 对应业务属性。 |
| correlation_id | TEXT | 否 | 是 | 保存 correlation_id 对应业务属性。 |
| actor | TEXT | 否 | 是 | 保存 actor 对应业务属性。 |
| source | TEXT | 否 | 否 | 保存 source 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| ok | INTEGER | 否 | 否 | 保存 ok 对应业务属性。 |
| acknowledged | INTEGER | 否 | 否 | 保存 acknowledged 对应业务属性。 |
| error_code | TEXT | 否 | 是 | 保存 error_code 对应业务属性。 |
| message | TEXT | 否 | 否 | 保存 message 对应业务属性。 |
| request_json | TEXT | 否 | 否 | 保存 request_json 对应业务属性。 |
| response_json | TEXT | 否 | 否 | 保存 response_json 对应业务属性。 |
| started_at_utc | TEXT | 否 | 否 | 保存 started_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 否 | 保存 completed_at_utc 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| persistence_attempt_count | INTEGER | 否 | 否 | 保存 persistence_attempt_count 对应业务属性。 |
| persistence_completed_at_utc | TEXT | 否 | 是 | 保存 persistence_completed_at_utc 对应业务属性。 |
| persistence_failure_reason | TEXT | 否 | 是 | 保存 persistence_failure_reason 对应业务属性。 |
| persistence_last_attempt_at_utc | TEXT | 否 | 否 | 保存 persistence_last_attempt_at_utc 对应业务属性。 |
| persistence_status | TEXT | 否 | 否 | 保存 persistence_status 对应业务属性。 |

# device_initialization_checks

## 表作用

存储设备配置、状态及设备相关信息。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| device_initialization_run_id | TEXT | 否 | 否 | 保存 device_initialization_run_id 对应业务属性。 |
| step_no | INTEGER | 否 | 否 | 保存 step_no 对应业务属性。 |
| module_code | TEXT | 否 | 否 | 保存 module_code 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| error_code | TEXT | 否 | 是 | 保存 error_code 对应业务属性。 |
| message | TEXT | 否 | 否 | 保存 message 对应业务属性。 |
| result_json | TEXT | 否 | 否 | 保存 result_json 对应业务属性。 |
| started_at_utc | TEXT | 否 | 是 | 保存 started_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |

# device_initialization_runs

## 表作用

存储设备配置、状态及设备相关信息。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| command_id | TEXT | 否 | 否 | 保存 command_id 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| device_mode | TEXT | 否 | 否 | 保存 device_mode 对应业务属性。 |
| adapter_name | TEXT | 否 | 否 | 保存 adapter_name 对应业务属性。 |
| attempt_no | INTEGER | 否 | 否 | 保存 attempt_no 对应业务属性。 |
| retry_of_run_id | TEXT | 否 | 是 | 保存 retry_of_run_id 对应业务属性。 |
| requested_by_user_id | TEXT | 否 | 是 | 保存 requested_by_user_id 对应业务属性。 |
| failure_code | TEXT | 否 | 是 | 保存 failure_code 对应业务属性。 |
| message | TEXT | 否 | 是 | 保存 message 对应业务属性。 |
| started_at_utc | TEXT | 否 | 否 | 保存 started_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# device_profiles

## 表作用

存储设备配置、状态及设备相关信息。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| name | TEXT | 否 | 否 | 名称。 |
| is_active | INTEGER | 否 | 否 | 是否有效。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# dispense_executions

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| device_command_execution_id | TEXT | 否 | 否 | 保存 device_command_execution_id 对应业务属性。 |
| reagent_bottle_id | TEXT | 否 | 是 | 保存 reagent_bottle_id 对应业务属性。 |
| reagent_code | TEXT | 否 | 否 | 保存 reagent_code 对应业务属性。 |
| volume_ul | INTEGER | 否 | 否 | 保存 volume_ul 对应业务属性。 |
| source_position_code | TEXT | 否 | 是 | 保存 source_position_code 对应业务属性。 |
| target_slot_code | TEXT | 否 | 是 | 保存 target_slot_code 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# drawers

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| name | TEXT | 否 | 否 | 名称。 |
| sort_order | INTEGER | 否 | 否 | 保存 sort_order 对应业务属性。 |
| heat_board_id | INTEGER | 否 | 否 | 保存 heat_board_id 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# engineering_sessions

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| command_id | TEXT | 否 | 否 | 保存 command_id 对应业务属性。 |
| user_id | TEXT | 否 | 否 | 保存 user_id 对应业务属性。 |
| username | TEXT | 否 | 否 | 保存 username 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| reason | TEXT | 否 | 否 | 保存 reason 对应业务属性。 |
| target | TEXT | 否 | 否 | 保存 target 对应业务属性。 |
| dangerous_operation_confirmed | INTEGER | 否 | 否 | 保存 dangerous_operation_confirmed 对应业务属性。 |
| authenticated_at_utc | TEXT | 否 | 否 | 保存 authenticated_at_utc 对应业务属性。 |
| expires_at_utc | TEXT | 否 | 否 | 保存 expires_at_utc 对应业务属性。 |
| revoked_at_utc | TEXT | 否 | 是 | 保存 revoked_at_utc 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# fluidics_telemetry

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| source_type | TEXT | 否 | 否 | 保存 source_type 对应业务属性。 |
| source_id | TEXT | 否 | 否 | 保存 source_id 对应业务属性。 |
| event_type | TEXT | 否 | 否 | 保存 event_type 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| pwm_channel_code | TEXT | 否 | 是 | 保存 pwm_channel_code 对应业务属性。 |
| drawer_code | TEXT | 否 | 是 | 保存 drawer_code 对应业务属性。 |
| liquid_source_type | TEXT | 否 | 是 | 保存 liquid_source_type 对应业务属性。 |
| speed_percent | INTEGER | 否 | 是 | 保存 speed_percent 对应业务属性。 |
| direction | TEXT | 否 | 是 | 保存 direction 对应业务属性。 |
| current_volume_ul | INTEGER | 否 | 是 | 保存 current_volume_ul 对应业务属性。 |
| capacity_ul | INTEGER | 否 | 是 | 保存 capacity_ul 对应业务属性。 |
| target_point_code | TEXT | 否 | 是 | 保存 target_point_code 对应业务属性。 |
| command_id | TEXT | 否 | 是 | 保存 command_id 对应业务属性。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 是 | 保存 workflow_step_execution_id 对应业务属性。 |
| device_command_execution_id | TEXT | 否 | 是 | 保存 device_command_execution_id 对应业务属性。 |
| fault_code | TEXT | 否 | 是 | 保存 fault_code 对应业务属性。 |
| recorded_at_utc | TEXT | 否 | 否 | 保存 recorded_at_utc 对应业务属性。 |

# hospital_barcode_mappings

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| hospital_code | TEXT | 否 | 否 | 保存 hospital_code 对应业务属性。 |
| primary_antibody_code | TEXT | 否 | 否 | 保存 primary_antibody_code 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# legacy_import_issues

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| legacy_import_run_id | TEXT | 否 | 否 | 保存 legacy_import_run_id 对应业务属性。 |
| file_path | TEXT | 否 | 否 | 保存 file_path 对应业务属性。 |
| record_identifier | TEXT | 否 | 是 | 保存 record_identifier 对应业务属性。 |
| field_name | TEXT | 否 | 是 | 保存 field_name 对应业务属性。 |
| issue_type | TEXT | 否 | 否 | 保存 issue_type 对应业务属性。 |
| message | TEXT | 否 | 否 | 保存 message 对应业务属性。 |
| raw_fragment | TEXT | 否 | 是 | 保存 raw_fragment 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# legacy_import_runs

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| imported_at_utc | TEXT | 否 | 否 | 保存 imported_at_utc 对应业务属性。 |
| source_path | TEXT | 否 | 否 | 保存 source_path 对应业务属性。 |
| source_hash_json | TEXT | 否 | 否 | 保存 source_hash_json 对应业务属性。 |
| is_dry_run | INTEGER | 否 | 否 | 保存 is_dry_run 对应业务属性。 |
| result | TEXT | 否 | 否 | 保存 result 对应业务属性。 |
| statistics_json | TEXT | 否 | 否 | 保存 statistics_json 对应业务属性。 |
| report_path | TEXT | 否 | 是 | 保存 report_path 对应业务属性。 |

# legacy_runtime_snapshots

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| legacy_import_run_id | TEXT | 否 | 否 | 保存 legacy_import_run_id 对应业务属性。 |
| source_file_path | TEXT | 否 | 否 | 保存 source_file_path 对应业务属性。 |
| source_file_hash | TEXT | 否 | 否 | 保存 source_file_hash 对应业务属性。 |
| run_id | TEXT | 否 | 是 | 保存 run_id 对应业务属性。 |
| status | TEXT | 否 | 是 | 当前状态。 |
| captured_at_utc | TEXT | 否 | 否 | 保存 captured_at_utc 对应业务属性。 |
| snapshot_json | TEXT | 否 | 否 | 保存 snapshot_json 对应业务属性。 |

# liquid_class_profiles

## 表作用

存储液体类型、液体参数及移液相关配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| aspirate_speed_ul_per_second | INTEGER | 否 | 是 | 保存 aspirate_speed_ul_per_second 对应业务属性。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| dispense_speed_ul_per_second | INTEGER | 否 | 是 | 保存 dispense_speed_ul_per_second 对应业务属性。 |
| enabled_version_id | TEXT | 否 | 是 | 保存 enabled_version_id 对应业务属性。 |
| excess_volume_ul | INTEGER | 否 | 是 | 保存 excess_volume_ul 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| leading_air_gap_ul | INTEGER | 否 | 是 | 保存 leading_air_gap_ul 对应业务属性。 |
| legacy_parameters_json | TEXT | 否 | 否 | 保存 legacy_parameters_json 对应业务属性。 |
| mix_cycles | INTEGER | 否 | 是 | 保存 mix_cycles 对应业务属性。 |
| name | TEXT | 否 | 否 | 名称。 |
| pre_wet_cycles | INTEGER | 否 | 是 | 保存 pre_wet_cycles 对应业务属性。 |
| trailing_air_gap_ul | INTEGER | 否 | 是 | 保存 trailing_air_gap_ul 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |

# liquid_class_validation_records

## 表作用

存储液体类型、液体参数及移液相关配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| liquid_class_version_id | TEXT | 否 | 否 | 保存 liquid_class_version_id 对应业务属性。 |
| stage | TEXT | 否 | 否 | 保存 stage 对应业务属性。 |
| is_valid | INTEGER | 否 | 否 | 保存 is_valid 对应业务属性。 |
| result_json | TEXT | 否 | 否 | 保存 result_json 对应业务属性。 |
| validated_by_user_id | TEXT | 否 | 是 | 保存 validated_by_user_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# liquid_class_version_differences

## 表作用

存储液体类型、液体参数及移液相关配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| liquid_class_version_id | TEXT | 否 | 否 | 保存 liquid_class_version_id 对应业务属性。 |
| parameter_name | TEXT | 否 | 否 | 保存 parameter_name 对应业务属性。 |
| previous_value | TEXT | 否 | 是 | 保存 previous_value 对应业务属性。 |
| new_value | TEXT | 否 | 是 | 保存 new_value 对应业务属性。 |
| unit | TEXT | 否 | 否 | 保存 unit 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# liquid_class_versions

## 表作用

存储液体类型、液体参数及移液相关配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| liquid_class_profile_id | TEXT | 否 | 否 | 保存 liquid_class_profile_id 对应业务属性。 |
| version_no | INTEGER | 否 | 否 | 保存 version_no 对应业务属性。 |
| version_label | TEXT | 否 | 否 | 保存 version_label 对应业务属性。 |
| name | TEXT | 否 | 否 | 名称。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| source_version_id | TEXT | 否 | 是 | 保存 source_version_id 对应业务属性。 |
| change_reason | TEXT | 否 | 否 | 保存 change_reason 对应业务属性。 |
| change_summary_json | TEXT | 否 | 否 | 保存 change_summary_json 对应业务属性。 |
| liquid_detection_enabled | INTEGER | 否 | 否 | 保存 liquid_detection_enabled 对应业务属性。 |
| liquid_detection_sensitivity_percent | INTEGER | 否 | 否 | 保存 liquid_detection_sensitivity_percent 对应业务属性。 |
| liquid_detection_speed_um_per_second | INTEGER | 否 | 否 | 保存 liquid_detection_speed_um_per_second 对应业务属性。 |
| aspirate_speed_ul_per_second | INTEGER | 否 | 否 | 保存 aspirate_speed_ul_per_second 对应业务属性。 |
| aspirate_delay_ms | INTEGER | 否 | 否 | 保存 aspirate_delay_ms 对应业务属性。 |
| dispense_speed_ul_per_second | INTEGER | 否 | 否 | 保存 dispense_speed_ul_per_second 对应业务属性。 |
| dispense_delay_ms | INTEGER | 否 | 否 | 保存 dispense_delay_ms 对应业务属性。 |
| leading_air_gap_ul | INTEGER | 否 | 否 | 保存 leading_air_gap_ul 对应业务属性。 |
| trailing_air_gap_ul | INTEGER | 否 | 否 | 保存 trailing_air_gap_ul 对应业务属性。 |
| blowout_volume_ul | INTEGER | 否 | 否 | 保存 blowout_volume_ul 对应业务属性。 |
| blowout_delay_ms | INTEGER | 否 | 否 | 保存 blowout_delay_ms 对应业务属性。 |
| volume_adjustment_ul | INTEGER | 否 | 否 | 保存 volume_adjustment_ul 对应业务属性。 |
| pre_wet_cycles | INTEGER | 否 | 否 | 保存 pre_wet_cycles 对应业务属性。 |
| mix_cycles | INTEGER | 否 | 否 | 保存 mix_cycles 对应业务属性。 |
| created_by_user_id | TEXT | 否 | 是 | 保存 created_by_user_id 对应业务属性。 |
| published_by_user_id | TEXT | 否 | 是 | 保存 published_by_user_id 对应业务属性。 |
| enabled_by_user_id | TEXT | 否 | 是 | 保存 enabled_by_user_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| published_at_utc | TEXT | 否 | 是 | 保存 published_at_utc 对应业务属性。 |
| enabled_at_utc | TEXT | 否 | 是 | 保存 enabled_at_utc 对应业务属性。 |

# liquid_container_states

## 表作用

存储液体类型、液体参数及移液相关配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| source_type | TEXT | 否 | 否 | 保存 source_type 对应业务属性。 |
| display_name | TEXT | 否 | 否 | 保存 display_name 对应业务属性。 |
| is_waste | INTEGER | 否 | 否 | 保存 is_waste 对应业务属性。 |
| capacity_ul | INTEGER | 否 | 否 | 保存 capacity_ul 对应业务属性。 |
| current_volume_ul | INTEGER | 否 | 否 | 保存 current_volume_ul 对应业务属性。 |
| low_threshold_ul | INTEGER | 否 | 否 | 保存 low_threshold_ul 对应业务属性。 |
| full_threshold_ul | INTEGER | 否 | 否 | 保存 full_threshold_ul 对应业务属性。 |
| level_status | TEXT | 否 | 否 | 保存 level_status 对应业务属性。 |
| is_connected | INTEGER | 否 | 否 | 保存 is_connected 对应业务属性。 |
| fault_code | TEXT | 否 | 是 | 保存 fault_code 对应业务属性。 |
| fault_message | TEXT | 否 | 是 | 保存 fault_message 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 否 | 保存 updated_at_utc 对应业务属性。 |

# lis_query_logs

## 表作用

存储系统日志和操作记录。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| source | TEXT | 否 | 否 | 保存 source 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| raw_code | TEXT | 否 | 否 | 保存 raw_code 对应业务属性。 |
| normalized_code | TEXT | 否 | 否 | 保存 normalized_code 对应业务属性。 |
| candidate_primary_antibody_codes_json | TEXT | 否 | 否 | 保存 candidate_primary_antibody_codes_json 对应业务属性。 |
| selected_primary_antibody_code | TEXT | 否 | 是 | 保存 selected_primary_antibody_code 对应业务属性。 |
| selected_at_utc | TEXT | 否 | 是 | 保存 selected_at_utc 对应业务属性。 |
| selected_by_user_id | TEXT | 否 | 是 | 保存 selected_by_user_id 对应业务属性。 |
| error_code | TEXT | 否 | 是 | 保存 error_code 对应业务属性。 |
| error_message | TEXT | 否 | 是 | 保存 error_message 对应业务属性。 |
| exception_json | TEXT | 否 | 否 | 保存 exception_json 对应业务属性。 |
| started_at_utc | TEXT | 否 | 否 | 保存 started_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |

# machine_resource_leases

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| resource_code | TEXT | 否 | 否 | 保存 resource_code 对应业务属性。 |
| resource_type | TEXT | 否 | 否 | 保存 resource_type 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 是 | 保存 workflow_step_execution_id 对应业务属性。 |
| device_command_execution_id | TEXT | 否 | 是 | 保存 device_command_execution_id 对应业务属性。 |
| command_type | TEXT | 否 | 是 | 保存 command_type 对应业务属性。 |
| wait_reason | TEXT | 否 | 是 | 保存 wait_reason 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| acquired_at_utc | TEXT | 否 | 是 | 保存 acquired_at_utc 对应业务属性。 |
| released_at_utc | TEXT | 否 | 是 | 保存 released_at_utc 对应业务属性。 |

# machine_runs

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |
| coordinate_profile_version_id | TEXT | 否 | 是 | 保存 coordinate_profile_version_id 对应业务属性。 |
| coordinate_snapshot_json | TEXT | 否 | 否 | 保存 coordinate_snapshot_json 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| current_major_step_code | TEXT | 否 | 是 | 保存 current_major_step_code 对应业务属性。 |
| fault_message | TEXT | 否 | 是 | 保存 fault_message 对应业务属性。 |
| pause_requested | INTEGER | 否 | 否 | 保存 pause_requested 对应业务属性。 |
| requested_by_user_id | TEXT | 否 | 是 | 保存 requested_by_user_id 对应业务属性。 |
| run_code | TEXT | 否 | 否 | 保存 run_code 对应业务属性。 |
| started_at_utc | TEXT | 否 | 是 | 保存 started_at_utc 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| stop_requested | INTEGER | 否 | 否 | 保存 stop_requested 对应业务属性。 |
| liquid_class_selection_status | TEXT | 否 | 否 | 保存 liquid_class_selection_status 对应业务属性。 |
| liquid_class_snapshot_json | TEXT | 否 | 否 | 保存 liquid_class_snapshot_json 对应业务属性。 |

# mixer_channel_states

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| drawer_code | TEXT | 否 | 否 | 保存 drawer_code 对应业务属性。 |
| channel_no | INTEGER | 否 | 否 | 保存 channel_no 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| is_connected | INTEGER | 否 | 否 | 保存 is_connected 对应业务属性。 |
| current_round_key | TEXT | 否 | 是 | 保存 current_round_key 对应业务属性。 |
| current_command_id | TEXT | 否 | 是 | 保存 current_command_id 对应业务属性。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 是 | 保存 workflow_step_execution_id 对应业务属性。 |
| device_command_execution_id | TEXT | 否 | 是 | 保存 device_command_execution_id 对应业务属性。 |
| fault_code | TEXT | 否 | 是 | 保存 fault_code 对应业务属性。 |
| fault_message | TEXT | 否 | 是 | 保存 fault_message 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 否 | 保存 updated_at_utc 对应业务属性。 |

# mock_demo_data_tags

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| entity_type | TEXT | 否 | 否 | 保存 entity_type 对应业务属性。 |
| entity_id | TEXT | 否 | 否 | 保存 entity_id 对应业务属性。 |
| demo_key | TEXT | 否 | 否 | 保存 demo_key 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# mock_lis_entries

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| normalized_code | TEXT | 否 | 否 | 保存 normalized_code 对应业务属性。 |
| primary_antibody_code | TEXT | 否 | 是 | 保存 primary_antibody_code 对应业务属性。 |
| scenario | TEXT | 否 | 否 | 保存 scenario 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| metadata_json | TEXT | 否 | 否 | 保存 metadata_json 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |

# needle_states

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| needle_code | TEXT | 否 | 否 | 保存 needle_code 对应业务属性。 |
| needle_no | INTEGER | 否 | 否 | 保存 needle_no 对应业务属性。 |
| is_connected | INTEGER | 否 | 否 | 保存 is_connected 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| loaded_source_type | TEXT | 否 | 否 | 保存 loaded_source_type 对应业务属性。 |
| loaded_reagent_code | TEXT | 否 | 是 | 保存 loaded_reagent_code 对应业务属性。 |
| source_bottle_id | TEXT | 否 | 是 | 保存 source_bottle_id 对应业务属性。 |
| dab_batch_id | TEXT | 否 | 是 | 保存 dab_batch_id 对应业务属性。 |
| system_liquid_source_type | TEXT | 否 | 是 | 保存 system_liquid_source_type 对应业务属性。 |
| source_position_code | TEXT | 否 | 是 | 保存 source_position_code 对应业务属性。 |
| volume_ul | INTEGER | 否 | 否 | 保存 volume_ul 对应业务属性。 |
| liquid_class_version_id | TEXT | 否 | 是 | 保存 liquid_class_version_id 对应业务属性。 |
| liquid_class_version_no | INTEGER | 否 | 是 | 保存 liquid_class_version_no 对应业务属性。 |
| liquid_class_parameters_json | TEXT | 否 | 否 | 保存 liquid_class_parameters_json 对应业务属性。 |
| needs_wash | INTEGER | 否 | 否 | 保存 needs_wash 对应业务属性。 |
| current_command_id | TEXT | 否 | 是 | 保存 current_command_id 对应业务属性。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 是 | 保存 workflow_step_execution_id 对应业务属性。 |
| device_command_execution_id | TEXT | 否 | 是 | 保存 device_command_execution_id 对应业务属性。 |
| last_error_code | TEXT | 否 | 是 | 保存 last_error_code 对应业务属性。 |
| last_error_message | TEXT | 否 | 是 | 保存 last_error_message 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 否 | 保存 updated_at_utc 对应业务属性。 |

# physical_slots

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| drawer_id | TEXT | 否 | 否 | 保存 drawer_id 对应业务属性。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| slot_no | INTEGER | 否 | 否 | 保存 slot_no 对应业务属性。 |
| vertical_order_from_bottom | INTEGER | 否 | 否 | 保存 vertical_order_from_bottom 对应业务属性。 |
| heat_point_id | INTEGER | 否 | 否 | 保存 heat_point_id 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# pipetting_operations

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| operation_type | TEXT | 否 | 否 | 保存 operation_type 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| needle_code | TEXT | 否 | 是 | 保存 needle_code 对应业务属性。 |
| execution_mode | TEXT | 否 | 否 | 保存 execution_mode 对应业务属性。 |
| target_point_code | TEXT | 否 | 是 | 保存 target_point_code 对应业务属性。 |
| secondary_target_point_code | TEXT | 否 | 是 | 保存 secondary_target_point_code 对应业务属性。 |
| coordinate_profile_version_id | TEXT | 否 | 是 | 保存 coordinate_profile_version_id 对应业务属性。 |
| liquid_class_version_id | TEXT | 否 | 是 | 保存 liquid_class_version_id 对应业务属性。 |
| liquid_class_version_no | INTEGER | 否 | 是 | 保存 liquid_class_version_no 对应业务属性。 |
| liquid_class_parameters_json | TEXT | 否 | 否 | 保存 liquid_class_parameters_json 对应业务属性。 |
| source_type | TEXT | 否 | 否 | 保存 source_type 对应业务属性。 |
| reagent_code | TEXT | 否 | 是 | 保存 reagent_code 对应业务属性。 |
| reagent_bottle_id | TEXT | 否 | 是 | 保存 reagent_bottle_id 对应业务属性。 |
| dab_batch_id | TEXT | 否 | 是 | 保存 dab_batch_id 对应业务属性。 |
| system_liquid_source_type | TEXT | 否 | 是 | 保存 system_liquid_source_type 对应业务属性。 |
| source_position_code | TEXT | 否 | 是 | 保存 source_position_code 对应业务属性。 |
| volume_ul | INTEGER | 否 | 否 | 保存 volume_ul 对应业务属性。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 是 | 保存 workflow_step_execution_id 对应业务属性。 |
| device_command_execution_id | TEXT | 否 | 是 | 保存 device_command_execution_id 对应业务属性。 |
| error_code | TEXT | 否 | 是 | 保存 error_code 对应业务属性。 |
| error_message | TEXT | 否 | 是 | 保存 error_message 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |

# primary_antibody_workflow_mappings

## 表作用

存储工作流定义、步骤配置及流程执行相关数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| primary_antibody_code | TEXT | 否 | 否 | 保存 primary_antibody_code 对应业务属性。 |
| workflow_version_id | TEXT | 否 | 否 | 保存 workflow_version_id 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# pump_channel_states

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| pwm_channel_code | TEXT | 否 | 否 | 保存 pwm_channel_code 对应业务属性。 |
| pwm_channel_no | INTEGER | 否 | 否 | 保存 pwm_channel_no 对应业务属性。 |
| drawer_code | TEXT | 否 | 否 | 保存 drawer_code 对应业务属性。 |
| speed_percent | INTEGER | 否 | 否 | 保存 speed_percent 对应业务属性。 |
| direction | TEXT | 否 | 否 | 保存 direction 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| is_connected | INTEGER | 否 | 否 | 保存 is_connected 对应业务属性。 |
| target_point_code | TEXT | 否 | 是 | 保存 target_point_code 对应业务属性。 |
| duration_ms | INTEGER | 否 | 是 | 保存 duration_ms 对应业务属性。 |
| current_command_id | TEXT | 否 | 是 | 保存 current_command_id 对应业务属性。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 是 | 保存 workflow_step_execution_id 对应业务属性。 |
| device_command_execution_id | TEXT | 否 | 是 | 保存 device_command_execution_id 对应业务属性。 |
| fault_code | TEXT | 否 | 是 | 保存 fault_code 对应业务属性。 |
| fault_message | TEXT | 否 | 是 | 保存 fault_message 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 否 | 保存 updated_at_utc 对应业务属性。 |

# reagent_bottles

## 表作用

存储试剂定义、试剂配置及试剂业务数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| reagent_definition_id | TEXT | 否 | 否 | 保存 reagent_definition_id 对应业务属性。 |
| full_barcode | TEXT | 否 | 否 | 保存 full_barcode 对应业务属性。 |
| reagent_code | TEXT | 否 | 否 | 保存 reagent_code 对应业务属性。 |
| production_batch_no | TEXT | 否 | 否 | 保存 production_batch_no 对应业务属性。 |
| serial_no | TEXT | 否 | 否 | 保存 serial_no 对应业务属性。 |
| initial_volume_ul | INTEGER | 否 | 否 | 保存 initial_volume_ul 对应业务属性。 |
| remaining_volume_ul | INTEGER | 否 | 否 | 保存 remaining_volume_ul 对应业务属性。 |
| expiration_date | TEXT | 否 | 否 | 保存 expiration_date 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| first_scanned_at_utc | TEXT | 否 | 是 | 保存 first_scanned_at_utc 对应业务属性。 |
| last_scanned_at_utc | TEXT | 否 | 是 | 保存 last_scanned_at_utc 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |

# reagent_consumptions

## 表作用

存储试剂定义、试剂配置及试剂业务数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| dab_batch_id | TEXT | 否 | 是 | 保存 dab_batch_id 对应业务属性。 |
| device_command_execution_id | TEXT | 否 | 是 | 保存 device_command_execution_id 对应业务属性。 |
| machine_run_id | TEXT | 否 | 否 | 保存 machine_run_id 对应业务属性。 |
| reagent_bottle_id | TEXT | 否 | 否 | 保存 reagent_bottle_id 对应业务属性。 |
| reagent_code | TEXT | 否 | 否 | 保存 reagent_code 对应业务属性。 |
| source_role | TEXT | 否 | 否 | 保存 source_role 对应业务属性。 |
| volume_ul | INTEGER | 否 | 否 | 保存 volume_ul 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 否 | 保存 workflow_step_execution_id 对应业务属性。 |

# reagent_definitions

## 表作用

存储试剂定义、试剂配置及试剂业务数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| reagent_code | TEXT | 否 | 否 | 保存 reagent_code 对应业务属性。 |
| name | TEXT | 否 | 否 | 名称。 |
| liquid_class_profile_id | TEXT | 否 | 是 | 保存 liquid_class_profile_id 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |
| legacy_metadata_json | TEXT | 否 | 否 | 保存 legacy_metadata_json 对应业务属性。 |
| minimum_alarm_volume_ul | INTEGER | 否 | 是 | 保存 minimum_alarm_volume_ul 对应业务属性。 |
| reagent_type | TEXT | 否 | 否 | 保存 reagent_type 对应业务属性。 |

# reagent_rack_placements

## 表作用

存储试剂定义、试剂配置及试剂业务数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| reagent_bottle_id | TEXT | 否 | 否 | 保存 reagent_bottle_id 对应业务属性。 |
| reagent_rack_position_id | TEXT | 否 | 否 | 保存 reagent_rack_position_id 对应业务属性。 |
| reagent_scan_session_id | TEXT | 否 | 是 | 保存 reagent_scan_session_id 对应业务属性。 |
| placed_at_utc | TEXT | 否 | 否 | 保存 placed_at_utc 对应业务属性。 |
| removed_at_utc | TEXT | 否 | 是 | 保存 removed_at_utc 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# reagent_rack_positions

## 表作用

存储试剂定义、试剂配置及试剂业务数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| position_no | INTEGER | 否 | 否 | 保存 position_no 对应业务属性。 |
| column_no | INTEGER | 否 | 否 | 保存 column_no 对应业务属性。 |
| row_no | INTEGER | 否 | 否 | 保存 row_no 对应业务属性。 |
| scanner_channel_no | INTEGER | 否 | 否 | 保存 scanner_channel_no 对应业务属性。 |
| scanner_channel_code | TEXT | 否 | 否 | 保存 scanner_channel_code 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# reagent_reservations

## 表作用

存储试剂定义、试剂配置及试剂业务数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| command_id | TEXT | 否 | 是 | 保存 command_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| created_by_user_id | TEXT | 否 | 是 | 保存 created_by_user_id 对应业务属性。 |
| dab_batch_id | TEXT | 否 | 是 | 保存 dab_batch_id 对应业务属性。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| reagent_bottle_id | TEXT | 否 | 是 | 保存 reagent_bottle_id 对应业务属性。 |
| reagent_code | TEXT | 否 | 否 | 保存 reagent_code 对应业务属性。 |
| required_volume_ul | INTEGER | 否 | 否 | 保存 required_volume_ul 对应业务属性。 |
| reservation_kind | TEXT | 否 | 否 | 保存 reservation_kind 对应业务属性。 |
| reserved_volume_ul | INTEGER | 否 | 否 | 保存 reserved_volume_ul 对应业务属性。 |
| source_role | TEXT | 否 | 否 | 保存 source_role 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |

# reagent_scan_items

## 表作用

存储试剂定义、试剂配置及试剂业务数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| reagent_scan_session_id | TEXT | 否 | 否 | 保存 reagent_scan_session_id 对应业务属性。 |
| reagent_rack_position_id | TEXT | 否 | 否 | 保存 reagent_rack_position_id 对应业务属性。 |
| scanner_channel_no | INTEGER | 否 | 否 | 保存 scanner_channel_no 对应业务属性。 |
| scanner_channel_code | TEXT | 否 | 否 | 保存 scanner_channel_code 对应业务属性。 |
| locator_code | TEXT | 否 | 是 | 保存 locator_code 对应业务属性。 |
| scan_result | TEXT | 否 | 否 | 保存 scan_result 对应业务属性。 |
| raw_barcode | TEXT | 否 | 是 | 保存 raw_barcode 对应业务属性。 |
| parsed_reagent_code | TEXT | 否 | 是 | 保存 parsed_reagent_code 对应业务属性。 |
| parsed_quantity_ul | INTEGER | 否 | 是 | 保存 parsed_quantity_ul 对应业务属性。 |
| parsed_batch_no | TEXT | 否 | 是 | 保存 parsed_batch_no 对应业务属性。 |
| parsed_serial_no | TEXT | 否 | 是 | 保存 parsed_serial_no 对应业务属性。 |
| is_validation_passed | INTEGER | 否 | 否 | 保存 is_validation_passed 对应业务属性。 |
| validation_message | TEXT | 否 | 否 | 保存 validation_message 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# reagent_scan_sessions

## 表作用

存储试剂定义、试剂配置及试剂业务数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| session_code | TEXT | 否 | 否 | 保存 session_code 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| started_at_utc | TEXT | 否 | 否 | 保存 started_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |
| created_by_user_id | TEXT | 否 | 是 | 保存 created_by_user_id 对应业务属性。 |

# robot_arm_states

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| is_homed | INTEGER | 否 | 否 | 保存 is_homed 对应业务属性。 |
| is_connected | INTEGER | 否 | 否 | 保存 is_connected 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| current_target_point_code | TEXT | 否 | 是 | 保存 current_target_point_code 对应业务属性。 |
| current_x_um | INTEGER | 否 | 是 | 保存 current_x_um 对应业务属性。 |
| current_y_um | INTEGER | 否 | 是 | 保存 current_y_um 对应业务属性。 |
| current_z_um | INTEGER | 否 | 是 | 保存 current_z_um 对应业务属性。 |
| coordinate_profile_version_id | TEXT | 否 | 是 | 保存 coordinate_profile_version_id 对应业务属性。 |
| current_command_id | TEXT | 否 | 是 | 保存 current_command_id 对应业务属性。 |
| machine_run_id | TEXT | 否 | 是 | 保存 machine_run_id 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 是 | 保存 workflow_step_execution_id 对应业务属性。 |
| device_command_execution_id | TEXT | 否 | 是 | 保存 device_command_execution_id 对应业务属性。 |
| last_error_code | TEXT | 否 | 是 | 保存 last_error_code 对应业务属性。 |
| last_error_message | TEXT | 否 | 是 | 保存 last_error_message 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 否 | 保存 updated_at_utc 对应业务属性。 |

# roles

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| name | TEXT | 否 | 否 | 名称。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# sample_scan_items

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| sample_scan_session_id | TEXT | 否 | 否 | 保存 sample_scan_session_id 对应业务属性。 |
| slot_code | TEXT | 否 | 是 | 保存 slot_code 对应业务属性。 |
| scan_kind | TEXT | 否 | 否 | 保存 scan_kind 对应业务属性。 |
| scan_status | TEXT | 否 | 否 | 保存 scan_status 对应业务属性。 |
| raw_code | TEXT | 否 | 是 | 保存 raw_code 对应业务属性。 |
| normalized_code | TEXT | 否 | 是 | 保存 normalized_code 对应业务属性。 |
| primary_antibody_code | TEXT | 否 | 是 | 保存 primary_antibody_code 对应业务属性。 |
| error_reason | TEXT | 否 | 是 | 保存 error_reason 对应业务属性。 |
| device_status | TEXT | 否 | 否 | 保存 device_status 对应业务属性。 |
| scanned_at_utc | TEXT | 否 | 否 | 保存 scanned_at_utc 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# sample_scan_sessions

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| session_code | TEXT | 否 | 否 | 保存 session_code 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| started_at_utc | TEXT | 否 | 否 | 保存 started_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |
| created_by_user_id | TEXT | 否 | 是 | 保存 created_by_user_id 对应业务属性。 |

# slide_tasks

## 表作用

存储任务定义、任务状态及执行数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| channel_batch_id | TEXT | 否 | 否 | 保存 channel_batch_id 对应业务属性。 |
| staining_task_id | TEXT | 否 | 否 | 保存 staining_task_id 对应业务属性。 |
| physical_slot_id | TEXT | 否 | 否 | 保存 physical_slot_id 对应业务属性。 |
| slot_code | TEXT | 否 | 否 | 保存 slot_code 对应业务属性。 |
| task_type | TEXT | 否 | 否 | 保存 task_type 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# staining_tasks

## 表作用

存储任务定义、任务状态及执行数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| task_code | TEXT | 否 | 否 | 保存 task_code 对应业务属性。 |
| task_type | TEXT | 否 | 否 | 保存 task_type 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| physical_slot_id | TEXT | 否 | 否 | 保存 physical_slot_id 对应业务属性。 |
| workflow_definition_id | TEXT | 否 | 否 | 保存 workflow_definition_id 对应业务属性。 |
| workflow_version_id | TEXT | 否 | 否 | 保存 workflow_version_id 对应业务属性。 |
| workflow_snapshot_json | TEXT | 否 | 否 | 保存 workflow_snapshot_json 对应业务属性。 |
| input_mode | TEXT | 否 | 是 | 保存 input_mode 对应业务属性。 |
| raw_code | TEXT | 否 | 是 | 保存 raw_code 对应业务属性。 |
| normalized_code | TEXT | 否 | 是 | 保存 normalized_code 对应业务属性。 |
| primary_antibody_code | TEXT | 否 | 是 | 保存 primary_antibody_code 对应业务属性。 |
| candidate_results_json | TEXT | 否 | 否 | 保存 candidate_results_json 对应业务属性。 |
| created_by_user_id | TEXT | 否 | 是 | 保存 created_by_user_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |
| compatibility_validation_message | TEXT | 否 | 是 | 保存 compatibility_validation_message 对应业务属性。 |
| compatibility_validation_status | TEXT | 否 | 是 | 保存 compatibility_validation_status 对应业务属性。 |
| confirmed_primary_antibody_code | TEXT | 否 | 是 | 保存 confirmed_primary_antibody_code 对应业务属性。 |
| lis_candidate_primary_antibody_codes_json | TEXT | 否 | 是 | 保存 lis_candidate_primary_antibody_codes_json 对应业务属性。 |
| lis_query_log_id | TEXT | 否 | 是 | 保存 lis_query_log_id 对应业务属性。 |
| normalized_sample_code | TEXT | 否 | 是 | 保存 normalized_sample_code 对应业务属性。 |
| raw_sample_code | TEXT | 否 | 是 | 保存 raw_sample_code 对应业务属性。 |

# system_liquid_usages

## 表作用

存储液体类型、液体参数及移液相关配置。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| machine_run_id | TEXT | 否 | 否 | 保存 machine_run_id 对应业务属性。 |
| workflow_step_execution_id | TEXT | 否 | 否 | 保存 workflow_step_execution_id 对应业务属性。 |
| device_command_execution_id | TEXT | 否 | 否 | 保存 device_command_execution_id 对应业务属性。 |
| dab_batch_id | TEXT | 否 | 否 | 保存 dab_batch_id 对应业务属性。 |
| source_type | TEXT | 否 | 否 | 保存 source_type 对应业务属性。 |
| volume_ul | INTEGER | 否 | 否 | 保存 volume_ul 对应业务属性。 |
| level_snapshot_json | TEXT | 否 | 否 | 保存 level_snapshot_json 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# temperature_telemetry

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| source_type | TEXT | 否 | 否 | 保存 source_type 对应业务属性。 |
| source_id | TEXT | 否 | 否 | 保存 source_id 对应业务属性。 |
| drawer_code | TEXT | 否 | 是 | 保存 drawer_code 对应业务属性。 |
| board_no | INTEGER | 否 | 是 | 保存 board_no 对应业务属性。 |
| slot_no | INTEGER | 否 | 是 | 保存 slot_no 对应业务属性。 |
| point_no | INTEGER | 否 | 是 | 保存 point_no 对应业务属性。 |
| current_temperature_deci_c | INTEGER | 否 | 否 | 保存 current_temperature_deci_c 对应业务属性。 |
| target_temperature_deci_c | INTEGER | 否 | 否 | 保存 target_temperature_deci_c 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| is_connected | INTEGER | 否 | 否 | 保存 is_connected 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| fault_code | TEXT | 否 | 是 | 保存 fault_code 对应业务属性。 |
| recorded_at_utc | TEXT | 否 | 否 | 保存 recorded_at_utc 对应业务属性。 |

# thermal_point_states

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| drawer_code | TEXT | 否 | 否 | 保存 drawer_code 对应业务属性。 |
| board_no | INTEGER | 否 | 否 | 保存 board_no 对应业务属性。 |
| slot_no | INTEGER | 否 | 否 | 保存 slot_no 对应业务属性。 |
| point_no | INTEGER | 否 | 否 | 保存 point_no 对应业务属性。 |
| current_temperature_deci_c | INTEGER | 否 | 否 | 保存 current_temperature_deci_c 对应业务属性。 |
| target_temperature_deci_c | INTEGER | 否 | 否 | 保存 target_temperature_deci_c 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| is_connected | INTEGER | 否 | 否 | 保存 is_connected 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| fault_code | TEXT | 否 | 是 | 保存 fault_code 对应业务属性。 |
| fault_message | TEXT | 否 | 是 | 保存 fault_message 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 否 | 保存 updated_at_utc 对应业务属性。 |

# user_roles

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| user_id | TEXT | 否 | 否 | 保存 user_id 对应业务属性。 |
| role_id | TEXT | 否 | 否 | 保存 role_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# users

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| username | TEXT | 否 | 否 | 保存 username 对应业务属性。 |
| display_name | TEXT | 否 | 否 | 保存 display_name 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| password_hash | TEXT | 否 | 是 | 保存 password_hash 对应业务属性。 |
| password_hash_algorithm | TEXT | 否 | 是 | 保存 password_hash_algorithm 对应业务属性。 |
| password_updated_at_utc | TEXT | 否 | 是 | 保存 password_updated_at_utc 对应业务属性。 |

# wash_positions

## 表作用

存储该业务模块对应的数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| wash_type | TEXT | 否 | 否 | 保存 wash_type 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# workflow_assignment_history

## 表作用

存储工作流定义、步骤配置及流程执行相关数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| action_type | TEXT | 否 | 否 | 保存 action_type 对应业务属性。 |
| actor_user_id | TEXT | 否 | 是 | 保存 actor_user_id 对应业务属性。 |
| channel_batch_id | TEXT | 否 | 否 | 保存 channel_batch_id 对应业务属性。 |
| command_id | TEXT | 否 | 是 | 保存 command_id 对应业务属性。 |
| correlation_id | TEXT | 否 | 是 | 保存 correlation_id 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| new_experiment_type | TEXT | 否 | 是 | 保存 new_experiment_type 对应业务属性。 |
| new_workflow_snapshot_json | TEXT | 否 | 是 | 保存 new_workflow_snapshot_json 对应业务属性。 |
| new_workflow_version_id | TEXT | 否 | 是 | 保存 new_workflow_version_id 对应业务属性。 |
| old_experiment_type | TEXT | 否 | 是 | 保存 old_experiment_type 对应业务属性。 |
| old_workflow_snapshot_json | TEXT | 否 | 是 | 保存 old_workflow_snapshot_json 对应业务属性。 |
| old_workflow_version_id | TEXT | 否 | 是 | 保存 old_workflow_version_id 对应业务属性。 |
| operator_user_id | TEXT | 否 | 是 | 保存 operator_user_id 对应业务属性。 |
| reason | TEXT | 否 | 否 | 保存 reason 对应业务属性。 |

# workflow_definitions

## 表作用

存储工作流定义、步骤配置及流程执行相关数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| code | TEXT | 否 | 否 | 业务编码。 |
| name | TEXT | 否 | 否 | 名称。 |
| workflow_type | TEXT | 否 | 否 | 保存 workflow_type 对应业务属性。 |
| is_enabled | INTEGER | 否 | 否 | 是否启用。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |
| description | TEXT | 否 | 否 | 描述信息。 |

# workflow_executions

## 表作用

存储工作流定义、步骤配置及流程执行相关数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| machine_run_id | TEXT | 否 | 否 | 保存 machine_run_id 对应业务属性。 |
| slide_task_id | TEXT | 否 | 否 | 保存 slide_task_id 对应业务属性。 |
| workflow_version_id | TEXT | 否 | 否 | 保存 workflow_version_id 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| started_at_utc | TEXT | 否 | 是 | 保存 started_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |

# workflow_reagent_requirements

## 表作用

存储工作流定义、步骤配置及流程执行相关数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| workflow_version_id | TEXT | 否 | 否 | 保存 workflow_version_id 对应业务属性。 |
| reagent_code | TEXT | 否 | 否 | 保存 reagent_code 对应业务属性。 |
| required_volume_ul | INTEGER | 否 | 是 | 保存 required_volume_ul 对应业务属性。 |
| is_required | INTEGER | 否 | 否 | 保存 is_required 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |

# workflow_step_executions

## 表作用

存储工作流定义、步骤配置及流程执行相关数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| workflow_execution_id | TEXT | 否 | 否 | 保存 workflow_execution_id 对应业务属性。 |
| step_no | INTEGER | 否 | 否 | 保存 step_no 对应业务属性。 |
| major_step_code | TEXT | 否 | 否 | 保存 major_step_code 对应业务属性。 |
| step_name | TEXT | 否 | 否 | 保存 step_name 对应业务属性。 |
| action_type | TEXT | 否 | 否 | 保存 action_type 对应业务属性。 |
| reagent_code | TEXT | 否 | 是 | 保存 reagent_code 对应业务属性。 |
| volume_ul | INTEGER | 否 | 是 | 保存 volume_ul 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| redo_count | INTEGER | 否 | 否 | 保存 redo_count 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| started_at_utc | TEXT | 否 | 是 | 保存 started_at_utc 对应业务属性。 |
| completed_at_utc | TEXT | 否 | 是 | 保存 completed_at_utc 对应业务属性。 |
| target_temperature_deci_c | INTEGER | 否 | 是 | 保存 target_temperature_deci_c 对应业务属性。 |

# workflow_steps

## 表作用

存储工作流定义、步骤配置及流程执行相关数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| workflow_version_id | TEXT | 否 | 否 | 保存 workflow_version_id 对应业务属性。 |
| step_no | INTEGER | 否 | 否 | 保存 step_no 对应业务属性。 |
| major_step_code | TEXT | 否 | 否 | 保存 major_step_code 对应业务属性。 |
| action_type | TEXT | 否 | 否 | 保存 action_type 对应业务属性。 |
| reagent_code | TEXT | 否 | 是 | 保存 reagent_code 对应业务属性。 |
| volume_ul | INTEGER | 否 | 是 | 保存 volume_ul 对应业务属性。 |
| duration_seconds | INTEGER | 否 | 是 | 保存 duration_seconds 对应业务属性。 |
| target_temperature_deci_c | INTEGER | 否 | 是 | 保存 target_temperature_deci_c 对应业务属性。 |
| mix_parameters_json | TEXT | 否 | 否 | 保存 mix_parameters_json 对应业务属性。 |
| wash_parameters_json | TEXT | 否 | 否 | 保存 wash_parameters_json 对应业务属性。 |
| failure_strategy | TEXT | 否 | 否 | 保存 failure_strategy 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |
| legacy_parameters_json | TEXT | 否 | 否 | 保存 legacy_parameters_json 对应业务属性。 |
| step_name | TEXT | 否 | 否 | 保存 step_name 对应业务属性。 |

# workflow_versions

## 表作用

存储工作流定义、步骤配置及流程执行相关数据。

## 字段说明

| 字段名 | 类型 | 主键 | 可空 | 字段含义 |
|---|---|---|---|---|
| id | TEXT | 是 | 否 | 记录唯一标识。 |
| change_note | TEXT | 否 | 否 | 保存 change_note 对应业务属性。 |
| created_at_utc | TEXT | 否 | 否 | 保存 created_at_utc 对应业务属性。 |
| default_experiment_type | TEXT | 否 | 是 | 保存 default_experiment_type 对应业务属性。 |
| published_at_utc | TEXT | 否 | 是 | 保存 published_at_utc 对应业务属性。 |
| retired_at_utc | TEXT | 否 | 是 | 保存 retired_at_utc 对应业务属性。 |
| status | TEXT | 否 | 否 | 当前状态。 |
| updated_at_utc | TEXT | 否 | 是 | 保存 updated_at_utc 对应业务属性。 |
| version_label | TEXT | 否 | 否 | 保存 version_label 对应业务属性。 |
| version_no | INTEGER | 否 | 否 | 保存 version_no 对应业务属性。 |
| workflow_definition_id | TEXT | 否 | 否 | 保存 workflow_definition_id 对应业务属性。 |

