# BATCH-11 — NLog file logging for HrotStrideApp (`editor_stride`)

**Task (STR-LOG-1):** Add persistent NLog file logging to the Stride app so the GPU
render path — which cannot be tested headlessly — has a primary debugging channel.
Mirror the canonical HROT NLog setup (`Hrot.ClusterRunner/Program.cs`).

Status: **DONE.** Solution builds clean (0 errors); Stride test projects green
(215 Core / 33 Game / 4 Animation). Not committed.

---

## 1. Where logging is configured

New file: **`Stride/HrotStrideApp.Game/StrideLogging.cs`** — a `static class StrideLogging`
with `Configure()` / `Shutdown()` (both idempotent, lock-guarded).

Wiring:
- **`Stride/HrotStrideApp.Windows/HrotStrideAppApp.cs`** (the WinExe entry point) calls
  `StrideLogging.Configure()` **before** `new StrideHrotGame().Run()`, inside a
  `try { ... } finally { StrideLogging.Shutdown(); }` so the file is flushed and closed
  on every exit path.
- **`Stride/HrotStrideApp.Game/StrideHrotGame.cs`** constructor also calls
  `StrideLogging.Configure()` defensively (idempotent) so logging works even if the game
  is constructed via a future host loop that bypasses the WinExe entry.

## 2. Log file path & layout

- **Active file:** `<AppContext.BaseDirectory>/logs/editor_stride.log`
  - For a Debug build that resolves to `Stride/Bin/Windows/Debug/logs/editor_stride.log`.
- **Rolling archives:** `editor_stride.{#}.log`, `ArchiveNumberingMode.Rolling`,
  `MaxArchiveFiles = 10`, `ArchiveAboveSize = 50 MB`, `KeepFileOpen = true`,
  `ConcurrentWrites = false` — same `FileTarget` options as ClusterRunner.
  The active file keeps a fixed name (easy to find); older runs roll into the numbered
  archives.
- **Layout:**
  `[${longdate}] [${level:uppercase=true}] [${logger:shortName=true}] ${message} ${exception:format=tostring}`
  (ClusterRunner's layout minus its `[Node-${scopeproperty:nodeId}]` field, which is
  cluster-specific and not relevant to the single-process editor app).
- **Rule:** `Trace → Fatal` to the file target (per task; ClusterRunner uses `Debug→Fatal`
  for its file but the task explicitly asked for Trace→Fatal here so even the most verbose
  diagnostics are captured). `LogManager.Configuration` is set to the built
  `LoggingConfiguration`.

Per-class loggers use the idiomatic `NLog.LogManager.GetCurrentClassLogger()`
(`StrideHrotGame` holds a `private static readonly NLog.Logger Log`).

## 3. Stride `GlobalLogger` → NLog bridge (API verified)

Verified by reflecting over `Stride.Core 4.2.1.2487` (`Stride.Core.dll` +
`Stride.Core.xml`):

- `GlobalLogger.GlobalMessageLogged` is an **`event Action<ILogMessage>`** (the message is
  delivered directly, not wrapped in `MessageLoggedEventArgs`). We subscribe a
  `static void ForwardStrideMessage(ILogMessage)`.
- `ILogMessage` properties: `Module` (string), `Type` (`LogMessageType`), `Text` (string),
  `ExceptionInfo` (`Stride.Core.Diagnostics.ExceptionInfo` — a *flattened* exception with
  `Message` / `StackTrace` / `TypeFullName`, **not** a `System.Exception`). We append the
  flattened exception text to the message so the file keeps it.
- `LogMessageType` enum = `{Debug, Verbose, Info, Warning, Error, Fatal}`. Mapping to NLog:
  Verbose/Debug→Debug, Info→Info, Warning→Warn, Error→Error, Fatal→Fatal.
- `Stride.Core.Diagnostics.Logger.MinimumLevelEnabled` is a **static** property gating which
  message types the engine emits. We lower it to `Verbose` (only if currently stricter) so
  Info+ (and asset/render/physics diagnostics) flow into the file.
- Forwarded entries log to NLog logger **`"Stride"`** as `[module] text`.

Name-clash note: `Stride.Core.Diagnostics.Logger` and `NLog.Logger` collide, so the NLog
logger field and `Logger.MinimumLevelEnabled` are fully qualified in `StrideLogging.cs`.

## 4. Diagnostic dump + unhandled-exception capture

- **Per-second diagnostics dump** (`StrideHrotGame.LogSpawnDiagnostics`, throttled to every
  60 frames) was converted from `Console.Out.WriteLine("[StrideHrotGame][diag] ...")` to a
  single concise **`Log.Info`** line (logger `StrideHrotGame`): FDP entity count, visual
  count, and each visual's name + Stride world position on one line. The two
  `BindCameraToCompositorSlot` "render black" warnings were also moved to `Log.Warn`.
- **Unhandled exceptions:** `Configure()` subscribes
  `AppDomain.CurrentDomain.UnhandledException` → `Log.Fatal(ex, ...)` (logs the full
  `System.Exception` including stack trace, plus the `IsTerminating` flag) and
  `TaskScheduler.UnobservedTaskException` → `Log.Fatal(ex, ...)` then `SetObserved()`.
  Both handlers call `LogManager.Flush()` immediately afterwards so a crashing GPU run still
  leaves a stack trace on disk. `Shutdown()` detaches both handlers and the GlobalLogger
  bridge, then `Flush()` + `LogManager.Shutdown()`.

## 5. WinExe-safe

Nothing in the logging path depends on a console window — the `FileTarget` works under
`WinExe` or `Exe`. Per the task, the project `OutputType` was **left as found**
(`HrotStrideApp.Windows.csproj` is currently `Exe`; not changed).

## 6. Package reference added

`HrotStrideApp.Game.csproj` now has a **direct** `<PackageReference Include="NLog"
Version="5.2.8" />` (matches every other NLog ref in the solution; NLog was already pulled
transitively via `Fdp.Core`, but a direct ref is cleaner since `StrideLogging.cs` uses the
NLog API directly). `HrotStrideApp.Windows` needs no ref — it only calls the static facade.

## 7. Verified / not verified

- **Verified:** full solution builds clean (`dotnet build Stride/HrotStrideApp.sln -c Debug`
  = 0 errors); Stride tests green (215 / 33 / 4); the Stride API surface
  (`GlobalMessageLogged` delegate type, `ILogMessage`/`ExceptionInfo` shape,
  `LogMessageType` enum, `Logger.MinimumLevelEnabled` being static) reflected directly out
  of `Stride.Core 4.2.1.2487`.
- **Could NOT verify (no GPU):** the actual `editor_stride.log` file is not produced in this
  environment because the GPU render path can't run headlessly. The NLog config and the
  Stride bridge are written to the verified APIs; the expected file path is
  `<BaseDirectory>/logs/editor_stride.log` with the layout in §2. First lines on a real run
  will include the startup `"NLog file logging initialized. Log file: ..."` Info entry, then
  per-second `[diag]` lines and any `[Stride] [...]` engine messages.

## Suggested commit message

```
feat(stride): NLog file logging for HrotStrideApp (BATCH-11, STR-LOG-1)

Add persistent NLog file logging to the editor_stride process — the GPU
render path can't be tested headlessly, so the log file is the primary
debugging channel (mirrors Hrot.ClusterRunner's NLog setup).

- StrideLogging.Configure()/Shutdown(): rolling FileTarget at
  <BaseDirectory>/logs/editor_stride.log, Trace->Fatal.
- Bridge Stride GlobalLogger.GlobalMessageLogged (Action<ILogMessage>) ->
  NLog at mapped levels; lower Logger.MinimumLevelEnabled to Verbose.
- Capture AppDomain.UnhandledException + TaskScheduler.UnobservedTaskException
  as Fatal and flush, so GPU-path crashes leave a stack trace.
- Route StrideHrotGame per-second diag dump + compositor warnings through NLog.
- Wire Configure/Shutdown into the WinExe entry point; flush on exit.
- Add direct NLog 5.2.8 PackageReference to HrotStrideApp.Game.

Solution builds clean; Stride tests green (215/33/4). OutputType left as-is.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```
