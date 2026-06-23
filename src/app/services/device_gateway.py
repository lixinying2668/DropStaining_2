from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime


@dataclass
class DeviceResult:
    ok: bool
    message: str
    raw: dict | None = None


class DeviceGateway:
    """模拟设备网关。

    正式项目中应把这里替换为：
    - 机械臂 SDK 调用
    - 下位机串口/以太网协议
    - 扫码器 DCR55 协议
    - 温控、泵、混匀电机、液位传感器命令
    """

    def _ok(self, message: str, **raw) -> DeviceResult:
        return DeviceResult(ok=True, message=message, raw={"time": datetime.now().isoformat(), **raw})

    async def initialize(self) -> DeviceResult:
        return self._ok("模拟初始化完成")

    async def move_arm_to(self, position: str) -> DeviceResult:
        return self._ok(f"机械臂移动到 {position}", position=position)

    async def aspirate(self, needle: int, position: str, volume_ul: float) -> DeviceResult:
        return self._ok(f"{needle}号针从 {position} 吸液 {volume_ul}uL", needle=needle, position=position, volume_ul=volume_ul)

    async def dispense(self, needle: int, channel: int, slot: int, volume_ul: float) -> DeviceResult:
        return self._ok(f"{needle}号针向通道{channel}-{slot} 加液 {volume_ul}uL", needle=needle, channel=channel, slot=slot, volume_ul=volume_ul)

    async def wash_needle(self, needle: int, seconds: int = 10) -> DeviceResult:
        return self._ok(f"{needle}号针洗针 {seconds}s：打水4s-停2s-打水4s", needle=needle, seconds=seconds)

    async def run_wash_pump(self, channel: int, duration_s: int, flow_rate_ml_min: float = 250.0) -> DeviceResult:
        return self._ok(f"通道{channel}清洗泵运行 {duration_s}s，流速 {flow_rate_ml_min}mL/min", channel=channel, duration_s=duration_s, flow_rate_ml_min=flow_rate_ml_min)

    async def run_mixer(self, channel: int, duration_s: int = 5, speed: int = 500) -> DeviceResult:
        return self._ok(f"通道{channel}混匀 {duration_s}s，速度 {speed}", channel=channel, duration_s=duration_s, speed=speed)

    async def set_slide_temperature(self, channel: int, slot: int, temperature_c: float) -> DeviceResult:
        return self._ok(f"通道{channel}-{slot} 目标温度 {temperature_c}℃", channel=channel, slot=slot, temperature_c=temperature_c)

    async def test_command(self, module: str, action: str, **kwargs) -> DeviceResult:
        return self._ok(f"调试命令执行：{module}.{action}", module=module, action=action, **kwargs)


gateway = DeviceGateway()
