# Codex Usage Widget

A small, local-only Windows desktop widget for Codex subscription usage. It starts
`codex app-server`, reads the official `account/rateLimits/read` response, and
listens for `account/rateLimits/updated` notifications.

## What the prototype shows

- Remaining percentage for the active Codex rate-limit window
- Main Codex usage window, including used percentage and reset time
- Spark-specific buckets are intentionally excluded
- Automatic refresh every two minutes and live refresh notifications
- Compact always-on-top widget that can be dragged from any non-button surface
- Persistent display choice between a desktop widget and a taskbar notification-area indicator
- Dynamic taskbar icon showing the remaining percentage; click it to open the widget
- No token scraping, browser automation, or external backend

## Display modes

- **Desktop widget** keeps the compact window visible and always on top.
- **Taskbar indicator** hides the window and shows the remaining percentage in the
  notification area. Left-click the indicator to open the widget temporarily.

Use the `−` button to switch to the taskbar indicator. Right-click the taskbar
indicator and open **Display mode** to switch between modes. Windows may initially
place the icon inside the system-tray overflow; the user controls whether it stays
visible directly on the taskbar.

## Requirements

- Windows 10 or newer
- .NET 10 SDK or runtime
- Codex CLI available on `PATH`
- A local ChatGPT sign-in completed with `codex login`

## Run

```powershell
dotnet run --project .\CodexUsageWidget.csproj
```

## Build

```powershell
dotnet publish .\CodexUsageWidget.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true
```

The published executable will be under `bin\Release\net10.0-windows\win-x64\publish`.

## Notes

This is subscription-limit visibility, not OpenAI API billing. API-key usage has
different accounting and would need a separate data source and UI mode.
