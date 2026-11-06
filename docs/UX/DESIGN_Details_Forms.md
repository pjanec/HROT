<!--STATUS
state: LIVE
build-state: DESIGN — top-level, for the user's review (R-4122); ui builds the forms, backend the attributes (R-4123)
updated: 2026-11-06 (revised after the user's review: the raw editor is local BY DESIGN — CE-4283 refuted; every open point measured: the leaf editors through ScalarEditBox, ammunition, posture, weather, routes (CE-4284), embarkation; Q5 replaced, Q7–Q8 added) · 2026-11-06 (first version — coordinator; editing-UX design queue item 8: E2, D3 / D7 and the raw inspector fixes)
current-answer: §2 the model · §3 the four forms · §4 stages · §5 questions with leans
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - DESIGN_Editing_UX.md — the programme: §3.3 the target tab set, D2 (tabs in the shared bundle with Requires), D3 (the Unit form's fields), D7 (expert detail), §3.7 the expected basics
  - DESIGN_Edit_Framework.md — OWNS the write: every form commit is one Edits.Submit(Write …); §2.5 the field-mark hook and the shared form-row helper this design builds
  - DESIGN_Action_Catalogue.md — OWNS Requires / Modes / Level; a form row declares the same three
  - DESIGN_Edit_Mode_Undo.md — one form commit = one undo step (R-4133)
  - DESIGN_New_Scenario_And_Recipe.md — the terrain shown read-only in the Scenario form (R-4135)
  - DESIGN_Map_Interaction.md — the Shape form's geometry is edited on the map; the form holds the rest
  - DESIGN_Seats_And_Hats.md — a row's Level greys it for a seat that may not change it
  - ../DESIGN_Semantic_Value_Types.md — OWNS the typed values (SV-1 time, SV-4 angle) that choose a row's editor and its unit
  - ../blueprints/Architect_Question_59_Attribute_Vocabulary_Single_Source.md — OWNS AttributeVocabulary, "a row HERE FIRST"; every form field is an attribute
  - ../DESIGN_Remote_Edit_Outcome.md — the ticket and outcome a form commit carries
  - ../designs/weather/DESIGN_Weather_And_Time.md — §2: weather edits were meant to be attribute requests; the Scenario form's weather rows are those attributes
  - ../DESIGN_Entity_Interactions.md — §7 OWNS EmbarkEntityCommand / EmbarkationSystem, which the Crew & cargo form raises
-->

# DESIGN — the Details forms: Unit, Crew & cargo, Shape, Scenario

## 0. Why, and what the user asked

🔒 *"The raw component editors are more a debugging tool … dedicated forms"* (user, `2026-11-06`, R-4120).
🔒 *"expert detail hidden, not removed"* (R-4121).

Today a user changes side, name, health, ammunition or spawn facing through the raw **Components** tab. It shows engine names, indices
and TKB numbers (programme §1.1). This design gives the common fields forms, and keeps the raw tab as the debugging tool.

🔒 *"Unowned component edit is not replicated. This is what it should be doing, because this is raw component edit. It's not a
user-faced editing mechanism. This is a debug tool. For whatever serious entity editing, we cannot rely on raw component edits.
Basically anything what the user should be able to edit … requires a well-designed form, because it might span across many different
components."* (user, `2026-11-06`) ⇒ a form is THE user-facing editor; one form spans many components, and one attribute's handler may
write several (§3: ammunition writes the mount's `WeaponState`, a different entity per mount). 🔒 *"Please measure whatever you do not
know … we should design with all the available knowledge we have."* — §1.2 is that measurement.

## 1. INVENTORY *(2026-11-06)*

Queries run:

- `search_graph ".*(Form|FieldView|FieldEditor|PropertyGrid|Inspector|DetailsTab|EntityView|TabView)$" Class`: **45**. The forms that exist are `OrderForm`, `BehaviorParamsForm` and `ParamsDefaultsForm` (StructEdit over a DTO); the field editors are StructEdit's plus `PickableLeafFieldEditor`, `MathFieldEditors`, `TypeFieldEditor` and others.
- `".*(IForm|IField|IDetailsTab|IEntityTab|IEntityView|IPropertyEditor|IValueFormatter|ISemantic).*" Interface`: **2** (`IImGuiFieldDrawer`, `IFieldReadWriter`). No form or row abstraction exists.
- `".*Attribute(Registry|Catalog|Interpreter|Map|Table|Translator|Names|Descriptor|Binding|Schema).*" Class`: **5**.
- grep for the component types, `AttributeVocabulary`, `ComponentEditWindow`, `EditRequests`.

Coverage could not be checked (the CLI has no `check_index_coverage`).

| what exists | where | for this design |
|---|---|---|
| **the attribute vocabulary — the ONE list of fields that can be written to any node**: 18 rows (`Name`, `Affiliation`, `Position.X/Y/Z`, `Heading`, `ScenarioStartUtcTicks`, `ScenarioUtcOffsetMinutes`, `Fear`, `FrightFrom.*`, `Rule.*`) | `Replication/Attributes/AttributeVocabulary.cs:45` (Q59: *"adding an attribute means adding a row HERE FIRST"*) | ⭐⭐ a form field that is an attribute gets the remote write, the ticket, the outcome, HTTP `/attribute`, rules and undo **for free**. ⛔ Strength, ammunition, posture, playable, weather are **not** attributes yet |
| **the write** — `EditRequests.Write / WriteJson` → `EntityWriteRouter` (apply here, or ask the owner) | `Replication/Edits/EditRequests.cs:22,37` | the edit framework's `Write` op wraps it (F1) |
| **the field editors** — `IImGuiFieldDrawer` + StructEdit leaf editors (pickable links, angles, quaternion-as-euler, type, utility curve) | `Fdp.Presentation/ImGui/Editing/IImGuiFieldDrawer.cs` | ⭐ a form row draws its value with the SAME drawer StructEdit picks for that type ⇒ a typed value edits the same everywhere |
| **the field-mark hook** — `Func<FieldPath, FieldMark?>` + `FieldMarks.Draw(entity, path)` (approved, not built) | Edit framework §2.5 (R-4127) | *"the new dedicated forms are built from a shared form-row helper that does it once"* — this design IS that helper |
| **the raw Components tab's write** — `ComponentEditWindow.ExecuteOkLogic` → `ls.SetComponent(...)` | `ComponentEditWindow.cs:157-170` | ✅ **local by design** — a debugging tool (user; CE-4283 refuted) |
| **the components** | `EntityInfo` (`Fdp.Core`), `Health`, `WeaponState`, `WeaponAmmo`, `StanceIntent`, `CarrierSeats`, `PassengerBuffer` (`Fdp.Toolkits`), `Playable`, `SpawnArea`, `EditablePolyline`, `RoutePlan` (`Hrot.Core`), `WeatherConditions` | each form row reads one of these |
| **the tabs** — `EntityDetailsBundle` gives every host Components, Summary, Objectives, Brain; the editor adds Scenario, Zones, Macro, Playable, Mission | `EntityDetailsBundle.cs:59-96`, `EditorSubsystem.cs:3295-3370` | D2 (approved): every tab in the shared bundle with `Requires` |

### 1.2 Measured for this revision *(`2026-11-06`)*

| question | measured | consequence |
|---|---|---|
| where ammunition lives | `WeaponAmmo` is a **replicated mirror** (`[DataPolicy(NoScenario)]`, written only by `WeaponAmmoMirrorSystem` on the brain, `WeaponAmmo.cs:12-15`); the real rounds are `WeaponState.Ammo` (saved, `CombatComponents.cs:14`) — mount 0 on the unit, mounts 1–3 on mount child entities (`WeaponMountInfo.MountIndex`, `WeaponAmmoMirrorSystem.cs:37-56`) | the row READS the mirror (works on every station) and WRITES attribute `Ammo.<mount>`, whose handler on the owner writes that mount's `WeaponState.Ammo` (clamped to `MaxAmmo`) — one attribute, two entities. Saved, because `WeaponState` is |
| whether a posture at start can be authored | `StanceIntent` is `NoScenario` (`StanceComponents.cs`) and is written by the BRAIN every tick through `StanceRequest.Set` (behaviours: `CivilianBehavior`, the utility `CombatPosture` with its own params DTO) | ⛔ a stance field would be overwritten by the first decision ⇒ **not a Unit form field**; posture is the unit's behaviour / orders (Q7) |
| how weather is meant to be edited | the weather design: *"UpdateEntityAttributeCommand: a new scenario start time (or a weather edit)"* (`DESIGN_Weather_And_Time.md` §2) — but the vocabulary has no weather rows, so today it writes locally (edit framework §1) | adding the weather attributes IS the designed path |
| how passengers aboard at start are authored | `EmbarkEntityCommand` / `DisembarkEntityCommand`, applied at once by `EmbarkationSystem` on the vehicle's owner — the designed authoring path (`DESIGN_Entity_Interactions.md` §7); saved through `PassengerBufferTranslator` → `InitialPassengersIntent` | Crew & cargo raises the commands (the edit framework's `Command` op); no live interaction needed in Edit mode |
| how a route edit reaches the owner | ⛔ it does not: local `RoutePlan` set + `UpdateEntityCommand`, which the egress forwards only for `EditablePolyline`; the route egress needs authority (`MapRouteEgressTranslator.cs:92`) | **CE-4284** filed; an area's points DO reach the owner (`UpdateEntityDescriptorRequest`) ⇒ the route goes the same way (Q8) |
| `Playable`, `SpawnArea` | `Playable { ForceId Side; FixedString32 SlotName }`, `SpawnArea { ForceId Side; float FacingDeg }`, both saved (`Default`) | four attributes; adding / removing `Playable` is edit framework F3 |
| strength | `Health { float Current; float Max }`, saved | attribute `Health.Current`; the row shows % of `Max` |

### 1.1 Claim table

| claim | code | design basis |
|---|---|---|
| an attribute field writes on any node | ✅ `EntityWriteRouter` (local or a request to the owner) | ✅ Remote_Edit_Outcome, Edit framework F1 |
| a non-attribute field cannot be written remotely today | ✅ the router speaks attribute ids only (`IEntityComponentWriter.Write(entity, attributeId, value)`); the raw window writes locally | ✅ Q59 §7.2 (the vocabulary is the list) |
| a form row can reuse StructEdit's leaf editors | ✅ **measured**: `IImGuiFieldDrawer.DrawInput(ref object, EditNode)` needs a StructEdit node, so not alone — but **`ScalarEditBox<T>`** (`Hrot.Editor.AiShared/Inspector/ScalarEditBox.cs`, built for BP-356 and used by `DefaultValueAuthoring`) wraps ONE value as a bound StructEdit document ⇒ a row is a one-field session and gets every leaf editor and picker | Edit framework §2.5; BP-356 |
| a session draws without allocating per frame | ⛔ **measured FALSE** (throwaway probe, not committed: `ImGuiTestFixture`, 100 steady frames): a frame drawing nothing **0 B**; one `float` through `ComponentEditDrawer` **64 B/frame**; five scalar fields **160 B/frame** — ~24 B per field, one boxed value each. **CE-4285** filed | ⇒ a row must NOT draw through a session every frame: it shows a **cached, typed, formatted value** (refreshed when the component's value changes) and opens its `ScalarEditBox<T>` session **only while that row is being edited** (click or Enter on the row) — the steady frame allocates nothing, whatever CE-4285's fix |
| the attributes needed are cheap to add | ✅ the rule and its rails exist (`TheFourRoutingTablesAgreeTests`); each is one row + a JSON setter + a binary handler | ✅ Q59 |

## 2. The model

### 2.1 The picture

```mermaid
classDiagram
  class FormDecl {
    <<★ new · a declaration, like an ActionDecl>>
    Id · Title · Order
    Requires (components)
    Rows
  }
  class FormRow {
    <<★ new>>
    Label · Group
    Attribute (a vocabulary id)
    Read(world, entity) → value
    Type (the semantic type → the editor and the unit)
    Modes · Level · Expert
    Range · Validate
  }
  class FormView {
    <<★ new · the shared form-row helper>>
    Draw(FormDecl, selection)
    multi-select: common rows, differing values greyed
    one commit → one Submit per entity
  }
  class FormCatalogue { <<★ new>> Unit · CrewAndCargo · Shape · Scenario }
  class AttributeVocabulary { <<existing · AttributeVocabulary.cs:45>> +new rows ★ }
  class ScalarEditBox { <<existing · AiShared/Inspector/ScalarEditBox.cs>> one value as a StructEdit document }
  class IImGuiFieldDrawer { <<existing>> the editor per type }
  class ValueFormatter { <<★ new>> typed display text, cached, refreshed on change }
  class FieldMarks { <<approved · R-4127>> busy / error mark per field }
  class Edits { <<approved · R-4128>> Submit(Write) }
  class EntityDetailsBundle { <<existing · EntityDetailsBundle.cs>> one tab per FormDecl, by Requires }
  class ComponentEditWindow { <<existing · debugging>> refuses on a non-owner ★ }
  class ExpertSwitch { <<★ new>> per panel, saved in user settings }

  FormCatalogue o-- FormDecl
  FormDecl o-- FormRow
  FormRow --> AttributeVocabulary : names its attribute
  EntityDetailsBundle --> FormCatalogue : a tab per form
  EntityDetailsBundle --> FormView
  FormView --> ValueFormatter : every frame (0 B)
  FormView --> ScalarEditBox : only the row being edited
  ScalarEditBox --> IImGuiFieldDrawer
  FormView --> FieldMarks
  FormView --> ExpertSwitch
  FormView ..> Edits : commit
```

*What the picture shows that the prose hid:* a form owns no write and no editor, and only ONE row at a time pays for an editing session — the
rest show cached text (CE-4285 measured the session at ~24 B per field per frame). Each row names an attribute, and the attribute already
knows how to reach the owner. The row's type picks the editor StructEdit would pick. The only new things are the declarations and the
view that lays them out. The forms are therefore one mechanism across every host, never four hand-drawn panels.

### 2.2 A commit, from a station that does not own the unit

```mermaid
sequenceDiagram
  actor U as instructor (Live)
  participant V as FormView (Unit)
  participant E as Edits
  participant R as EntityWriteRouter
  participant O as owner node
  participant M as FieldMarks
  U->>V: Side: Hostile, Enter
  V->>E: Submit(Write entity 42, Affiliation = Hostile)
  E->>R: not the owner ⇒ a request with a RequestId
  E->>M: the Side row ⏳
  R->>O: UpdateEntityAttributeRequest
  O-->>E: EntityEditOutcome Applied
  E->>M: mark cleared — the replica shows Hostile
  Note over V,M: Refused ⇒ ✗ on the row with the reason + a sticky notification (R-4127)
```

*What the picture shows that the prose hid:* the form never decides "local or remote" and never polls. The same commit works in the
network-less editor (applied at once, one undo step) and on a cluster.

### 2.3 Rules every form follows

| rule | |
|---|---|
| **a field is an attribute** | ⛔ no form writes a component directly. A field without an attribute gets one first (Q59's rule: a vocabulary row + setter + handler, the rails red until all three exist) |
| **when a value commits** | on Enter, on leaving the field, or on picking from a list. ⛔ No commit per keystroke. Typing in one field is one undo step |
| **units and ranges** | from the semantic type (m, °, s, %, a time of day); out of range ⇒ red with the reason, never silently clamped (§3.7) |
| **references** | a unit, area, type or behaviour is picked from the one picker or the map, never typed as a number |
| **modes** | Edit and Live: editable · Review: read-only, every row (the Gate refuses, the view greys) |
| **levels** | a row's `Level` greys it for a seat that may not change it, with the reason (D4, seats and hats) |
| **multi-select** | the rows common to all selected entities; a value that differs shows *mixed*; setting it writes all (one Submit per entity, ONE undo step) |
| **expert detail** | rows flagged `Expert` sit in one *Details for experts* expander at the bottom; the panel's Expert switch opens it by default; saved per panel in the user settings (D7) |
| **hot path** | `FormDecl` and rows are built once; `Draw` allocates nothing in a steady frame (no LINQ, cached labels) — a rail measures it |

## 3. The four forms

| form | offered when (`Requires`) | rows (common · ⚙ expert) | attributes to add ★ |
|---|---|---|---|
| **Unit** — replaces Summary and Playable, and the raw trip for the common fields | `EntityInfo` | name / callsign · side · type (read-only, Q4) · strength % · rounds per mount (read from the `WeaponAmmo` mirror) · playable + slot · ⚙ entity id, owner node, TKB template, loaded round per mount | ★ `Health.Current`, `Ammo.<mount>` (→ the mount's `WeaponState.Ammo`), `Playable.Side`, `Playable.SlotName` (+ add / remove `Playable` = edit framework F3). ⛔ no posture (Q7) |
| **Crew & cargo** | `CarrierSeats` or `PassengerBuffer` | each seat: role, who sits there, its mounted weapon · *Put aboard…* (pick a unit) · *Take off* | ★ none: `EmbarkEntityCommand` / `DisembarkEntityCommand` (the edit framework's `Command` op), applied at once on the vehicle's owner, in Edit and Live |
| **Shape** | `EditablePolyline`, `RoutePlan`, `SpawnArea` or a weather region | kind · name · point count, length / area (read-only; the geometry is edited on the map, E8) · spawn area: side, facing · weather region: conditions · route: speed per leg · ⚙ the raw points | ★ `SpawnArea.Side`, `SpawnArea.FacingDeg`, the weather fields; a route's legs travel as the whole `RoutePlan` to the owner (CE-4284, Q8) — a list is not a scalar attribute |
| **Scenario** (nothing selected) | the world environment | General: name, description, *Capture picture* · Time: start, offset (attributes exist) · Weather: a form · Terrain: read-only (R-4135) · Parameters · ⚙ subsystem type, schema | ★ the global weather fields; name / description are file header (a document step, undo design §1.1) |

⏸ Not in E2, as the programme lists them: Sides & relations, Traffic, Civilians & crowds, Gameplay (respawn, deploy) and Checks. Each
gets a row in this Scenario form when its own design lands.

### 3.1 The raw Components tab — kept as the debugging tool

| | |
|---|---|
| nested structs expand, inline arrays draw, a typed field uses its formatter | programme §1.1 findings |
| it writes the local copy, on any station — **by design** (user; CE-4283 refuted) | a debugging tool, never the user's way to edit |
| last in the tab order, behind the forms | R-4120 |

## 4. Stages

| stage | what | owner | rails |
|---|---|---|---|
| **D1** | `FormDecl` / `FormRow` / `FormView` / `FormCatalogue`; a row shows a cached, typed, formatted value and opens a one-field `ScalarEditBox<T>` session only while it is edited (CE-4285: a session allocates ~24 B per field per frame); field marks; multi-select; the expert expander and switch | ui | a declared form draws and commits through `Edits.Submit`; mixed values; **0 B per steady frame with the form open and no row in edit** (the probe's shape as a rail) |
| **D2** | the attributes: `Health.Current`, `Ammo.<mount>`, `Playable.Side` / `SlotName`, `SpawnArea.Side` / `FacingDeg`, the weather fields — each a vocabulary row + setter + handler; and CE-4284 (the route to the owner) | backend | `TheFourRoutingTablesAgreeTests` green; each written from a non-owner reaches the owner (a two-node rail) |
| **D3** | the Unit form (replaces Summary, Playable) | ui | side, name, strength, ammo, posture, playable from the form, in Edit and Live; read-only in Review |
| **D4** | the Scenario form (time, weather, terrain read-only, parameters, capture picture) | ui | weather set in Live reaches every node |
| **D5** | the Shape and Crew & cargo forms | ui | a route's leg speed set on a non-owner reaches the owner; a unit put aboard in Edit mode is aboard after save and reload |
| **D6** | the raw tab fixes | ui | — |

Order: D1 and D2 in parallel → D3 → D4 → D5 → D6. D1 needs edit framework F1 (`Edits.Submit` + `Write`).

## 5. Questions — leans for the user

| # | question | lean | rejected |
|---|---|---|---|
| **Q1** | how a form is defined | **declarations** (`FormDecl` rows naming an attribute, a type, Requires / Modes / Level / Expert) drawn by ONE `FormView` | a hand-drawn ImGui panel per form: four layouts that drift · StructEdit over a per-form DTO: needs a read and write map per DTO anyway, and hides the per-row mode / level / expert |
| **Q2** | what a field writes | **an attribute, always** — missing ones are added to the vocabulary first | a direct component write: lost on a non-owner, no ticket, no rules / HTTP reach |
| **Q3** | when a value commits | **on Enter, on leaving the field, or on a pick** — one undo step per field edit | per keystroke: hundreds of requests and steps · an Apply button per form: one more click on every change |
| **Q4** | changing a unit's type | **read-only in E2**; a later *Replace with…* action (new type, same place, side and name; references re-pointed) gets its own small design | editable in the form: it changes the entity's identity, which a field edit must not do |
| **Q5** | the raw Components tab on a non-owner | ✏ **settled by the user: local by design**, a debugging tool; no change | — |
| **Q6** | what the Unit form replaces | **Summary and Playable** (their content moves into it); Components stays, last | keep Summary beside it: two tabs saying the same |
| **Q7** | posture at start | **not a form field** — the brain sets the stance every tick (`StanceRequest`), so posture belongs to the unit's orders and behaviour (e.g. the `CombatPosture` parameters in the Orders tab) | a saved `StanceIntent`: overwritten by the first decision · a new start-posture component: a second owner of the stance |
| **Q8** | a route edited on a non-owner (CE-4284) | **the route travels to the owner as `UpdateEntityDescriptorRequest`, the way an area's points already do** — the edit framework's `Write` of a structured value | a scalar attribute per waypoint: a list is not a scalar, and 64 attributes per route would be the raw editor again |
