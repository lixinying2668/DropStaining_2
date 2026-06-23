from __future__ import annotations

import asyncio
from datetime import datetime
from uuid import uuid4

from app.models import ChannelStatus, RunStatus, SlideStatus
from app.services.device_gateway import gateway
from app.services.protocol_engine import engine
from app.services.store import store


class Scheduler:
    def __init__(self):
        self._task: asyncio.Task | None = None
        self._stop_requested = False
        self._speed_factor = 0.03  # 演示压缩：真实秒数 * 0.03，最低 0.35s

    def is_running_task(self) -> bool:
        return self._task is not None and not self._task.done()

    async def start(self) -> None:
        if self.is_running_task():
            raise RuntimeError("实验已在运行")
        if not store.state.initialized:
            raise RuntimeError("请先完成初始化")
        if not store.all_slides():
            raise RuntimeError("未识别到玻片，请先扫描样本区")
        missing = store.check_required_reagents()
        if missing:
            raise RuntimeError("缺少试剂：" + "、".join(missing))
        self._stop_requested = False
        store.state.run_id = datetime.now().strftime("RUN-%Y%m%d-%H%M%S") + "-" + uuid4().hex[:6]
        store.state.status = RunStatus.running
        for ch in store.state.channels:
            if ch.slides:
                ch.status = ChannelStatus.waiting
                ch.current_step = "等待调度"
                ch.progress = 0
                for slide in ch.slides:
                    if slide.status != SlideStatus.completed:
                        slide.status = SlideStatus.running
                        slide.progress = 0
                        slide.current_step = "等待调度"
        store.log(f"实验开始：{store.state.run_id}")
        store.save()
        self._task = asyncio.create_task(self._run_loop())

    async def pause(self) -> None:
        if store.state.status != RunStatus.running:
            raise RuntimeError("当前状态不能暂停")
        store.state.status = RunStatus.paused
        store.log("实验暂停")
        store.save()

    async def resume(self) -> None:
        if store.state.status != RunStatus.paused:
            raise RuntimeError("当前状态不能继续")
        store.state.status = RunStatus.running
        store.log("实验继续")
        store.save()

    async def stop(self) -> None:
        self._stop_requested = True
        store.state.status = RunStatus.stopped
        for ch in store.state.channels:
            if ch.status not in (ChannelStatus.empty, ChannelStatus.completed):
                ch.status = ChannelStatus.waiting
                ch.current_step = "已终止"
        store.log("实验终止")
        store.save()

    async def _wait_scaled(self, seconds: int) -> None:
        delay = max(seconds * self._speed_factor, 0.35)
        elapsed = 0.0
        while elapsed < delay:
            if self._stop_requested or store.state.status == RunStatus.stopped:
                return
            while store.state.status == RunStatus.paused:
                await asyncio.sleep(0.2)
            await asyncio.sleep(0.1)
            elapsed += 0.1

    async def _run_loop(self) -> None:
        try:
            active_channels = [ch for ch in store.state.channels if ch.slides]
            for ch in active_channels:
                await self._run_channel(ch.id)
            if not self._stop_requested and store.state.status != RunStatus.stopped:
                store.state.status = RunStatus.completed
                for ch in store.state.channels:
                    if ch.slides:
                        ch.status = ChannelStatus.completed
                        ch.progress = 100
                        ch.current_step = "完成"
                for slide in store.all_slides():
                    slide.status = SlideStatus.completed
                    slide.progress = 100
                    slide.current_step = "完成"
                store.log("实验全部完成")
                store.save()
        except Exception as exc:
            store.state.status = RunStatus.error
            store.alarm(f"调度异常：{exc}")
            store.save()

    async def _run_channel(self, channel_id: int) -> None:
        ch = store.state.channels[channel_id - 1]
        slides = [s for s in ch.slides if s.status != SlideStatus.completed]
        if not slides:
            return
        protocol = engine.get_protocol(slides[0].protocol_code)
        executable_steps = [s for s in protocol.steps if s.machine_execute]
        total = len(executable_steps)
        for idx, step in enumerate(executable_steps, start=1):
            if self._stop_requested:
                return
            percent = int((idx - 1) / total * 100)
            ch.progress = percent
            ch.current_step = step.name

            if step.step_type == "wash":
                ch.status = ChannelStatus.washing
                for slide in slides:
                    slide.current_step = step.name
                    slide.progress = percent
                store.log(f"通道{channel_id} 清洗：{step.name} {step.duration_s}s")
                await gateway.run_wash_pump(channel_id, step.duration_s)
                if step.mix_after:
                    ch.status = ChannelStatus.mixing
                    ch.current_step = f"{step.name}后混匀"
                    await gateway.run_mixer(channel_id, duration_s=min(max(step.duration_s, 3), 10))
                store.save()
                await self._wait_scaled(step.duration_s)
                continue

            if step.step_type == "mix":
                ch.status = ChannelStatus.mixing
                store.log(f"通道{channel_id} 混匀：{step.duration_s}s")
                await gateway.run_mixer(channel_id, step.duration_s)
                store.save()
                await self._wait_scaled(step.duration_s)
                continue

            if step.step_type == "incubate":
                ch.status = ChannelStatus.incubating
                for slide in slides:
                    slide.current_step = step.name
                    slide.progress = percent
                store.log(f"通道{channel_id} 孵育：{step.name} {step.duration_s}s")
                store.save()
                await self._wait_scaled(step.duration_s)
                continue

            if step.step_type == "dab_prepare":
                ch.status = ChannelStatus.waiting
                calc = engine.dab_for_slides(slides)
                ch.current_step = f"DAB配制 {calc.total_ml}mL"
                store.log(f"DAB配制：{calc.total_ml}mL，A {calc.dab_a_ml}mL / B {calc.dab_b_ml}mL / 水 {calc.pure_water_ml}mL")
                store.save()
                await self._wait_scaled(step.duration_s)
                continue

            if step.step_type == "dispense":
                ch.status = ChannelStatus.dispensing
                for slide in slides:
                    reagent = slide.antibody_code if step.reagent_code == "PRIMARY" else step.reagent_code
                    volume = slide.primary_volume_ul if step.reagent_code == "PRIMARY" else (step.volume_ul or step.max_volume_ul or 0)
                    target_temp = slide.temperature_c if step.per_slide_temperature else (step.temperature_c or 42)
                    slide.current_step = f"{step.name} / {reagent}"
                    slide.progress = percent
                    await gateway.set_slide_temperature(slide.channel, slide.slot, target_temp)
                    await gateway.aspirate(1, reagent or "UNKNOWN", volume)
                    await gateway.dispense(1, slide.channel, slide.slot, volume)
                    await gateway.wash_needle(1)
                if step.mix_after:
                    ch.status = ChannelStatus.mixing
                    ch.current_step = f"{step.name}后混匀"
                    await gateway.run_mixer(channel_id, duration_s=5)
                # dispense + incubation wait in same step
                ch.status = ChannelStatus.incubating
                ch.current_step = f"{step.name}等待 {step.duration_s}s"
                store.log(f"通道{channel_id} 加样/等待：{step.name} {step.duration_s}s")
                store.save()
                await self._wait_scaled(step.duration_s)
                continue

        ch.status = ChannelStatus.completed
        ch.progress = 100
        ch.current_step = "完成"
        for slide in slides:
            slide.status = SlideStatus.completed
            slide.progress = 100
            slide.current_step = "完成"
        store.log(f"通道{channel_id} 完成")
        store.save()


scheduler = Scheduler()
