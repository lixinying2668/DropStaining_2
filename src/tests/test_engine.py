from app.models import Slide
from app.services.protocol_engine import engine


def test_build_ihc_tasks_has_primary_antibody():
    slide = Slide(id='S01', channel=1, slot=1, barcode='X', antibody_code='AB-CK', primary_volume_ul=80)
    tasks = engine.build_tasks_for_slide(slide)
    primary = [t for t in tasks if t.step_name == '加一抗试剂'][0]
    assert primary.payload['reagent_code'] == 'AB-CK'
    assert primary.payload['volume_ul'] == 80
