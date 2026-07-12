# Windows Validation Prompt (paste into a Claude Code session on the Windows box)

Paste the block below into Claude Code running on the Windows machine. It validates
that the Linux/Windows port branch introduces no regression on Windows. Have the
branch reachable: `git fetch origin`.

---

## Prompt (paste this)

You are validating a cross-platform (Linux+Windows) port on THIS Windows machine.
All the porting work was done and verified on Linux; your job is to confirm it does
not regress Windows. Work read-only except for updating the status doc. Do NOT open
a pull request.

Branch under test: `claude/linux-windows-port`. Read `docs/porting/PORT_STATUS.md`
and `docs/porting/LINUX_WINDOWS_PORT_SPEC.md` first - PORT_STATUS lists every work
item (WI-1..WI-12) with a `NEEDS-WIN-VALIDATION` marker on the ones you must confirm.

### Method: differential (baseline vs branch)
This repo has many PRE-EXISTING failing/red tests unrelated to the port, so an
absolute failure count is meaningless. Compare instead:
1. Check out `origin/main`, build, and run the target test projects. Record pass/fail
   per test (a .trx or console log). This is the BASELINE.
2. Check out `claude/linux-windows-port`, do the same.
3. A REGRESSION = a test/project that BUILT or PASSED on main but FAILS on the branch.
   Anything already red on main is out of scope - do not chase it.

Use your normal Windows toolchain (Visual Studio / `dotnet` CLI). The engine targets
net8.0; use the same SDK you normally build with.

### Build checks (both Debug and Release)
- `dotnet build IOS-IG-SimHost.sln -c Debug` and `-c Release` must succeed with no NEW
  errors/warnings vs main. Release matters specifically for WI-1 (see below).
- `dotnet build Stride/HrotStrideApp.sln` must still build on Windows exactly as before
  (the port excludes Stride from the LINUX build only; Windows is untouched). Confirm
  no regression here.

### Per-work-item Windows checkpoints
- WI-1 (NativeMemoryAllocator): the Windows backend is a VERBATIM relocation of the
  original VirtualAlloc/VirtualFree code into `Fdp.Core/Memory/WindowsVirtualMemoryBackend.cs`.
  Run `Fdp.Core.Tests` filtered to `NativeMemoryAllocator` - all 14 must pass on Windows.
  IMPORTANT: also build `Fdp.Core` in RELEASE. The fix deliberately preserves the
  original behavior where `Decommit`/`Free` only throw under `FDP_PARANOID_MODE`
  (Debug-only); confirm Release compiles clean (TreatWarningsAsErrors) and the ECS
  smoke path works (run the broader `Fdp.Core.Tests`, compare to main).
- WI-2 (CycloneDDS.NET 0.3.2): on Windows this uses the win-x64 `ddsc.dll` native.
  Run the DDS tests: `Fdp.Toolkits.Tests` (DdsCommandClientTests) and
  `Hrot.Network.NED.Tests` - confirm they pass (they pass on Linux). This proves the
  0.2.3->0.3.2 bump is fine on Windows too.
- WI-3 (file dialogs): on Windows, `FileDialogServiceFactory.Create()` MUST return the
  comdlg32-backed `WinFormsFileDialogService` (verify by reading the factory + a quick
  manual smoke: open/save/multi-select dialogs in the ReplayBrowser/Editor still show
  the native Windows dialog). The added `WindowManager.SetFileDialogService(...)` calls
  are a no-op on Windows. Run `Hrot.ReplayBrowser.Tests` and `Fdp.Presentation.Tests`;
  compare to main (Fdp.Presentation.Tests has pre-existing reds - only flag NEW ones).
- WI-4 (staging root): BEHAVIOR CHANGE TO WATCH - the default staging root moved from
  the hardcoded `C:\FDP_Temp` to `Environment.GetEnvironmentVariable("FDP_STAGING_ROOT")
  ?? Path.Combine(Path.GetTempPath(), "FDP_Temp")`. On Windows with no env var that is
  now `%TEMP%\FDP_Temp` instead of `C:\FDP_Temp`. Callers/config that pass an explicit
  root are unchanged. Confirm: (a) the orchestrator/SimHost/CGF/IG tests pass vs main,
  and (b) if any Windows deployment or launcher RELIES on the literal `C:\FDP_Temp`
  default, set `FDP_STAGING_ROOT=C:\FDP_Temp` to preserve it, and note that.
- WI-5 (case-insensitive discovery + path equality): on Windows this is behavior-neutral
  (NTFS is already case-insensitive; `PlatformPathComparison` returns `OrdinalIgnoreCase`
  on Windows = the old behavior). Just confirm the Blueprint/HSM/BTree/scenario/AiEditor
  tests are green vs main.
- WI-6: `NodeEditor.Demo` font fallback (Windows path still first) and
  `ClusterDiagnosticsPanel` open-file now wrapped in try/catch - both build; no Windows
  behavior change expected.
- WI-7: `Fdp.Examples.CarKinem.csproj` lost its `win-x64` RID pin - confirm it still
  builds/publishes on Windows (default and, if you publish, `-r win-x64`).
- WI-10: one `string.Contains(x, StringComparison)` -> `IndexOf(x, StringComparison) >= 0`
  in the Blueprints compiler - identical behavior; just confirm the compiler + its
  consumers (CGF/IG/Editor) build.
- WI-11: `DebugGizmoLayerCaptureTests` now creates a headless ImGui context. On Windows
  the original crash never occurred (native asserts are off), so this is harmless -
  confirm those tests still pass.
- WI-12: 5 DDS/ECS test files had ref-struct-in-async blocks extracted into sync local
  functions - behavior-preserving. Confirm the affected test classes build and pass vs
  main (`Fdp.Toolkits.Tests`, `Hrot.Network.NED.Tests`; `Hrot.SimHost.Integration.Tests`
  EpisodeInjectionTests has 3 PRE-EXISTING runtime fails at an EntityCount assert - check
  they fail the SAME way on main, i.e. not a new regression).
- WI-8 (.sh scripts) and WI-9 (Stride exclusion) need no Windows action beyond the Stride
  build check above.

### Reporting
When done, edit `docs/porting/PORT_STATUS.md`: fill in the `Build Win` / `Test Win`
columns for each `NEEDS-WIN-VALIDATION` item, flip them to `validated` if clean, and add
a coordination-log entry summarizing the Windows results (SDK version, baseline vs branch
diff, any real regressions, and the WI-4 default-path note). Commit that doc update to
`claude/linux-windows-port` and push (`git push -u origin claude/linux-windows-port`,
retry on transient network errors). If you find a REAL regression (green on main, red on
branch), stop and report it with the failing test + the diff of the relevant WI change
rather than trying to fix it blind. Do NOT open a pull request.

Start by reading PORT_STATUS.md, then run the baseline (origin/main), then the branch.
```
```
