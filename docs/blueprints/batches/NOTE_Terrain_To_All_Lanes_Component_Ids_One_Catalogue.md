<!--STATUS
state: LIVE — a cross-lane convention (R-5069), FYI to every lane · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - NOTE_Ui_To_All_Lanes_Component_Ids_Near_Full.md — the count this corrects
  - NOTE_Backend_To_All_Lanes_Component_Id_Space.md — R-2056, the lane blocks (unchanged)
-->
# NOTE terrain → every lane: component ids — ONE catalogue, and the space is NOT near-full (R-5069)

🔒 **User, `2026-11-19`:** *"Why everyone says component ids are nearly used up when there are around 100 free out of 512?"* — then *"Shouldn't all component ids
have a symbolic constant? Apart from test components. And all written at one single place?"* — *"yes go ahead, both."*

## ① The count — measured by a rail, never by hand again

`ComponentIdCatalogueTests` (Fdp.Core.Tests) reads every production `[ComponentId(…)]` and prints the space:

```bash
dotnet test FDP/Engine/Fdp.Core.Tests --filter ComponentIdCatalogueTests --logger "console;verbosity=detailed"
```

| measured `2026-11-19` (terrain, after merging coordinator + ui) | |
|---|---|
| production component ids in use | **380** |
| ⭐ **FREE in 0–495** (no component, no name) | **94** — 8–19 · 72 · 86–108 · 158 · 171 · 174–176 · 193–199 · 217–219 · 228–229 · 231–236 · 241–250 · 252–255 · 266 · 280 · 289 · 292–299 · 320–322 · 327–329 · 457–459 · 492 |
| named but unused (retired / reserved — reuse only deliberately) | 26 — e.g. 2 `HealthData`, 161 `EntityDamage`, 165 `RetiredPlayerHealthState`, the `_RESERVED` brain ids, the fake-backend ids |
| held back | 496–511 (the mask; 500–511 the test-only range) |

⛔ **Why "only 8 free" was wrong:** `NOTE_Ui_To_All_Lanes_Component_Ids_Near_Full.md` counted **test-only structs** (~107 ids that only a test suite
registers — a test struct may sit on a production-free id) and **lane reservations** as taken. The second time this happened (CE-4670, `2026-11-18`, found
the same overcount). ⚠ A reservation that exists only as prose (convoy 108 / 158, agg-ui 327–329, traffic 457–459) still reads FREE here — ⭐ make a
reservation a named constant in the catalogue (`Reserved_Convoy3 = 108`) and it moves to "named but unused".

## ② ONE catalogue — `Fdp.Core.GlobalComponentIds`

| ⭐ the rule (R-5069) | |
|---|---|
| ⭐⭐ every PRODUCTION component's id is a constant of **`GlobalComponentIds`**, and its `[ComponentId(…)]` names it | ⛔ never a literal, never another class. Test / benchmark / example structs are exempt |
| the old side catalogues — `HrotComponentIds`, `NavigationContractsComponentIds`, `BehaviorApplicationComponentIds`, `GeographicComponentIds`, `UtilityApplicationComponentIds`, `PerceptionApplicationComponentIds`, `ReplayComponentIds`, `ScenarioComponentIds`, `NavFakeIds` | ⭐ now **ALIASES** (`= GlobalComponentIds.X`) — your code that reads them still compiles; ⛔ add nothing to them |
| ⛔ **no id changed** | 47 constants moved, values kept: recordings and the wire are untouched |
| the rail | ① a production attribute outside the catalogue ⇒ red, naming the file and line; ② one id on two production components ⇒ red; ③ prints the free ids |

**Your files changed** (one-line attribute edits, a merge conflict keeps both sides then take `GlobalComponentIds.X`): ~35 component files across
Fdp.Toolkits (Geographic, Utility, Behavior, Navigation, Replay, Perception, Scenario), Hrot.Core, Hrot.Common, Hrot.CGF, Hrot.AI.Behaviors (the five
`Bp…Demo`), plus the nine side catalogues. ⭐ A NEW id from now on: add `public const int YourComponent = n;` to `GlobalComponentIds` from your lane's
block (R-2056 unchanged), measure free with the command above.

## ③ One defect it found
`GlobalComponentIds.PerceptionReceptor` said **72** while the `PerceptionReceptor` struct registered **251** (a literal) — the catalogue was wrong for as long as
nobody read the constant. ⭐ Corrected to 251 (what recordings carry); 72 is free.
