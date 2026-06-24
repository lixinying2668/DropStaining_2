# ASP.NET Core Web Host Startup

This project now uses `backend/Stainer.Web` as the local ASP.NET Core web host for the kiosk UI.

## Start

From the repository root:

```powershell
dotnet run --project backend\Stainer.Web\Stainer.Web.csproj
```

The default local URL is:

```text
http://127.0.0.1:5205
```

Open the browser in full-screen kiosk mode and navigate to:

```text
http://127.0.0.1:5205/
```

## Stop

In the terminal running the service, press:

```text
Ctrl+C
```

If the process was started in the background, stop the matching `dotnet` process from Task Manager or with PowerShell after confirming the process id:

```powershell
Get-Process dotnet
Stop-Process -Id <pid>
```

## Boundaries

- The C# ASP.NET Core service is the formal local web host.
- The old FastAPI/Jinja source under `src/app` is retained only as prototype reference.
- Jinja is not used by the ASP.NET Core host.
- The current hosted pages use Mock API state and static placeholder data where database-backed application services are not wired yet.
- The default binding is local-only (`127.0.0.1`), not LAN-facing.
