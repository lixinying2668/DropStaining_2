from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates

from app.api.routes import router
from app.core.config import APP_TITLE, STATIC_DIR, TEMPLATES_DIR

app = FastAPI(title=APP_TITLE, version="0.1.0")
app.mount("/static", StaticFiles(directory=STATIC_DIR), name="static")
app.state.templates = Jinja2Templates(directory=TEMPLATES_DIR)
app.include_router(router)


@app.get("/health")
async def health():
    return {"ok": True, "app": APP_TITLE}
