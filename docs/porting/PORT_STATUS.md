# Linux / Windows Port - Status Tracker

Shared source of truth between the Windows box and the Linux VM. Update this on
every push. See `LINUX_WINDOWS_PORT_SPEC.md` for the work-item definitions and
`KICKOFF_PROMPT.md` for how to run the two sessions.

## Legend
- State: `todo` | `in-progress` | `code-done` | `validated` | `blocked` | `deferred`
- `code-done` = change committed and builds in the author's lane.
- `validated` = meets acceptance criteria on the OS(es) required by the
  verification matrix (spec section 7).
- Build/test cells: `pass` | `fail` | `n/a` | `-` (not yet run).

## Baselines (fill in once, before making code changes)

| Environment | SDK / RID | Baseline build | Baseline tests | Date | Notes |
|---|---|---|---|---|---|
| Windows box | net8.0 / win-x64 | - | - | - | build FDP.sln + master sln + Stride sln |
| Linux (this box) | 8.0.128 / linux-x64 | Fdp.Core: pass | allocator: 13/14 FAIL | 2026-07-12 | SDK 8.0.128 via apt. `Fdp.Core` builds clean. Allocator tests fail with `DllNotFoundException: kernel32.dll` at `NativeMemoryAllocator.Reserve` line 48 = WI-1 confirmed (13 fail, 1 arg-guard passes). Restore required creating the empty `nugets/` feed (see below). Full-solution baseline not yet run. |

## Work items

| WI | Title | Owner OS | State | Build Win | Build Linux | Test Win | Test Linux | Notes |
|---|---|---|---|---|---|---|---|---|
| WI-1 | Cross-platform NativeMemoryAllocator (Win/POSIX backends) | Linux | code-done | - | pass | NEEDS-WIN-VALIDATION | 14/14 pass | Backend split done + reviewed. Facade unchanged; Windows backend = verbatim relocation; POSIX backend = mmap/mprotect/madvise/munmap with 64KB-aligned trim. Decommit/Free throw only under FDP_PARANOID_MODE (original Release semantics preserved). Debug+Release build 0 warnings. Event-stream NativeMemory simplification deferred. |
| WI-2 | Bump CycloneDDS.NET -> 0.3.2 | Linux | code-done | pass | pass | - | - | References bumped (`3113b4b`). Restore resolves 0.3.2 from nuget.org. API-compat VERIFIED: reflection probe shows DdsLoan<T>/DdsSample<T> are ref structs in BOTH 0.2.3 and 0.3.2, so the bump introduces zero new compile errors; all DDS production code builds on Linux. Remaining: live DDS loopback smoke test only. |
| WI-3 | File-dialog factory + wire ImGui fallback + multi-select | split | code-done | - | pass | NEEDS-WIN-VALIDATION | pass (ReplayBrowser) | Done + reviewed. `FileDialogServiceFactory.Create()` (Win32 on Windows, ImGui else) at 4 call sites; `SetFileDialogService` wired in each subsystem's RegisterWindows (harmless no-op on Windows). ImGui multi-select implemented (checkboxes + full-path HashSet + separate TCS). 5 projects build 0 warnings; ReplayBrowser.Tests 27/27. ImGui modal itself needs manual runtime check on a Linux desktop (can't test headless). Fdp.Presentation.Tests crash is pre-existing = WI-11. |
| WI-12 | 3 test projects fail to compile: ref structs in async methods | either | todo | FAIL | FAIL | - | - | PRE-EXISTING at origin/main, NOT the port (platform-independent C# rules; fail on Windows too). Fdp.Toolkits.Tests (DdsCommandClientTests) + Hrot.Network.NED.Tests: DdsLoan/DdsSample ref structs in async. Hrot.SimHost.Integration.Tests (EpisodeInjectionTests): EntityQuery ref-struct enumerator + ref local in async. Fix = extract the ref-struct loops into non-async local functions / make the methods sync. User's test-health domain; blocks a fully-green master build on ALL platforms. |
| WI-11 | Fdp.Presentation.Tests crash on headless Linux (ImGui no-context) | Linux | code-done | - | n/a | NEEDS-WIN-VALIDATION | completes (382 pass / 34 pre-existing fail) | Done + reviewed. Real cause was `DebugGizmoLayerCaptureTests` (a non-ImGui class whose Update() reads ImGui.GetIO() with no context) - added `[Collection("ImGui Sequential")]` + `using var fixture = new ImGuiTestFixture()` (12-line test-only diff, no product code). Suite now runs to completion. ALSO REQUIRES xvfb: `FdpApplicationTests` calls Raylib.InitWindow which needs an X display - run headless Linux Presentation tests under `xvfb-run -a`. The 34 remaining FAILs are pre-existing (ctx.Resources NRE in DebugPrimitiveRenderer2D tests, hardcoded input stubs) - separate triage, not port regressions. |
| WI-4 | Centralize `C:\FDP_Temp` staging root (FDP_STAGING_ROOT / temp) | Linux | code-done | - | pass* | NEEDS-WIN-VALIDATION | pass* | Done + reviewed. `OrchestrationConstants.ResolveStagingRoot()` (FDP_STAGING_ROOT env or temp); ~24 sites updated (removing the const forced all refs). Default-param sites -> `= null` + `?? ResolveStagingRoot()`. Fdp.Toolkits/Orchestrator/SimHost/ExCon build clean. *CGF/IG/Editor edits verified via temp-patch only - their Linux build is blocked by WI-10 (netstandard2.0), not by WI-4. |
| WI-10 | Hrot.Blueprints.Compiler netstandard2.0 API gap | Linux | code-done | - | pass | NEEDS-WIN-VALIDATION | - | FIXED. Was a single occurrence: `Stage5_Schedule.cs:1641` `string.Contains(string, StringComparison)` (not in netstandard2.0). Replaced with `IndexOf(..., StringComparison) >= 0` (identical semantics). Compiler builds both TFMs; CGF/IG/Editor now build clean on Linux, which also confirms WI-4's edits in those projects. |
| WI-5 | Case-insensitive asset discovery (EnumerationOptions) + path-equality fixes | Linux | code-done | - | pass | NEEDS-WIN-VALIDATION | pass | Done + reviewed. 12 enumeration sites -> MatchCasing.CaseInsensitive (recursion preserved per-site); 2 netstandard2.0 loaders use "*"+case-insensitive EndsWith fallback; 2 path-equality sites -> platform-aware PlatformPathComparison (OrdinalIgnoreCase on Windows, Ordinal else). New regression test (Widget.HSM.JSON found via *.hsm.json) fails pre-fix, passes post-fix. Hsm.Editor.Tests 504/504, BTree.Editor.Tests 575/575. Deliberate exclusions (non-asset globs) documented. |
| WI-6 | Portable one-offs (SpecialFolder.Fonts, UseShellExecute open-file) | Linux | code-done | - | pass | NEEDS-WIN-VALIDATION | n/a | Done + reviewed. NodeEditor.Demo font list gains Linux DejaVu/msttcorefonts paths (SpecialFolder.Fonts is empty on Linux; graceful fallback preserved). MessageLogPanel Process.Start left as-is (already UseShellExecute+try/catch = portable). ClusterDiagnosticsPanel Process.Start wrapped in try/catch (was unguarded -> would throw unhandled on Linux w/o xdg-open); surfaces via existing _inlineError. |
| WI-7 | Relax CarKinem win-x64 RID | either | code-done | pass | pass | n/a | n/a | Done + reviewed. Removed `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` + `<PlatformTarget>x64</PlatformTarget>`. Builds default and `-r linux-x64`. |
| WI-8 | Linux launch scripts (.sh mirrors of run_*.bat) | Linux | code-done | n/a | n/a | n/a | bash -n pass | Done + reviewed. 6 scripts: run_SimHost/IG/IOS/Editor/all_together/all_standalone.sh. `start`->`nohup &`/backgrounded subshells; SIGINT trap+cleanup for multi-role launchers; SCRIPT_DIR-relative paths; -d/-m flags preserved; .bat untouched. No robocopy in these launchers. Not runtime-tested (needs built binary/desktop); bash -n clean. Dirigent not ported (out of scope). |
| WI-9 | Exclude Stride from Linux build | Windows | validated | pass | n/a | pass | n/a | DECIDED. Master `IOS-IG-SimHost.sln` = 119 projects, 0 windows-locked, real Stride apps absent. Linux builds master sln, never `Stride/HrotStrideApp.sln`. No project edits. |

## Coordination log (newest first)

- 2026-07-12 - Master-solution Linux build sweep (Opus). Retargeted the lone
  net9.0 project (Fdp.Examples.CarKinem.Tests) to net8.0 so the whole master
  solution uses the net8 SDK. Full `dotnet build IOS-IG-SimHost.sln` on Linux:
  ALL production code and nearly all test projects build. The only compile
  failures are 3 test projects, all the same root cause (ref structs used in
  async methods) and all PRE-EXISTING at origin/main (not the port, not the DDS
  bump - proven): Fdp.Toolkits.Tests (DdsCommandClientTests) and
  Hrot.Network.NED.Tests use DdsLoan/DdsSample (ref structs in 0.2.3 AND 0.3.2)
  in async; Hrot.SimHost.Integration.Tests/EpisodeInjectionTests uses
  EntityQuery's ref-struct enumerator + a ref local in async. These are
  platform-independent C# errors (fail on Windows too) = user's parked test debt,
  filed as WI-12. Net: the port introduces no build regression; production is
  Linux-clean.
- 2026-07-12 - WI-11 fixed (Sonnet) + reviewed (Opus). Bisected the host crash to
  DebugGizmoLayerCaptureTests (not the ImGui/ folder, which was already
  serialized); gave it a headless ImGui context. Independently confirmed the
  suite now completes under xvfb-run: 382 pass / 34 pre-existing fail, no abort.
  KEY OPS NOTE: headless Linux runs of Presentation/Raylib tests must use
  `xvfb-run -a` (FdpApplicationTests calls Raylib.InitWindow -> needs an X display).
- 2026-07-12 - WI-6 + WI-7 implemented (Sonnet) + reviewed (Opus). CarKinem RID
  pin removed (builds default + linux-x64); NodeEditor.Demo font fallback gains
  Linux paths; ClusterDiagnosticsPanel Process.Start guarded with try/catch
  (genuine unguarded-throw defect); MessageLogPanel left as-is (already portable).
  This completes the code work items; remaining are validation (Windows box) and
  the two pre-existing test-harness items (WI-11, and the DDS restore/API check
  in WI-2).
- 2026-07-12 - WI-3 implemented (Sonnet) + reviewed (Opus). File-dialog factory,
  4 call sites, SetFileDialogService wiring per subsystem, ImGui multi-select.
  5 projects build clean; ReplayBrowser.Tests green. Review found the
  Fdp.Presentation.Tests suite crashes on Linux (native ImGui "no current
  context" assertion) - confirmed PRE-EXISTING by stashing WI-3 and reproducing
  on baseline; filed as WI-11. WI-8 (Linux launch scripts) also landed.
- 2026-07-12 - WI-5 implemented (Sonnet) + reviewed (Opus). Case-insensitive
  asset/scenario/blueprint discovery (12 sites, recursion preserved per-site;
  netstandard2.0 loaders use a "*"+EndsWith fallback) plus platform-aware path
  equality (2 sites). New mixed-case regression test verified fail-before /
  pass-after. Touched projects build 0 warnings; Hsm/BTree editor test suites
  green. Pre-existing Hrot.Blueprints.Tests failures (8, Roslyn/PDB golden +
  alloc-threshold) confirmed unrelated via stash.
- 2026-07-12 - WI-10 fixed (Opus, one-liner): `string.Contains(string,
  StringComparison)` -> `IndexOf(...) >= 0` in Stage5_Schedule.cs for the
  netstandard2.0 target. Unblocks CGF/IG/Editor on Linux (all now build clean),
  which retroactively confirms WI-4's edits in those three projects.
- 2026-07-12 - WI-4 implemented (Sonnet) + reviewed (Opus). Removed the
  `C:\FDP_Temp` const default; added `OrchestrationConstants.ResolveStagingRoot()`.
  ~24 sites updated. Fdp.Toolkits/Hrot.Orchestrator/Hrot.SimHost/Hrot.ExCon build
  0 warnings on Linux. Surfaced WI-10: a pre-existing netstandard2.0 API gap in
  Hrot.Blueprints.Compiler that blocks CGF/IG/Editor on Linux (not caused by WI-4;
  reproduces on unmodified branch). Configured-root behavior unchanged; the
  pre-existing 2/5 ReferenceArchiveHandlerTests failures (explicit `C:\FDP_Temp`
  literals in the tests) are out of WI-4 scope.
- 2026-07-12 - WI-1 implemented (Sonnet) + reviewed (Opus). `NativeMemoryAllocator`
  now delegates to a runtime-selected backend; all 14 allocator tests pass on
  Linux; Debug+Release build with 0 warnings. Full `Fdp.Core.Tests` = 1144 pass /
  6 fail / 9 skip; the 6 failures are pre-existing and unrelated to WI-1:
  3x Serialization.Migrations InMemoryMigrationStorage hash/corruption tests,
  1x AsyncRecorderTests background-worker timing, 1x EntityLifecycle (flaky under
  full-suite ordering - passes in isolation), 1x EntityIndexSync
  Performance_100K_Entities (hardcoded <10ms threshold, VM measured ~15ms).
  NEEDS-WIN-VALIDATION: Windows box must build + run allocator tests to confirm
  the verbatim Windows backend is unchanged.
- 2026-07-12 - Linux toolchain established (SDK 8.0.128 via apt). Added
  `nugets/.gitkeep` so the empty LocalFeed exists on fresh checkouts (restore
  hard-fails NU1301 otherwise). `Fdp.Core` builds clean on Linux; allocator
  tests reproduce WI-1 (13/14 fail, kernel32.dll DllNotFoundException). Note:
  api.nuget.org is reachable through the egress proxy; dotnet download hosts
  (builds.dotnet.microsoft.com) are NOT - install the SDK via apt, not the MS
  install script.
- 2026-07-12 - branch rebased on main; DDS bumped to 0.3.2 (`3113b4b`); Stride
  exclusion finalized (`c61fcb5`); this tracker stubbed.

## Notes / decisions
- Single build artifact, runtime OS detection (`OperatingSystem.IsWindows()`),
  no `#if WINDOWS` forks. (spec section 2)
- Use Sonnet subagents for mechanical implementation; Opus for orchestration and
  review of every diff.
- `nuget.config` lists a local `./nugets` feed before nuget.org. The feed dir is
  now kept via `nugets/.gitkeep` so restore no longer hard-fails on a fresh
  checkout. It is empty, so it does not shadow CycloneDDS.NET 0.3.2 (restore
  falls through to nuget.org).
