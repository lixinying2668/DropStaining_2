from __future__ import annotations

import json
from pathlib import Path
from datetime import datetime
from uuid import uuid4

from app.core.config import DATA_DIR, MAX_CHANNELS
from app.models import Channel, Reagent, RuntimeState, Slide, User, Role, RunStatus, ChannelStatus


class JsonStore:
    def __init__(self, data_dir: Path = DATA_DIR):
        self.data_dir = data_dir
        self.runtime_path = data_dir / "runtime.json"
        self.protocol_dir = data_dir / "protocols"
        self.reagents_path = data_dir / "reagents.json"
        self.users_path = data_dir / "users.json"
        self._state = self._load_or_init()

    @property
    def state(self) -> RuntimeState:
        return self._state

    def _load_json(self, path: Path, default):
        if not path.exists():
            return default
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            return default

    def _load_or_init(self) -> RuntimeState:
        raw = self._load_json(self.runtime_path, None)
        if raw:
            try:
                return RuntimeState(**raw)
            except Exception:
                pass
        return RuntimeState(channels=self.default_channels())

    def save(self) -> None:
        self.runtime_path.write_text(
            self.state.model_dump_json(indent=2), encoding="utf-8"
        )

    def reset_runtime(self) -> RuntimeState:
        self._state = RuntimeState(channels=self.default_channels())
        self.log("运行状态已重置")
        self.save()
        return self._state

    def default_channels(self) -> list[Channel]:
        return [Channel(id=i, name=f"通道{i}") for i in range(1, MAX_CHANNELS + 1)]

    def get_users(self) -> list[dict]:
        return self._load_json(self.users_path, [])

    def authenticate(self, username: str, password: str, role: Role) -> User | None:
        for item in self.get_users():
            if item.get("username") == username and item.get("password") == password and item.get("role") == role.value:
                if not item.get("enabled", True):
                    return None
                user = User(username=username, role=role, display_name=item.get("display_name", username))
                self.state.active_user = user
                self.log(f"用户登录：{user.display_name} / {role.value}")
                self.save()
                return user
        return None

    def load_reagents_from_seed(self) -> list[Reagent]:
        raw = self._load_json(self.reagents_path, [])
        return [Reagent(**item) for item in raw]

    def load_protocols_raw(self) -> list[dict]:
        protocols = []
        for path in sorted(self.protocol_dir.glob("*.json")):
            protocols.append(self._load_json(path, {}))
        return protocols

    def log(self, message: str) -> None:
        stamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        self.state.logs.insert(0, f"[{stamp}] {message}")
        self.state.logs = self.state.logs[:300]

    def alarm(self, message: str) -> None:
        self.state.alarms.insert(0, message)
        self.state.alarms = self.state.alarms[:50]
        self.log(f"报警：{message}")

    def initialize(self) -> RuntimeState:
        s = self.state.system
        s.robotic_arm_home = True
        s.reagent_cooling = True
        s.scanner_online = True
        s.liquid_sensor = True
        s.needle_wash = True
        self.state.initialized = True
        self.state.status = RunStatus.initialized
        self.log("仪器初始化完成：机械臂回零、制冷、扫码器、液位、洗针均通过")
        self.save()
        return self.state

    def mock_scan_samples(self, count: int = 8) -> RuntimeState:
        count = max(1, min(16, count))
        self.state.channels = self.default_channels()
        slide_no = 1
        for channel in self.state.channels:
            for slot in range(1, 5):
                if slide_no > count:
                    break
                slide = Slide(
                    id=f"S{slide_no:02d}",
                    channel=channel.id,
                    slot=slot,
                    barcode=f"SLIDE-{datetime.now().strftime('%m%d')}-{slide_no:02d}",
                    antibody_code="AB-DEFAULT" if slide_no % 2 else "AB-CK",
                    temperature_c=42.0,
                )
                channel.slides.append(slide)
                channel.status = ChannelStatus.loaded
                channel.current_step = "已上载"
                slide_no += 1
        self.state.status = RunStatus.ready if self.state.initialized else self.state.status
        self.log(f"样本区扫描完成：识别到 {count} 张玻片")
        self.save()
        return self.state

    def mock_scan_reagents(self) -> RuntimeState:
        self.state.reagents = self.load_reagents_from_seed()
        self.log(f"试剂区扫描完成：识别到 {len(self.state.reagents)} 个试剂位")
        missing = self.check_required_reagents()
        if missing:
            self.alarm("缺少试剂：" + "、".join(missing))
        self.save()
        return self.state

    def check_required_reagents(self) -> list[str]:
        required = {"BLOCK", "SECONDARY", "DAB-A", "DAB-B", "WATER", "HEMATOXYLIN", "WASH"}
        for ch in self.state.channels:
            for slide in ch.slides:
                if slide.protocol_code == "IHC" and slide.antibody_code:
                    required.add(slide.antibody_code)
        available = {r.code for r in self.state.reagents if r.available}
        return sorted(required - available)

    def all_slides(self) -> list[Slide]:
        return [slide for ch in self.state.channels for slide in ch.slides]

    def find_slide(self, slide_id: str) -> Slide | None:
        for slide in self.all_slides():
            if slide.id == slide_id:
                return slide
        return None

    def update_slide_config(self, slide_id: str, protocol_code: str, antibody_code: str, primary_volume_ul: float, temperature_c: float) -> RuntimeState:
        slide = self.find_slide(slide_id)
        if slide is None:
            raise KeyError(f"slide not found: {slide_id}")
        slide.protocol_code = protocol_code
        slide.antibody_code = antibody_code
        slide.primary_volume_ul = primary_volume_ul
        slide.temperature_c = temperature_c
        slide.status = "configured"
        slide.current_step = "已配置"
        self.log(f"玻片配置：{slide.id} {protocol_code} {antibody_code} {temperature_c}℃ {primary_volume_ul}uL")
        self.save()
        return self.state

    def add_slide(self, channel: int, slot: int, barcode: str, protocol_code: str, antibody_code: str, temperature_c: float) -> RuntimeState:
        ch = self.state.channels[channel - 1]
        for slide in ch.slides:
            if slide.slot == slot:
                raise ValueError(f"通道{channel}-{slot} 已有玻片")
        slide_id = f"S{len(self.all_slides()) + 1:02d}"
        slide = Slide(
            id=slide_id,
            channel=channel,
            slot=slot,
            barcode=barcode,
            protocol_code=protocol_code,
            antibody_code=antibody_code,
            temperature_c=temperature_c,
            status="configured",
            current_step="中途添加，等待调度",
        )
        ch.slides.append(slide)
        ch.slides.sort(key=lambda x: x.slot)
        ch.status = ChannelStatus.loaded
        self.log(f"中途添加玻片：通道{channel}-{slot} {barcode}")
        self.save()
        return self.state


store = JsonStore()
