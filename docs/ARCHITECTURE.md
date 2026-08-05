# Architecture

The solution uses explicit composition in `App.xaml.cs` and keeps framework,
application, domain, and infrastructure responsibilities separate without a
dependency-injection package.

## Project layout

```text
src/CodexUsageWidget/
├── Application/             Refresh orchestration and presentation formatting
├── Domain/                  UsageSnapshot and UsageWindow domain records
├── Infrastructure/
│   ├── Codex/               CLI discovery, process creation, JSON-RPC and parsing
│   ├── Logging/             Local file diagnostics
│   ├── Settings/            Persistent display preference
│   └── Windows/             Tray icon and taskbar Win32 integration
└── Views/                   WPF windows and view-only interaction logic
tests/CodexUsageWidget.Tests/ Unit tests for parsing, formatting and persistence
```

## Runtime flow

1. `App` acquires the single-instance mutex and constructs the object graph.
2. `UsageMonitor` owns refresh scheduling, timeout handling and refresh coalescing.
3. `CodexAppServerClient` ensures an initialized app-server connection.
4. `JsonRpcConnection` owns stdin/stdout request correlation and process lifetime.
5. `CodexUsageParser` converts the server payload into domain records.
6. `MainWindow` renders domain state and delegates tray/taskbar work to dedicated
   Windows infrastructure components.

## Dependency direction

- Domain types do not depend on WPF, WinForms, process APIs, or JSON.
- Application orchestration depends on domain types and the `IUsageProvider` port.
- Infrastructure implements that port and owns OS/external-process details.
- Views consume application/domain state and do not parse protocol payloads.

## Reliability decisions

- All transport awaits use `ConfigureAwait(false)` so shutdown cannot deadlock the
  WPF UI thread.
- Failed app-server startup is disposed before a later refresh reconnects.
- A semaphore prevents concurrent refreshes and a mutex prevents duplicate apps.
- Unhandled exceptions and CLI diagnostics are recorded locally for support.
- Publish trimming is disabled because WPF is not a safe trimming boundary.

## Extending the app

- Add another usage source by implementing `IUsageProvider`.
- Add new Codex payload variants inside `CodexUsageParser` with fixture-based tests.
- Keep Win32 calls under `Infrastructure/Windows` and UI rendering under `Views`.
- Avoid placing persistence, process management, or protocol parsing in code-behind.
