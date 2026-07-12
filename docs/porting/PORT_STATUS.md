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
| Windows box | net8.0 (SDK 10.0.109) / win-x64 | pass | see log | 2026-07-12 | Baseline = origin/main `73c990e8`. All 3 solutions build (master Debug+Release, Stride Debug, 0 errors). Target-project test counts recorded in the 2026-07-12 Windows-validation coordination-log entry; this is the regression baseline. NOTE: Windows has SDK 10, so C#13 permits ref-structs-in-async - the 3 WI-12 test projects that were BUILD-red on the Linux SDK-8 box already compile here on main, so the WI-12 diff is test-level not build-level. |
| Linux (this box) | 8.0.128 / linux-x64 | Fdp.Core: pass | allocator: 13/14 FAIL | 2026-07-12 | SDK 8.0.128 via apt. `Fdp.Core` builds clean. Allocator tests fail with `DllNotFoundException: kernel32.dll` at `NativeMemoryAllocator.Reserve` line 48 = WI-1 confirmed (13 fail, 1 arg-guard passes). Restore required creating the empty `nugets/` feed (see below). Full-solution baseline not yet run. |

## Work items

| WI | Title | Owner OS | State | Build Win | Build Linux | Test Win | Test Linux | Notes |
|---|---|---|---|---|---|---|---|---|
| WI-1 | Cross-platform NativeMemoryAllocator (Win/POSIX backends) | Linux | validated | pass | pass | 14/14 pass | 14/14 pass | WIN-VALIDATED (2026-07-12): master sln Debug+Release both build 0 errors on Windows; the 14 NativeMemoryAllocator tests pass on the branch (and identically on main - Windows backend is a verbatim relocation); Release preserves the Debug-only FDP_PARANOID_MODE throw semantics. Backend split done + reviewed. Facade unchanged; Windows backend = verbatim relocation; POSIX backend = mmap/mprotect/madvise/munmap with 64KB-aligned trim. Decommit/Free throw only under FDP_PARANOID_MODE (original Release semantics preserved). Debug+Release build 0 warnings. Event-stream NativeMemory simplification deferred. |
| WI-2 | Bump CycloneDDS.NET -> 0.3.2 | Linux | validated | pass | pass | pass | pass | WIN-CONFIRMED (2026-07-12): restore resolves 0.3.2 from nuget.org (LocalFeed only pins 0.2.3, falls through); DDS tests green on Windows via the 0.3.2 win-x64 `ddsc.dll` - Fdp.Toolkits.Tests 1892/0, Hrot.Network.NED.Tests 98/0. References bumped (`3113b4b`). Restore resolves 0.3.2. API-compat verified (DdsLoan/DdsSample ref structs in both 0.2.3 and 0.3.2 -> no new compile errors). DDS LOOPBACK SMOKE TEST PASSED on Linux: 12 DDS tests green (participant create, publish/subscribe, cross-participant, TransientLocal late-joiner) via the WI-12 test build. The 0.3.2 linux-x64 native works. |
| WI-3 | File-dialog factory + wire ImGui fallback + multi-select | split | validated | pass | pass | pass (ReplayBrowser 27/27) | pass (ReplayBrowser) | WIN-VALIDATED (2026-07-12): 5 projects build 0 errors on Windows; ReplayBrowser.Tests 27/27 (unchanged vs main). `FileDialogServiceFactory.Create()` returns the comdlg32 `WinFormsFileDialogService` under `OperatingSystem.IsWindows()`. CAVEAT: the interactive open/save/multi-select GUI smoke was NOT run (headless validation session) - factory path + build + automated tests confirmed, live comdlg32 click-through deferred. Done + reviewed. `FileDialogServiceFactory.Create()` (Win32 on Windows, ImGui else) at 4 call sites; `SetFileDialogService` wired in each subsystem's RegisterWindows (harmless no-op on Windows). ImGui multi-select implemented (checkboxes + full-path HashSet + separate TCS). 5 projects build 0 warnings; ReplayBrowser.Tests 27/27. ImGui modal itself needs manual runtime check on a Linux desktop (can't test headless). Fdp.Presentation.Tests crash is pre-existing = WI-11. |
| WI-12 | 3 test projects fail to compile: ref structs in async methods | either | validated | pass | pass | pass | mostly pass | WIN-VALIDATED (2026-07-12): the 5 refactored test files compile+run on Windows; Fdp.Toolkits.Tests 1892/0, Hrot.Network.NED.Tests 98/0, Hrot.SimHost.Integration.Tests 27/14 with the SAME 14-test failure set as main (name-level diff: 0 regressions) - incl. EpisodeInjection's 3 pre-existing fails (pre-existing `ComponentId(215)` collision -> entities spawn 0). NOTE: SDK 10 (C#13) already compiles these on main too, so on Windows this was never a build blocker (it was on the Linux SDK-8 box). Done + reviewed. Extracted each ref-struct block (DdsLoan/DdsSample take-loops; EntityQuery foreach + ref-readonly local) into a synchronous local function/helper called without await; no product code, no weakened assertions (5 test files, 120+/68-). All 3 projects build clean on SDK 8. Runtime: DDS tests 12/12 pass on Linux (incl. cross-participant + TransientLocal late-joiner). EpisodeInjectionTests 2/5 - the 3 fails hit a pre-existing `Assert.Equal(3, EntityCount)` BEFORE the refactored code (entity-spawn returns 0; needs scenario infra), unrelated to the fix. |
| WI-11 | Fdp.Presentation.Tests crash on headless Linux (ImGui no-context) | Linux | validated | pass | n/a | no regression (382/34) | completes (382 pass / 34 pre-existing fail) | WIN-VALIDATED (2026-07-12): suite compiles+runs to completion on Windows with the SAME 382 pass / 34 fail as Linux. Rigorous name-level diff vs main: 0 regressions - every test green on main is green on the branch. On main the suite host-crashes after ~149 tests, so the extra 267 tests only execute on the branch (257 new-pass + 10 latent pre-existing fails, NOT port-introduced). CAVEAT re the checkpoint's "DebugGizmoLayerCaptureTests still pass": the class now runs (WI-11 goal met) but 2 of its 3 tests fail on logic asserts (SC_B28_4, SC_B28_6) - these are among the 34 pre-existing/latent fails (same on Linux), not regressions. Done + reviewed. Real cause was `DebugGizmoLayerCaptureTests` (a non-ImGui class whose Update() reads ImGui.GetIO() with no context) - added `[Collection("ImGui Sequential")]` + `using var fixture = new ImGuiTestFixture()` (12-line test-only diff, no product code). Suite now runs to completion. ALSO REQUIRES xvfb: `FdpApplicationTests` calls Raylib.InitWindow which needs an X display - run headless Linux Presentation tests under `xvfb-run -a`. The 34 remaining FAILs are pre-existing (ctx.Resources NRE in DebugPrimitiveRenderer2D tests, hardcoded input stubs) - separate triage, not port regressions. |
| WI-4 | Centralize `C:\FDP_Temp` staging root (FDP_STAGING_ROOT / temp) | Linux | validated | pass | pass* | pass (build; behavior-neutral) | pass* | WIN-VALIDATED (2026-07-12): Fdp.Toolkits/Orchestrator/SimHost/ExCon build 0 errors on Windows and their target tests are green; behavior-neutral on Windows. **BEHAVIOR NOTE (ops): the default staging root moved from the literal `C:\FDP_Temp` to `%TEMP%\FDP_Temp`. Any Windows deployment that relies on the old literal default must set `FDP_STAGING_ROOT=C:\FDP_Temp`.** Done + reviewed. `OrchestrationConstants.ResolveStagingRoot()` (FDP_STAGING_ROOT env or temp); ~24 sites updated (removing the const forced all refs). Default-param sites -> `= null` + `?? ResolveStagingRoot()`. Fdp.Toolkits/Orchestrator/SimHost/ExCon build clean. *CGF/IG/Editor edits verified via temp-patch only - their Linux build is blocked by WI-10 (netstandard2.0), not by WI-4. |
| WI-10 | Hrot.Blueprints.Compiler netstandard2.0 API gap | Linux | validated | pass | pass | pass (build) | - | WIN-VALIDATED (2026-07-12): master sln Debug+Release build 0 errors on Windows (compiler builds both TFMs; `IndexOf(..., StringComparison) >= 0` compiles everywhere). FIXED. Was a single occurrence: `Stage5_Schedule.cs:1641` `string.Contains(string, StringComparison)` (not in netstandard2.0). Replaced with `IndexOf(..., StringComparison) >= 0` (identical semantics). Compiler builds both TFMs; CGF/IG/Editor now build clean on Linux, which also confirms WI-4's edits in those projects. |
| WI-5 | Case-insensitive asset discovery (EnumerationOptions) + path-equality fixes | Linux | validated | pass | pass | pass (Hsm 504/504, BTree 575/575) | pass | WIN-VALIDATED (2026-07-12): builds 0 errors; Hsm.Editor.Tests 504/504 (the +1 vs main's 503 is the new mixed-case regression test) and BTree.Editor.Tests 575/575 on Windows - `MatchCasing.CaseInsensitive` + platform-aware `PlatformPathComparison` (OrdinalIgnoreCase on Windows) preserve Windows behavior. Done + reviewed. 12 enumeration sites -> MatchCasing.CaseInsensitive (recursion preserved per-site); 2 netstandard2.0 loaders use "*"+case-insensitive EndsWith fallback; 2 path-equality sites -> platform-aware PlatformPathComparison (OrdinalIgnoreCase on Windows, Ordinal else). New regression test (Widget.HSM.JSON found via *.hsm.json) fails pre-fix, passes post-fix. Hsm.Editor.Tests 504/504, BTree.Editor.Tests 575/575. Deliberate exclusions (non-asset globs) documented. |
| WI-6 | Portable one-offs (SpecialFolder.Fonts, UseShellExecute open-file) | Linux | validated | pass | pass | pass (build) | n/a | WIN-VALIDATED (2026-07-12): touched projects build 0 errors on Windows; changes are additive/guarded (Linux font paths appended, Process.Start try/catch) so Windows behavior is unchanged. Done + reviewed. NodeEditor.Demo font list gains Linux DejaVu/msttcorefonts paths (SpecialFolder.Fonts is empty on Linux; graceful fallback preserved). MessageLogPanel Process.Start left as-is (already UseShellExecute+try/catch = portable). ClusterDiagnosticsPanel Process.Start wrapped in try/catch (was unguarded -> would throw unhandled on Linux w/o xdg-open); surfaces via existing _inlineError. |
| WI-7 | Relax CarKinem win-x64 RID | either | validated | pass | pass | n/a | n/a | WIN-VALIDATED (2026-07-12): master sln Debug+Release build 0 errors on Windows with the RID pin removed (CarKinem builds without `win-x64`/`PlatformTarget=x64`). Done + reviewed. Removed `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` + `<PlatformTarget>x64</PlatformTarget>`. Builds default and `-r linux-x64`. |
| WI-8 | Linux launch scripts (.sh mirrors of run_*.bat) | Linux | code-done | n/a | n/a | n/a | bash -n pass | Done + reviewed. 6 scripts: run_SimHost/IG/IOS/Editor/all_together/all_standalone.sh. `start`->`nohup &`/backgrounded subshells; SIGINT trap+cleanup for multi-role launchers; SCRIPT_DIR-relative paths; -d/-m flags preserved; .bat untouched. No robocopy in these launchers. Not runtime-tested (needs built binary/desktop); bash -n clean. Dirigent not ported (out of scope). |
| WI-9 | Exclude Stride from Linux build | Windows | validated | pass | n/a | pass | n/a | WIN-RECONFIRMED (2026-07-12): `Stride/HrotStrideApp.sln` still builds Debug 0 errors on Windows alongside the master sln, so the Windows Stride path is intact. DECIDED. Master `IOS-IG-SimHost.sln` = 119 projects, 0 windows-locked, real Stride apps absent. Linux builds master sln, never `Stride/HrotStrideApp.sln`. No project edits. |
| WI-13 | Pin LangVersion to 12.0 (kill SDK-version language divergence) | either | code-done | pass | pass* | n/a | n/a | Changed 28 `<LangVersion>latest</LangVersion>` -> `12.0` so net8 projects compile as C#12 on ANY SDK (was C#12 on SDK8 / C#13 on SDK10 - the root cause of WI-12 only surfacing on the Linux lane). Provably safe: the Linux C#12 build already compiles all 28. Verified a sample (incl. the 3 WI-12 projects) builds under the default SDK 10 with C#12 forced. *Linux build unaffected (there `latest` already resolved to 12 on SDK8). Residual: SDK-version analyzer/warning behavior can still differ (LangVersion pin does not cover that; full SDK pin was declined by choice). |

## Coordination log (newest first)

- 2026-07-12 - WI-13 (Opus): pinned the 28 `LangVersion=latest` projects to 12.0
  after the Windows box flagged the SDK-8/SDK-10 divergence (C#12 vs C#13). This
  permanently prevents the WI-12 class of "builds on one lane, not the other".
  User chose LangVersion pinning over a global.json SDK pin. WI-3 interactive
  comdlg32 dialog click-through remains the one un-automatable validation item
  (needs a human on an interactive Windows desktop).
- 2026-07-12 - Windows validation PASS (Windows box). Differential vs origin/main
  on SDK 10.0.109: master sln Debug+Release + Stride sln all build 0 errors; no
  test regressions (name-level diffs cleared the Fdp.Presentation +
  SimHost.Integration count mismatches as pre-existing). All NEEDS-WIN-VALIDATION
  items flipped to validated.

- 2026-07-12 - **WINDOWS VALIDATION PASS (Opus orchestrator + Sonnet workers).**
  Differential baseline-vs-branch on the Windows box. SDK: **10.0.109** (note:
  NOT 8.0 - this matters, see below). Method: build + run the 8 target test
  projects on `origin/main` (73c990e8), repeat on `claude/linux-windows-port`
  (d70e6f15), diff. **Result: ZERO regressions** - every build green on main is
  green on the branch, and every test that passed on main passes on the branch.
  * Builds (both commits): master sln Debug **pass**, master sln Release **pass**,
    `Stride/HrotStrideApp.sln` Debug **pass**. 0 errors each.
  * Tests (branch): allocator 14/14; Fdp.Core.Tests 1157/0 (main had 2 flaky
    perf fails - branch better, not a regression); Fdp.Toolkits.Tests 1892/0;
    Hrot.Network.NED.Tests 98/0; ReplayBrowser.Tests 27/27; Hsm.Editor 504/504
    (+1 = WI-5 regression test); BTree.Editor 575/575; Fdp.Presentation.Tests
    382/34; SimHost.Integration.Tests 27/14.
  * The two count-mismatch areas were resolved by rigorous per-test-NAME TRX
    diffs, not aggregate counts:
    - **Fdp.Presentation.Tests** (main 149 run before a host-crash vs branch 416
      run to completion): name-diff = 0 regressions. The extra 267 tests only run
      on the branch because WI-11 stops the host-crash; 257 new-pass + 10 latent
      pre-existing fails. WI-11 caveat: `DebugGizmoLayerCaptureTests` now runs but
      2 of its 3 tests fail on logic asserts (SC_B28_4, SC_B28_6) - latent, same
      as Linux's 382/34, not port-introduced.
    - **SimHost.Integration.Tests** (both 27/14): name-diff = IDENTICAL 14-test
      failure set on both sides (pre-existing `ComponentId(215)` collision incl.
      EpisodeInjection's 3). 0 regressions.
  * **SDK note:** Windows has SDK 10, whose C#13 permits ref-structs-in-async, so
    the 3 WI-12 test projects that were BUILD-red on the Linux SDK-8 box already
    compile on main here - on Windows WI-12 was never a build blocker, and the
    branch refactor introduces no test-level regression.
  * **WI-4 ops note (carried forward):** default staging root moved
    `C:\FDP_Temp` -> `%TEMP%\FDP_Temp`; Windows deployments depending on the old
    literal must set `FDP_STAGING_ROOT=C:\FDP_Temp`.
  * WI-1, WI-3, WI-4, WI-5, WI-6, WI-7, WI-10, WI-11, WI-12 flipped
    NEEDS-WIN-VALIDATION -> validated. WI-3 caveat: interactive comdlg32
    open/save GUI smoke not run in this headless session (build + automated tests
    only). No PR opened per instructions.
- 2026-07-12 - WI-12 fixed (Sonnet) + reviewed (Opus). Extracted ref-struct usage
  out of async test methods across 5 test files; all 3 projects now compile on
  Linux. Bonus: the DDS tests run green (12/12) on Linux, which serves as WI-2's
  live loopback smoke test - so WI-2 is now fully validated (0.3.2 native works).
  The 3 EpisodeInjectionTests failures are pre-existing (fail at an EntityCount
  assert before the refactored code). Master solution now compiles fully on Linux
  except those pre-existing runtime test failures.
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
