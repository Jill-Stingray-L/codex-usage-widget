# Codex Usage Widget

A local-only Windows widget that shows the remaining Codex subscription allowance.
It talks directly to the official Codex app server through
`account/rateLimits/read` and listens for live `account/rateLimits/updated`
notifications.

> This is an independent utility and is not an official OpenAI application.

## Features

- Remaining percentage and reset time for the active Codex rate-limit window
- Compact, movable, always-on-top desktop widget
- Native-looking taskbar label beside the Windows notification area
- Persistent desktop/taskbar display preference
- Automatic refresh every two minutes and live server notifications
- Single-instance protection to prevent overlapping labels
- Per-monitor DPI support, local diagnostic logs, and graceful CLI reconnects
- Spark-specific buckets intentionally excluded
- No browser automation, token scraping, telemetry, or external backend

## Requirements

- Windows 10 version 1809 or newer
- Codex CLI available on `PATH`
- A completed local sign-in (`codex login`)

The portable release includes the .NET runtime. A separate .NET installation is
therefore not required on the destination computer.

## Install on another computer

1. Download the `codex-usage-widget-win-x64.zip` build artifact or create it with
   `scripts/publish.ps1`.
2. Extract the ZIP to a permanent directory.
3. Ensure `codex --version` works in PowerShell and run `codex login` if needed.
4. Start `CodexUsageWidget.exe`.

Only one instance can run at a time. Starting the executable again exits quietly.

If Codex is installed in a non-standard location, set
`CODEX_USAGE_WIDGET_CODEX_PATH` to the full path of `codex.cmd` or `codex.exe`.

## Display modes

- **Desktop widget** keeps the compact window visible and always on top.
- **Taskbar label** shows `Codex 75%` directly to the left of the notification area.

Use the `−` button to switch to taskbar mode. Right-click the taskbar label or tray
icon to refresh, change display mode, or exit.

## Development

The repository pins the .NET SDK in `global.json`.

```powershell
dotnet restore .\CodexUsageWidget.slnx
dotnet test .\CodexUsageWidget.slnx -c Release
dotnet run --project .\src\CodexUsageWidget\CodexUsageWidget.csproj
```

Warnings are treated as errors and the recommended .NET analyzers run during every
build.

## Portable release

```powershell
.\scripts\publish.ps1 -Runtime win-x64
```

The script runs the complete test suite and creates:

```text
artifacts/release/codex-usage-widget-win-x64.zip
```

`win-arm64` is also supported through the script's `-Runtime` parameter.

## Local data

The application only writes under `%LOCALAPPDATA%\CodexUsageWidget`:

- `display-mode.txt` — the selected display mode
- `logs\codex-usage-widget-YYYYMMDD.log` — diagnostics, retained for 14 days

No credentials are read or stored by the widget. Authentication remains owned by
the locally installed Codex CLI.

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for component boundaries,
runtime flow, and extension guidance.

## Usage semantics

This displays ChatGPT/Codex subscription rate limits. It does not display OpenAI
API billing or API-key usage, which use a different accounting system.

## License

Released under the [MIT License](LICENSE). You may use, modify, fork, publish,
redistribute, sublicense, or sell copies of the software as long as the copyright
notice and license text are retained.
