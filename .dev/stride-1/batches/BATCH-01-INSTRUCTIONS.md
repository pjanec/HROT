# BATCH-01: Stride project scaffolding + coordinate seam
**Tasks:** STR-P0-T1, STR-P0-T2, STR-P0-T3, STR-P0-T4   **Phase:** P0 (Scaffolding)   **Est:** ~10–12h
**Dependencies:** none (first batch). Pre-task work already committed: `StrideRenderModelDefDto` + `CollisionShapeKind`, and the `Stride/HrotStrideApp.sln` Stride solution (Bullet).

This is the foundational batch. Its purpose is to (a) stand up the two new Stride class libraries with correct references, (b) wire them + the mock + FDP into the Stride app and prove the **whole Stride solution still builds and compiles assets**, and (c) implement and rigorously test the pure `FdpStrideTransform` coordinate seam. No runtime/host-loop behavior yet — that is BATCH-02 (T5–T8).

## Onboarding (read in order, before any code)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract (test quality section is binding).
2. `.dev/stride-1/Stride-Integration_v0_3.md` §3 (project layout), §4 (coordinate seam — the spec for T4), §1.1 (the bootstrapper seam / why the Raylib tag-along is fine).
3. `.dev/stride-1/TASK-DETAIL.md` — tasks STR-P0-T1 … STR-P0-T4 (success conditions are authoritative).

Use the **codebase-memory MCP first** (`list_projects` → project `D-Work-IOS-IG-SimHost-FDP`; `get_architecture`, `search_graph`, `get_code_snippet`) for all code exploration. Fall back to Read/Grep only for raw text you must edit.

### Verified facts (don't re-derive)
- **No git submodules** in this repo; everything is one working tree.
- FDP math types are `System.Numerics.Vector3` / `System.Numerics.Quaternion`. `Fdp.Core.SimTransform` = `{ Vector3 Position; Quaternion Rotation; }`; `Fdp.Core.SimVelocity` = `{ Vector3 Linear; Vector3 Angular; }` — see [SimComponents.cs](../../../FDP/Engine/Fdp.Core/CoreComponents/SimComponents.cs). FDP is right-handed: **X=East, Y=North, Z=Up**; rotation order yaw-pitch-roll = (Z, Y, X), yaw 0 = +X (east), yaw +90 = +Y (north). So `FdpVector3` = `System.Numerics.Vector3` and `FdpRotation` = `System.Numerics.Quaternion` — do **not** invent wrapper types.
- `HrotStrideApp.Game` is `net8.0-windows`, Stride **4.2.1.2487** packages — see [HrotStrideApp.Game.csproj](../../../Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj).
- `IAnimationBackend` lives in `Hrot.MuscleCharacter.Animation` (net8.0) at [Contracts/IAnimationBackend.cs](../../../Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Contracts/IAnimationBackend.cs); it is large (RegisterEntity/UnregisterEntity/TryResolve/PlayMontageOnSlot/StopMontageOnSlot/SetAimTarget*/ReleaseAim/RequestStanceChange/Tick/DrainNotifies×2/GetCurrentStance/SnapshotMetrics/IsAnySlotActive/IsAnySlotInBlendOut/CrossfadeMontageOnSlot).
- `Hrot.StrideMock` (net8.0, refs Raylib) is at [Hrot.StrideMock.csproj](../../../Hrot/Subsystems/Hrot.StrideMock/Hrot.StrideMock.csproj); it transitively references `Hrot.Common`, `Hrot.SimHost`, `Fdp.Core`, `Fdp.Presentation`, `Fdp.Toolkits`, `Hrot.IG`, `Fdp.Examples.Scenarios`.
- Test-project naming convention in this repo: `<Project>.Tests` (xUnit 2.5.x, `Microsoft.NET.Test.Sdk` 17.8.0, Moq).
- A `net8.0-windows → net8.0` ProjectReference compiles (already proven in this solution).

**Complete the tasks in sequence; do NOT start the next task until the current task's implementation is done, its tests are written, and ALL tests (yours + any you can reach in the touched solution) pass.** Work autonomously — run builds/tests and fix root causes to completion; do not stop for permission. Only stop on a genuine breaking design flaw or unrecoverable build-environment blocker (e.g. Stride asset compiler cannot run at all — if so, document precisely what failed).

---

## Tasks

### Task 1: Create `Hrot.Stride.Core` project + references (STR-P0-T1)
**File:** `Stride/Hrot.Stride.Core/Hrot.Stride.Core.csproj` (NEW) + a placeholder `.cs`.
Per design §3. `net8.0-windows` class library, `RootNamespace`/`AssemblyName` = `Hrot.Stride.Core`, `Nullable` enable, `AllowUnsafeBlocks` true, `LangVersion latest`.
References:
- Stride NuGet **4.2.1.2487**: `Stride.Engine`, `Stride.Physics`, `Stride.Rendering`, `Stride.Games` (match the version/`PrivateAssets` style used by `HrotStrideApp.Game.csproj`).
- `DotRecast.*` NuGet — **[VERIFY]** the exact published package id(s) and a version compatible with net8.0 (e.g. `DotRecast.Recast`, `DotRecast.Detour`, `DotRecast.Detour.Crowd`). If a DotRecast package cannot be restored cleanly, **stop and report** rather than faking it — navigation needs it in P2, but the project must restore now. Pin the version you choose and record it in the report.
- `ProjectReference`s to the FDP/HROT assemblies needed now: `Fdp.Core` (SimTransform/SimVelocity), `Fdp.Toolkits` (StrideRenderModelDefDto / CollisionShapeKind). Add `Hrot.Common` and others only if a type you actually use requires it (don't add unused refs).
- **NO** `Raylib-cs` / `rlImGui-cs`; **NO** `Hrot.StrideMock` reference.

Place a trivial type that references **both** a `Stride.Engine` type and an FDP `net8.0` type (e.g. holds a `Stride.Engine.Entity` field and an `Fdp.Core.EntityRepository` field) to prove the cross-TFM reference compiles.

**Tests required** (`Stride/Hrot.Stride.Core.Tests/Hrot.Stride.Core.Tests.csproj`, NEW, `net8.0-windows`, xUnit):
- A **reference-guard** test that reflects over `typeof(<a Hrot.Stride.Core type>).Assembly.GetReferencedAssemblies()` and asserts **no** referenced assembly name contains `Raylib`, `rlImGui`, or `Hrot.StrideMock`. (Assertion on the real loaded assembly list — not a string scan of the csproj.)
- A test that instantiates the trivial cross-TFM type and asserts both fields are assignable / a `Stride.Engine` type and `Fdp.Core.EntityRepository` are both resolvable at runtime from this assembly.

### Task 2: Create `Hrot.Stride.Animation` project + references (STR-P0-T2)
**File:** `Stride/Hrot.Stride.Animation/Hrot.Stride.Animation.csproj` (NEW).
Per design §3. `net8.0-windows` class library. References: `Stride.Engine`, `Stride.Animations` (4.2.1.2487), `ProjectReference` to `Hrot.MuscleCharacter.Animation`. Distinct from the fake `Hrot.MuscleCharacter.Animation.Stride`.
Add a **stub** `StrideAnimationBackend : Hrot.MuscleCharacter.Animation.Contracts.IAnimationBackend` that compiles against the real interface. Implementation is deferred to P4 — each member may `throw new NotImplementedException()`, but it must **fully implement the interface surface** (all members above) so it compiles. Document with `/// <summary>` that it is a P4 stub.

**Tests required** (`Stride/Hrot.Stride.Animation.Tests/...`, NEW, `net8.0-windows`, xUnit):
- A test that constructs `StrideAnimationBackend`, assigns it to an `IAnimationBackend` variable (proves it satisfies the contract at runtime), and asserts the type implements every interface member (e.g. `typeof(IAnimationBackend).GetMethods()` all have a matching public implementation — no missing members). Do **not** invoke the NotImplementedException stubs as "passing behavior".

### Task 3: Wire `HrotStrideApp.Game` references (STR-P0-T3)
**File:** `Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj` (UPDATE) + `Stride/HrotStrideApp.sln` (UPDATE — add the two new libs and their test projects so they participate in the solution).
Add `ProjectReference`s from `HrotStrideApp.Game` to `Hrot.Stride.Core`, `Hrot.Stride.Animation`, `Hrot.StrideMock`, and the FDP/HROT assemblies it needs (at minimum what `Hrot.StrideMock` exposes for `StrideNodeBootstrapper`). Confirm the Raylib tag-along from `Hrot.StrideMock` is accepted (design §1.1, §8.3 — both modes host diagnostic raylib windows).

**Success / Tests required:**
- `dotnet build Stride/HrotStrideApp.sln -c Debug` builds clean (Game + Windows head), **0 errors**. Capture the output in the report.
- **Stride asset compilation still succeeds** — confirm the asset/content build step runs without regression (note in the report exactly how you verified: full solution build that triggers `Stride.Core.Assets.CompilerApp`, or the explicit asset-compile target). If the asset pipeline cannot run in this environment, document precisely and stop.
- A test (in `Hrot.Stride.Core.Tests` or a small new app-facing test, your call — but it must be a real runtime assertion) proving `Hrot.StrideMock.StrideNodeBootstrapper` is **resolvable/loadable from the `HrotStrideApp.Game` assembly's reference closure** (e.g. `Type.GetType` / `Assembly.Load` of the StrideMock type succeeds, or a reflection assertion that `HrotStrideApp.Game` references `Hrot.StrideMock`). [VERIFY] the exact `StrideNodeBootstrapper` type name/namespace in `Hrot.StrideMock`.

### Task 4: `FdpStrideTransform` coordinate seam (STR-P0-T4)
**File:** `Stride/Hrot.Stride.Core/FdpStrideTransform.cs` (NEW). Pure static class. Spec: design §4 (read it — axis table + handedness note).
Implement, using `System.Numerics.Vector3`/`Quaternion` on the FDP side and `Stride.Core.Mathematics.Vector3`/`Quaternion` on the Stride side:
- `ToStridePosition(in Vector3 p)` / `ToFdpPosition(in Stride…Vector3 s)` — axis swizzle FDP (X=E, Y=N, Z=Up) ↔ Stride (X=E, Y=Up, Z=N): i.e. Stride = `(p.X, p.Z, p.Y)`.
- `ToStrideRotation(in Quaternion r)` / `ToFdpRotation(in Stride…Quaternion q)` — must handle the **handedness flip** (right-handed FDP ↔ left-handed Stride), not merely relabel axes. [VERIFY] the exact Euler order of `SimTransform.Rotation` (Z-Y-X per SimComponents.cs) and the quaternion winding Bullet/Stride expects; derive the conversion and document the derivation in code comments.
- `ToStrideVelocity` / `ToFdpVelocity` — **same swizzle as position**, no translation term.
- `ToFdpAngularVelocity(in Stride…Vector3 s)` — angular velocity conversion (account for the handedness sign).
- `ScreenRayToFdp(in Stride.Engine.CameraComponent cam, Vector2 screenPx)` → an FDP-space ray. [VERIFY] the camera unproject API; if a `CameraComponent`'s matrices aren't available without a running game, you may compute from the camera's view/projection matrices passed through — keep it pure where possible. Provide an `FdpRay`-style return (define a small struct `{ Vector3 Origin; Vector3 Direction; }` in `Hrot.Stride.Core` if the engine has no existing ray type you should reuse — [VERIFY] whether `Fdp.Core` already has a ray type and reuse it if so).

Do **not** leak `Stride.Core.Mathematics` types across the bootstrapper seam — they appear only inside `Hrot.Stride.Core`.

**Tests required** (`Hrot.Stride.Core.Tests`, behavioral — assert real numeric values, not "not null"):
- **Position round-trip:** for a battery of vectors (incl. negatives, zero, large), `ToFdpPosition(ToStridePosition(p)) ≈ p` within 1e-5.
- **Exact axis mapping:** assert concrete components, e.g. FDP `(1,2,3)` → Stride `(1,3,2)`; FDP unit East `(1,0,0)`→`(1,0,0)`, North `(0,1,0)`→`(0,0,1)`, Up `(0,0,1)`→`(0,1,0)`.
- **Rotation homomorphism (handedness-proving):** for several rotations `q` (incl. a yaw-only, a pitch-only, and a combined rotation) and several vectors `v`, assert `ToStridePosition(Vector3.Transform(v, q)) ≈ Vector3.Transform(ToStridePosition(v), ToStrideRotation(q))` within tolerance. **This must fail if the handedness flip is omitted** (a pure axis-relabel without sign flip breaks it for non-axis-aligned rotations) — include at least one combined yaw+pitch rotation that exercises this.
- **Known facing:** an FDP yaw of +90° (facing North) maps to a Stride rotation whose forward (the swizzled FDP North `(0,0,1)` in Stride) is produced when that rotation is applied to the swizzled FDP-yaw-0 forward — i.e. derive the expected Stride facing from first principles and assert it. Document the chosen "forward" convention.
- **Rotation round-trip:** `ToFdpRotation(ToStrideRotation(r)) ≈ r` (account for quaternion double-cover: compare via `|dot| ≈ 1` or by transformed-vector equality) for a battery of rotations.
- **Velocity:** `ToStrideVelocity(v)` uses the identical swizzle as `ToStridePosition(v)` (assert equality of components); round-trip holds; angular velocity conversion verified on a known input.

---

## Success Criteria
- [ ] STR-P0-T1: `Hrot.Stride.Core` builds clean; reference-guard test proves no Raylib/rlImGui/StrideMock ref; cross-TFM type instantiates. + tests pass.
- [ ] STR-P0-T2: `Hrot.Stride.Animation` builds clean; `StrideAnimationBackend` implements the full `IAnimationBackend` surface and is assignable to it at runtime. + tests pass.
- [ ] STR-P0-T3: `Stride/HrotStrideApp.sln` builds clean (Game + Windows head); Stride asset compilation succeeds (verified, documented); `StrideNodeBootstrapper` resolvable from `HrotStrideApp.Game`. + test pass.
- [ ] STR-P0-T4: `FdpStrideTransform` implemented per §4; all behavioral tests above pass (round-trips, exact axis mapping, **rotation homomorphism proving the handedness flip**, known facing, velocity swizzle).
- [ ] Full test suite for the touched projects green; no new warnings; report submitted.

## Report Requirements (`reports/BATCH-01-REPORT.md`)
Answer: issues encountered; the **exact DotRecast package id(s)+version** you settled on and any restore trouble; how you verified Stride **asset compilation** (command + result, and whether it actually ran the content compiler); the derivation of the rotation handedness conversion (what winding/sign you chose and why) and which test specifically proves it; any [VERIFY] item where the live source differed from the design's assumption (record as a deviation); the `ScreenRayToFdp` approach (and whether you could test it purely); weak points spotted; suggested one-line commit message. Report actual test counts/output, not "all pass". Do NOT ask comprehension questions.
