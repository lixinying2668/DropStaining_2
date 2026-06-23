from __future__ import annotations

from enum import Enum
from typing import Any, Literal
from pydantic import BaseModel, Field


class Role(str, Enum):
    operator = "operator"
    engineer = "engineer"
    admin = "admin"


class RunStatus(str, Enum):
    idle = "idle"
    initialized = "initialized"
    ready = "ready"
    running = "running"
    paused = "paused"
    stopped = "stopped"
    completed = "completed"
    error = "error"


class ChannelStatus(str, Enum):
    empty = "empty"
    loaded = "loaded"
    waiting = "waiting"
    dispensing = "dispensing"
    incubating = "incubating"
    washing = "washing"
    mixing = "mixing"
    completed = "completed"
    error = "error"


class SlideStatus(str, Enum):
    empty = "empty"
    loaded = "loaded"
    configured = "configured"
    running = "running"
    completed = "completed"
    error = "error"


class LoginRequest(BaseModel):
    username: str
    password: str
    role: Role


class User(BaseModel):
    username: str
    role: Role
    display_name: str
    enabled: bool = True


class SystemCheck(BaseModel):
    robotic_arm_home: bool = False
    reagent_cooling: bool = False
    scanner_online: bool = False
    liquid_sensor: bool = False
    needle_wash: bool = False
    pure_water_ok: bool = True
    waste_tank_full: bool = False
    toxic_tank_full: bool = False
    current_temperature_c: float = 42.0
    reagent_temperature_c: float = 8.0


class ProtocolStep(BaseModel):
    id: int
    name: str
    step_type: Literal["manual", "dispense", "wash", "incubate", "mix", "dab_prepare"]
    reagent_code: str | None = None
    volume_ul: float | None = None
    min_volume_ul: float | None = None
    max_volume_ul: float | None = None
    duration_s: int = 0
    temperature_c: float | None = 42.0
    per_slide_temperature: bool = False
    mix_after: bool = False
    channel_level: bool = False
    machine_execute: bool = True
    note: str = ""


class Protocol(BaseModel):
    code: str
    name: str
    version: str = "1.0"
    description: str = ""
    default_temperature_c: float = 42.0
    steps: list[ProtocolStep]


class Slide(BaseModel):
    id: str
    channel: int
    slot: int
    barcode: str
    protocol_code: str = "IHC"
    antibody_code: str = "AB-DEFAULT"
    primary_volume_ul: float = 80.0
    temperature_c: float = 42.0
    status: SlideStatus = SlideStatus.loaded
    current_step: str = "待配置"
    progress: int = 0
    error: str | None = None


class Channel(BaseModel):
    id: int
    name: str
    status: ChannelStatus = ChannelStatus.empty
    progress: int = 0
    current_step: str = "空闲"
    slides: list[Slide] = Field(default_factory=list)
    selected: bool = False


class Reagent(BaseModel):
    position: str
    barcode: str
    name: str
    code: str
    reagent_type: str
    volume_ml: float
    min_alarm_ml: float = 1.0
    available: bool = True
    lot_no: str | None = None
    expire_date: str | None = None


class DABCalculation(BaseModel):
    slide_count: int
    extra_slide_equivalent: int = 2
    total_ml: float
    dab_a_ml: float
    dab_b_ml: float
    pure_water_ml: float


class RuntimeState(BaseModel):
    run_id: str = ""
    status: RunStatus = RunStatus.idle
    initialized: bool = False
    system: SystemCheck = Field(default_factory=SystemCheck)
    channels: list[Channel] = Field(default_factory=list)
    reagents: list[Reagent] = Field(default_factory=list)
    active_user: User | None = None
    alarms: list[str] = Field(default_factory=list)
    logs: list[str] = Field(default_factory=list)


class SlideUpdate(BaseModel):
    slide_id: str
    protocol_code: str = "IHC"
    antibody_code: str
    primary_volume_ul: float = Field(default=80, ge=50, le=100)
    temperature_c: float = Field(default=42, ge=40, le=55)


class AddSlideRequest(BaseModel):
    channel: int = Field(ge=1, le=4)
    slot: int = Field(ge=1, le=4)
    barcode: str
    protocol_code: str = "IHC"
    antibody_code: str = "AB-DEFAULT"
    temperature_c: float = Field(default=42, ge=40, le=55)


class EngineerCommand(BaseModel):
    module: Literal["arm", "scanner", "pump", "mixer", "heater", "needle", "serial"]
    action: str
    channel: int | None = Field(default=None, ge=1, le=4)
    position: str | None = None
    volume_ul: float | None = None
    speed: float | None = None
    duration_s: int | None = None
    temperature_c: float | None = None
    payload: dict[str, Any] = Field(default_factory=dict)
