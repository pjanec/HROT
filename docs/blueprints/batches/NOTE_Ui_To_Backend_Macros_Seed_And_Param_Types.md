<!--STATUS
state: LIVE — ui → backend: two findings from CE-4210 P1 (macro authoring)
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Macro_Authoring_Editor.md — §6 AS BUILT P1 (MA-4 header fix, MA-3 ParamTypes)
  - ../../DESIGN_Scenario_Macros.md — §14.1 ① (macros/ through the one resolver), SR-162 (the validator's type half)
-->
# NOTE — ui → backend: **macros/ is never seeded · `ParamTypes` for SR-162**

Found while building [CE-4210](../Blueprint_Issues_Tracker.md) (the editor's macro authoring) on `ui`. Both items are yours to decide. (The `EditorHarness` feature gate ui also found is already fixed on `backend`, `3b9ed7a20`.)

| # | finding (measured) | ⭐ lean | why it is yours |
|---|---|---|---|
| ① | 🔴 **a dev start never puts `macros/` on the NAS.** `ContentLayout.SeededDirectories` = `scenarios`, `assets`, `recipes` (`Fdp.Toolkits/Orchestration/ContentLayout.cs:77`); `ContentSeed` copies only those. So an EDIT load of `macros/test/fire-team` resolves to `{nas}/macros/test/fire-team` (the resolver arm exists) and finds NOTHING — silently, an empty scenario. ⚠ `StageMacros` (prefetch) also stages from the NAS, so a macro authored in the repo reaches a node only through `FileMacroLibrary`'s content-source fallback | add `MacrosDirectoryName` to `SeededDirectories`. ⚠ Check `CuratedScenarios` first (`Hrot.Presentation/ScenarioEditor/Services/CuratedScenarios.cs:64` iterates the same list) — a macro must not appear as a curated scenario. ⭐ And an edit load of a name that resolves to no folder should FAIL loudly, not load empty | `ContentSeed` / `ContentLayout` / the load path are backend's |
| ② | `ParamTypes` now exists on `ui` (`Fdp.Toolkits/Scenario/ParamTypes.cs`, CE-4210 MA-3): `TryResolve(typeId)`, `Fits(typeId, Type)`, both spellings (`"float"` and `"System.Single"`), enums by registered name (`ForceId` pre-registered). `MacroValidator` still reports a param's TYPE against its target only at expansion (DESIGN_Scenario_Macros §15.2, *"SR-162's second half"*) | the validator uses `ParamTypes.Fits` in the PREPARE, so a wrong-typed binding aborts the load with every problem listed, like the other SR-162 checks | `MacroValidator` is backend's file |

_ui lane, 2026-11-02_
