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
| WI-1 | Cross-platform NativeMemoryAllocator (Win/POSIX backends; move event streams to NativeMemory) | Linux | todo | - | pass (compiles) | - | 13/14 FAIL | Primary technical risk. Keep public API identical. Linux failure reproduced 2026-07-12: DllNotFoundException kernel32.dll at Reserve():48. |
| WI-2 | Bump CycloneDDS.NET -> 0.3.2 | Linux | code-done | - | - | n/a | - | References bumped in commit `3113b4b`. Remaining: restore resolves 0.3.2, 0.2.x->0.3.2 API-compat, Linux DDS loopback smoke test. |
| WI-3 | File-dialog factory + wire ImGui fallback + multi-select | split | todo | - | - | - | - | 4 hardcoded call sites; factory picks Win32 on Windows else ImGui. |
| WI-4 | Centralize `C:\FDP_Temp` staging root (FDP_STAGING_ROOT / temp) | Linux | todo | - | - | - | - | ~9 sites -> one constant. |
| WI-5 | Case-insensitive asset discovery (EnumerationOptions) + path-equality fixes | Linux | todo | - | - | - | - | Silent-failure class; add mixed-case regression test. |
| WI-6 | Portable one-offs (SpecialFolder.Fonts, UseShellExecute open-file) | Linux | todo | - | - | - | - | Demo + editor conveniences. |
| WI-7 | Relax CarKinem win-x64 RID | either | todo | - | - | n/a | n/a | Trivial csproj edit. |
| WI-8 | Linux launch scripts (.sh mirrors of run_*.bat) | Linux | todo | n/a | - | n/a | - | No Dirigent port. |
| WI-9 | Exclude Stride from Linux build | Windows | validated | pass | n/a | pass | n/a | DECIDED. Master `IOS-IG-SimHost.sln` = 119 projects, 0 windows-locked, real Stride apps absent. Linux builds master sln, never `Stride/HrotStrideApp.sln`. No project edits. |

## Coordination log (newest first)

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
