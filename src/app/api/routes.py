from __future__ import annotations

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import HTMLResponse, RedirectResponse

from app.models import AddSlideRequest, EngineerCommand, LoginRequest, SlideUpdate
from app.services.dab import calculate_dab
from app.services.device_gateway import gateway
from app.services.protocol_engine import engine
from app.services.scheduler import scheduler
from app.services.store import store

router = APIRouter()


def templates(request: Request):
    return request.app.state.templates


def page_ctx(request: Request, **extra):
    return {"request": request, "state": store.state, "user": store.state.active_user, "slide_count": len(store.all_slides()), **extra}


@router.get("/", response_class=HTMLResponse)
async def login_page(request: Request):
    return templates(request).TemplateResponse("login.html", page_ctx(request))


@router.get("/dashboard", response_class=HTMLResponse)
async def dashboard(request: Request):
    return templates(request).TemplateResponse("dashboard.html", page_ctx(request))


@router.get("/samples", response_class=HTMLResponse)
async def samples_page(request: Request):
    return templates(request).TemplateResponse("samples.html", page_ctx(request))


@router.get("/reagents", response_class=HTMLResponse)
async def reagents_page(request: Request):
    return templates(request).TemplateResponse("reagents.html", page_ctx(request))


@router.get("/configure", response_class=HTMLResponse)
async def configure_page(request: Request):
    return templates(request).TemplateResponse(
        "configure.html",
        page_ctx(request, protocols=engine.list_protocols(), dab=engine.dab_for_slides(store.all_slides())),
    )


@router.get("/run", response_class=HTMLResponse)
async def run_page(request: Request):
    return templates(request).TemplateResponse("run.html", page_ctx(request))


@router.get("/engineer", response_class=HTMLResponse)
async def engineer_page(request: Request):
    return templates(request).TemplateResponse("engineer.html", page_ctx(request))


@router.get("/admin", response_class=HTMLResponse)
async def admin_page(request: Request):
    return templates(request).TemplateResponse("admin.html", page_ctx(request, users=store.get_users()))


@router.post("/api/login")
async def login(payload: LoginRequest):
    user = store.authenticate(payload.username, payload.password, payload.role)
    if user is None:
        raise HTTPException(401, "用户名、密码或角色不正确")
    return {"ok": True, "user": user, "redirect": "/dashboard"}


@router.post("/api/logout")
async def logout():
    store.state.active_user = None
    store.log("用户退出")
    store.save()
    return {"ok": True}


@router.get("/api/state")
async def api_state():
    return store.state


@router.post("/api/system/initialize")
async def initialize():
    await gateway.initialize()
    return store.initialize()


@router.post("/api/system/reset")
async def reset_runtime():
    return store.reset_runtime()


@router.post("/api/samples/scan")
async def scan_samples(count: int = 8):
    return store.mock_scan_samples(count)


@router.post("/api/reagents/scan")
async def scan_reagents():
    return store.mock_scan_reagents()


@router.get("/api/protocols")
async def list_protocols():
    return engine.list_protocols()


@router.post("/api/slides/configure")
async def configure_slide(payload: SlideUpdate):
    try:
        return store.update_slide_config(
            slide_id=payload.slide_id,
            protocol_code=payload.protocol_code,
            antibody_code=payload.antibody_code,
            primary_volume_ul=payload.primary_volume_ul,
            temperature_c=payload.temperature_c,
        )
    except KeyError as exc:
        raise HTTPException(404, str(exc))


@router.get("/api/dab")
async def dab(slide_count: int | None = None):
    count = slide_count if slide_count is not None else len([s for s in store.all_slides() if s.protocol_code == "IHC"])
    return calculate_dab(count)


@router.post("/api/run/start")
async def start_run():
    try:
        await scheduler.start()
        return store.state
    except RuntimeError as exc:
        raise HTTPException(400, str(exc))


@router.post("/api/run/pause")
async def pause_run():
    try:
        await scheduler.pause()
        return store.state
    except RuntimeError as exc:
        raise HTTPException(400, str(exc))


@router.post("/api/run/resume")
async def resume_run():
    try:
        await scheduler.resume()
        return store.state
    except RuntimeError as exc:
        raise HTTPException(400, str(exc))


@router.post("/api/run/stop")
async def stop_run():
    await scheduler.stop()
    return store.state


@router.post("/api/run/add-slide")
async def add_slide(payload: AddSlideRequest):
    try:
        return store.add_slide(
            channel=payload.channel,
            slot=payload.slot,
            barcode=payload.barcode,
            protocol_code=payload.protocol_code,
            antibody_code=payload.antibody_code,
            temperature_c=payload.temperature_c,
        )
    except ValueError as exc:
        raise HTTPException(400, str(exc))


@router.post("/api/engineer/command")
async def engineer_command(payload: EngineerCommand):
    result = await gateway.test_command(
        payload.module,
        payload.action,
        channel=payload.channel,
        position=payload.position,
        volume_ul=payload.volume_ul,
        speed=payload.speed,
        duration_s=payload.duration_s,
        temperature_c=payload.temperature_c,
        **payload.payload,
    )
    store.log(result.message)
    store.save()
    return result


@router.get("/api/logs")
async def logs():
    return {"logs": store.state.logs, "alarms": store.state.alarms}
