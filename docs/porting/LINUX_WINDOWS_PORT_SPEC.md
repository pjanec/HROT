# Linux / Windows Cross-Platform Port - Specification

Status: DRAFT / ready to execute
Branch: `claude/linux-windows-port`
Owner: (cross-platform port effort)

## 1. Purpose and scope

The IOS-IG-SimHost-FDP engine currently runs on Windows only. The goal of this
effort is to make the engine build and run on **both Windows and Linux from a
single codebase and a single build artifact**, so the same binaries can be
published for either OS by runtime identifier only.

This spec is written to be executed **in parallel in two environments**:

- a **Windows box** (build + test the Windows path, guard against regressions,
  own the Windows-only concerns), and
- a **Linux VM** (build + test the Linux path, prove the port actually runs).

Both environments work from the **same branch** (`claude/linux-windows-port`).
The scope is limited to the changes cataloged in section 6. Anything not listed
there is explicitly out of scope for this pass.

### What is NOT in scope
- Rewriting the external Dirigent orchestrator (separate Windows-only tool, not
  in this repo). Linux launch uses shell scripts / systemd instead.
- macOS support.
- Porting the Stride game-engine visualization heads (decision item WI-9;
  default is to exclude them from the Linux build, not port them).

## 2. Guiding principles (non-negotiable)

1. **Single build artifact, runtime OS detection - not `#if`.** Select
   platform behavior at runtime with `OperatingSystem.IsWindows()` /
   `OperatingSystem.IsLinux()`. Do not fork the code with `#if WINDOWS`. The
   same DLL must load and run on both OSes; only the native runtime assets
   differ per RID. This lets both boxes build the identical code and lets tests
   exercise both branches in CI.
2. **Preserve public API surface.** Where an implementation is split behind an
   interface (e.g. the memory allocator), the existing public signatures must
   not change, so no consumer code is touched.
3. **Follow `AGENTS.md` editing invariants.** Preserve existing comments and
   Unicode exactly; minimize textual diffs; ASCII-only in new comments/strings
   where ASCII suffices; the solution MUST compile before any commit.
4. **Small, focused commits, one work item at a time.** Commit message prefix
   with the work-item id (e.g. `WI-1: cross-platform NativeMemoryAllocator`).
5. **Every code change must be validated on at least one OS before it is
   considered done, and must not regress the other OS.** See the verification
   matrix (section 7).
6. **Use Sonnet subagents for the mechanical work** (the backend split, the
   `EnumerationOptions` sweep, the staging-path centralization, script
   authoring). Reserve Opus for orchestration, architecture decisions
   (WI-2 DDS strategy, WI-9 Stride decision), and code review of every diff.

## 3. Two-environment execution model

Both sessions share the branch. To avoid write conflicts, ownership is split by
work item, and the Linux VM is the **primary implementation driver** for the
portable code changes (it is the environment that must prove the port works),
while the Windows box is the **continuous regression validator** and the owner
of the Windows-specific concerns.

| Concern | Windows box | Linux VM |
|---|---|---|
| Authors the portable code changes (WI-1, WI-4, WI-5, WI-6) | reviews / re-tests | **authors** |
| POSIX memory backend (WI-1) runtime proof | builds only | **authors + runtime-tests** |
| DDS Linux native (WI-2) | n/a | **authors + tests** |
| File-dialog factory (WI-3) | **validates Win32 path** | authors ImGui path |
| Windows regression: full build + test suite still green | **owns** | n/a |
| Stride heads decision (WI-9) | **owns** | consumes decision |
| Launch scripts (WI-8): `.sh` equivalents | n/a | **authors** |

### Coordination protocol
- One work item in flight per author at a time. Commit and push as soon as an
  item builds and its lane's tests pass.
- Always `git pull --rebase origin claude/linux-windows-port` before starting a
  new item and before every push.
- After each portable code change lands, the Windows box pulls, rebuilds, runs
  the affected test projects, and reports pass/fail on the branch (a short note
  in `docs/porting/PORT_STATUS.md`, created by the first session to need it).
- If a change cannot be validated in your lane, mark the work item
  `NEEDS-<other-os>-VALIDATION` in `PORT_STATUS.md` and continue.

## 4. Environment setup

### Common prerequisites (both OSes)
- .NET SDK 8.0 (the engine targets `net8.0`; some tools target `net10.0` /
  `net9.0` - install those SDKs too if you build the whole solution). Verify:
  `dotnet --info`.
- Git, with this branch checked out: `git checkout claude/linux-windows-port`.

### Windows box
- Existing setup already works. Build the FDP engine solution:
  `FDP\build.bat` (or `dotnet build FDP\FDP.sln -c Debug`).
- Run tests: `dotnet test FDP\FDP.sln` (and the Hrot solution / relevant test
  projects). Confirm baseline is green BEFORE making changes, so regressions are
  attributable.
- The Stride solution (`Stride\HrotStrideApp.sln`) builds here as today.

### Linux VM
- Install .NET SDK 8.0 (+ 9/10 if building tool projects). On the target
  simulation deployment, DDS also needs the native Cyclone runtime (WI-2).
- Native library search: Raylib-cs, rlImgui-cs and ImGui.NET already ship
  `linux-x64` natives via NuGet (`libraylib.so`, `libcimgui.so`) - confirmed.
- First-run expectation BEFORE any fix: `dotnet build` of the portable
  projects succeeds (they target plain `net8.0`), but running anything that
  touches the ECS will throw `DllNotFoundException` from `kernel32.dll` (WI-1),
  and anything touching DDS will fail to load `ddsc` (WI-2). That is the
  baseline this effort removes.
- Exclude the Stride `net8.0-windows` projects from Linux builds by building the
  specific solution/projects rather than every csproj (see WI-9).

## 5. Findings summary (why each work item exists)

The engine's native Windows surface is small and well-contained. The whole tree
has exactly **two** native Win32 P/Invoke files, most projects already target
plain `net8.0`, the render/UI stack is already cross-platform, and there are no
named pipes, named mutexes, memory-mapped files, thread-affinity calls, or
high-resolution-timer P/Invokes. The port is dominated by two hard blockers
(ECS virtual memory, DDS native) plus a set of "compiles but silently
misbehaves on Linux" issues (case sensitivity, hardcoded paths, file dialogs).

## 6. Work items

Each item lists: files, approach, acceptance criteria, the OS lane that
validates it, effort (S/M/L), and the recommended model.

---

### Tier 0 - Hard blockers (nothing runs on Linux until these land)

#### WI-1: Cross-platform native virtual-memory allocator  [effort M, Sonnet impl + Opus review]
**File:** `FDP/Engine/Fdp.Core/NativeMemoryAllocator.cs` (+ new backend files),
consumers `NativeChunkTable.cs`, `NativeEventStream.cs`,
`UntypedNativeEventStream.cs`, tests `NativeMemoryAllocatorTests.cs`.

**Problem:** `NativeMemoryAllocator` P/Invokes `kernel32.dll`
`VirtualAlloc`/`VirtualFree` unconditionally (lines 20-31), no OS guard ->
`DllNotFoundException` at the first ECS allocation on Linux. Note
`FDP_PARANOID_MODE` is defined for `Fdp.Core`, so the guarded error checks in
`Decommit`/`Free`/`Reserve`/`Commit` are compiled in and active.

**Approach:** Keep the public static API identical
(`Reserve`/`Commit`/`Decommit`/`Free`/`Is64KBAligned`). Introduce an internal
backend selected once at type-init by `OperatingSystem.IsWindows()`:
```
Fdp.Core/Memory/
  IVirtualMemoryBackend.cs        // Reserve/Commit/Decommit/Free + AllocationGranularity
  WindowsVirtualMemoryBackend.cs  // existing VirtualAlloc/VirtualFree, moved verbatim
  PosixVirtualMemoryBackend.cs    // libc mmap/munmap/mprotect/madvise
```
POSIX mapping:
- Reserve: `mmap(NULL, size, PROT_NONE, MAP_PRIVATE|MAP_ANONYMOUS|MAP_NORESERVE, -1, 0)`
- Commit: `mprotect(ptr, size, PROT_READ|PROT_WRITE)` (Linux demand-pages;
  optionally `madvise(MADV_POPULATE_WRITE)` on kernels >= 5.14 to mirror the
  eager-commit behavior the Windows tests assert)
- Decommit: `mprotect(ptr, size, PROT_NONE)` + `madvise(ptr, size, MADV_DONTNEED)`
  (the `MADV_DONTNEED` is what actually returns physical pages and makes recommit
  zero-fill, matching `Decommit_ReleasesPhysicalRAM`)
- Free: `munmap(ptr, size)` - **must use the real size**, unlike Windows
  `MEM_RELEASE` which requires size 0. The `originalReservedSize` parameter that
  Windows currently ignores becomes load-bearing here; all call sites already
  track and pass a correct size, so no caller changes are needed.
- Alignment: `mmap` guarantees only page (typically 4KB) alignment, not the
  Windows 64KB granularity. `Is64KBAligned` is used only by tests today. Choose
  one: (a) over-allocate-and-trim in the POSIX backend to keep 64KB alignment
  true everywhere (recommended - cheap insurance), or (b) relax the alignment
  assertions on non-Windows.

**Simplification (do it in the same item):** `NativeEventStream<T>.Buffer` and
`UntypedNativeEventStream` always Reserve-then-immediately-Commit the whole
buffer; they never use the sparse trick. Move them to portable
`System.Runtime.InteropServices.NativeMemory.Alloc/Realloc/Free`. After this,
`NativeChunkTable<T>` is the ONLY consumer of the OS-specific backend, shrinking
the platform-conditional surface to one class.

**Acceptance:**
- Windows: existing `NativeMemoryAllocatorTests` still pass unchanged; full
  Fdp.Core test suite green.
- Linux: `NativeMemoryAllocatorTests` pass (adjust alignment assertions only if
  option (b) chosen); an ECS smoke test (create world, spawn+destroy entities
  across chunk boundaries, trigger decommit) runs without native errors.

#### WI-2: DDS native runtime for Linux  [effort L, Opus decision + Sonnet packaging]
**Files:** all csproj referencing `CycloneDDS.NET` 0.2.3 (~20 projects incl.
`FDP/Network/Fdp.Network.Cyclone`, `Hrot/Network/*`, `Hrot/Engine/Hrot.Core`,
`Hrot/Runner/Hrot.ClusterRunner`, `GizmoMap.Network`). App code is already
abstracted (`DdsParticipant`, `IDdsReader<T>`/`IDdsWriter<T>`); no app-code
P/Invoke changes expected.

**Problem:** The `CycloneDDS.NET` 0.2.3 NuGet package ships **only**
`runtimes/win-x64/native/ddsc.dll`; there is no `linux-x64`/`libddsc.so`.
Eclipse Cyclone DDS itself is cross-platform - only this .NET binding was
published Windows-only.

**Approach (Opus to choose, then Sonnet executes):**
1. Preferred: build Eclipse Cyclone DDS for `linux-x64` to produce `libddsc.so`,
   and obtain/produce a matching Linux runtime for the binding. Upstream binding
   repo referenced by the package is `pjanec/CycloneDds.NET`. Stage the `.so`
   either via a Linux runtime NuGet or via a repo-local `runtimes/linux-x64/native/`
   asset wired with `NativeLibrary.SetDllImportResolver` / a `.targets` file so
   `dotnet publish -r linux-x64` copies it.
2. Fallback: evaluate an alternate DDS binding with published multi-platform
   natives, behind the existing `IDds*` abstraction.

**Acceptance:**
- Linux: `Hrot.ClusterRunner -m simhost` (and one peer role) start, create a DDS
  participant, and exchange at least one sample over loopback without native
  load errors. `CYCLONEDDS_URI` config path resolves (coordinate with WI-4).
- Windows: unchanged; still uses `ddsc.dll`.

---

### Tier 1 - Compiles, but silently wrong on Linux

#### WI-3: File-dialog factory + wire the ImGui fallback  [effort M, Sonnet]
**Files:** `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IFileDialogService.cs`,
`.../Panels/WinFormsFileDialogService.cs`, `.../Panels/ImGuiFileDialogService.cs`,
`.../ImGui/WindowManager/WindowManager.cs`, and the four call sites:
`Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs:244`,
`Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs:252`,
`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs:1340`,
`Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs:302`.

**Problem:** The Win32 `comdlg32` dialog is already `OperatingSystem.IsWindows()`
guarded (no crash), and a pure-ImGui fallback exists, but the four call sites
hardcode `new WinFormsFileDialogService()`, `WindowManager.SetFileDialogService`
is never called (so the ImGui popup never renders), and the ImGui backend's
multi-select is stubbed to `null` (blocks `ReplayTimelinePanel`'s load-group).

**Approach:** Add `FileDialogServiceFactory.Create()` returning
`WinFormsFileDialogService` when `OperatingSystem.IsWindows()` else
`ImGuiFileDialogService`; replace the four `new` sites with it; ensure each
subsystem also calls `WindowManager.SetFileDialogService(...)` so the ImGui
modal draws. Implement ImGui multi-select. Optional: swap the
`Directory.GetLogicalDrives()` drive combo for a home/root/bookmarks row on
non-Windows.

**Acceptance:**
- Windows: Open/Save dialogs behave exactly as today (native comdlg32).
- Linux: Open/Save/multi-select all function via the in-app ImGui dialog.

#### WI-4: Centralize the `C:\FDP_Temp` staging root  [effort S, Sonnet]
**Files (~9):** `FDP/Toolkits/Fdp.Toolkits/Orchestration/OrchestrationConstants.cs:14`,
`Hrot/Subsystems/Hrot.Orchestrator/ClusterConfiguration.cs:28`,
`Hrot/Subsystems/Hrot.Orchestrator/GlobalContextClusterOpHandler.cs:50`,
`Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs:216,223`,
`Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs:78,89`,
`Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs:175`,
`Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfScenarioLoadHandler.cs:74,84`,
`Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs:222-224`,
`FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceLiveLoadHandler.cs:63,67`.

**Approach:** Change `OrchestrationConstants.DefaultStagingDirectory` to
`Environment.GetEnvironmentVariable("FDP_STAGING_ROOT") ?? Path.Combine(Path.GetTempPath(), "FDP_Temp")`
and make every site reference that single constant instead of re-hardcoding the
literal. Do NOT change behavior when a caller/config already supplies a root.

**Acceptance:** Windows default resolves under the temp dir (or `FDP_STAGING_ROOT`
if set) with no functional change to existing configured deployments; Linux
staging/scenario-load creates and uses a valid root.

#### WI-5: Case-insensitive asset discovery  [effort S-M, Sonnet]
**Hot-spot files:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintPeerSource.cs:62`,
`.../Catalog/BlueprintAssetContributor.cs:46`,
`Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Hsm/HsmJsonServices.cs:136`,
`.../BTree/BTreeJsonServices.cs:148`, the `*JsonAssetContributor` files,
`Hrot/Subsystems/Hrot.Editor/Browser/ScenarioEnumeration.cs:44`,
`Hrot/Subsystems/Hrot.Orchestrator/StorageGatewayModule.cs` (multiple
`Directory.GetFiles(..., "*.json"/"*.fdp"/"*.meta.json")`).

**Problem:** `Directory.GetFiles/EnumerateFiles` glob matching is case-sensitive
on Linux by default. Any casing drift makes blueprints/HSMs/BTrees/scenarios
silently invisible (empty result, no exception) - the hardest class of bug to
catch. Also several path-equality checks use `OrdinalIgnoreCase`
(`ScenarioFileService.cs:115`, `StorageGatewayModule.cs:271`) that wrongly treat
case-differing Linux paths as the same file.

**Approach:** Pass `new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = <as-before> }`
to every extension-glob enumeration. For path equality that must reflect real
filesystem semantics, use `Ordinal` (or branch:
`OperatingSystem.IsWindows() ? OrdinalIgnoreCase : Ordinal`), reserving
`OrdinalIgnoreCase` for genuinely case-insensitive tokens (enum names,
protocol strings), not paths.

**Acceptance:** Linux - a blueprint/HSM/scenario whose on-disk extension casing
differs from the code's literal is still discovered and loaded. Windows -
identical behavior to today. Add a regression test that seeds a mixed-case asset
filename and asserts discovery on both OSes.

#### WI-6: Portable one-offs  [effort S, Sonnet]
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/Program.cs:14` -
  `Environment.SpecialFolder.Fonts` returns empty string on Linux (silent
  failure). Bundle/embed a font or platform-branch the path. (Demo only.)
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/MessageLogPanel.cs:694` and
  `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterDiagnosticsPanel.cs:647` -
  `Process.Start(UseShellExecute=true)` "open in default app". Works via
  `xdg-open` on Linux desktops; keep the existing try/catch, and verify on the
  target distro (no-op acceptable on headless).

**Acceptance:** No crashes; documented behavior on Linux.

---

### Tier 2 - Build targeting

#### WI-7: Relax the CarKinem RID  [effort S, Sonnet]
**File:** `FDP/Examples/Fdp.Examples.CarKinem/Fdp.Examples.CarKinem.csproj:6-7`.
Remove/relax hardcoded `RuntimeIdentifier=win-x64` + `PlatformTarget=x64` (TFM is
already portable `net8.0`; Raylib is cross-platform), or make it
`<RuntimeIdentifiers>win-x64;linux-x64</RuntimeIdentifiers>`.
**Acceptance:** builds on both OSes; `dotnet publish -r linux-x64` succeeds.

#### WI-9: Stride heads decision  [Opus decision, Windows box owns]
**Files:** `Stride/HrotStrideApp.Windows/*.csproj`,
`Stride/HrotStrideApp.Game/*.csproj`, `Stride/BepuSample/*`.
These are `net8.0-windows` / `win-x64` / `WinExe`. First confirm the actual sim
binaries (`Hrot.ClusterRunner`) do not reference the Stride projects (they
appear not to). **Default decision:** exclude the Stride solution from the Linux
build matrix (do not attempt to port). Document this in `PORT_STATUS.md`. Only
if a Linux visualization head is required, revisit adding a `.Linux` Stride head
(SDL/Vulkan) as a separate, larger effort.
**Acceptance:** Linux build/test scripts build the FDP+Hrot engine solutions and
skip Stride; Windows continues to build Stride as today.

---

### Tier 3 - Launch tooling

#### WI-8: Linux launch scripts  [effort S, Sonnet]
Author `.sh` equivalents of `run_SimHost.bat`, `run_IG.bat`, `run_IOS.bat`,
`run_Editor.bat`, `run_all_together.bat` using `nohup`/`setsid`/`&` in place of
cmd's `start`, and `rsync`/`cp -a` in place of `robocopy`. Keep the `.bat` files
untouched for Windows. Do not attempt to port the external Dirigent orchestrator.
**Acceptance:** on Linux, the scripts launch `Hrot.ClusterRunner` in the
requested mode(s).

## 7. Verification matrix

| Work item | Build Win | Build Linux | Test Win | Test Linux | Primary lane |
|---|---|---|---|---|---|
| WI-1 allocator | required | required | required | required | Linux |
| WI-2 DDS native | required | required | n/a | required | Linux |
| WI-3 dialogs | required | required | manual | manual | split |
| WI-4 staging root | required | required | required | required | Linux |
| WI-5 case sensitivity | required | required | required | required | Linux |
| WI-6 one-offs | required | required | manual | manual | Linux |
| WI-7 CarKinem RID | required | required | n/a | n/a | either |
| WI-8 launch scripts | n/a | manual | n/a | manual | Linux |
| WI-9 Stride | required | excluded | required | n/a | Windows |

"required" = must be green before the item is done. The Windows box re-runs the
full engine test suite after every portable change to guard against regressions.

## 8. Definition of done

- FDP + Hrot engine solutions build on both Windows and Linux from this branch.
- The engine test suites are green on both OSes (Stride excluded on Linux per
  WI-9).
- `Hrot.ClusterRunner` starts a sim role and exchanges at least one DDS sample on
  Linux (WI-2) without native load errors.
- No `#if WINDOWS`-style compile-time forking was introduced; all platform
  behavior is runtime-selected.
- `PORT_STATUS.md` records the final state of every work item and both boxes'
  build/test results.

## 9. Open questions / risks

- **WI-2 is the schedule risk.** Producing a Linux `libddsc.so` + binding runtime
  may require building upstream Cyclone DDS and possibly engaging the binding
  maintainer. Start this first on the Linux VM in parallel with WI-1.
- The repo `nuget.config` references a `./nugets` LocalFeed that is absent in a
  fresh checkout; confirm restore works on Linux or point the feed appropriately.
- The `originalReservedSize` -> `munmap` size dependency (WI-1) assumes all call
  sites pass correct sizes; verified true today, but re-check if new callers are
  added during the work.
