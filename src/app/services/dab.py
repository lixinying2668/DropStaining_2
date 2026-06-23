from app.models import DABCalculation


def calculate_dab(slide_count: int, per_slide_ml: float = 0.2, extra_ml: float = 0.4) -> DABCalculation:
    """Calculate DAB preparation volume.

    Formula:
        Total = slide_count * 0.2 mL + 0.4 mL
        A:B:Water = 1:1:18
    """
    if slide_count < 0:
        raise ValueError("slide_count must be non-negative")
    total_ml = slide_count * per_slide_ml + extra_ml
    return DABCalculation(
        slide_count=slide_count,
        total_ml=round(total_ml, 3),
        dab_a_ml=round(total_ml / 20, 3),
        dab_b_ml=round(total_ml / 20, 3),
        pure_water_ml=round(total_ml * 18 / 20, 3),
    )
