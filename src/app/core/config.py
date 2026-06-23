from pathlib import Path

BASE_DIR = Path(__file__).resolve().parents[1]
DATA_DIR = BASE_DIR / "data"
TEMPLATES_DIR = BASE_DIR / "templates"
STATIC_DIR = BASE_DIR / "static"

APP_TITLE = "全自动冰冻切片染色机"
MAX_CHANNELS = 4
SLOTS_PER_CHANNEL = 4
REAGENT_COLUMNS = 5
REAGENT_ROWS = 8
