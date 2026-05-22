# BATCH-07: Tech Debt Fix — Events Section CanCreate

**Batch Number:** BATCH-07  
**Tasks:** DEBT TD-001 (P2)  
**Phase:** Debt Resolution  
**Estimated Effort:** 0.25 hours  
**Priority:** P2  
**Dependencies:** BATCH-06 (completed)

---

## 📋 Onboarding

This is a minimal debt-fix batch. Only one change is needed.

**Do not stop or ask for permission. Fix, build, test, report.**

---

## ✅ Task: Fix Events Section CanCreate

**File:** `src/NodeEditor.Demo/FakeBlueprint/FakeMyBlueprintModel.cs` (MODIFY)

In `Sections`, the `"events"` entry has `CanCreate = false`. S17's Description
tells users to "Click '+' next to 'Events'", but the '+' button is hidden when
`CanCreate = false`. Fix: change to `true`.

**Current:**
```csharp
new("events",      "Events",      4, null, true,  false, null),
```

**Change to:**
```csharp
new("events",      "Events",      4, null, true,  true,  null),
```

---

## 🧪 Verification

```powershell
dotnet build "d:\Work\IOS-IG-SimHost-FDP-2\FDP\ExtDeps\NodeEdit\NodeEditor.sln" -v quiet
dotnet test  "d:\Work\IOS-IG-SimHost-FDP-2\FDP\ExtDeps\NodeEdit\NodeEditor.sln" --no-build -v quiet
```

Expected: **0 warnings, 0 errors; 67/67 tests pass**.

---

## 📊 Report

Submit to: `.dev/final/reports/BATCH-07-REPORT.md`

Include: the one-line diff, build result, test result, and a one-line commit message.

---

## 🎯 Success Criteria

- [ ] `events` section has `CanCreate = true` in `FakeMyBlueprintModel`
- [ ] Build: 0 warnings, 0 errors
- [ ] Tests: 67/67 pass
- [ ] Report submitted
