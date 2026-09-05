<!--STATUS
state: LIVE
updated: 2026-09-02
current-answer: §2 is the task. §1 is what to know before starting.
stale-below: nothing.
note: EPHEMERAL verification handoff. It carries NO design content and NO UML —
  the design is docs/DESIGN_Entity_Creation_Unification.md §3, §3.3, which this
  only points at. Delete or archive once the Stride verification is reported.
-->

# ⭐ HANDOFF — **verify host (e), the Stride editor, on WINDOWS**

**Branch:** `claude/reset-working-branch-qd1qpv` · **Commit to verify:** `7a64572bf`
**Owner of the change:** the Linux session that could not build `Stride/`.

---

## 1. ⭐ What landed, and why it is unverified

`CE-146` folded the Stride editor's **second, hand-assembled entity-creation pipeline** into the
shared `EntityCreationPack`. It is the sixth and last of `CE-140`'s six composition roots.

⛔⛔ **The whole edit is UNCOMPILED.** `Stride/` needs `Microsoft.WindowsDesktop.App`, which the
authoring session did not have. Every claim below is *reasoned*, not *measured*.

| what changed in `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` | |
|---|---|
| `:592-635` | the hand-built translators / ELM / `NetworkSpawningSystem` / `CreateEntityRequestSystem` block became **one `EntityCreationPack.Build(…)`** |
| `:668-682` | `creation.FinalizationSystem` is registered — ⭐ **NEW to this host**, it never had an ACK path — plus an `Unserviceable(…)` warn |
| `:1656` | `BuildTranslators()` **retired** |
| `:33` | added `using Hrot.Common.EntityCreation;` |

⭐ **`CE-145` rode along.** `InfantryVehicleStateStripTkbTranslator` must run *immediately after*
`VehicleKinematicsTkbTranslator` — *"position in the list is the guarantee"*. `BasePlus` **appends**,
so the pack could not express it. New shared API: `TkbTranslatorSet.BaseWith` + `TranslatorPlacement`,
surfaced as `EntityCreationContext.TranslatorPlacements`. The anchor is a **TYPE, not an index**, and a
missing anchor **THROWS**. 🔒 `R-137` — unification puts a lost capability back as configuration.

---

## 2. ⭐⭐⭐ THE TASK

### ⓐ BUILD — the only thing that actually settles this

```powershell
dotnet build Stride\HrotStrideApp.sln
```

⛔ **Do not "fix" a compile error by reverting to `BuildTranslators()`.** The pack adoption is the
deliverable. Fix the call site, or — if the pack's shape is genuinely wrong for this host — **STOP and
report which member does not fit**, so the shared code is corrected rather than this one host exempted.

⭐ **The four things most likely to be wrong**, in the order I would check them:

1. **`Log.Warn(...)`** at `:683`. The file uses `NLog`, and I picked `Warn` after finding `Log.Warning`
   appears nowhere else in `Stride/`. If NLog's signature differs, fix the call, not the logic.
2. **`creation.Unserviceable(...)` returns a `string`, not a collection.** I use `.Length > 0`. If it
   were `IEnumerable<string>`, `.Length` will not compile.
3. **`TranslatorPlacements = new[] { … }`** — a `TranslatorPlacement[]` assigned to an
   `IReadOnlyList<TranslatorPlacement>?`. Fine in principle; watch for an inference surprise.
4. **`World` / `EntityMap` / `EditorNodeId` are all assigned before `:614`** — verified by line number
   on Linux, but worth a glance.

### ⓑ RUN the Stride test projects

```powershell
dotnet test Stride\Hrot.Stride.Core.Tests\Hrot.Stride.Core.Tests.csproj      --no-build
dotnet test Stride\Hrot.Stride.Animation.Tests\Hrot.Stride.Animation.Tests.csproj --no-build
dotnet test Stride\HrotStrideApp.Game.Tests\HrotStrideApp.Game.Tests.csproj  --no-build
```

⭐⭐ **Report counts, and confirm every red is PRE-EXISTING against `2b0a703b3`** (the commit before
this one). ⛔ Do not attribute a red to this change without that comparison — 📌 on Linux a red in
`Hrot.Editor.Tests` looked exactly like a regression and turned out to be a rotating flake that needed
**four runs on each side** to characterise.

### ⓒ THE `StrD21` NAVIGATION REDS — **the row that has been waiting on you**

📄 `Stride/HrotStrideApp.Game.Tests/StrD21NavigationFixTests.cs`. Two of its reds are recorded as
**UNATTRIBUTED** in [`BOOTSTRAP_Entity_Creation_Session.md`](../BOOTSTRAP_Entity_Creation_Session.md)
§4, explicitly parked *"until host (e) is done and they are re-run"*. Host (e) is now done.

⇒ ⭐ **Re-run them and settle it:** are they pre-existing, or did the pack adoption touch them?
`VehicleNavIntentSystem_WritesVehicleState_OnFirstTick_WithFakeNavmesh` and
`VehicleNavIntentSystem_AdvancesCorners_AcrossMultipleTicks` are the plausible candidates —
⚠ **they read `VehicleState`, which is exactly what the strip removes**, so `CE-145`'s ordering fix is
the one change that could move them. ⭐ **That makes this the highest-information check in the handoff.**

### ⓓ RUNTIME smoke — ⭐ optional but cheap, and it is the only end-to-end evidence

```powershell
$env:STRIDE_SELFTEST=1
dotnet run --project Stride\HrotStrideApp.Windows\HrotStrideApp.Windows.csproj
```

⭐ **What to look for in the log:**

| ✅ expected | ⛔ a finding |
|---|---|
| entities spawn from the scenario as before | `[EntityCreation] …` warn ⇒ a pack piece was constructed and never scheduled |
| no `InvalidOperationException` at startup | `TkbTranslatorSet.BaseWith: cannot place …` ⇒ the anchor type is missing from `Base()` |

---

## 3. ⭐ REPORT BACK

| # | say this |
|---|---|
| ① | **did `Stride\HrotStrideApp.sln` build** — and if not, the exact `error CS…` lines and what you changed |
| ② | per test project: **passed / failed / skipped**, and the **base-commit comparison** for every red |
| ③ | ⭐⭐ **the `StrD21` verdict** — pre-existing, fixed, or newly broken. This closes a parked row either way |
| ④ | the self-test outcome, if you ran it |
| ⑤ | ⛔ **any place the pack's shape did not fit this host** — that is a defect in the SHARED code, not a licence to exempt the host |

⭐ **Then update, in the same push:**
- [`BOOTSTRAP_Entity_Creation_Session.md`](../BOOTSTRAP_Entity_Creation_Session.md) §3 host table — (e)'s
  *"UNVERIFIED BY BUILD"* caveat, and §4's `StrD21` row
- [`Blueprint_Issues_Tracker.md`](../Blueprint_Issues_Tracker.md) — the `CE-146` row's same caveat

⛔ **Do NOT start host (d) CGF from this session** — that is the next step in the same lane and belongs
to whoever is carrying P1, or the two will collide.
