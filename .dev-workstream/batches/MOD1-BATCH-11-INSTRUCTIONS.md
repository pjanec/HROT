# MOD1-BATCH-11: Phase 3 Translator Pack Completion + Component ID Boundary Fixes

**Batch Number:** MOD1-BATCH-11  
**Tasks:** DB-MOD1-22, DB-MOD1-24, MOD1-P3T-PACKS (Phase 3 translator pack factories)  
**Phase:** Phase 3 completion + Component ID boundary cleanup  
**Estimated Effort:** 10-12 hours  
**Priority:** HIGH  
**Dependencies:** MOD1-BATCH-10

---

## 📋 Onboarding & Workflow

### Who You Are
You are a developer implementing the modularization of the IOS-IG-SimHost application. Read this section entirely before touching code.

### Project Goal
Refactoring towards better modularization and generalization. **What should be generic must come under FDP, not be left in the Bagira domain.** This batch closes two long-standing Phase 3 gaps (missing translator pack factories) and fixes a component ID boundary violation that was introduced early in the project and never corrected.

### Non-Negotiable Rules
1. **Application must keep working.** `Bagira.Runner -x all` integration tests must pass after every task.
2. **Tests must check real behaviour** — verify observable outcomes, not call counts.
3. **`FDP.*` assemblies may never reference `Bagira.*` assemblies.**
4. **Component IDs belonging to Bagira-domain components go in `BagiraComponentIds`, never in `GlobalComponentIds`.**
5. **Do not modify third-party submodules** under `FDP\ExtDeps\`.

### Required Reading (IN ORDER)
1. `.dev-workstream/README.md` — developer workflow
2. `docs/modularizing/MOD1-DESIGN.md` — Phase 3 §3.3 (translator composition root pattern) and Phase 5 §3.5 (component ID boundaries)
3. `.dev-workstream/reviews/MOD1-BATCH-10-REVIEW.md` — previous review
4. `docs/modularizing/MOD1-DEBT-TRACKER.md`

### Source Code Locations
- **Component ID fix:** `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs`, `Bagira.Map.Common/Components/IgSymbolOverride.cs`, `Bagira.Map.Common/Replication/` (translator referencing it), `Bagira.IG/` (systems referencing it)
- **Translator packs:** `Bagira.SimHost/Network/` — follow the existing `SharedTranslatorPack` as the template
- **God-class to refactor:** `Bagira.SimHost/SimHostApp.cs` (or wherever `OnLoad` lives)

### Report Submission
`.dev-workstream/reports/MOD1-BATCH-11-REPORT.md`

---

## 🚨 DEBT FIXES (Complete These First)

### DB-MOD1-22: Move `IgSymbolOverride` Component ID Out of `GlobalComponentIds`

**Why this matters:** `GlobalComponentIds` is in `Fdp.Kernel` — the lowest layer of the entire dependency graph. Putting an IG-specific visual override ID there means every project that references `Fdp.Kernel` is contaminated with application-specific knowledge. Phase 5 established `BagiraComponentIds` precisely to hold IDs like this.

**What to do:**
1. Add a new entry in `BagiraComponentIds` (in `Bagira.Map.Definitions` or `Bagira.Map.Common`) for `IgSymbolOverride`. Assign it the next available ID in the Bagira-owned block (160–255).
2. Update `[ComponentId(GlobalComponentIds.IgSymbolOverride)]` → `[ComponentId(BagiraComponentIds.IgSymbolOverride)]` in `Bagira.Map.Common/Components/IgSymbolOverride.cs`.
3. Remove the `IgSymbolOverride` entry from `GlobalComponentIds.cs`. Also remove the `// IDs 67–68 are used for Navigation toolkit components` comment if it has drifted.
4. Run `dotnet build` — fix any compilation errors from other files referencing `GlobalComponentIds.IgSymbolOverride`.
5. **Do NOT change the numeric ID value if this component's state is persisted to disk** (e.g. in replay `.fdp` files). If it is persisted, add a note to the report.

**Success criteria:** `grep -r "GlobalComponentIds.IgSymbolOverride"` returns zero matches in any source file.

---

### DB-MOD1-24: Create `KinematicTranslatorPack` and `CognitiveTranslatorPack`

**Why this matters:** `SimHostApp.OnLoad` currently contains a long, brittle list of `translators.Add(new XxxTranslator(...))` calls. Phase 3 of the design established `SharedTranslatorPack` as a static factory class; the kinematic and cognitive packs were never built. This is a direct maintenance liability — adding or removing a translator requires finding and editing the correct spot inside a large method.

**What to do:**

**Step 1 — Identify which translators belong to each pack.** Grep `SimHostApp.OnLoad` for all `translators.Add(...)` calls. Categorise each translator:
- **Kinematic:** translators that read/write `SimTransform`, `SimVelocity`, `LocomotionChannel`, navigation state, route commands, formation state.
- **Cognitive:** translators that read/write `MissionData`, doctrine commands, `BehaviorTree` activation commands, AI state.
- **Shared:** already in `SharedTranslatorPack` — leave untouched.
- **Other:** translators that don't fit the above three — document in the report and leave in `OnLoad` for now.

**Step 2 — Create the two factory classes** in `Bagira.SimHost.Network`, following the exact same pattern as `SharedTranslatorPack`:
```csharp
// Bagira.SimHost.Network/KinematicTranslatorPack.cs
public static class KinematicTranslatorPack
{
    public static IReadOnlyList<IDescriptorTranslator> Create(DdsParticipant participant, EntityRepository world)
    {
        return new List<IDescriptorTranslator>
        {
            new SimTransformEgressTranslator(participant, world),
            // ... all kinematic translators
        };
    }
}
```

**Step 3 — Replace the manual `translators.Add` calls** in `SimHostApp.OnLoad` with:
```csharp
translators.AddRange(SharedTranslatorPack.Create(participant, world));
translators.AddRange(KinematicTranslatorPack.Create(participant, world));
translators.AddRange(CognitiveTranslatorPack.Create(participant, world));
```

**Step 4 — Write tests** following the same pattern as the existing `TranslatorPackTests`:
- `KinematicTranslatorPack_Create_ReturnsExpectedTranslatorTypes`
- `CognitiveTranslatorPack_Create_ReturnsExpectedTranslatorTypes`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **DB-MOD1-22:** Move `IgSymbolOverride` ID to `BagiraComponentIds` → **`dotnet build` clean, ALL tests pass** ✅
2. **DB-MOD1-24 Step 1:** Audit `SimHostApp.OnLoad`, categorise all translators → document in report ✅
3. **DB-MOD1-24 Step 2:** Create `KinematicTranslatorPack` + `CognitiveTranslatorPack` → **ALL tests pass** ✅
4. **DB-MOD1-24 Step 3:** Replace manual `translators.Add` in `SimHostApp.OnLoad` → **ALL tests pass** ✅
5. **DB-MOD1-24 Step 4:** Write pack tests → **ALL tests pass** ✅
6. **Final:** `Bagira.Runner -x all` integration tests pass unconditionally ✅

---

## 📊 Report Requirements

`.dev-workstream/reports/MOD1-BATCH-11-REPORT.md`

**Developer Insights**

**Q1:** For DB-MOD1-22 — is `IgSymbolOverride` component state persisted to `.fdp` replay files? If yes, what is the consequence of changing its component ID?

**Q2:** For DB-MOD1-24 — how many translators remain in `SimHostApp.OnLoad` after the refactor (i.e. did not fit neatly into Kinematic, Cognitive, or Shared)? List them and explain why they don't belong in any of the three packs.

**Q3:** Were there any translators that needed parameters (constructor arguments) that made them awkward to move into a static factory? How did you handle them?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `grep -r "GlobalComponentIds.IgSymbolOverride" --include="*.cs"` returns zero matches.
- [ ] `IgSymbolOverride` uses `BagiraComponentIds.IgSymbolOverride` for its component ID.
- [ ] `KinematicTranslatorPack` and `CognitiveTranslatorPack` exist in `Bagira.SimHost.Network`.
- [ ] `SimHostApp.OnLoad` uses `AddRange` calls to the three packs instead of individual `translators.Add(new ...)` for kinematic and cognitive translators.
- [ ] Pack creation is covered by at least 2 new unit tests (one per pack).
- [ ] `Bagira.Runner -x all` integration tests pass unconditionally.
- [ ] All unit and integration test suites pass with 0 failures.
