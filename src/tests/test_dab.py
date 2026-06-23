from app.services.dab import calculate_dab


def test_dab_for_16_slides():
    calc = calculate_dab(16)
    assert calc.total_ml == 3.6
    assert calc.dab_a_ml == 0.18
    assert calc.dab_b_ml == 0.18
    assert calc.pure_water_ml == 3.24
