# BATCH-S2-E — Unify the hosted-mode TkbDb (fixes visuals=0, VehicleState-on-infantry, orphan bodies)

**Topic dir:** `.dev/stride-2/` · **Guide:** `.dev/.guides/DEV-GUIDE_claude.md`
**Mode:** sonnet coder. CPU-verifiable (build + full test suite). User does GPU sign-off afterwards.

---

## Background (read once, do not re-derive)

In hosted mode (`STRIDE_HOST_REAL_EDITOR=1`) the real `EditorSubsystem` runs inside the Stride app and is the
node that **spawns scenario entities**. Today there are **two disjoint `TkbDatabase` instances**:

1. **The editor's spawn DB** — built in `EditorSubsystem.Initialize` at
   `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs:864` via `HrotEnvironment.CreateTkb()` (registers the
   **NED catalog**: Tank_M1Abrams=100, IFV_Bradley=101, Truck_HMMWV=102, Tank_T72=103, Infantry_Rifleman=200,
   composites 301/302/303, tac-graphics 8801/8802/8803), then `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates`
   (1001–2003) at `:867`. **`NetworkSpawningSystem` + all `ITkbEntityTranslator`s use THIS instance.**
   The NED templates carry **no** `StrideRenderModelDefDto`.

2. **The Stride view DB** — `EditorStrideSubsystem.InitializeHosted` builds a **separate** `new TkbDatabase()` +
   `RegisterUrbanCombatTkbTemplates` at `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs:943-944`.
   **`StrideVisualBindingSystem` reads THIS instance** (only has UrbanCombat 1001–2003 + their render-defs).

Because they are disjoint, when a real scenario (e.g. `scenarios/hill-attack/scenario.json`, entities of TkbType
**100**, **303**, **8803**) spawns:
- **visuals = 0** — `StrideVisualBindingSystem.TryGetByType(100)` misses (its DB only has 1001–2003) → no visual
  (silent null at `Stride/Hrot.Stride.Core/StrideVisualBindingSystem.cs:~218`).
- **VehicleState-on-infantry risk** — `VehicleKinematicsTkbTranslator` (which reads the *editor's* DB) checks
  `template.GetDescriptor<StrideRenderModelDefDto>()`; for NED types it's `null` → `isVehicleShaped=true`
  unconditionally (`FDP/Toolkits/Fdp.Toolkits/CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs:54-56`). Harmless for
  tanks, wrong for NED infantry (type 200).
- contributory orphaned bodies.

**Decision (user, option A): generic placeholders by shape.** Map every NED *vehicle* type to the existing
`Models/Box2x1x1` (OrientedBox) and every NED *infantry* type to `Models/mannequinModel` (Capsule) — the two models
the Stride app already ships. No new assets. Specific per-type models can come later.

**Fix = ONE TkbDb.** Point the Stride view at the editor's spawn DB, and augment that DB's NED platform/infantry
templates with `StrideRenderModelDefDto`. `TkbDatabase.TryGetByType` returns the **live** stored `TkbTemplate`
reference and `TkbTemplate.AddDescriptor` mutates it **in place** (verified in
`FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDatabase.cs` + `FDP/Engine/Fdp.Core/Abstractions/TkbTemplate.cs`), so augmenting
*after* registration is seen by the already-wired spawn pipeline and translators.

---

## Task 1 — Expose the editor's spawn TkbDb

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

1. Add a public read-only accessor next to the other promoted host accessors (`World`/`Kernel`/`TimeController`/
   `EntityCreationRequestSource`). Use the project's existing `TkbDatabase` type
   (`Fdp.Toolkit.Tkb.TkbDatabase` — already in scope via `HrotEnvironment.CreateTkb()`'s return type):

   ```csharp
   /// <summary>
   /// The editor's authoritative spawn TKB (NED catalog + UrbanCombat templates). This is the
   /// instance NetworkSpawningSystem and every ITkbEntityTranslator resolve templates from.
   /// Exposed so an in-process host (e.g. the Stride muscle's StrideVisualBindingSystem) can
   /// bind to the SAME database instead of a duplicate, avoiding template-resolution drift.
   /// Null until Initialize() has run.
   /// </summary>
   public TkbDatabase TkbDatabase { get; private set; }
   ```

2. At `EditorSubsystem.cs:864`, after the DB is fully populated (i.e. **after** the
   `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkbDb)` call at `:867`), assign the field. Keep the
   existing local `tkbDb` and all its downstream uses unchanged — just add the assignment:

   ```csharp
   var tkbDb = HrotEnvironment.CreateTkb();
   UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkbDb);
   TkbDatabase = tkbDb;   // expose the authoritative spawn DB to in-process hosts
   ```

   (Add `using Fdp.Toolkit.Tkb;` only if not already present.)

**Do not** change any other behavior. This is purely additive — the SimHost/clusterrunner path never reads the new
accessor.

---

## Task 2 — Stride render-descriptor augmentation for NED types

**New file:** `Stride/HrotStrideApp.Game/StrideNedRenderDescriptors.cs`

Add a small static helper that augments NED platform/infantry templates with a `StrideRenderModelDefDto` **only if
the template exists and doesn't already carry one** (idempotent, safe to call once per hosted init). Use literal
TkbType IDs with name comments (avoids taking a new project reference; the IDs are stable public constants in
`Hrot.Map.Common.TkbEntityTypes`).

```csharp
using Fdp.Interfaces;            // ITkbDatabase, TkbTemplate
using Fdp.Toolkit.Tkb.Domain;    // StrideRenderModelDefDto, CollisionShapeKind

namespace HrotStrideApp.Game
{
    /// <summary>
    /// Augments the NED-catalog platform/infantry templates with Stride render + collision
    /// descriptors (generic placeholders: Box for vehicles, mannequin for infantry — the two
    /// models the Stride app ships). Called once on the editor's authoritative spawn TkbDb in
    /// hosted mode so StrideVisualBindingSystem and VehicleKinematicsTkbTranslator resolve the
    /// SAME templates the scenario spawns from. NED composites (301/302/303) and tactical
    /// graphics (8801/8802/8803) are intentionally left with no render-def: they are abstract
    /// HQ markers / map overlays with no 3D body.
    /// </summary>
    public static class StrideNedRenderDescriptors
    {
        public static void Apply(ITkbDatabase tkb)
        {
            if (tkb == null) return;

            // ── Vehicles → OrientedBox / Box2x1x1 (half-extents from NedTkbCatalog dims) ──
            AddVehicle(tkb, 100, halfX: 3.97f, halfY: 1.83f, halfZ: 1.22f, height: 2.44f); // Tank_M1Abrams
            AddVehicle(tkb, 101, halfX: 3.28f, halfY: 1.80f, halfZ: 1.49f, height: 2.98f); // IFV_Bradley
            AddVehicle(tkb, 102, halfX: 2.29f, halfY: 1.08f, halfZ: 0.92f, height: 1.83f); // Truck_HMMWV
            AddVehicle(tkb, 103, halfX: 3.48f, halfY: 1.80f, halfZ: 1.12f, height: 2.23f); // Tank_T72

            // ── Infantry → Capsule / mannequinModel ──
            AddInfantry(tkb, 200); // Infantry_Rifleman
            AddInfantry(tkb, 201); // Infantry_Officer (registered only if catalog defines it; guarded)
        }

        private static void AddVehicle(ITkbDatabase tkb, long tkbType,
                                       float halfX, float halfY, float halfZ, float height)
        {
            if (!tkb.TryGetByType(tkbType, out var t) || t == null) return;
            if (t.HasDescriptor<StrideRenderModelDefDto>()) return;
            t.AddDescriptor(new StrideRenderModelDefDto
            {
                ModelAssetRef = "Models/Box2x1x1",
                ShapeKind     = CollisionShapeKind.OrientedBox,
                ShapeHeight   = height,
                BoxHalfX      = halfX,
                BoxHalfY      = halfY,
                BoxHalfZ      = halfZ,
            });
        }

        private static void AddInfantry(ITkbDatabase tkb, long tkbType)
        {
            if (!tkb.TryGetByType(tkbType, out var t) || t == null) return;
            if (t.HasDescriptor<StrideRenderModelDefDto>()) return;
            t.AddDescriptor(new StrideRenderModelDefDto
            {
                ModelAssetRef    = "Models/mannequinModel",
                SkeletonAssetRef = "Models/mannequinModel Skeleton",
                ShapeKind        = CollisionShapeKind.Capsule,
                ShapeRadius      = 0.3f,
                ShapeHeight      = 1.8f,
            });
        }
    }
}
```

> Confirm `ITkbDatabase` exposes `TryGetByType(long, out TkbTemplate)` (it does — `TkbDatabase` implements it).
> If `HasDescriptor`/`AddDescriptor`/`TryGetByType` are on `TkbTemplate`/`ITkbDatabase` under different names in the
> current tree, match the actual signatures — do not invent. (`TkbTemplate.HasDescriptor<T>()`,
> `AddDescriptor<T>(T, int=0)` exist per `FDP/Engine/Fdp.Core/Abstractions/TkbTemplate.cs`.)

---

## Task 3 — Bind the Stride view to the editor's DB (hosted path)

**File:** `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs`, in `InitializeHosted`, **replace** the duplicate-DB
block at lines `941-944`:

```csharp
// TkbDb: EditorSubsystem builds its own TkbDb internally. Mirror the same
// UrbanCombat registration so IsAnimatedClass works correctly (template lookup).
TkbDb = new TkbDatabase();
UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(TkbDb);
```

with:

```csharp
// TkbDb UNIFICATION (BATCH-S2-E): bind the Stride view to the editor's authoritative spawn DB
// — the exact instance NetworkSpawningSystem + translators resolve from — instead of a duplicate.
// Then augment its NED platform/infantry templates with Stride render+collision descriptors
// (generic placeholders). UrbanCombat types already carry render-defs (added inside CreateTkb path),
// so we must NOT re-register them here (would throw "already exists").
TkbDb = _editor.TkbDatabase
        ?? throw new InvalidOperationException(
            "[EditorStrideSubsystem] Hosted mode: _editor.TkbDatabase is null after Initialize.");
StrideNedRenderDescriptors.Apply(TkbDb);
```

Leave the `StrideVisualBindingSystem` construction at `:952` unchanged — it now receives the unified DB.

> Verify the editor field is named `_editor` in this method (it is — the method already uses `_editor.World`,
> `_editor.TimeController`, `_editor.EntityCreationRequestSource`). If `TkbDb`'s declared type is `TkbDatabase`
> (it is, `EditorStrideSubsystem.cs:194`) and `_editor.TkbDatabase` is also `TkbDatabase`, the assignment is direct.

---

## Task 4 — Tests (CPU, must be green)

Add focused tests (place beside existing Stride game tests, e.g.
`Stride/HrotStrideApp.Game.Tests/StrideNedRenderDescriptorsTests.cs`):

1. **Augmentation, vehicle:** build a `TkbDatabase`, `NedTkbCatalog.RegisterAll(db)`, call
   `StrideNedRenderDescriptors.Apply(db)`. Assert `db.TryGetByType(100, out var t)` and
   `t.GetDescriptor<StrideRenderModelDefDto>()` is non-null with `ShapeKind == CollisionShapeKind.OrientedBox` and
   `ModelAssetRef == "Models/Box2x1x1"`.
2. **Augmentation, infantry:** type 200 → render-def non-null, `ShapeKind == Capsule`,
   `ModelAssetRef == "Models/mannequinModel"`, `SkeletonAssetRef` non-empty.
3. **Composite/overlay untouched:** type 303 and 8803 → `GetDescriptor<StrideRenderModelDefDto>()` is null (no body).
4. **Idempotent:** calling `Apply` twice does not throw and the type-100 render-def is still exactly one
   OrientedBox def (`HasDescriptor` true, value unchanged).
5. **Translator integration (the real payoff):** register `NedTkbCatalog` + `Apply`, then drive
   `VehicleKinematicsTkbTranslator.Inject` on (a) a type-100 template → injects `VehicleState`/`VehicleParams`
   (isVehicleShaped true via OrientedBox); (b) a type-200 template (give it a `VehicleParametersDto` descriptor so
   `dto != null`) → does **NOT** inject `VehicleState` (Capsule ⇒ isVehicleShaped false). Use a minimal
   `EntityRepository` with the relevant components registered, mirroring the pattern in
   `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TranslatorWiringTests.cs`.

Run the **full** suite with the test-health filter
(`--filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"`). 0 failed. Pre-existing unrelated failure
`FileMenuHasSaveCommands` (noted in tracker) is the only acceptable red — confirm it's pre-existing, don't "fix" it
here.

---

## Out of scope (do NOT touch)
- The edit/preview authority model (`#2` in the tracker) — separate issue.
- The shared `FDP/Toolkits` nav/physics regression audit (`#5`).
- Any change to `NedTkbCatalog` / shared `Hrot.Core` (keep the Stride concern in the Stride assembly).
- Orphan-body cleanup — note in the report whether it still reproduces after unification, but don't implement here.

## Definition of done
- [ ] Tasks 1–3 implemented exactly; additive; SimHost/clusterrunner path untouched.
- [ ] Task 4 tests added and verifying **real values** (descriptor kind/asset + translator component injection).
- [ ] Full filtered suite 0-failed; build 0 warnings introduced.
- [ ] Report at `.dev/stride-2/reports/BATCH-S2-E-REPORT.md` per DEV-GUIDE §4, including: does orphan-body cleanup
      still reproduce? any `using`/type-name adjustments you had to make vs this spec.
