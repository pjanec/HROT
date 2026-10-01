<!--STATUS
state: LIVE — DISPATCHED at dba2233c4 (2026-09-30, user: "lets go implementing the handoff"); scope frozen there
updated: 2026-09-30 (tuned in the UI lane: §2 intent→hostings rows, ③ rewritten on two user rulings, D1 picker shape, D7 added)
current-answer: the whole file — a FRAME handoff (goal, fences, decisions with leans, acceptance). The UI lane designs the
  details (inventory, UML, seams) in its own docs/ design as step 1.
stale-below: nothing.
known-rot: none.
known-conflict: UXR-40's wording ("asks what the behavior should do, NOT which of three graph technologies") vs the user's
  2026-09-30 ruling ("the technology is a secondary choice") — reconciled in decision D3, not silently.
related-designs:
  - docs/blueprints/Architect_Question_77_Blueprint_As_A_Behaviour.md — §5.5 (E4 re-framed) and §5.13 (first slice built);
    owns the blueprint-behaviour runtime/compiler this UI surfaces.
  - docs/UX/UX_Requirements.md — UXR-40 (one New Behaviour entry), UXR-41 (assignable without restart).
  - docs/UX/UX_Design.md — UXD-03 (behaviour as a first-class concept; OPEN → Q25-C), owns affinity — OUT of scope here.
-->

# HANDOFF — E4: author by PRODUCT first, technology second

**For:** the UI lane. **From:** the behaviours lane (`behaviors`). **Status:** ⭐ **Dispatched at `dba2233c4`** — the scope is frozen there; later documents are FYI only.
The detail design is [`DESIGN_Product_First_Authoring.md`](../DESIGN_Product_First_Authoring.md) (ids `CE-460`–`CE-462`).

## 1. Goal — and the user's words

> 🔒 *"User adds certain product features/building blocks like behaviors, conditions, actions and the technology is a
> secondary choice … I need to be able to author new condition (and likely select what technology — blueprint, …), and
> when picking conditions i need to see all available ones no matter what technology they are based on (hardcoded,
> blueprint ai primitive…). Same with other types of stuff like actions or behaviors."* · *"the 'New asset' is still a valid
> option. 'New Behaviour / New Action / New Condition' is additive."* (2026-09-30)

⇒ A game-AI author creates a **Behaviour / Action / Condition**, *then* picks how it is implemented. Every picker lists
**all** of that kind, whatever the technology, with the technology shown as a label.

## 2. What exists — measured `2026-09-30` *(verify before building — this is a state doc)*

| surface | today | file |
|---|---|---|
| New Asset | technology-first: kinds Blueprint / BTree / Hsm via `NewAssetLauncher` → per-kind `INewAssetService` | `Hrot/Editor/Hrot.Editor.AiShared/Browser/NewAssetLauncher.cs` |
| Blueprint blank templates | two rows: **Empty** (Instance, seed graph `Tick`) and **Function Library**; ⛔ **AiPrimitive deliberately not offered** (*"needs a Primitive declaration and hostings that this flow does not populate"*) · ⛔ no **Behaviour** row | `Hrot.Blueprints.Editor/BlueprintNewAssetService.cs:28-54` |
| action / condition pickers | ✅ **already merge technologies** — they read one list from `ActionSchemaExporter` | `Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs` |
| behaviour assignment picker | ✅ **lists BTree + HSM + Blueprint** (built by the behaviours lane, `CE-446` E4 first slice); ⛔ names only — no technology label | `Hrot/Engine/Hrot.Presentation/Adapters/ScenarioMissionService.cs` (`AppendEditorBTreeBehaviors`; the name is now too narrow, and a rename should go through Roslyn) |
| its contract | `IReadOnlyList<string> GetAvailableBehaviors(long entityId)`. Production implementations: `ScenarioMissionService` (Presentation) and `MissionEditorService` + `ExConMissionShim` (ExCon); 3 test doubles | `Hrot/Subsystems/Hrot.ExCon/Services/IMissionEditorService.cs:18` |
| **intent → hostings** | ✅ **one table, in the compiler**: `ActionHostings = {BTreeAction, HsmAction}` · `ConditionHostings = {BTreeCondition, HsmGuard}`, enforced by **BP1022/BP1023**; `BlueprintCall` is in neither ⇒ intent-neutral. ⚠ the arrays are `private` | `Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs:102-105, :154-166` |
| multi-hosting | ✅ a primitive has ONE intent and a LIST of hostings; the emitter writes **one thunk per hosting** around the same graph. Shipped: `MoveAndFireCombo.bp.json` = `[BTreeAction, HsmAction]` | `BlueprintAsset.cs` (`AiPrimitiveDecl`) · `AiPrimitiveEmitter.cs:~295-320` |
| a blueprint BEHAVIOUR | ✅ runtime + compiler built: `BlueprintDispatchKind.Behavior`, a `Tick` graph whose `Return` finishes it, its own resolver (Construction graph), hot reload. Shipped example: `Hrot.AI.Behaviors/Assets/Blueprints/BlueprintBehaviourDemo.bp.json` | Q77 §5.9–§5.12 |

## 3. The items

| # | item | lean |
|---|---|---|
| **①** | **New Behaviour… / New Action… / New Condition…** entries, **added beside** New Asset (which stays unchanged) | one entry per PRODUCT; step 2 is the technology choice (D2) |
| **②** | New Behaviour → **BTree / HSM / Blueprint**. Blueprint needs a new blank-template row: `Dispatch = Behavior`, seed graph `Tick` | ⭐ reuse `BlueprintNewAssetService`'s table and the existing BTree/HSM services — ⛔ no second creation path |
| **③** | New Action / New Condition → **Blueprint** (an AiPrimitive recipe) or **C#**. 🔒 **User, 2026-09-30:** *"actions should be usable for btrees/hsms and blueprint behaviors"* · *"same for blueprint conditions (usable as btree conditions AND hsm guards)"* ⇒ ⭐ **the recipe declares EVERY hosting valid for its intent** — Action → `[BTreeAction, HsmAction]`, Condition → `[BTreeCondition, HsmGuard]` (+ `BlueprintCall`, intent-neutral) — and **the host is NOT a picker level**; narrowing is an asset property (Details). ⛔ An earlier draft of this row said *"action → `BTreeAction`"*: it mirrored shipped USAGE (all but one primitive is single-hosted BTree), not the model | the AiPrimitive row is the real work: it must populate the Primitive declaration + hosting the current comment says the flow cannot. Measure what a minimal valid AiPrimitive needs (the compiler's validators say) before designing it |
| **④** | **Technology label** in every picker (behaviour / action / condition) | action/condition: the exporter already knows the source — add the label. Behaviour: D4 |
| **⑤** | a newly authored item **appears without a restart** (UXR-41) | check it end to end: author → compile/reload → it shows in the picker → assign → it runs |

## 4. Decisions for the session — each with a lean *(argue any of them)*

| | question | lean | why / what would change it |
|---|---|---|---|
| **D1** | where do the entries live? | ⭐ **ONE tree picker, the one New Asset already uses** — measured: it is a Tree `PickerRequest` whose path is `Kind/Sub/recipe` (`RecipePickerSource.cs:150`) and `PickerTreeBuilder.cs:43` splits on `/` to any depth ⇒ product-first is just a different PATH: `Behaviour/BTree`, `Behaviour/Blueprint`, `Action/Blueprint`, `Condition/Blueprint`, `…/C#` (disabled). The menu items *New Behaviour… / New Action… / New Condition…* each open THAT tree rooted at their product; *New Asset…* keeps the technology-first path. ⚠ **Single-recipe folders collapse to a leaf.** ⚠ `RecipePickerSource` hard-builds `Kind/Sub` today — **that is the one real seam change** | one launcher, one discoverability point; changes if the UI lane finds a better product-level home (e.g. the Behaviours perspective) |
| **D2** | how is the technology chosen? | a second step in the same dialog, with plain-language descriptions and a default (Behaviour → BTree; Action/Condition → Blueprint) | fewest clicks; the default serves the author who doesn't care |
| **D3** | UXR-40 says *"not which of three technologies"* — the user now says *"technology is a secondary choice"* | **product first, technology second, with a default** — satisfies both: nobody must understand the technologies to start | the user ruling is newer and narrower; ⚠ confirm with the user, and note the reconciliation in UX_Requirements |
| **D4** | behaviour label: how does the picker learn the technology? | change the contract to return `(Name, Technology)` — a small record — across the 3 production implementations + 3 test doubles | ⛔ a parallel "technology of name X" call is a second lookup to keep in sync; `BehaviorDefinition.BrainTier` already holds the answer |
| **D5** | "C#" for New Action / Condition | show it, disabled, with one line on where hand-written actions live — so the menu tells the truth about what exists | changes if the user prefers not to show what the editor cannot create |
| **D7** | where does the recipe get the intent→hostings table? | ⭐ **read the compiler's** (`Stage2_Validate` `ActionHostings`/`ConditionHostings`) — ⛔ a second copy in the editor is two implementations of one rule | ⚠ they are `private`: exposing them is a compiler change ⇒ **per §5's fence, the behaviours lane makes it**. Changes only if that lane prefers a different home for the table |
| **D6** | affinity (which entity types may use a behaviour) | ⛔ **out of scope** — UXD-03 / Q25-C are OPEN; the list stays ungated, as today | — |

## 5. Fences

| ✅ UI lane owns | ⛔ not in this batch |
|---|---|
| editor menus, dialogs, pickers, `BlueprintNewAssetService` recipes, the `IMissionEditorService` contract change | runtime (`Fdp.Toolkits` behaviour systems), the blueprint compiler — ⭐ if a recipe needs a compiler change, **STOP and report** to the behaviours lane |
| | affinity / UXD-03 |

⚠ **Touch-points shared with the behaviours lane:** `ScenarioMissionService.cs`, which that lane edited on `2026-09-30`
(sync from `coordinator` first), and `Hrot.ExCon` (the contract).

## 6. Step 1 — the session designs the detail *(the FRAME rule)*

Investigate → write the design **in `docs/`**, with an `INVENTORY` (`search_graph` for every New-asset / picker surface),
a `classDiagram`, a `sequenceDiagram` and a module diagram → build → fold the as-built back. ⭐ The inventory must list
**every** surface that creates or picks a behaviour, action or condition, so the new entries REUSE rather than duplicate
them (the seam law: a "new shared X" usually already exists). ⭐ Allocate your own ids.

## 7. Acceptance shape

- ① From the editor: **New Behaviour → Blueprint** opens a behaviour blueprint that compiles; after a reload it is in the
  assignment picker **labelled "Blueprint"**, and assigning it runs it — **no restart**.
- ② The same for **New Behaviour → BTree / HSM**, and for **New Action / New Condition → Blueprint** (it appears in the
  BTree/HSM action/condition pickers, labelled).
- ③ New Asset behaves exactly as before.
- ④ Rails in each feature's OWN suite (`NewAssetLauncherTests`, `ActionSchemaExporterTests`, the mission-service tests —
  e.g. `EditorMissionServiceTests.CE446_GetAvailableBehaviors_ListsEveryTechnology`), each red-proofed.

## 8. Design basis — read before designing

`Architect_Question_77_Blueprint_As_A_Behaviour.md` §5.5, §5.9–§5.13 · `docs/UX/UX_Requirements.md` UXR-40/41 ·
`docs/UX/UX_Design.md` UXD-03 · `Architect_Question_25_Scenario_Authoring_Golden_Path.md` Q25-C (`BehaviorRegistry` as the
single source) · `BlueprintNewAssetService.cs` header (why AiPrimitive was left out).
