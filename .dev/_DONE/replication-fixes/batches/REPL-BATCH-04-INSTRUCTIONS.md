# BATCH-04: Architectural Cleanups & Def Fixes

**Batch Number:** REPL-BATCH-04
**Tasks:** REPL-C03, REPL-C04, REPL-C05, GhostPromotion-Crash-Fix
**Phase:** Corrective Architecture Phase
**Estimated Effort:** ~5 hours
**Priority:** CRITICAL
**Dependencies:** REPL-BATCH-03 (Rejected)

---

## 📋 Onboarding & Workflow

### Developer Instructions
BATCH-03 successfully migrated the translators and implemented the Phase 4 test harnessing. However, the batch **failed** architectural review because the Ghost promotion pipeline was stalled, the global test suites (`dotnet test`) were not run (thus missing a critical `TkbType 0` crash in `GhostPromotionSystem`), and Data-Oriented Component (ECS) definitions were used improperly as runtime components. 

In this fourth batch, you will clean up these architectural holes. 

**This batch comes with EXPLICIT autonomy instructions:**
You are to work autonomously until **ALL DONE**. The entire solution MUST compile cleanly. Do not ask me for permission to start a build or run tests—you are an intelligent developer. Use your intelligence to decide your steps autonomously, execute `dotnet build`, execute `dotnet test`, verify your logic natively by clicking "Spawn" in the Runner UI, and *only* submit a report once everything is successful.

### Required Reading (IN ORDER)
1. **Previous Review:** `.dev-workstream/reviews/REPL-BATCH-03-REVIEW.md` - Learn from your rejection!

### Source Code Location
- **Primary Work Area:** `FDP.Toolkit.Replication/Systems/GhostPromotionSystem.cs`, `Hrot.IG/IgApplication.cs`, `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs`.

### Report Submission
**When done, submit your report to:**
`.dev-workstream/reports/REPL-BATCH-04-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests.**

1. **Task 1:** Implement → Compile → **Verify/Run Tests** ✅
2. **Task 2:** Implement → Compile → **Verify/Run Tests** ✅
3. **Task 3:** Implement → Compile → **Verify/Run Tests** ✅

---

## ✅ Tasks

### Task 1: Fix `GhostPromotionSystem` TkbType 0 Crash
**Problem:** `GhostPromotionSystem` currently calls `_tkbDatabase.GetTemplate(spawnReq.TkbType)` which throws a `KeyNotFoundException` if a Ghost enters the queue with a default `TkbType = 0` (e.g., when a Ghost receives a `WorldPos` packet first and is waiting for `EntityMaster`). This crashes the entire kernel schedule!
**Action:** Update `PromoteGhost` to safely process templates. Use `_tkbDatabase.TryGetByType(...)` and simply `return;` (leaving it as a Ghost) if the TKB hasn't resolved yet. 

### Task 2: Fix zero-participant stalling in EntityLifecycleModule
**Problem:** You successfully connected `GhostPromotionSystem` to call `_lifecycleModule.BeginConstruction`! However, the IG app configures the `EntityLifecycleModule` with 0 global participants (`Array.Empty<int>()`). When `BeginConstruction` checks required ACKs for 0 participants, it leaves the entity in `_pendingConstruction` forever because no module will ever reply with an ACK. Because `EntityQuery` defaults to `Alive` entities, the Ghost entity gets permanently marooned.
**Action:** 
Fix `FDP/Toolkits/FDP.Toolkit.Lifecycle/EntityLifecycleModule.cs`: inside `BeginConstruction` and `BeginDestruction`, explicitly verify if `participants.Count == 0`. If so, bypass the pending queue and IMMEDIATELY promote the entity to `EntityLifecycle.Active` (or call `DestroyEntity` for destruction), ensuring no entity gets stuck indefinitely just because the environment lacks modules that care to acknowledge them.

### Task 3: Remove "Ghost-Only" Assumptions from Ingress Translators (REPL-C04)
**Files:** `Hrot.Map.Common/Replication/Ingress/*.cs` (Migrated in BATCH-02)
**Action:** The developer previously added comments and logic assuming "IG is a ghost-only node". Purge all comments stating "IG is a ghost-only node" and ensure no runtime logic makes this specific assumption simply because these are Ingress translators.

### Task 4: Data-Oriented Component Initialization (REPL-C05)
**Problem:** In `BdcTkbBuilder.cs`, prototype definitions like `SimVehicleDef` and `IgVisualDef` are lazily added as managed components (`AddManagedComponent`) instead of being eagerly mapped to pure runtime structs as strict ECS design requires. 
**Action:** 
1. Convert `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs` methods to map `SimVehicleDef` and `IgVisualDef` to unmanaged FDP structs (`VehicleParams` from `CarKinem`, and a new `VisualData` struct). Add these runtime structs into the TKB instead, then discard the `Def` objects!
2. Create the `VisualData` unmanaged struct (with `ComponentId`) to hold `FixedString` paths for `ModelPath` and `SymbolCode`. Update `Hrot.IG/Systems/StyleResolutionSystem.cs` to query this unmanaged `VisualData` rather than `IgVisualDef`.
3. Remove the `[ComponentId(...)]` attribute entirely from the POCO definitions (`SimVehicleDef`, `IgVisualDef`), ensuring they are never placed directly into the ECS.
4. Clean up `GlobalComponentIds.cs` to free the old IDs (you may re-use one for `VisualData`).

---

## ⚠️ Quality Standards

**❗ CODE QUALITY EXPECTATIONS**
- **REQUIRED:** You MUST check `dotnet build` from root.
- **REQUIRED:** Check `dotnet test` across the *entire* solution to guarantee no background systems crashed.

**❗ REPORT QUALITY EXPECTATIONS**
- Supply evidence of fixing the TkbType exception.
- Provide evidence the ID pool exhaustion is solved and IG natively displays Spawned entities from Runner.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TkbType 0 error resolved and `test` passes cleanly.
- [ ] REPL-C03 (ELM Bug) implemented so `BeginConstruction` doesn't stall for empty modules.
- [ ] REPL-C04 implemented removing false comments.
- [ ] REPL-C05 implemented making architecture pure ECS data-driven.
- [ ] Report submitted documenting outcomes.
