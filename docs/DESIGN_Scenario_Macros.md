<!--STATUS
state: LIVE
build-state: ⏸ ON HOLD (user, 2026-11-02 — not to be started yet; reasons to follow) · §14 (SR-167…SR-176) ✅ accepted 2026-11-02 — READY-TO-BUILD after S0 + CE-4153 S9′ (accepted 2026-11-01: no reverse remap — authored refs are EntityLink, a rail, the migration writes GUIDs; DESIGN_Scenario_Entity_Keys.md S9′) — SR-146…SR-157 ✅ accepted 2026-11-01 (§9.y: the expander is the creation path's child branch) — slice S3 (§1); CE-4153 S9 (GUID file form) still a prerequisite
updated: 2026-11-01 (split out of docs/DESIGN_Scenario_Rules.md §8 / §9.1–§9.7 — a MOVE: every decision id, its wording, its approval and every user quote kept verbatim; no id renumbered)
current-answer: ⭐ §14.3 placement as a solver query + many instances (SR-184…SR-188, leans await the user) · §14.2 the user's answers (SR-177 withdrawn, SR-178 ✅, SR-179 superseded) + PLACEMENT (SR-180…SR-183 ✅) · §14.1 the four preconditions measured · §14 the critical review (SR-167…SR-176, ✅ accepted 2026-11-02) · §5.1 where a binding lives (SR-159…SR-166, ✅ accepted 2026-11-02 — §5.1.1 the example, §5.1.2 the generic binder + recording) · §10 relocation (SR-127, SR-131, SR-152…SR-157) · §9.y the gaps closed (SR-146…SR-151) · §0 what a macro is + SR-144 (the founding decision) · §1 the slice (S3) · §2 INVENTORY · §3 the model (class diagram) · §4 params,
  presets and their overrides (SR-27, SR-28, SR-32) · §5 the macro file, the instance, its bindings (SR-38…SR-43, SR-57, SR-69, SR-70) · §6 macro
  entities are full TRANSIENT entities (SR-42′, SR-44, SR-44a, SR-45) · §7 a macro is CLOSED (SR-58, SR-60) · §8 the three reference forms and
  RefKey (SR-63…SR-66, SR-68) · §9 expansion at load — the sequence, SR-80, GAP-1…GAP-5 · §10 relocation + the reference types (still in the
  rules doc §9.8) · §11 the rows that stayed in the rules doc · §12 retired ids · §13 where the old section numbers went
stale-below: nothing
known-rot: ⚠ the moved rows keep their ORIGINAL section references (§8.0, §9.3, §9.7 …) verbatim — those are DESIGN_Scenario_Rules.md's old
  numbers; §13 maps each to its section here (the rules doc's stubs forward them too). Every related design links back (2026-11-01).
known-conflict: none
related-designs:
  - docs/DESIGN_Scenario_Rules.md — the RULES (objectives, triggers, the director, evaluation): the first USER of macros (`InsertMacro`, a director IS a macro — SR-99, a macro insertion reports its own completion — SR-96, bindings in a `do` entry — SR-94); still OWNS §9.8 relocation + the reference / place / orientation types until slice S0 lands, and the objective rows of its §8 / §9 (§11 here)
  - docs/DESIGN_Scenario_Entity_Keys.md — CE-4153 rev 2: STABLE entity GUIDs, derived for macro-made entities — the cross-scope reference (SR-62); its S9 (internal refs of saved components as GUIDs) is S3's prerequisite
  - docs/DESIGN_Node_Features.md — F-19: macro expansion is CORE, ⛔ never behind a feature switch (SR-144)
  - docs/DESIGN_Content_Layout.md — §2 OWNS the content folders (scenarios/, recipes/scenarios/) a macros/ folder joins (SR-38)
  - docs/DESIGN_Artifact_Staging.md — OWNS the prefetch (NAS → every node's staging) that SR-80 extends with the macros package
  - docs/DESIGN_Deterministic_Network_Ids.md — §11b OWNS network id allocation and the per-load re-allocation SR-45 / SR-50 rely on
  - docs/blueprints/Architect_Question_79_One_Ownership_Truth.md — §0.10 OWNS PartMetadata (the multi-instance part); §6 here explains why macro entities are NOT parts
  - docs/DESIGN_Entity_Hierarchy_Views.md — OWNS the tree views: the ORBAT stays the command tree (HV-1); a macro's entities are shown in its Scenario view (§14.2)
  - docs/DESIGN_Add_Entity_Picker.md — D6b OWNS SpawnHeight, the placement intent the creating node resolves; SR-180's PlacementResolver extends it to x, y
  - docs/DESIGN_Node_Roles_And_Policies.md — §7.3 OWNS IsTransient → ScenarioIgnoreTag (R-140) that macro entities reuse
-->

# DESIGN — **scenario MACROS** *(a general mechanism: scenario-like files inserted as an entity and expanded on the creation path — forces, units, props, objectives, directors)*

> 🔒 **User, `2026-11-01`:** *"Scenario macros are definitely a separate feature."* — split out of `DESIGN_Scenario_Rules.md` (§8, §9.1–§9.7) the same day.

## 0. What a macro is

A **scenario macro** is a scenario-shaped file in `macros/` (`$meta` · `header` with its own `params` · `entities`) that a scenario — or another
macro, or a running trigger — inserts as ONE ordinary entity, the **macro instance** (`MacroInstance {macro, args}` + a `SimTransform` anchor).
Wherever that entity is created, the **Expander** on the entity creation path binds the macro's params into its entities, resolves their
references, relocates their literals to the anchor and creates them as ordinary TRANSIENT entities (own network id, own authority, never
saved); the file keeps only the instance and its args. A scenario with parameters IS a top-level macro — one principle at every level.

⭐ **Founding decision —**

⭐ **SR-144** ✅ `2026-11-01` — **macro expansion is CORE infrastructure, not part of the rules.** 🔒 user: *"Macro expansion is not part of scenario rules, is it? Isn't it a standalone feature which is usable in general, just being used by the rules system? So it should not be gated by scenario.rules. I think this is the core system feature."* ⭐ The `Expander` lives on the entity CREATION path (§9.7) and serves forces, units, props and events alike; the rules are one user of it (`InsertMacro`). ⇒ composed wherever entities are created, ⛔ never gated by `scenario.rules` (`DESIGN_Node_Features.md` F-19), and its slice (S3) does not depend on the rules' slices except for `InsertMacro`

> 🔒 **The quotes that introduced the mechanism** (were at the head of `DESIGN_Scenario_Rules.md` §9):
>
> 🔒 **User, `2026-10-31`:** *"Isn't that SOP name the name of the behavior that should evaluate the objective?"* · *"Could we generalize this
> package parameters into scenario parameters so we can get scenarios with parameters that looks like exactly the same principle as for
> packages."* · *"Scenario editor is now saving the whole scenario and i would keep it like that. Those packages might need to be a special type
> of assets, like scenario macros, stored likely in special folder. I would like to make this macros a general concept that is not used just
> for objective, because it might be used also for inserting whole parts of the scenarios."* · *"We need to store just the reference to the
> macro, which is itself living as an independent scenario-like assets in different folder."*

## 1. The slices — S3a (the core) and S3b (runtime, variants, director) *(split `2026-11-01`)*

| slice | builds | done when | prerequisites | build-state |
|---|---|---|---|---|
| **S3a — the macro CORE** *(split `2026-11-01`; §14 ✅ accepted `2026-11-02`; ⏸ ON HOLD `2026-11-02` — user: *"I think we are not yet ready for starting the Scenario macros implementation I will explain later. So please delete the handoff"*; the queued handoff was deleted)* | the `MacroInstance` TKB type (on the map, SR-70) · the macro FILE (`macros/`, header params SR-44 + the authored anchor SR-152 + `relocatable`) · the `Expander` = `CreateEntityRequestSystem`'s child branch with a second source (SR-146: bind · resolve · relocate · create, derived `macro:` GUIDs, fresh ids SR-151) · `ParamBindings` (SR-57) and `bind` maps (SR-129) · the validator in the prepare (SR-149) · `MacroExpansionStatus` (SR-96) · reconcile-by-GUID on an args / anchor edit (SR-150, SR-157) · RefKey's LEXICAL lookup in macro scope (SR-65) · the relocators (SR-154) · the macros package staged on EVERY node (SR-80, SR-148) · scenario-level params in the header (SR-41) + `--param` / HTTP | the same platoon macro placed twice, rotated, both expand and play on a cluster; a nested macro composes its transforms; a missing macro / wrong binding / cycle fails the load naming the path, nothing created; an args edit keeps ids and outside references; a pinned macro placed elsewhere is refused | S0 · CE-4153 S9′ · §14.3 SR-184…SR-188 | ⏸ **DESIGN** — §14.3 awaits the user (⏸ on hold by the user) |
| **S3b — macros at runtime, variants, the director** | `InsertMacro` (the trigger action, reads `MacroExpansionStatus`) · run variants (SR-101) + the run setup in the picker (AQ108 D24, ui) · `SetSkillAmplifier` (SR-100) · the counter-attack director as a macro (SR-99) | the director is a macro used by two scenarios with different params; variant 3 replays identically | S3a · rules S2 (the trigger runtime) | DESIGN-complete, after S2 |

⭐ The module picture — who registers the Expander and who calls it (the creation path, at load and at runtime `InsertMacro`) — is
`DESIGN_Scenario_Rules.md` §00.5; the Expander is composed wherever entities are created, ⛔ never behind any switch (there is no rules switch either — SR-145) (SR-144).

## 2. INVENTORY

⭐ The measurements moved with their rows (dated as measured, `2026-10-31`); this split ran no new query — it moved text. ⚠ coverage unchecked —
`check_index_coverage` is not available through the CLI.

### 2.1 Templates and layered params *(was §8.1 — the macro rows; the objective rows stayed in the rules doc §8.1)*

⭐ Queries (as named in §8.1): *(four measurements, `2026-10-31`: graph `.*(Template|Prefab|Recipe|EntityClass|Archetype|Composition).*` Class (35) + `.*(Parent|Hierarchy|ChildOf).*` Class (13) + grep; a throwaway probe; ⚠ coverage unchecked)*

| what exists | where | for objectives |
|---|---|---|
| ⭐⭐ **the TKB type is the one template applied at BOTH scenario load and runtime spawn** — `BehaviorProfileDto.DefaultSop` + `DefaultSopParamsJson`, applied by `BehaviorTkbTranslator.Inject` | `Tkb/Domain/BehaviorProfileDto.cs:44-49`; `BehaviorTkbTranslator.cs:113-125` | ⭐ the carrier of a block's DEFAULTS |
| ⭐⭐ **layered params = the user's principle, built** — DTO initializers ▸ asset `ParamsDefaultJson` ▸ the scenario / order JSON (only the keys it sends); scenario wins by origin rank | `DESIGN_Orders_Params_Entity_Classes.md` §13.10a (R-3001); `InitialBrainMaterializationSystem.cs:16-50` | ⭐ a block inserted with `{area, deadline}` overrides only those |
| ⭐⭐ **per-TYPE child entities, deterministic, rebuilt on every node, not saved** — the sensor pattern (`SensorChildFactory.EnsureTkbChild`, part ids 1000+i) | `PerceptionTkbTranslator.cs:78-80`; `SensorChildFactory.cs:13-44`; Sensors & Doctrine §4, §5.1 | ⭐ the shape for "every member of type X gets its evaluator" AND for expanding a block's template children |
| ⛔ no include / fragment mechanism; recipes are one-time copies | `ContentLayout.cs:91-109`; `DESIGN_Content_Layout.md` §2 | the block TEMPLATE is a new asset kind |

### 2.2 The scenario file and its derived entities *(was §9.1 "Measured")*

| fact | where | so |
|---|---|---|
| a scenario entity = a GUID key → components by name: `SimTransform`, `NetworkIdentity`, **`TkbIdentity {TkbType: 305}`** (the type — ⛔ there is no `Type` attribute), `EntityInfo {Name, ForceId}`, brain slots **`Behavior`** (task) / **`Sop`** as `{Name, Params (a JSON string), Origin}`; the file = `$meta` · `header` · `entities` | `scenarios/uc-urban-battle/scenario.json` | the type is `TkbIdentity`; an objective's evaluator sits in the `Behavior` (task) slot (SR-51) |
| a `.params.json` asset = `{AssetId, Name, Fields: [{Name, TypeId, DefaultJson, Comment}]}` → a generated DTO, defaults as initialisers | `assets/behaviors/Params/Tests/T41Params.params.json`; §13.10a | ⭐ the package / macro params type |
| ⭐⭐ **derived entities are already kept out of saves**: `ScenarioIgnoreTag` (the serializer skips the whole entity; 22 users) + `PartMetadata {ParentEntity, InstanceId}` (the parent + part id, also the wire key) — the sensor children use both (CE-3045: *"rebuilt … on load, so saving it too duplicated it"*) | `Fdp.Toolkits/Scenario/ScenarioIgnoreTag.cs`; `Replication/Components/PartMetadata.cs:14`; `SensorChildFactory.cs` | ⭐⭐ the answer to *"how do we know what came from a macro"* — the save exclusion is BUILT; `PartMetadata` is the wrong carrier for macro entities (§9.3) |

⭐ More measured tables sit beside the decisions they carry: §6 (parts vs transient entities, network ids, `EntityRefRemap`), §7 (names in
scenario tooling), and — for relocation — `DESIGN_Scenario_Rules.md` §9.8.1.

## 3. The model *(was §9.2)*

```mermaid
classDiagram
    direction LR
    class ScenarioFile { <<existing, + header.params>> header · entities (top level) }
    class MacroAsset { <<new asset kind, macros/ folder>> the SAME shape · header.params · entities }
    class MacroInstanceEntity { <<new TKB type, SAVED>> SimTransform anchor · MacroInstance(macro, args) · MacroExpansionStatus }
    class ParamBindings { <<new component, saved in the macro>> Component.field to macro param }
    class ExpandedEntity { <<ordinary entity, TRANSIENT>> own network id · ScenarioIgnoreTag · derived EntityGuid }
    ScenarioFile *-- MacroInstanceEntity : a top-level entity
    MacroInstanceEntity ..> MacroAsset : by name
    MacroAsset *-- ParamBindings
    MacroInstanceEntity ..> ExpandedEntity : expands into
    ExpandedEntity ..> MacroInstanceEntity : nested macros recurse
```

*What the picture shows that prose hid:* ONE file shape serves a scenario and a macro — both have a params declaration and entities — so a
scenario with parameters IS a top-level macro. What is saved is only the top-level entities and the macro-instance entities; every expanded
entity is created TRANSIENT (SR-42′) and carries the tag the serializer already skips.

## 4. Params, presets and their overrides *(was §8)*

> 🔒 **User, `2026-10-31`:** *"What I would like to avoid is to define the entities per every scenario, because the evaluation criteria might be
> common across multiple scenarios, common across multiple team members … It needs to be simple and reusable."* · *"Such a block, probably at
> every level … should need to come with its own parameters, and those entities and their objective would need to read and use those
> parameters … What is baked in is good as a default, but it always needs to be replaceable when inserted into the final place … This very
> principle is used for using behaviors on entities that the scenario can override the default parameters."* · *"Ticking cost should be no
> issue."* · *"If there are some runtime catalogs missing, we will simply make them a shared code."*

### 4.0 Why the override surface is FLAT *(was §8.0)*

⭐ measured: the existing override layering is FLAT — `BehaviorParams.OverlayFlat` merges top-level keys only (`DESIGN_Orders_Params_Entity_Classes.md` §13.10a). Macro params keep every override at the top level, so the existing mechanism serves unchanged; a deep merge into a child's nested args would be new code and ambiguous (arrays by index).

### 4.1 Decisions *(was §8.3 — SR-31 stayed in the rules doc)*

| # | decision | ⭐ current answer |
|---|---|---|
| **SR-27** | WHO expands — the entity CREATION path, not the scenario loader (`2026-10-31`) | ⚠ **wording superseded by SR-146 + SR-174 (§14)** — the EXPANDER is `CreateEntityRequestSystem`'s child branch, which runs only where a creation REQUEST is processed, ⛔ NOT the translator list (that also runs at ghost promotion, which would expand a second time); the principle stands: one path for scenario load AND runtime spawn alike ⇒ a macro inserted at load, by the instructor mid-run or by a trigger expands the same way, through one path. What it creates: ordinary transient entities (SR-42′). ⛔ rejected: expansion in `ScenarioLoadStep` — a macro spawned at runtime would not expand (two paths) |
| **SR-28** | how a child takes a macro's value (user, `2026-10-31`: *"The objectives inside a package can carry the values on their own, but they should be able to reference a parameter of the whole package instead of hard coding the values inside."*) | a child's value is EITHER a literal (fixed in the macro) OR bound to the enclosing macro's param (SR-40 / SR-57 `ParamBindings`, SR-129 `bind`). ⭐ The OVERRIDE surface is the macro's FLAT param list — exactly the existing `BehaviorParams.OverlayFlat` (DTO default ▸ macro default ▸ instance args, top-level keys only, §8.0) — so overriding never needs a deep merge into a child. ⇒ to make an inner literal overridable, the macro author PROMOTES it to a param. Applied once at expansion; a nested macro receives its args the same way (a binding refers only to the IMMEDIATE macro) |
| **SR-32** | one mechanism for every kind of preset | missions, objective sets, evaluation criteria, triggers / directors are all scenario MACROS (SR-38); a preset of a preset (e.g. `SeizeVillageFast` = `SeizeArea` with `deadline: 30 min` fixed) is a macro containing one macro instance with some args fixed — no inheritance mechanism |

### 4.2 Rejected — one line each *(was §8 — the macro lines; SR-29 / SR-36 and SR-30 stayed in the rules doc)*

- *A block-template asset `*.objective.json` (SR-25) / a package fragment file (SR-34) / a package as its own named behaviour with a package runner* — one scenario-like MACRO file serves every preset (SR-38), opened in the scenario editor.
- *Inserting a block as a `Sop: {"Name": "ObjectiveBlock"}` entry (SR-26)* — a macro instance is an ordinary entity (SR-39a).
- *`"^param"` strings inside typed fields (SR-35's first form)* — a float field cannot hold a string; the binding sits BESIDE the value (SR-57, SR-94, SR-129).
- *Expanded children as sensor-pattern part entities (SR-27's first lean)* — parts have no authority, life or save identity of their own (§9.3).

## 5. The macro file, the instance, its bindings *(was §9.2's rows + SR-57 from §9.4; SR-37 and SR-71 stayed in the rules doc)*

| # | decision | ⭐ current answer |
|---|---|---|
| **SR-38** | a macro asset | `macros/<name>/macro.json` (its own folder in `ContentLayout`, like `scenarios/` and `recipes/scenarios/`) — the SAME shape as `scenario.json` (`$meta` · `header` · `entities`) + `header.params` naming its `.params.json`. Authored by opening it in the scenario editor, which saves the whole file as today |
| **SR-39** | inserting a macro | a top-level scenario entity with a `MacroInstance {macro: "SeizeArea", args: {"area": "@Village", "side": "Blue", "deadline": "10:00"}}` component; the args override the params DTO's defaults key by key (`OverlayFlat`). Editable in the inspector as a params form over the macro's params type |
| **SR-39a** | ✅ the INSTANCE is an ordinary entity (user, `2026-10-31`: *"Couldn't a scenario macro be saved as an entity that gets replaced by the macro? … that would avoid the need for special treatment like insert or delete as adding and deleting entities is handled already. picking the macro file would be an edit on the parameters of that entity, again in the details panel"*) | a "Scenario macro" entity type, placed / deleted / moved / copied like any entity; selecting it shows in Details: the MACRO picker (the scenario picker over `macros/`) + the ARGS form built from the picked macro's `header.params` (defaults shown, overridable). Changing the macro or an arg re-expands that instance; deleting it removes its expanded entities ⇒ ⛔ no special insert / delete command |
| **SR-69** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | positions inside a macro | ⭐ lean: RELATIVE to the instance entity's transform — a macro's entities are authored around its origin and the instance's position + heading place them, so one platoon macro can be dropped anywhere; an objective macro with no geometry ignores it |
| **SR-70** | ✅ CONFIRMED `2026-11-01` (user: *"References and macros approved."*) · the instance entity is on the MAP (user, `2026-10-31`: *"the scenario macro entity needs to have the position component as well to be displayed on the map and clickable from the map"*) | ✅ it carries a `SimTransform` — the anchor SR-69's relative positions use — and a map symbol of its own (a "macro" marker), so it is selected by a click and dragged like any entity; dragging it moves everything it expanded. ⭐ ORBAT: shown (it is on the map), its expanded entities beneath it. ⛔ unmeasured: how a non-unit positioned entity gets its symbol today (the visual family / `EntityClassShapeLibrary` path) |
| **SR-40** | binding a part's field to a macro param | stored as SR-57's `ParamBindings` (typed components) and SR-129's `bind` maps (JSON) — e.g. `"Behavior.Params.area": "area"` = *this entity's task param `area` takes the macro's `area`*. Flat (both sides are top-level names, SR-37's typed DTOs), applied after the literals at expansion |
| **SR-41** | scenario parameters | the scenario `header.params` names a params type; its args live in the header (or are passed by whoever starts the run); top-level entities `Bind` to them exactly as macro parts do ⇒ ONE principle at every level |
| **SR-43** | editing an expanded part in the editor | ⭐ v1: shown under its instance, READ-ONLY, with *"from macro SeizeArea — edit the macro or its args"*. ⏭ later: per-instance overrides of a part's field |
| **SR-57** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | where a macro entity's BINDINGS are saved — user, `2026-10-31`: *"we might need a new field on the behavior intention component for those bindings, correct? Otherwise, they would not be saved in the scenario."* | ⭐ yes, a new saved field — but ⭐ NOT on the brain intent alone: a general macro binds OTHER components too (a unit's `EntityInfo.ForceId` ← `side`, its `SimTransform` position ← `position`, a child's `Behavior` params ← `area`). ⇒ ONE generic per-entity component `ParamBindings {"<Component>.<field path>": "<macro param>"}` (e.g. `"Behavior.Params.area": "area"`, `"EntityInfo.ForceId": "side"`), saved and loaded by its own scenario translator, so it round-trips when a macro file is opened and saved in the scenario editor; the expander applies it when instantiating and does NOT copy it onto the created entities. The brain slot record (`SavedBrainSlot {Name, Params, Origin}`) stays as it is |

**Measured `2026-11-02`** (§14.1): ① opening / saving under `macros/` — one resolver arm · ② the tree — SR-177 · ③ the map symbol — SR-178.

### 5.1 Where a binding LIVES so no editor can drop it *(`2026-11-02` — ✅ SR-159 … SR-163 ACCEPTED — user: *"Approved."*; amends SR-129; ⚠ SR-165 amended by SR-168, §14)*

> 🔒 **User, `2026-11-02`:** *"how do we handle this during the macro expansion? How does the loader know that some parts use parameter binding?
> And also, how are we going to save these bindings back to the scenario? How the editor who manages this predicate definition knows to save the
> bindings? Because if it doesn't know, if it doesn't keep the binding information together with the predicate, the next write will lose it."*

| measured `2026-11-02` | where |
|---|---|
| ⛔ the ONE params form HYDRATES the JSON into the typed params object and writes it back FROM that object — and the shared options *"ignore an unknown key"* ⇒ a `bind` key inside params JSON (SR-129 as written) is **LOST on the first save** | `Hrot.Presentation/Behavior/BehaviorParamsForm.cs:43-85` (`Hydrate`, `ToJson`); `Fdp.Toolkits/Behavior/BehaviorParams.cs:121-130` |
| no params / predicate DTO has a catch-all (`[JsonExtensionData]`) | grep over `ReplayBrowser/Search`, `MapDefinitions/Behavior` — none |
| SR-57's `ParamBindings` component is designed, not built | grep — no type |
| ⭐ the authored types already carry HOW a value was written, inside the value, and round-trip through the typed form (the `Ref` forms) | S0 — `RefSpec` (`DESIGN_Scenario_Rules.md` §9.8.11) |

⭐ **The rule:** a binding must live where the TYPED round-trip carries it — inside a type the editor of that value serialises, never as a stray
key it does not know. That gives exactly THREE homes, one per kind of value:

```mermaid
classDiagram
    direction LR
    class RefSpec { <<existing, S0>> Form · Key / Exact — gains Form = Param }
    class EntityLink { <<existing>> }
    class PointLink { <<existing>> }
    class AreaLink { <<existing>> }
    class BindableNode { <<NEW base, managed DTO trees>> Bind: name → param (keys = THIS node's property names) }
    class SearchPredicateDto { <<existing tree, gains the base>> }
    class ActionEntry { <<the do entry, SR-94>> act · args · Bind }
    class ParamBindings { <<NEW component, saved — SR-57>> field path → param }
    class ParamsForm { <<existing, the ONE form>> reads + writes ParamBindings for its slot }
    class PredicateBuilder { <<existing>> edits Bind with its node }
    class Expander { <<SR-146>> step ① bind applies all three }
    EntityLink *-- RefSpec
    PointLink *-- RefSpec
    AreaLink *-- RefSpec
    SearchPredicateDto --|> BindableNode
    ActionEntry --|> BindableNode
    ParamsForm ..> ParamBindings
    PredicateBuilder ..> BindableNode
    Expander ..> RefSpec : Form Param
    Expander ..> BindableNode : Bind
    Expander ..> ParamBindings
```

*What the picture shows that prose hid:* every binding is inside something TYPED that its own editor already round-trips — so the editor cannot
lose it, the loader finds it by type, and save writes it with no extra step.

| # | decision | ⭐ lean |
|---|---|---|
| **SR-159** | ① a REFERENCE value (entity, point, area) | the binding is a FORM of the value: `RefSpec.Form = Param`, `Key` = the param's name — JSON `"^area"` (SR-55's shorthand, now real). The type round-trips it through every hydrate / write like `"@Key"`; the editor's leaf shows *"bound to ^area"* (CE-4206's form switch gains *Param*). ⭐ The commonest macro params — subject, area, position, target — are all this case |
| **SR-160** | ② a node in a MANAGED DTO tree — a predicate node, a `do` entry | a typed `Bind` property on a shared base (`BindableNode`), keys = THAT node's own property names (`{"Min": "minDelay"}`) — ⭐ moving or reordering a node moves its bindings (SR-94's reason); the predicate builder and the `do`-list editor show a chip on the bound property and write `Bind` with the node. Supersedes SR-129's *"any JSON object may carry `bind`"* (an untyped key is exactly what a typed editor drops) |
| **SR-161** | ③ a field of an UNMANAGED params struct or a typed COMPONENT (scalars, enums, strings) | the per-entity component `ParamBindings` (SR-57): `"<Component>.<field path>" → param` (`"Behavior.Params.Speed": "speed"`, `"EntityInfo.ForceId": "side"`), field paths by NAME (params structs carry no arrays of bound scalars — a rail refuses an index in a path). ⭐ The ONE params form and the component editor READ it to draw the chip on that field and WRITE it on *Bind / Unbind*; they never put a binding into the params JSON. ⛔ A `bind` key inside params JSON is refused at load (it would be dropped by the next save) |
| **SR-162** | how the loader knows | ⭐ by TYPE, in the expansion's step ① (§10.1): the walker applies `RefSpec.Form = Param` leaves; the JSON walk applies every `Bind` of a `BindableNode`; `ParamBindings` is read per child entity. ⭐ The validator (SR-149) checks first: every bound name is a declared param of the macro (or scenario, SR-41) and its type fits the target (a `PointLink` ← a point param, a `float` ← a number). A binding is applied ONCE at expansion (SR-44); the live copy holds values only |
| **SR-163** | how it is saved | ⭐ nothing extra: ① and ② are inside values the serializer already writes; ③ is a saved component with its own translator. The EXPANDED children are transient and never saved (SR-42′) — the bindings live in the macro FILE, whose entities are edited unexpanded (literal defaults + bindings). ⭐ A RAIL round-trips a macro with all three kinds through hydrate → edit an unrelated field → commit → save → load and asserts every binding survives |

#### 5.1.1 Example — a macro file with all three kinds *(illustrative names; ⚠ the trigger's definition component is named in rules S2 — SR-97's record)*

```jsonc
// macros/forces/RedQRF/scenario.json — the scenario name "macros/forces/RedQRF" (SR-173, §14.1 ①)
{
  "header": {
    "kind": "macro",
    "anchor": { "position": [0, 0, 0], "heading": 0 },                       // SR-152
    "params": [                                                                // SR-44 — declared here, never compiled
      { "Name": "area",     "TypeId": "AreaLink", "Default": "@RedStaging" },
      { "Name": "speed",    "TypeId": "float",    "Default": 4.0 },
      { "Name": "side",     "TypeId": "ForceId",  "Default": "Hostile" },
      { "Name": "minDelay", "TypeId": "float",    "Default": 30 }
    ]
  },
  "entities": {
    "7b1e…": {                                                                // the squad leader
      "EntityInfo":   { "Name": "QRF lead", "ForceId": "Hostile" },            // literal = the value when the macro is opened alone
      "SimTransform": { "Position": [0, 0, 0], "Rotation": [0, 0, 0, 1] },
      "Behavior":     { "Name": "ClearArea",
                        "Params": "{\"Area\": \"^area\", \"Speed\": 4.0}" },   // ① "^area" — a FORM of the AreaLink value (SR-159)
      "ParamBindings": { "Bindings": {                                         // ③ scalars / enums the value type cannot hold (SR-161)
                          "Behavior.Params.Speed": "speed",
                          "EntityInfo.ForceId":    "side" } }
    },
    "c40a…": {                                                                // a trigger inside the macro
      "TriggerDefinition": {                                                   // the managed record (SR-97), typed DTOs
        "When": { "$type": "Compound", "Operator": "And", "Conditions": [
                  { "$type": "ScenarioTime", "Clock": "Elapsed", "Min": 30,
                    "Bind": { "Min": "minDelay" } },                           // ② a typed property of the NODE (SR-160)
                  { "$type": "OnEntity", "Ref": "^area", "Predicate": { "$type": "…" } } ] },
        "Do":   [ { "Act": "Message", "Args": { "Text": "QRF moving", "Seconds": 8 } },
                  { "Act": "Order",   "Args": { "Speed": 4.0 },
                    "Bind": { "Speed": "speed" } } ]                           // ② on the ENTRY (SR-94), never inside Args
      }
    }
  }
}
```

| case | who EDITS it | where the editor finds the binding | why the next save keeps it |
|---|---|---|---|
| ① `"^area"` | the params form's `AreaLink` leaf | IN the value — the leaf reads `RefSpec.Form == Param` and shows *"⛓ area"* | the form writes the typed value back, form included |
| ② `Bind` on a node / entry | the predicate builder · the `do`-list editor | a typed property of the node it is drawing — a chip on `Min` / `Speed` | the node (with its `Bind`) is what that editor serialises |
| ③ `ParamBindings` | ⭐ the params form and the component editor READ it; *Bind / Unbind* WRITES it (an ordinary component edit — undoable) | the entity's `ParamBindings` component, looked up by the path of the field being drawn (`"Behavior.Params.Speed"`) — the same field path the component editor already uses | the params form only ever writes `Params`; `ParamBindings` is a separate saved component it never overwrites |

**Rejected — one line each:**
- *`bind` keys inside the JSON (SR-129 as written)* — measured: the typed params form drops unknown keys on the first save.
- *`[JsonExtensionData]` on every DTO* — the binding would survive but no editor would SEE it; editing the bound field silently diverges from it.
- *A `Bindable<T>` wrapper on every bindable field* — every params struct and blob layout changes, ~36 bytes per field, for a minority of fields.
- *Path-keyed bindings for predicate nodes* — reordering conditions would break the paths (the SR-94 objection); the node carries its own.

#### 5.1.2 Applying bindings generically, and what is recorded *(`2026-11-02` — ✅ SR-164 … SR-166 ACCEPTED — user: *"Approved."*; ⚠ SR-165 amended by SR-168, §14)*

> 🔒 **User, `2026-11-02`:** *"It's not yet clear to me how I, in the editor, can pick that the field will be fed from a scenario macro parameter. or
> maybe basically from any kind of parameter, because for example the predicate conditions, I guess they are used in many other places … are the
> parameter expansion implemented in a somewhat generic way, like that when the JSON is deserialized, we pass to the deserializer some interface
> that provides the actual values for the parameters? How the components keeping the bindings are saved to the recordings? Are they managed
> components? … any managed component means that during the replay we will need to reinstantiate it … which increases the garbage collection
> pressure, and it's in general undesired."*

| measured `2026-11-02` | where |
|---|---|
| S0's ONE walker has an operation per leaf TYPE (`LeafApplier`: `Ref`, `Link`, `Point`, `PointLink`, `Area`, `Rotation`, `Bearing`) — it visits typed reference / place leaves, ⛔ not scalars | `Fdp.Toolkits/Replication/EntityRefRemap.cs:455-470` (backend) |
| the flight recorder writes a managed component as a DELTA — only when it changed since the previous tick; playback re-creates the object on those frames and on a keyframe / seek | `Fdp.Core/FlightRecorder/RecorderSystem.cs:24-40, :96-140, :283` |
| `DataPolicy.NoReplay` keeps a component out of the recorder and checkpoints | `Fdp.Core/DataPolicyAttribute.cs:41` |
| the condition language is shared by data breakpoints, the timeline's indexed lanes, the Blueprint `When` node and the triggers (SR-95) | ⚠ the film director's cues were NOT measured for it |

| # | decision | ⭐ lean |
|---|---|---|
| **SR-164** | ONE generic binder, any parameter source | ⭐ `IParamSource { bool TryGet(string name, Type target, out object value) }` and ONE `ParamBinder.Apply(value, IParamSource)` that does all three kinds: a `ParamBind : LeafApplier` on the walker for the `Param` FORM (①), the type plan extended to visit a `BindableNode`'s `Bind` (②), and `ParamBindings` paths set by name (③). ⭐ It runs AFTER deserialising, on the typed value — ⛔ NOT inside the JSON converter: the EDITOR must deserialise WITHOUT applying (or it would lose the binding it is editing), the runtime with. ⭐ Any host passes its own source: the macro expander (the instance's args ▸ the header defaults), a scenario load (the scenario's params, SR-41), and later any other user of the shared languages (a film take, a blueprint asset with params) — ⛔ never a second binder |
| **SR-165** | is `ParamBindings` recorded? — ⛔ NO | it is an AUTHORING instruction, not runtime state: the binder applies it and the component is NOT put on the created / loaded entity (SR-57: *"does NOT copy it onto the created entities"*) — so a live or replayed world never holds one. ⭐ Declared `DataPolicy.NoReplay` as well, so even an edit world's checkpoint never carries it. ⇒ no managed allocation in replay from bindings. It may stay a managed component (a small path → name map) because it exists only in the authoring world |
| **SR-166** | the trigger's `When` / `Do` (a managed record, SR-97) IS recorded | ⚠ stated honestly: it is runtime state of the trigger entity (a branch to live from a checkpoint needs it), so it is recorded — but written once (it never changes after assignment), so the recorder's delta writes it at the keyframes only and playback allocates it on a seek, not per frame. ⭐ If that proves measurable, the lean is to keep it out of the recording (`NoReplay`) and re-derive it on a branch from the scenario's own trigger entity — a rules-S2 measurement, not a macro one · ⭐ **immutable by type** (user, `2026-11-02`): `TriggerDefinition` and its `When`/`Do` tree are `record`s with `init`-only members; a runtime change (if ever supported) builds a NEW record and `SetManagedComponent`s it — ⛔ never an in-place edit, which the chunk version (`ManagedComponentTable.cs:153-205`) would not see and the shallow snapshot copy (`:298-365`) would leak into earlier snapshots. CLAUDE.md *"NEW COMPONENTS"* carries the general rule |

**Rejected — one line each:**
- *Applying bindings inside the JSON converter* — the editor deserialises the same JSON and would lose the binding it is editing.
- *One binder per host (macro, scenario, film, blueprint)* — four copies of one mechanism.
- *Recording `ParamBindings`* — nothing at runtime reads it.

## 6. Macro entities are full, TRANSIENT entities, not parts *(was §9.3, `2026-10-31`; SR-47…SR-51 stayed in the rules doc)*

| measured | where | so |
|---|---|---|
| `PartMetadata` IS the ECS side of NED's MULTI-INSTANCE descriptors (turret pose / weapon task, EQS sensor config / results): the key is (parent network id, instance id); instance ids are RANGES agreed in code (mounts 1…N, behaviour sensors 1–999 allocated, TKB sensors 1000+i, turrets 2000+i), not a protocol enum | AQ79 §0.10 (`Architect_Question_79_One_Ownership_Truth.md:206-240`); `BDC_NED_SST_Descriptor_Rules.md:29,49-57`; `TurretParts.cs:15-45` | — |
| a part has NO `NetworkIdentity`; authority resolves to its PARENT (`AuthorityExtensions.cs:23`), ownership recompute skips it (`OwnershipRecomputeSystem.cs:251`), it dies with the parent (`SubEntityCleanupSystem.cs:24`), scenario extraction folds it into the parent (`StagingEntityExtractor.cs:292-310`) | as cited | ⛔⛔ a macro entity (a unit, an objective) built as a part would lose its own authority, life and save identity |
| ⭐⭐ **the save exclusion already works per FULL entity**: `EntityCreationRequest.IsTransient` → `SpawnEntityCommand.IsTransient` → every node stamps `ScenarioIgnoreTag`; TKB children inherit it; save ⇔ primary owner AND NOT `ScenarioIgnoreTag` | `EntityCreationRequest.cs:~95-125`; `NetworkSpawningSystem.cs:199`; `ScenarioSaveCore.cs:16`; Node Roles §7.3 (R-140) | ⭐ macro entities = ordinary entities created TRANSIENT, each with its own network id |
| network ids: ONE allocator per domain (orchestrator), blocks of 100, reset to 1000 per world; ⭐ **scenario load RE-ALLOCATES every id** (old → new map, `PreAllocatedNetworkId`) — the file's ids only look stable because of the reset | `DdsIdAllocatorServer.cs:119-136`; `StagingEntityExtractor.cs:~245-262`; `DESIGN_Deterministic_Network_Ids.md` §11b | a file id is a FILE-LOCAL key either way |
| `EntityRefRemap` rewrites every `EntityRef` (nested, lists) by the old → new map at load; an id not in the map is left as is | `EntityRefRemap.cs:25`; `DESIGN_Entity_Reference.md` | ⚠ a reference to a macro-made entity (never saved) would dangle — or, with ids reset to 1000, hit an unrelated entity |
| the scenario picker enumerates one root recursively | `ScenarioEnumeration.EnumerateRelPaths` | ⭐ macros = a second root |

| # | decision | ⭐ lean |
|---|---|---|
| **SR-42′** | who creates a macro's entities, and what is saved | ⭐ creating a `MacroInstance` entity issues ordinary `EntityCreationRequest`s for its entities with **`IsTransient = true`** (own network ids from the allocator, own authority, own replication, own brain); save writes top-level entities + the instances. ⛔ not `PartMetadata` (above) |
| **SR-44** | macro / scenario params | declared INLINE in the macro's or scenario's `header.params` (rows `{Name, TypeId, Default, Comment}`, the `.params.json` row shape) — ⛔ never compiled: only the expander reads them, copying values into the children's JSON params; each `Bind` is type-checked once against the child's compiled DTO field. the form: SR-44a |
| **SR-44a** | ✅ MEASURED `2026-10-31` — the inline params list IS editable with existing parts (user: *"not an issue if the editor doesn't support the editing without the C# DTO. We will teach the editor"*) | the variable editor already edits ONE value of a runtime-chosen type: `VariableEditModal` wraps it in a generic one-field `ScalarEditBox<T>` (because `CreateLeafBinding` needs a member), then draws only the wrapper's child, renamed, through the public `ComponentEditDrawer.DrawEditNode` (`Hrot.Editor.AiShared/Variables/VariableEditModal.cs:340-380`); `EditNode` / `EditDocument` have public constructors and every per-type editor (`EntityRef` / `PickableGeoPoint` map picking, `FixedString*`, `Guid`, `UtilityDecisionRef`) is a registered `ICustomFieldEditor` that takes ANY `IValueBinding` (`ComponentEditServices.cs:17-27`, `StructEdit.Core/Plugins/ICustomFieldEditor.cs`). ⇒ a macro's params form = one `ScalarEditBox<T>` row per declared param, `T` resolved from its `TypeId` — no generated type, no new editor kind |
| **SR-45** | ids inside a macro | a macro file's network ids are MACRO-LOCAL keys; each instance remaps them to fresh ids through the existing `EntityRefRemap` old → new map, so two instances never collide and refs between a macro's own entities stay right |

**Rejected — one line each:**
- *Macro entities as `PartMetadata` parts (SR-42)* — authority, life and save identity fold into the parent (the rows above).
- *Keying scenario entities by their network id (SR-50)* — the key stays a — now stable — GUID (`DESIGN_Scenario_Entity_Keys.md` rev 2); a network id is re-allocated on every load.

## 7. References across macro scopes — a macro is CLOSED *(was §9.5, `2026-10-31`; SR-61 stayed in the rules doc)*

> 🔒 **User, `2026-10-31`:** *"if in the exercise objective there is a reference to an entity in the scenario, but that entity comes from a
> scenario macro which is using its own internal IDs, and the exercise objective is also a scenario macro using its own scope of IDs. These two
> can never reference each other reliably. Would we use some names for referencing?"*

| measured | where |
|---|---|
| scenario tooling ALREADY names units: the checks verifier (*"no unit named 'X' in <scenario>"*), the film director's anchors and takes (*"no unit named 'X' (alike: …)"*) | `ScenarioVerification.cs:764`; `AnchorBook.cs:226`; `TakeStore.cs:289` |
| `EntityInfo.Name` is unique within 114 of 117 scenarios; duplicates in `hill-attack` / `hill-attack-close` (`M1 Abrams` ×2), `test-fire` (`M2 Bradley IFV` ×2) | measured `2026-10-31` |

| # | decision | ⭐ current answer |
|---|---|---|
| **SR-58** | ✅ CONFIRMED `2026-11-01` (user: *"References and macros approved."*) · a macro is CLOSED | inside a macro, references use its own local ids (remapped per instance, SR-45); ⛔ a macro never reaches OUT — anything outside it comes IN through its params (the exercise macro gets its `subject` / `area` as params) |
| **SR-60** | ✅ CONFIRMED `2026-11-01` (user: *"References and macros approved."*) · names unique per scope | a macro's entity names are unique within the macro; instance names unique within their parent scope; a duplicate that a path references is refused at load with the alike-names hint the director already gives. (Today 3 of 117 scenarios have duplicates — they only matter once a path names them) |

**Rejected — one line each:**
- *Name paths (`Blue Platoon/Squad 1/Rifleman 2`, resolved after expansion through a `MacroOrigin` index, SR-59)* — the user: an entity name is a label, not a stable identifier (SR-63).
- *A stable GUID as THE cross-scope reference (SR-62)* — a GUID is the EXACT form only; a reusable macro needs a key each scenario supplies (SR-63, SR-64).
- *A qualified `{instance, local id}` reference (SR-46)* — ids are file-local and change per load.

⭐ Rejected — **macro EXPORTS** (declared public aliases) for v1: the macro's entity names already are its interface; add exports only if renaming internals breaks references in practice.

## 8. Three reference forms; `RefKey` *(was §9.6 — ✅ user, `2026-10-31`: "ref key is fine. recorded."; SR-67 stayed in the rules doc)*

> 🔒 **User, `2026-10-31`:** *"Entity name is by no means a stable identifier during the authoring."* · *"sometimes the objectives might be written
> in a generic way referencing some subjects that are expected to be in the scenario, but every scenario might have different ones … So the
> entity would need some kind of extra key which is not the entity's label but is used for referencing it."*

| # | decision (✅ approved) |
|---|---|
| **SR-63** | ✅ CONFIRMED `2026-11-01` (user: *"References and macros approved."*) · three reference forms: **exact** (the entity's GUID — *this one entity*) · **key** (`RefKey` — *whatever entity this scenario gives that key*) · **query** (side, class, command chain, `TacticalDesignation` — *the squad leader of @Squad1*, *all members of @BluePlatoon*). ⇒ `EntityInfo.Name` is a LABEL only, never a reference; ⛔ SR-59 (name paths) and SR-62 (GUIDs for cross-scope refs) are superseded |
| **SR-64** | ✅ CONFIRMED `2026-11-01` (user: *"References and macros approved."*) · `RefKey` — an author-assigned attribute on an entity (`"RefKey": "BlueHQ"`), separate from the label, set deliberately, rarely changed, unique within its scope; most entities have none. Precedent: `TacticalDesignation` (`Fdp.Core/CommandHierarchy/TacticalDesignation.cs`) is a role key scoped to one command group, a fixed enum on the wire |
| **SR-65** | ✅ CONFIRMED `2026-11-01` (user: *"References and macros approved."*) · ⚠ **amended `2026-11-02` by SR-188 (§14.3): duplicate keys allowed, first match wins, ambiguity warns** · reusable macros reference keys THROUGH PARAMS — `subject` defaults to `"@BlueHQ"`, a scenario with another key overrides it at insertion; lookup is LEXICAL: inside the enclosing macro instance first, then outward to the scenario; from outside, a key inside an instance is qualified by keys only (`@BluePlatoon/SL`) |
| **SR-66** | ✅ CONFIRMED `2026-11-01` (user: *"References and macros approved."*) · ⚠ **amended `2026-11-02` by SR-188 (§14.3): duplicate keys allowed, first match wins, ambiguity warns** · a missing key fails LOUDLY: the objective stays inactive with *"no entity with key 'BlueHQ' here"* (the director's *"no unit named …"* shape); duplicate keys in one scope are refused at save and load |
| **SR-68** | ✅ CONFIRMED `2026-11-01` (user: *"References and macros approved."*) · where a macro's (and a scenario's) PARAMS DECLARATION is shown and stored — ⭐ rev `2026-10-31` b (user: *"I am not aware that a scenario has a world entity saved as such. What world entity carries is usually written in the scenario header, and I think the scenario macro parameters should be the same case."*) | measured: the World Environment IS saved as an ENTITY (`ua-weather-regions`, `ua-weather-sensing`, `ua-night-sensing` carry `{NetworkIdentity, ScenarioClock, TkbIdentity, WeatherConditions}`; the others get a default at load and save it next time — Weather W-0 / C-3); the header holds `subsystemType · schemaVersion · terrainName · description` (`ScenarioHeader.cs:33`, `ScenarioHeaderDto.cs`). ⭐ lean (user's model): the declaration is FILE metadata ⇒ `header.params` (rows `{Name, TypeId, Default, Comment}`), by extending `ScenarioHeader` / `ScenarioHeaderDto` — ⛔ not a component on the World Environment entity (a macro file's World Environment would be a never-instantiated oddity). Shown and edited in a **Parameters** map-background Details view (the `DetailsViews.Add` pattern beside *Scenario* / *Zones*, `EditorSubsystem.cs:3141-3200`) with SR-44a's `ScalarEditBox<T>` rows. ⚠ small new code: the header gains an edit path (no view edits the header today — unmeasured) |

⚠ The film director's takes keep naming units in a RECORDING by name for now — a GUID would need to exist at runtime and in the recording; a separate decision.

## 9. Expanding a scenario macro at LOAD — the sequence *(was §9.7, `2026-10-31`; the gaps are marked ⚠ GAP)*

> 🔒 **User, `2026-10-31`:** *"the scenario files are distributed by the orchestrator before the scenario load. And now we will need to distribute
> not just a single scenario file, but also the macros … we distribute all the macro scenarios, because we do not know upfront the whole dependency
> chain. So we might distribute a zipped package of all the macros together with the scenario."*

```mermaid
sequenceDiagram
    participant OR as Orchestrator (prefetch)
    participant ST as every node's staging dir
    participant B as Brain (scenario load)
    participant CP as entity creation path
    participant N as every node
    OR->>ST: copy the scenario dir AND the macros package (ALL of macros/, one zip)
    B->>CP: create the scenario's own entities (as today)
    CP->>CP: the MacroInstance entity is created: read its macro from staging
    CP->>CP: resolve params = header defaults overlaid by the instance args
    loop each entity of the macro
        CP->>CP: apply ParamBindings · position relative to the instance · GUID = uuidv5(instance, local) · remap its internal refs
        CP->>N: an ordinary TRANSIENT creation request (replicated, never saved), stamped with its origin
    end
    Note over CP: a created entity that is itself a MacroInstance repeats the loop (nesting)
    B->>B: after ALL expansion: resolve RefKey references (nearest scope outwards), a missing key fails loudly
    Note over B: save later writes the scenario's own entities + the MacroInstance (args), never the expanded ones
```

*Caption:* expansion is NOT a loader step — it happens where the instance entity is created, so a macro inserted at RUNTIME (a trigger's
`InsertMacro`, the instructor) expands by the same path (SR-27). The orchestrator only has to make the macro files present beforehand.

| # | decision / ⚠ GAP | lean — ✅ ACCEPTED by the user `2026-10-31` (*"Gap-X Leans accepted."*) |
|---|---|---|
| **SR-80** | ✅ CONFIRMED `2026-11-01` (user: *"References and macros approved."*) · distributing the macros | the existing prefetch (`StorageGatewayModule.PrefetchScenarioAsync:229`, `DESIGN_Artifact_Staging.md` — *"an artifact a scenario NAMES must arrive before the scenario LOADS"*) also stages the WHOLE `macros/` tree as ONE zip (the TKB already travels as a zip, cached on its timestamp) — ALL macros, because the dependency chain is not known without expanding (user) |
| ⚠ **GAP-1** → closed by **SR-147** (§9.y) | WHEN is expansion finished? | RefKey resolution (step "after ALL expansion") needs an end point, but creation is asynchronous across nodes — lean: hook the load gate that already waits for the scenario's entities to be constructed (CE-4087) and count the expanded entities into it |
| ⚠ **GAP-2** → closed by **SR-148** (§9.y) | which nodes READ the macro files | the Brain (it owns the instances) and the editor (it expands to show the parts read-only) — both are in the prefetch's node set; a node that only receives replicated entities never reads a macro |
| ⚠ **GAP-3** → closed by **SR-149** (§9.y) | a missing macro, a wrong `TypeId` in a binding, a cycle (macro A contains B contains A) | fail the LOAD loudly naming the instance — where the check lives (the creation path or a validation pass before load) is undecided; a cycle needs a depth guard |
| ⚠ **GAP-4** → closed by **SR-150** (§9.y) | editing an instance's args in the editor | re-expand: destroy its parts, create them again — their GUIDs stay the same (derived); undecided whether references from OUTSIDE into those parts survive the re-expansion within one session (they should, by GUID) |
| ⚠ **GAP-5** → closed by **SR-151** (§9.y) | ids | the expanded entities get fresh network ids from the allocator like any creation; a macro's internal ids are only its file-local keys (SR-45) |

### 9.x The completion of an expansion *(moved here from `DESIGN_Scenario_Rules.md` §10.13, `2026-11-01`)*

| # | decision | lean |
|---|---|---|
| **SR-96** | ✅ a macro insertion reports its OWN completion (user, `2026-11-01`: *"maybe this is a process that runs on the brain node and takes care about all its subparts like creating entities and so on, and it reports the completion on its own. So the action that triggers the macro expansion just monitors this final completion signal from the node that executed the macro expansion and doesn't care about the individual subparts inside."* → *"Yes write those two leans down."*) | ⭐ the macro INSTANCE entity (`MacroInstance{macro,args}`) carries a small replicated `MacroExpansionStatus {State: Expanding\|Done\|Failed, Reason}`. ⭐ ONE writer — the node that expands it; it alone tracks its child creation requests (each closed by an `EntityConstructionCompleted` naming its `RequestId`, `LifecycleEvents.cs:114`). ⭐ The `InsertMacro` action (SR-93) only creates the instance and reads that status: `Expanding` ⇒ `Running`, `Done` ⇒ `Success`, `Failed` ⇒ `Failure` with its reason — ⛔ it never counts the parts. ⭐ STATE, not an event: readable on every node, survives save / restore / replay — the objective pattern (*"nobody calls it, everyone reads its status"*). ⚠ amended `2026-11-01` by SR-148: the node that processes the instance's creation request expands — any ECS node may; the original reading (user: *"there is already a concept of finding the least loaded node of certain role … practically there will be just one brain node, that this is where the macro expansion should take place"*): the role's least-loaded node, the existing pick (`RoleGroupOwnershipStrategy.cs:56` → `IClusterStateCache.GetLeastLoadedNode(role)`); in practice ONE Brain node — CGF (+ SimHost split), the all-in-one editor or the server) still describes who USUALLY creates, not a rule |

### 9.y The gaps, closed — the expander IS the creation path's child branch *(✅ ACCEPTED `2026-11-01` — user: *"Approved"*, with the note in SR-148)*

> 🔒 **User, `2026-11-01`:** *"Macro gaps"* (close them before S3) · earlier: *"Gap-X Leans accepted."* (the direction) · *"Macro expansion is
> … a standalone feature … the core system feature."* (SR-144)

**INVENTORY** *(grep `class CreateEntityRequestSystem|GenesisMaterializationSystem|ScenarioLoadStep|EntityCreationPack`, read the bodies; ⚠ coverage unchecked)*

| what exists | where | for the gaps |
|---|---|---|
| ⭐⭐ the creation path ALREADY expands children: a TKB template's `ChildBlueprints` become child `SpawnEntityCommand`s in the SAME pass as the parent — each with per-child overrides (`ChildComponentOverrides` → `(PreAllocatedId, Components)`), a fresh id (`_idAllocator.AllocateId()`) unless pre-allocated, a DERIVED GUID (`WithDerivedGuid(parentGuid, "child:<InstanceId>")`, CE-4153), and the parent's owner, init type, transience and spawn height INHERITED; seat presets use the same pattern (`"seat:<n>"`) | `Hrot.Common/Systems/CreateEntityRequestSystem.cs:296-575` | ⭐ the macro expander is THIS branch with a second SOURCE (SR-146) |
| it processes requests targeted at the LOCAL node (the owner), on every ECS node | the same file's header (Q65: *"processes a request targeted at the LOCAL node regardless of that flag"*); composed by `EntityCreationPack` (`Hrot.Common/EntityCreation/EntityCreationPack.cs:71`) | who reads macro files (SR-148) |
| ⭐ the ONE readiness owner of a scenario load: ① request queue drained · ② nothing `Constructing` · ③ every `Initial*Intent` (passengers, vehicle, hierarchy, targets, route, subordinate) resolved · plus the awaited requested ids | `Hrot.Core/Services/LoadPhase/ScenarioLoadStep.cs:280-305` (`DESIGN_Cluster_Load_Phase.md` — it replaced three drifted handlers) | when expansion is finished (SR-147) |
| ⭐ cross-entity references are resolved AFTER creation by one system, through transient intent DTOs it removes when done | `Hrot.EcsNode/Systems/GenesisMaterializationSystem.cs:31`, registered by `GenesisTail.cs:20` | `@Key` / GUID resolution rides it (SR-147) |
| ⛔ the old GAP-1 lean's anchor | CE-4087 is the MANDATORY-ROLES wait before a load (`ClusterMaster.Bootstrap.cs`), not an entity-construction wait | corrected here |

| # | decision | ⭐ lean |
|---|---|---|
| **SR-146** ✅ `2026-11-01` | ⭐⭐ ONE child-spawn routine, two sources | the `Expander` is `CreateEntityRequestSystem`'s child branch GENERALISED: it takes child definitions from a TKB template's `ChildBlueprints` OR from a macro file's entities, and spawns each the same way — overrides, fresh id, derived GUID (`macro:<local GUID>` beside today's `child:<id>` / `seat:<n>`), owner / init type / transience inherited, plus the macro steps (bind · resolve · relocate, §10). ⛔ never a second spawning loop (one implementation per concept). A nested macro is a child that is itself an instance — the same branch recurses |
| **SR-147** ✅ `2026-11-01` | GAP-1 — when expansion is finished | ⭐ BY CONSTRUCTION: the children are ordinary creation requests issued in the parent's pass, so `ScenarioLoadStep` ①–② already wait for them, as for TKB children today. ⭐ `@Key` / GUID references are carried as one more transient intent (`InitialLinksIntent`) that `GenesisMaterializationSystem` resolves once every entity exists — so condition ③ waits for it too, and a missing key fails there loudly (SR-66). ⭐ A runtime `InsertMacro` reads `MacroExpansionStatus` (SR-96): `Done` when every child's `EntityConstructionCompleted` arrived and its links resolved. ⚠ The one thing to prove with a rail: no frame where the queue is empty and the children's commands are still in flight |
| **SR-148** ✅ `2026-11-01` · amended | GAP-2 — who reads the macro files | the node that PROCESSES the instance's creation request — ⭐ ANY ECS node can create entities, so ANY node may read and expand a macro; ⛔ nothing depends on it being the Brain (user: *"the scenario expansion should not be made dependent on the brain for no good reason"*); the Brain is merely who usually creates (scenario load, a trigger's `InsertMacro`), the editor's world when it creates locally. ⭐ The orchestrator therefore deploys the macros package to EVERY node (SR-80, one zip — user: *"we never know which one we'll need to load them"*); ⛔ a node that only receives ghosts never reads a macro |
| **SR-149** ✅ `2026-11-01` | GAP-3 — a missing macro, a wrong binding type, a cycle | ⭐ a VALIDATOR runs BEFORE anything is created: on a load, inside the creating node's PREPARE (the load is two-phase — a failed prepare aborts it on every node, nothing half-built); on a runtime `InsertMacro`, before the first child request. It walks the instance tree: the macro file exists · every `bind` names a declared param of the right type · no macro appears twice on one path (a cycle) · depth ≤ 8. The reason names the path (*"Blue Platoon / Squad 2: macro 'FireTeam' not found"*). A runtime failure sets `MacroExpansionStatus = Failed` with that reason and creates nothing |
| **SR-150** ✅ `2026-11-01` | GAP-4 — editing an instance's args | ⭐ RECONCILE by derived GUID, not destroy + recreate: a child whose GUID persists KEEPS its entity and network id and gets its components re-applied; a new GUID is created; a vanished one destroyed; a child whose TKB type changed is destroyed and created. ⇒ every reference from outside survives — an authored `EntityLink` by its GUID, an internal `EntityRef` by its unchanged id. The same path serves a live edit (SR-109, this run only) |
| **SR-151** ✅ `2026-11-01` | GAP-5 — ids | ⭐ as TKB children today: a fresh id from the allocator per child, or the override's pre-allocated id; a macro file's own ids are only file-local keys (SR-45); width per CE-4200 (`int32`) |

```mermaid
sequenceDiagram
    participant L as load PREPARE / InsertMacro (owner node)
    participant V as validator
    participant C as CreateEntityRequestSystem (owner node)
    participant G as GenesisMaterializationSystem
    participant S as ScenarioLoadStep
    L->>V: the instance tree (files, binds, cycles, depth)
    V-->>L: ok — or FAIL: the prepare aborts / status Failed, nothing created
    L->>C: the instance's creation request
    C->>C: child branch: TKB ChildBlueprints OR the macro's entities — bind · resolve literals · relocate · derived GUID · fresh id
    C->>C: one SpawnEntityCommand per child, same pass (a child instance recurses)
    G->>G: once all exist — resolve InitialLinksIntent (@Key / GUID), remove it
    S->>S: ① queue drained · ② none Constructing · ③ no Initial*Intent left ⇒ load done
    C-->>L: MacroExpansionStatus Done (runtime InsertMacro reads it)
```

*What the picture shows that prose hid:* nothing new waits for anything — the load step's existing three conditions cover macros because the
children travel the TKB children's road, and reference resolution is one more intent for the existing resolver.

```mermaid
graph TD
    subgraph every["every ECS node — EntityCreationPack (shared bootstrap)"]
        CERS["CreateEntityRequestSystem — child branch = the Expander (SR-146)"]
        GEN["GenesisMaterializationSystem (GenesisTail)"]
        LS["ScenarioLoadStep (the load part)"]
    end
    OWNER["the instance's OWNER node processes its request"] --> CERS
    CERS --> GEN --> LS
    GHOST["a node that only receives the instance as a ghost"]:::dead -. "never expands, never reads a macro" .-> CERS
    classDef dead fill:#fde2e2,stroke:#c33
```

*Caption:* every node composes the expander (it is the creation path), but only the instance's owner runs it — the red edge is the path that
must stay dead, or a macro would expand once per node.

**Rejected — one line each:**
- *A separate macro-expansion system beside the creation path* — a second spawning loop that would drift from the TKB children's (ids, GUIDs, ownership, transience).
- *A new "expansion finished" barrier* — `ScenarioLoadStep` already waits for exactly this, by construction.
- *Resolving `@Key` in a pass of the expander* — `GenesisMaterializationSystem` already resolves cross-entity references after creation.
- *Validating while creating* — a failure halfway leaves a half-built macro; the prepare / pre-flight leaves nothing.
- *Destroy + recreate on an args edit* — every outside reference would dangle.

## 10. Relocation — placing a macro anywhere *(moved here `2026-11-01`; ✅ SR-152…SR-157 ACCEPTED — user: *"Approved."*)*

🔒 **User, `2026-11-01`:** *"every node can potentially create entities, so it might mean that every node can potentially read and expand scenario macros if that happens to be performed by that node. It likely doesn't mean anything new for us, just that the scenario expansion should not be made dependent on the brain for no good reason. This is also the reason why the scenario macros should be deployed to every node by the orchestrator as we never know which one we'll need to load them. And the macro relocation should be made part of this design, I guess. Until then, I don't think it's buildable."*

⭐ The TYPES relocation moves (`WorldPoint`, `WorldRotation`, `Bearing`, `PointLink`, `AreaLink`, `EntityLink`) and the ONE walker that finds them are `DESIGN_Scenario_Rules.md` §9.8 (built by slice S0 — its as-built is §9.8.11: the walker is one type plan with three appliers, remap · resolve · **`Relocation`**). This section owns WHEN and HOW a macro is relocated.

### 10.1 The sequence *(moved from `DESIGN_Scenario_Rules.md` §9.8.2)*

```mermaid
sequenceDiagram
    participant X as Expander (one macro instance)
    participant J as each entity's JSON (components, params, when, do)
    participant P as TypePlan
    participant C as creation path
    X->>J: ① bind — every "bind" map and ParamBindings: copy the macro / scenario param values in
    X->>P: ② resolve — every EntityLink / PointLink / AreaLink written as GUID or @Key
    P-->>J: EntityLink to a network id · PointLink to that entity's position · AreaLink to its polygon
    X->>P: ③ relocate — every LITERAL point / area, every WorldPoint, WorldRotation and Bearing
    P-->>J: anchor transform applied · the few untyped vectors by their relocators
    X->>C: ④ create the entity (live form: ids and metres only)
```

*What the picture shows that prose hid:* RESOLVE runs BEFORE RELOCATE, so a point that came from a reference is never moved — that is the
"do not relocate this" switch the user asked for, with no flag. The FILE keeps `@Key` / GUID; only the live copy holds resolved numbers.

### 10.2 What is measured

| fact | where |
|---|---|
| ⭐ the `Relocation` applier is BUILT (S0): translate X / Y, turn about the anchor by the heading difference (compass clockwise), Z by the anchors' height difference, `WorldRotation` pre-multiplied, `Bearing` + turn; a link written as an entity is not moved | `DESIGN_Scenario_Rules.md` §9.8.11 (backend, `2026-11-01`), rails `S0WalkerTests.Relocate_*` |
| route waypoints are absolute world points (`RouteWaypoint.Position` `Vector3`) ⇒ a `WorldPoint` after S0 item 6 ⇒ moved by the walker by type | `Hrot.Core/Components/Map/RoutePlan.cs:14-20, :70-74` |
| ⭐ an area entity's outline is RELATIVE to its `SimTransform` position (`WeatherField` adds the origin, `:207`; S0 as-built: *"an area entity's outline is relative to its SimTransform"*) — but the outline is NOT rotated by `SimTransform.Rotation` | `Fdp.Toolkits/Weather/WeatherField.cs:201-243` |
| the creation path spawns children in the parent's pass and any ECS node can process a creation request targeted at it | `CreateEntityRequestSystem.cs:296-575`; its header (Q65) |

### 10.3 Decisions

| # | decision | ⭐ lean |
|---|---|---|
| **SR-127** *(moved)* | the relocation transform | the macro file records its AUTHORED ANCHOR (position + heading — where it was built, on real terrain); every entity keeps its authored coordinates; placing an instance applies *authored anchor → instance anchor* (translate X / Y, rotate by the heading difference, Z + the anchors' height difference). Heights ABOVE GROUND (`RoutePlan.HeightAgl`) unchanged. Vehicles settle through the existing ground clamp; ⚠ dismounted soldiers — measure with one rail before building. A macro whose plan needs particular buildings declares `relocatable: false`: its instance is pinned at the anchor and the editor refuses the drag, saying why |
| **SR-131** *(moved)* | not relocating a value | ⭐ the user's rule, by construction: a LITERAL point moves with the macro; a REFERENCE does not (resolved first — step ② of the sequence above). A point entity INSIDE the macro moves (it is one of the macro's entities); one OUTSIDE, passed in through a param, stays where it is. ⛔ no `relocate: false` flag per field |
| **SR-152** ✅ `2026-11-01` | where the anchors live | the AUTHORED anchor is in the macro file's header (`anchor: {position: [x,y,z], heading: deg}`, written when the macro is saved — the editor's *Save selection as macro* takes the selection's centre and heading 0); the INSTANCE anchor is the `MacroInstance` entity's `SimTransform` (position + `WorldRotation`'s heading). ⇒ the transform is computed ONCE per instance, at the start of its expansion |
| **SR-153** ✅ `2026-11-01` | when, and by whom | step ③ of the expansion (§10.1), inside the creation path's child branch (SR-146), per child, after bind and resolve — by WHICHEVER node processes the instance's creation request (SR-148 as amended: not the Brain by rule) |
| **SR-154** ✅ `2026-11-01` | what moves | ⭐ by TYPE through the walker: every `WorldPoint` / `WorldRotation` / `Bearing` in a child's components and params (after S0 item 6 that is `SimTransform`, `NavigationIntent.FinalDestination`, `RouteWaypoint.Position`, engagement bearings …) and every LITERAL `PointLink` / `AreaLink`. ⭐ Two relocators, for what the walker cannot see by type: **`EditablePolyline`** — its points are relative to the entity's position, so they only TURN by the heading difference (the position moves with `SimTransform`); **`SimVelocity.Linear`** — a direction, turned only. ⭐ Cleared: `NavState`'s road / trajectory ids (they name terrain features of the authoring place). ⇒ a rail fails on a saved component with a vector member that is neither a typed point nor one of these |
| **SR-155** ✅ `2026-11-01` | nesting | a child that is itself an instance is relocated like any child (its anchor moves with the parent); ITS children are then relocated from its OWN authored anchor to its now-moved instance anchor — the transforms COMPOSE, nothing special |
| **SR-156** ✅ `2026-11-01` | a pinned macro (`relocatable: false`) | the expansion uses the IDENTITY transform whatever the instance's `SimTransform` says, and a load whose instance stands elsewhere is REFUSED loudly by the validator (SR-149: *"macro 'VillageDefence' is pinned at its anchor"*); the editor draws the instance at the anchor and refuses the drag, saying why |
| **SR-157** ✅ `2026-11-01` | moving an instance | the editor's drag or a changed heading is an anchor change ⇒ RECONCILE (SR-150): every child keeps its entity and id and gets its relocated values re-applied. ⭐ A runtime `InsertMacro` names its anchor in its args — `at: PointLink` (coordinates or `@Key`) + `heading: Bearing` (or `facing: PointLink`) — the instance entity is created there and expands like any other. ⚠ Still open, one rail before the build: do dismounted soldiers settle on the ground after a Z change, as vehicles do through `GroundClampWhilePausedSystem` / `GroundClampingConfig`? |

**Rejected — one line each:**
- *Anchors at (0,0) — macros authored around an origin* — authors would build on empty ground or convert coordinates (SR-127).
- *Rotating area outlines by `SimTransform.Rotation` at draw time* — every outline reader would change; turning the stored points once at expansion keeps one frame.
- *A per-field `relocate: false`* — a reference already means the world, not the macro (SR-131).
- *Relocating on the Brain only* — any node that creates the instance expands it (user).

## 11. Rows that stayed in `DESIGN_Scenario_Rules.md` — macro-adjacent, owned by the rules

| id | where | why it stays (and what it says about macros) |
|---|---|---|
| SR-31 | rules §8.3 | the objective state and tree on the wire |
| SR-33 | rules §8.4 | an objective's params are edited by the existing form; a macro INSTANCE's args use SR-44a's rows (§6 here) |
| SR-37, SR-51 | rules §9.2, §9.3 | which brain slot runs an objective's evaluating behaviour |
| SR-47 … SR-49 | rules §9.3 | an objective's two links, replication, position |
| SR-52 … SR-56 | rules §9.4 | the ORBAT, `ObjectiveLinks`, `Parent` (② a macro's ROOT objective), `Subject` (a macro param by default), the Objectives view |
| SR-61 | rules §9.5 | groups by the command chain — a macro's roster can change without touching the objective |
| SR-67 | rules §9.6 | per-member thresholds — nearest enclosing macro instance's `memberArgs` wins, the `RefKey` lexical rule (SR-65) |
| SR-71 | rules §9.2 | the World Environment saved in the header |
| SR-94 | rules §10.13 | a `do` entry carries its OWN `bind` — now the typed `Bind` of a `BindableNode` (SR-160, which supersedes SR-129's *"any JSON object"*) |
| SR-96 | rules §10.13 | ⚠ a macro insertion reports its own completion — `MacroExpansionStatus`, ONE writer = the node that expands it (SR-148 — not the Brain by rule); macro MECHANISM, kept there for now (its context is `InsertMacro`) — a candidate to move here |
| SR-99 | rules §10.13 | a director IS a macro of objectives + triggers |
| SR-101 | rules §10.13 | run variants — `presence` on a macro instance, `InsertMacro` / a macro choosing ONE of N |
| SR-125 … SR-143 | rules §9.8 | relocation and the reference types (§10 here) |

## 12. Retired decision ids *(macro ids; each also stays listed in `DESIGN_Scenario_Rules.md` §12)*

| id | replaced by |
|---|---|
| SR-25 | SR-38 (the block template `*.objective.json` → a scenario macro) |
| SR-26 | SR-39 / SR-39a (inserting = an ordinary macro-instance entity) |
| SR-34 | SR-38 (the macro file) |
| SR-35 | SR-40 / SR-57 / SR-94 / SR-129 (bindings beside the value) |
| SR-36 | SR-37 (typed kinds; no runtime-typed sub-form) |
| SR-42 | SR-42′ (transient full entities, not `PartMetadata` parts) |
| SR-46 | SR-63 (references by RefKey/GUID, not instance-local ids) |
| SR-50 | `DESIGN_Scenario_Entity_Keys.md` rev 2 (the entity key stays a — now stable — GUID) |
| SR-59 | SR-63 (names are labels; reusable refs use `RefKey`) |
| SR-62 | SR-63 (a GUID is the exact form; cross-scope reuse goes through `RefKey` params) — `DESIGN_Scenario_Entity_Keys.md` rev 2 G1–G6; it was only ever an open question (user: *"It was a question, not a imperative"*) |

## 13. Where the old section numbers went *(the moved rows cite these verbatim)*

| was (`DESIGN_Scenario_Rules.md`) | now |
|---|---|
| §8 intro quote, §8.0 | §4, §4.0 |
| §8.1 (the template / layered-params / child-entity / include rows) | §2.1 |
| §8.3 SR-27, SR-28, SR-32 · §8 rejected (macro lines) | §4.1 · §4.2 |
| §9 intro quotes | §0 |
| §9.1 | §2.2 |
| §9.2 diagram · rows | §3 · §5 |
| §9.3 | §6 |
| §9.4 SR-57 | §5 |
| §9.5 | §7 |
| §9.6 | §8 |
| §9.7 | §9 |
| §9.8 | not moved — still `DESIGN_Scenario_Rules.md` §9.8 (§10 here) |

## 14. Critical review — what the design got wrong or left out *(`2026-11-02` — ✅ SR-167 … SR-176 ACCEPTED — user: *"Approved."*)*

> 🔒 **User, `2026-11-02`:** *"Approved. Please take a wider critical look at the scenario macro feature and find gaps and flaws."*

**INVENTORY** *(`2026-11-02`: read `CreateEntityRequestSystem.cs` :340-660 (the request + child branch); `ScenarioSaveCore.cs` :11-35;
`DESIGN_Distributed_Scenario_Persistence.md` §6 (I1); grep `OwnerAppInstanceId =` over `Hrot/Engine` + `Hrot/Subsystems`; grep `InsertMacro` /
`MacroOrigin` over the rules and macros docs; graph `.*(Rewind|Baseline|Revert).*` Class (17 — none is an authoring-vs-live split of the world);
⚠ coverage unchecked — `check_index_coverage` is not in the CLI)*

| measured | where | breaks |
|---|---|---|
| ⛔ **a TRANSIENT request gets NO GUID** — the GUID is minted only `if (!pending.Request.IsTransient)`, and `WithDerivedGuid` adds nothing when the parent GUID is empty | `CreateEntityRequestSystem.cs:352-366`, `:641-647` | macro children are transient (SR-42′) ⇒ no derived GUID ⇒ SR-150 reconcile, an Exact ref into a macro and nested derivation have nothing to key on |
| ⛔ **children INHERIT the parent's transience** (*"a sketch's auto-spawned TKB children are part of the same sketch"*) | `CreateEntityRequestSystem.cs:439`, `:544-546` | a SAVED instance would give SAVED children ⇒ the expansion is written into the file and expanded again on the next load |
| ⛔ **save writes the LIVE components** of every entity a node is primary owner of (`IsPrimaryOwner AND NOT ScenarioIgnoreTag`), from the editor or any node | `ScenarioSaveCore.cs:11-19`; persistence §6 I1 | a top-level entity is BOTH authored and live ⇒ a scenario-level binding (SR-41) applied at load is overwritten into a literal by the next save, and SR-165 has already removed its `ParamBindings` |
| ⛔ **nothing links a TKB child to its parent** except the command hierarchy (`InitialUnitSubordinateIntent`); `SubEntityCleanupSystem` serves PARTS only | `CreateEntityRequestSystem.cs:512-519`; §6 row 2 | SR-39a (*deleting the instance removes its entities*), SR-43 (shown under the instance) and the reconcile scope have no mechanism |
| ✅ **ownership is already right** — the child branch gives children the parent's owner + their own template's `DeferredTakeOwnership` grants, exactly what a top-level scenario entity gets (load requests are `Owner = 0`) | `CreateEntityRequestSystem.cs:407-460`, `:555-575`; `StagingEntityExtractor.cs:379` | nothing — split authority survives expansion |
| ⚠ a runtime-inserted instance has no transience rule; the game-master's live additions lean transient | `DESIGN_Scenario_Rules.md` SR-104 Z6 | an `InsertMacro` mid-run, then a save ⇒ the instance is saved and inserted again on the next run |

```mermaid
classDiagram
    direction LR
    class ChildBranch { <<existing, CreateEntityRequestSystem>> one spawn loop, two sources (SR-146) }
    class TkbSource { <<existing>> ChildBlueprints — transience INHERITED · GUID child:id }
    class MacroSource { <<NEW>> macro file entities — ALWAYS transient · GUID ALWAYS derived macro:local }
    class EntityGuid { <<existing, CE-4153>> IDENTITY — who it is }
    class ScenarioIgnoreTag { <<existing>> SAVE — whether the file holds it }
    class MacroOrigin { <<NEW, unmanaged, replicated>> EntityRef Instance · Guid Local }
    class MacroInstance { <<NEW, immutable record>> macro path · args }
    ChildBranch --> TkbSource
    ChildBranch --> MacroSource
    MacroSource ..> EntityGuid : always
    MacroSource ..> ScenarioIgnoreTag : always
    MacroSource ..> MacroOrigin : stamps on each child
    MacroOrigin --> MacroInstance : Instance
```

*What the picture shows that prose hid:* today ONE flag (`IsTransient`) decides two things — whether an entity has an identity and whether it is
saved. A macro child needs the identity WITHOUT the save, so the macro source must set them apart; and only an explicit `MacroOrigin` lets
anything find a macro's entities again.

| # | finding → decision | ⭐ lean |
|---|---|---|
| **SR-167** | ⛔ transient ⇒ no GUID (measured above) | ⭐ IDENTITY and SAVE are two axes: the macro source ALWAYS stamps `Derive(instanceGuid, "macro:<local GUID>")`, whatever the transience; a nested instance derives from its derived GUID. Sketches keep no GUID (unchanged). A runtime-inserted instance (SR-171) derives its GUID from the inserting trigger's GUID + its insertion count, so replay and a branch get the same ids. Rail: two instances of one macro ⇒ distinct, stable child GUIDs on every load and node |
| **SR-168** *(amends SR-164, SR-165)* | ⛔ a scenario-level binding is lost on the next save (measured above); the same holds for a macro FILE opened in the editor | ⭐ the binder is NON-DESTRUCTIVE — it fills the live value and KEEPS the binding: ① a `Param`-form `RefSpec` keeps `Form = Param` and gets its live answer from the param's resolved value (as `@Key` keeps its key); ② `Bind` stays on its node; ③ `ParamBindings` stays on the entity (`NoReplay`). ⭐ It is removed only from TRANSIENT macro children, which are never saved. ⇒ a save writes the current value as the literal PLUS the binding, and the binding wins on the next load. ⇒ the editor MAY apply bindings (SR-164's *"must deserialise without applying"* is relaxed — applying is now harmless). Rail: load a scenario with params → save → every binding still there |
| **SR-169** | ⛔ children inherit transience (measured above) | ⭐ the macro source sets `IsTransient = true` on every child; the TKB source keeps inheriting. Rail: load → save writes the instance and ⛔ none of its entities |
| **SR-170** | ⛔ no child → instance link (measured above) | ⭐ `MacroOrigin {EntityRef Instance; Guid Local}` on every expanded entity — unmanaged (CLAUDE.md *"NEW COMPONENTS"*), replicated, recorded. ⭐ It serves: destroying an instance destroys every entity naming it (recursively, each through its owner); SR-43's tree and read-only marking; SR-150's reconcile scope; SR-96's tracking. ⛔ not `PartMetadata` (§6) |
| **SR-171** | ⚠ a runtime-inserted instance gets saved (above) | ⭐ an instance created during a RUN (`InsertMacro`, the game-master's Z1) is TRANSIENT — it and its entities never reach the file; the trigger that inserted it is what is saved (consistent with SR-104 Z6) |
| **SR-172** | ⛔ a live args edit RESURRECTS destroyed children — SR-150 creates *"a new GUID"*, and in a run a child killed and removed looks exactly like a new one | ⭐ the reconcile compares the OLD args' child set with the NEW args' child set (both computed from the file), ⛔ not with what currently exists: a child is created only if its GUID is new to the new set; one that the run destroyed stays destroyed. No tombstones |
| **SR-173** | ⛔ the macro's id and layout disagree — SR-38 says `macros/<name>/macro.json`, §5.1.1 says `macros/forces/RedQRF.macro.json`, and `MacroInstance.macro` is a bare name although `macros/` is a tree | ⭐ the id is the RELATIVE PATH under `macros/` (`forces/RedQRF`), like scenarios' rel paths (`ScenarioEnumeration.EnumerateRelPaths`); one layout, `macros/<path>/scenario.json` (a folder per macro, as for scenarios, so it can carry assets — ⚠ amended by §14.1 ①: the file keeps the ONE name `scenario.json`); §5.1.1 is corrected to it |
| **SR-174** *(restates SR-27)* | ⚠ SR-27 named the translator list — which also runs at GHOST PROMOTION — as the expander | ⭐ expansion runs ONLY where a creation REQUEST is processed (SR-146); ⛔ never in a translator. Rail: a promoted ghost instance creates nothing |
| **SR-175** | ⚠ `MacroInstance` has no wire, recording or edit-routing design (only `MacroExpansionStatus` is called replicated) | ⭐ `MacroInstance` is an immutable record written once (keyframes only); replicated as a reliable descriptor (an internal type + its wire twin, R-134) so a UI on a non-owning node shows it and routes an args edit to the owner (`EntityWriteRouter`). `MacroExpansionStatus` is unmanaged (`State` + a `FixedString128` reason) |
| **SR-176** | ⚠ a run's scenario args are not recorded — the recording holds the entities, not which args produced them | ⭐ `RecordingMetadata.ScenarioArgs` beside `FeaturesAtLoad` (`DESIGN_Node_Features.md` §6) — an evaluation report and *"run it again with the same setup"* read it |

⭐ **The four preconditions** (the editor and `macros/`, the tree, the map symbol, soldiers' height) are MEASURED in §14.1 below.

⭐ **Editor work this adds** (ui, after S3a): *Save selection as macro* must turn every reference to an entity OUTSIDE the selection into a
param (a macro is closed, SR-58) — or refuse, naming them. Filed with CE-4210.

**Rejected — one line each:**
- *Keep one `IsTransient` flag and give transient entities GUIDs everywhere* — sketches and other throwaway entities would get identities nobody uses; only the macro source needs both.
- *Strip bindings at load and keep a side copy for the save* — a second store of one fact (R-132); the non-destructive binder needs none.
- *Find a macro's entities by recomputing their GUIDs* — works for reconcile, not for a destroyed instance on another node or for the tree; an explicit link is cheaper and visible.
- *Tombstones for destroyed children* — the old-vs-new args diff answers the question with no state.

### 14.1 The four preconditions, MEASURED *(`2026-11-02` — leans SR-177 … SR-179 await the user)*

> 🔒 **User, `2026-11-02`:** *"Pls make the missing measurements"*

**Queries:** grep `ResolveScenarioDirectory(` (5 callers) and `"scenario.json"` · graph `.*(EntityTree|Orbat|Hierarchy|Outliner).*(Panel|Window|View|Model|Builder)$`
Class (1: `SharedOrbatPanelWindow`) and `.*(EntityList|EntityBrowser|SceneTree|EntitiesPanel|EntityPicker)\w*$` Class (0) · grep `ScenarioIgnoreTag`
(22 files, ⛔ none a panel) · read `ScenarioOrbatAdapter.cs`, `NotAUnit.cs`, `EntityClassShapeLibrary.cs`, `DebugPrimitiveRenderer2D.cs:500-540`,
`SpawnHeight.cs`, `GroundClampWhilePausedSystem.cs`, `CarKinematicsSystem.cs:135-147`, `InfantryVehicleStateStripTkbTranslator.cs` · ⚠ coverage unchecked

| # | the question | measured | ⇒ |
|---|---|---|---|
| **①** | can the scenario editor open and save a file under `macros/`? | ⭐ every NAS reader resolves a scenario NAME through the ONE resolver `ContentLayout.ResolveScenarioDirectory` — the prefetch (`StorageGatewayModule.cs:327`), the load (`ClusterMaster.cs:1832`), the save merge (`StorageProcessManager.cs:217`), the inventory (`AssetInventoryProcessManager.cs:292, :375`). ⭐ Two non-`scenarios/` roots already work this way, by a NAME PREFIX: recipes (`recipes/scenarios/<seed>`) and opened copies (`opened/…`) (`ContentLayout.cs:128-140`). The file in the folder is always `scenario.json` — ⚠ spelled as a literal in FIVE places (`EditorScenarioSession.cs:55`, `HrotScenarioSaveHandler.cs:40`, `StorageProcessManager.cs:250`, `ScenarioEnumeration.cs:55`, `ScenarioNewAssetService.cs:171`) | ✅ **yes, with one arm in the resolver**: a macro is the scenario NAME `macros/<path>` ⇒ open, load, save and prefetch follow with no other change. ⇒ SR-173 amended: the file is `macros/<path>/scenario.json` (⛔ not `macro.json` — one file name), `header.kind = macro`. ⚠ The *Open* listing enumerates only `scenarios/` (`ContentLayout.cs:46-50`) ⇒ an *Open macro* listing over `macros/` (same `ScenarioEnumeration`) — ui, with CE-4210. The five literals → CE-4211 |
| **②** | does the entity tree show expanded entities under their instance? | the ONE entity tree is the ORBAT (`SharedOrbatPanelWindow` → `ScenarioOrbatAdapter`): every entity with `EntityInfo`, parent = `UnitSubordinate.Commander` ONLY (`ScenarioOrbatAdapter.cs:19, :61-75`); drag assigns a commander (`:230-260`). ⭐ No UI reads `ScenarioIgnoreTag` | ⚠ **shown, but not under the instance** — as roots, or under their commander. ⇒ SR-177 |
| **③** | does a non-unit positioned entity get a map symbol? | every positioned entity except the map camera, a round and a cloud is drawn, picked and labelled as a unit (`NotAUnit.cs`); its outline comes from its DIS class chain (`EntityClassShapeLibrary.ProfileOfClass`, R-280's one class table); an UNMAPPED class draws a fallback rectangle (`DebugPrimitiveRenderer2D.cs:519-540`) — still pickable and draggable | ✅ **SR-70 works as soon as the instance exists** (a rectangle). ⇒ SR-178 for a proper marker |
| **④** | do dismounted soldiers land on the ground after relocation? | ⭐ the CREATION path already chooses each entity's birth Z: `SpawnHeight` = `OnLevel(n)` (the n-th surface at its own x, y — 0 = the ground, also inside a building) or `AboveLevel(n, h)`, resolved by `NetworkSpawningSystem` through `TerrainWorld.ResolveLevel`; with no terrain resident the sent Z stands (`SpawnHeight.cs`); children inherit the request's (`CreateEntityRequestSystem.cs:547`). Afterwards persons stay on the surface: on the shared muscle they are movement-model bodies (they keep `VehicleState`; the paused clamp covers *"a vehicle or person"*, `GroundClampWhilePausedSystem.cs:6-12`), on the Stride muscle crowd agents (`InfantryVehicleStateStripTkbTranslator`) | ✅ **yes — at birth, by the existing mechanism**. ⇒ SR-179 replaces SR-127's *"Z + the anchors' height difference"* |

| # | decision | ⭐ lean |
|---|---|---|
| **SR-177** | the ORBAT and a macro | ⭐ the tree's parent = the commander if there is one, ELSE `MacroOrigin.Instance` (SR-170) — a macro's top units sit under the instance, their subordinates under them; the instance carries `EntityInfo` (its name). ⭐ Dragging a macro entity to a new commander is refused with SR-43's *"from macro X — edit the macro or its args"* |
| **SR-178** | the instance's map marker | ⭐ ONE class in `EntityClassTable` (`ScenarioMacro`) + ONE `ProfileOfClass` row — the R-280 table every display reads; until then the fallback rectangle (pickable, draggable) is enough to build S3a |
| **SR-179** *(amends SR-127)* | a relocated child's Z | ⭐ a relocated child of a GROUNDED type (its template clamps — `GroundClampingConfig.BaseRequiresClamping = 1`, persons and vehicles) is born with `SpawnHeight.OnGround` at its new x, y; a non-grounded one (an aircraft) keeps *authored Z + the anchors' height difference*. ⭐ A PINNED macro (SR-156) uses the authored Z as is. ⇒ uneven terrain, slopes and a soldier on open ground all land right, with no new mechanism. ⚠ A soldier authored on an upper FLOOR of a building lands on the ground at a new place — correct, the building is not there |

### 14.2 The user's answers to §14.1, and PLACEMENT at a new place *(`2026-11-02` — ✅ SR-180 … SR-183 ACCEPTED, SR-180 amended by SR-184 §14.3)*

> 🔒 **User, `2026-11-02`:** *"being created from macro doesn't change the commander hierarchy, so the current orbat panel has no need to somehow
> reflect the entity that are created by macros … I think we need some other hierarchy view"* · *"Yes, to a specific map marker for a macro
> entity."* · *"regarding how entities get created from the macro. This might not be trivial, because there might be specific rules how to create
> entities at places that are not prepared or ready or suitable for placing a new entity. Like if we create a platoon on the beach, half of the
> platoon would be standing in the water, which is not desired. The same to create entities inside the buildings. That needs some design."* ·
> *"scenario macros are not units, just scenario part. They shouldn't be movable under a commander, so the refusal is okay."*

| id | outcome |
|---|---|
| **SR-177** | ⛔ **WITHDRAWN** — the ORBAT stays the command tree; macro entities are shown by the Scenario view of `DESIGN_Entity_Hierarchy_Views.md` (HV-1 … HV-5). ✅ The refusal half stands: a macro entity cannot be dragged under a commander |
| **SR-178** | ✅ **ACCEPTED** — one `ScenarioMacro` class in `EntityClassTable` + one `ProfileOfClass` row, a dedicated marker |
| **SR-179** | ⛔ **SUPERSEDED by SR-180 … SR-183** — *"born on the ground"* is only the height half; WHERE on the ground is the real question |

**INVENTORY** *(`2026-11-02`: graph `.*(Water|Surface(Type|Kind|Class)|Terrain(Type|Material)|Passab|Walkab|Traversab).*` Class (6) and
`.*(Placement|Placeable|SpawnPoint|SpawnSpot|Snap|NearestValid|FindValid|Footprint).*` Class (~60, mostly snapshots); grep `ProjectToNavmesh`,
`SurfaceTypeAt`, `INavmeshProvider`; read `DESIGN_Add_Entity_Picker.md` D6b; ⚠ coverage unchecked)*

| what exists | where | so |
|---|---|---|
| ⭐ the creating node already resolves a PLACEMENT INTENT carried on the request — `SpawnHeight {Mode, Level}`, resolved from its own terrain, the sent Z ignored | `DESIGN_Add_Entity_Picker.md` D6b; `SpawnHeight.cs`; `NetworkSpawningSystem` | ⭐ the pattern to EXTEND — a horizontal intent beside the vertical one |
| ⭐ nearest valid point on the navmesh, per agent layer (infantry / vehicle): `INavmeshProvider.ProjectToNavmesh(pos, out snapped, layerMask)` — the navmesh already excludes water (*"water = unwalkable"*), walls and too-steep ground, and includes building floors | `Navigation/INavmeshProvider.cs:28`; `DESIGN_Terrain_World.md:90`; `NavmeshGizmo.cs:13` (Infantry / Vehicle) | ⭐ "is this spot fit for THIS entity" is already answered |
| the terrain without a navmesh still knows ground cover: `TerrainWorld.SurfaceTypeAt(x, y)` → `Open \| Forest \| Water` | `Terrain/TerrainWorld.cs:17, :749` | the fallback check where no navmesh is resident |
| a navmesh is held on the SimHost (`SimHostNodeBootstrapper.cs:60`), the editor (`EditorSubsystem.cs:213`) and the shared terrain residency (`Hrot.Core/Services/TerrainResidency.cs:50`) | as cited | ⚠ whether the BRAIN node (who usually creates) keeps one resident — not measured |
| an entity's footprint (length × width) | `Diagnostics/Gizmos/EntityFootprint.cs` | spacing between placed entities |

```mermaid
sequenceDiagram
    participant X as Expander (child branch)
    participant R as PlacementResolver (NEW, shared)
    participant N as INavmeshProvider (existing)
    participant T as TerrainWorld (existing)
    participant C as creation (SpawnEntityCommand)
    X->>X: relocate the child's authored point to the instance anchor (§10)
    X->>R: Resolve(point, layer of its template, MaxShift, already taken spots)
    alt a navmesh is resident
        R->>N: ProjectToNavmesh(point, layer)
        N-->>R: the nearest walkable spot (dry, not inside a wall, not too steep)
    else terrain only
        R->>T: SurfaceTypeAt(point) is not Water, else search outward in rings
    end
    R-->>X: Exact · Shifted(by d m) · Failed(why)
    alt every child placed
        X->>C: create each at its resolved spot (SpawnHeight gives Z)
    else any child Failed
        X-->>X: expansion FAILS — nothing created, the reason names the child
    end
```

*What the picture shows that prose hid:* the placement check is a step BETWEEN relocation and creation that the whole macro passes or fails
together — so a platoon is never half in the water and half missing; and the check itself is the navmesh's own answer, not a new rule set.

| # | decision | ⭐ lean |
|---|---|---|
| **SR-180** | ONE placement resolver, not a macro feature | ⭐ `PlacementResolver.Resolve(point, layer, maxShift, taken)` → `Exact \| Shifted \| Failed`, in the shared creation code beside `SpawnHeight`; used by the macro expander, ⭐ and offered to Add Entity, the game-master placement (SR-104 Z1) and a runtime `InsertMacro` — ⛔ never a second placement check per tool. The LAYER comes from the child's template mobility: infantry, vehicle, amphibious (water allowed), air (always Exact) |
| **SR-181** | what "suitable" means | ⭐ the spot projects onto the navmesh of its layer within `maxShift` — so dry land, not inside a building's walls, not too steep, for that kind of entity. ⭐ With no navmesh resident: terrain only — not `Water` (unless amphibious), searched outward in rings. ⭐ Entities already placed in this expansion count as taken, spaced by their footprints, so two never land on one spot |
| **SR-182** | when a spot cannot be found | ⭐ the WHOLE expansion fails — nothing created, `MacroExpansionStatus = Failed` (runtime) or the load refused (validator, SR-149), naming the child and why (*"Blue Platoon / Rifleman 3: no dry ground within 30 m"*) — ⛔ never a partly placed group. ⭐ `maxShift` is per macro (`header.placement.maxShift`, default 30 m); a macro that must stand exactly where authored is `relocatable: false` (SR-156) and skips the resolver |
| **SR-183** | buildings, and the author's view | ⭐ a RELOCATABLE macro places on the ground: an indoor-authored child projects to the nearest walkable spot at the new place (usually outside — the building is not there). ⭐ Occupying a building at the new place is a BEHAVIOUR, not placement: the building comes in as a param (`EntityLink`) and an *occupy* order moves the units in after spawn. ⭐ In the editor, dragging an instance shows every child's ghost — green (exact), yellow with an arrow (shifted), red (no spot) — from the SAME resolver; dropping with any red is refused, saying which |

**Rejected — one line each:**
- *Spawn as authored and let the movement model sort it out* — a soldier born in the sea or inside a wall stays there; the navmesh never sees it.
- *Per-macro placement rules written by the author* — every macro would re-encode what the navmesh already knows.
- *Drop the children that do not fit* — a platoon silently missing a section is worse than a loud refusal.
- *Placement inside the expander only* — Add Entity and the game master need the same answer (one implementation).

### 14.3 Placement is a QUERY the navigation solver answers · many instances of one macro *(`2026-11-02` — leans SR-184 … SR-188 await the user)*

> 🔒 **User, `2026-11-02`:** *"approved the switchable hierarchy view. approved the relocation of entities to suitable place. Just that cannot happen
> on the brain, because the brain doesn't have the navmesh. The navmesh is part of the muscle component, the sim host usually. So the whole
> placement needs to be a query that the muscle node answers."* · *"showing the outcome when we drag the macro instance entity in the editor on the
> map. This idea is nice, but not sure what would it take. As a macro can create nested macro entities that needs to be expanded as well."* ·
> *"how do we handle instantiation of the same macro multiple times in the scenario? Do the entities expanded from that macro get different,
> unique GUIDs? And what happens to their ref keys? … the upper level scenario might want to reference entities by ref keys. So I would not refuse
> it. I mean the duplication. And just find the first matching entity."* · *"Entities should find the place outside of the building."* · *"yes to
> refusing to instantiate macro where some entity fails to find a suitable place."*

✅ **Accepted:** SR-180 … SR-183 (placement — with SR-184's amendment: it runs where the navmesh is) · HV-1 … HV-5 (`DESIGN_Entity_Hierarchy_Views.md`).

| measured `2026-11-02` | where |
|---|---|
| ⭐ the precedent: a Brain request answered by the NAVIGATION SOLVER over DDS, batched, keyed by a request id, points as ENU offsets from a batch origin (AQ107 B) — `PathRequestBatch` → `PathResponseBatch`, four translators (Brain egress / Solver ingress / Solver egress / Brain ingress); in-process the same pair is the `PathfindingRequestEvent` / `PathfindingResultEvent` bus events | `Hrot.Network.NED/SimHost/PathfindingTranslators.cs:27, :127, :233, :313`; `Navigation/PathfindingEvents.cs:7-19`; `NavigationSolverModule` |
| the navmesh lives with the solver: the SimHost (`SimHostNodeBootstrapper.cs:60`) and the editor (`EditorSubsystem.cs:213`) | as cited |

```mermaid
sequenceDiagram
    participant X as Expander (the creating node, e.g. Brain)
    participant S as Navigation solver (SimHost / editor) — NavigationSolverModule
    participant G as ScenarioLoadStep
    X->>X: FLATTEN the whole instance tree from the files — nested macros, composed transforms, every child's relocated point + layer
    X->>X: the instance gets MacroExpansionStatus = Expanding and an InitialPlacementIntent (the load waits on it)
    X->>S: PlacementRequestBatch { RequestId, origin, slots[ point, layer, footprint ] , maxShift }
    S->>S: per slot in order: ProjectToNavmesh(point, layer) · ring search · spacing against the slots already placed
    S-->>X: PlacementResponseBatch { RequestId, per slot: Exact | Shifted(point) | Failed(reason) }
    alt every slot placed
        X->>X: create every child at its spot, nested instances included, in ONE pass · remove the intent
    else any slot Failed
        X->>X: create nothing · status Failed, reason names the child · a load's prepare is refused
    end
    G->>G: condition ③ (no Initial*Intent left) already waits for the intent
```

*What the picture shows that prose hid:* nesting costs nothing extra — the creating node works the whole tree out from the FILES before
anything exists, so ONE request carries every child of every level, and the load gate waits for it by the condition it already has.

| # | decision | ⭐ lean |
|---|---|---|
| **SR-184** *(amends SR-180)* | WHERE placement runs | ⭐ on the node that has the navmesh — the NAVIGATION SOLVER (`NavigationSolverModule`, usually the SimHost; in the editor its own) — as a request / response pair beside the path one: `PlacementRequestBatch` → `PlacementResponseBatch` (wire twins of internal events, R-134; ENU offsets from a batch origin like `PathRequestBatch`). The creating node only ASKS. ⭐ In one process (editor, `--mode all`) the same events go over the in-process bus — one solver code path. ⚠ No solver in the cluster ⇒ a terrain-only check on the creating node (not `Water`, SR-181), stated in the status |
| **SR-185** | nested macros, and timing | ⭐ the creating node FLATTENS the whole instance tree first (pure computation from the macro files — transforms composed, SR-155) and sends ONE batch for every grounded child at every level; on the answer it creates everything in one pass. ⭐ The wait is an `InitialPlacementIntent` on the instance, so `ScenarioLoadStep` ③ waits by construction (SR-147's pattern) and a runtime `InsertMacro` stays `Expanding` until the answer. ⇒ ONE round trip per expansion, whatever the depth |
| **SR-186** | the editor's drag preview | ⭐ NOT in v1: a drop is a real re-expansion (SR-157 reconcile), which places through the solver and shows the outcome — the entities at their spots, or a refusal naming the child that has no spot. ⏭ The live ghost while dragging is the SAME request (SR-185 makes it one call), added later if wanted — S3b |
| **SR-187** | many instances of one macro — GUIDs | ✅ already unique by construction: a child's GUID is `Derive(instance GUID, "macro:<local GUID>")` (SR-167) and every instance has its own GUID, so two instances never collide; a nested instance's children derive from ITS derived GUID, so the same macro twice inside another macro is unique too |
| **SR-188** *(amends SR-65, SR-66)* | many instances — `RefKey`s | ⭐ duplicates are ALLOWED (user). ⭐ `@Key` resolves to the FIRST match, searched deterministically: ① the referencing scope and outward (nearest scope wins, SR-65); ② if none, the entities of NESTED instances, breadth-first, instances in file order. ⭐ A qualified path `@PlatoonB/SL` names one exactly. ⭐ An ambiguous key is a WARNING, never a refusal — the editor and the load log say *"@SL matches 3 entities; using Platoon A's"*. ⛔ SR-66's *"duplicate keys in one scope are refused"* is withdrawn; a MISSING key still fails loudly |

**Rejected — one line each:**
- *Placement on the Brain* — the Brain holds no navmesh (user).
- *One request per nesting level* — N round trips and N waits; flattening from the files needs none.
- *A live drag ghost in v1* — real cost for a convenience; the drop already shows the outcome.
- *Refusing duplicate keys* — an upper scenario legitimately references "the squad leader" of whichever platoon comes first (user).
