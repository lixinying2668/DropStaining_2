from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import json

from app.core.config import DATA_DIR
from app.models import Protocol, ProtocolStep, Slide
from app.services.dab import calculate_dab


@dataclass
class Task:
    task_id: str
    channel: int
    slot: int | None
    slide_id: str | None
    action: str
    step_name: str
    duration_s: int
    payload: dict


class ProtocolEngine:
    def __init__(self, protocol_dir: Path | None = None):
        self.protocol_dir = protocol_dir or DATA_DIR / "protocols"

    def list_protocols(self) -> list[Protocol]:
        protocols: list[Protocol] = []
        for path in sorted(self.protocol_dir.glob("*.json")):
            data = json.loads(path.read_text(encoding="utf-8"))
            protocols.append(Protocol(**data))
        return protocols

    def get_protocol(self, code: str) -> Protocol:
        for protocol in self.list_protocols():
            if protocol.code == code:
                return protocol
        raise KeyError(f"protocol not found: {code}")

    def step_volume_for_slide(self, step: ProtocolStep, slide: Slide) -> float | None:
        if step.reagent_code == "PRIMARY":
            return slide.primary_volume_ul
        if step.volume_ul is not None:
            return step.volume_ul
        if step.min_volume_ul is not None and step.max_volume_ul is not None:
            return (step.min_volume_ul + step.max_volume_ul) / 2
        return None

    def step_reagent_for_slide(self, step: ProtocolStep, slide: Slide) -> str | None:
        if step.reagent_code == "PRIMARY":
            return slide.antibody_code
        return step.reagent_code

    def build_tasks_for_slide(self, slide: Slide) -> list[Task]:
        protocol = self.get_protocol(slide.protocol_code)
        tasks: list[Task] = []
        for step in protocol.steps:
            if not step.machine_execute:
                continue
            volume = self.step_volume_for_slide(step, slide)
            reagent = self.step_reagent_for_slide(step, slide)
            temperature = slide.temperature_c if step.per_slide_temperature else step.temperature_c
            tasks.append(
                Task(
                    task_id=f"{slide.id}-{step.id}",
                    channel=slide.channel,
                    slot=slide.slot,
                    slide_id=slide.id,
                    action=step.step_type,
                    step_name=step.name,
                    duration_s=step.duration_s,
                    payload={
                        "reagent_code": reagent,
                        "volume_ul": volume,
                        "temperature_c": temperature,
                        "mix_after": step.mix_after,
                        "channel_level": step.channel_level,
                    },
                )
            )
        return tasks

    def dab_for_slides(self, slides: list[Slide]):
        ihc_count = sum(1 for s in slides if s.protocol_code == "IHC")
        return calculate_dab(ihc_count)


engine = ProtocolEngine()
