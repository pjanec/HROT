# Enum-field editing — design analysis & architect questions

## ✅ RESOLVED (architect + user, 2026-06-06) — authoritative; supersedes the proposal/questions below

**Settled architecture (user fully agrees with the architect):**

1. **Two authoring surfaces, deliberately bifurcated — do NOT collapse them:**
   - **BTree / HSM** params → **StructEdit reflection property grid** in the shared `InspectorWindow` (no
     data-flow pins exist there). **Wire the stubbed `InspectorWindow` render loop to StructEdit** — this is the
     intended next step and the foundational prerequisite for **Blackboard Slice 1.5** (TASK-BB-1e-01 etc.).
   - **Blueprint** → **bifurcated**: StructEdit/Inspector is for **static node metadata only** (e.g. selecting a
     ChannelCommand's channel + action id). **Parameter VALUES stay as dynamic data-IN pins** (so designers can
     wire math / GetVariable / EQS outputs into them). **System B (NodeEdit inline pin editors) is retained** for
     literal defaults on unconnected pins. **Blueprint nodes keep PINS for every settable parameter.**
   - In-node inline editors (System B) remain a separate implementation, tailored for in-node use.

2. **DD-1 REJECTED for Blueprints** (per-action nodes whose params live in a StructEdit facet): that would break
   data-flow wiring. Blueprint channel/action params MUST be dynamically-projected data-IN pins
   (`NodePinSchema.GetCanonicalPins` reflects the catalog's `ParamsTypeFqn` → one data-IN pin per public member).
   StructEdit-for-params is correct ONLY for BTree/HSM. (The *palette-per-action* idea is still discussed under
   the separate ACTION-NODE design — that's orthogonal to where params live.)

3. **Enum mechanism (settled):**
   - **Member list:** reflect the **project enum TYPE** at edit time (net8.0). For BTree/HSM, StructEdit's
     `ComponentEditDrawer` does this automatically once the Inspector stub is wired. For Blueprint enum-typed
     **data pins**, wire `EnumPinEditor` (System B) via an `IEnumValueProvider` that reflects project enums. Do
     NOT carry enum members in JSON.
   - **Persistence:** JSON stores only `TypeId = "global::Ns.MyEnum"` + the chosen value in `DefaultValueJson`.
     Persist the value as an **integer** (byte-stable; survives member renames). (`JsonStringEnumConverter` is
     available if a name is ever preferred, but integer is the recommendation.)
   - **Compiler:** the reflection-less generator emits a **direct cast `(global::FQN)Value`**; semantic validation
     is deferred to the C# compiler (a bad enum → ordinary CS error, caught gracefully by hot-reload). **No
     source-generated enum catalog.**
   - **`StaticTypeRegistry`:** does NOT need enum members. It needs only to **accept the enum FQN as an unmanaged
     type** (recognize "is an enum" → blittable, byte size = underlying, typically 4) for the blittable/fixed-size
     invariant. Members are strictly an authoring-time UI concern.

**Implied work items (NOT yet scheduled — design still open on the action-node duality; see ACTION-NODE-DESIGN.md):**
- Wire `InspectorWindow` StructEdit render-loop stub (BTree/HSM facets + Blueprint node metadata). [foundational]
- `StaticTypeRegistry`: accept enum FQNs as unmanaged (size = underlying). Generator: emit `(global::FQN)N` for
  enum-typed literals (and resolve the Stage3 default-materialization gap, DD-4, for this to actually compile).
- Blueprint System B: implement an `IEnumValueProvider` (reflect project enums) + register `EnumPinEditor` for
  enum-typed data pins; `BlueprintPinModel.ParseValue` enum case (parse as long/int).

---

# Enum-field editing — design analysis & architect questions

**Goal:** a correct, unified mechanism for editing ENUM-typed fields across (a) blueprint pins/params,
(b) BTree action/condition params, (c) HSM state/activity/guard params. The user flagged this is cross-cutting
and that the **Blackboard Authoring DD** (`docs/blueprints/Blackboard_Authoring_Detailed_Design.md`) must anchor
it. "Design first" — no code yet.

## Grounded findings (code-verified)

### Two distinct editing systems exist
- **System A — StructEdit** (`FDP/ExtDeps/StructEdit` + `Fdp.Presentation` `ComponentEditDrawer`): a
  reflection-based property grid. **Already fully handles enum fields** — `ComponentEditDrawer.DrawPrimitiveInput`
  (Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs:481-531) does `Enum.GetNames/GetValues` → `ImGui.Combo`
  (flags → checkboxes). `ReflectionEditDocumentBuilder` classifies `t.IsEnum → EditNodeKind.Enum`. Runs at
  **net8.0 runtime with game assemblies loaded** → reflection works. This is what the **FDP entity inspector**
  uses for live param-DTO editing today.
- **System B — NodeEdit pin editors** (`FDP/ExtDeps/NodeEdit` `IPinDefaultValueEditor`/`PinDefaultValueEditorRegistry`):
  the blueprint canvas inline pin editors. `EnumPinEditor` + `IEnumValueProvider` exist but are **unregistered**;
  no `IEnumValueProvider` implementation exists; `StaticTypeRegistry` has **no enum path**.

### Where each subsystem stands
- **HSM/BTree node params:** edited via StructEdit **facets** (`HsmFacetDispatcher`/`BTreeFacets` + picker
  attribute drawers). The dispatcher/mapper/drawers are wired — but the actual render is **STUBBED**:
  `InspectorWindow.DrawClientArea` (Hrot.Editor.AiShared/Windows/InspectorWindow.cs:208-213) just shows an
  "Apply" button instead of a `ComponentEditDrawer` render loop. **Consequence: replacing that stub with a real
  StructEdit render loop would give HSM/BTree facet enum fields combos automatically** (and is exactly the DD-2
  "StructEdit property grid for HSM state params" gap).
- **HSM/BTree action PARAM VALUES** (the per-entity `BehaviorParameters` payload) are NOT authored in the
  HSM/BTree editors at all today — they're edited in the **FDP entity inspector** (System A, enums already work),
  and persist into **scenario entity data**, not the `.hsm.json`/`.btree.json`.
- **Blueprint pins:** System B; enum unwired (see above).

### The hard constraint (all three compilers)
Blueprint / BTree / HSM generators are all **`netstandard2.0` + Roslyn `IsRoslynComponent`** → **cannot reflect
over game assemblies**. They see only JSON text. So: an enum value that must appear in generated code has to be
stored in JSON as a **member-name string or integer**, resolved to a number **at editor time (net8.0, can
reflect)**, and the generator emits it directly (e.g. `(global::Ns.MyEnum)3`). The generator needs the enum's
**FQN** (+ that it's enum-backed, for size) but does NOT need the member list.

### Blackboard Authoring DD alignment (the anchor)
- §3.4 / §4.4: editor-managed blackboard variable types explicitly include **"any enum type declared in the
  project."** Discovered by **reflection at editor time** via the action schema (§10), stored in the asset JSON
  (`Blackboard` block, v2 JSON-backed), C# struct generated at build.
- v2.2 Q3: the Category-2 Variables panel renders from the **JSON in-memory model**, NOT by reflecting the
  generated struct. Reflection (`[BlackboardDtoStruct]`/schema exporter) is **Category-1 (read-only) only**.
- The DD does **not** explicitly spec enum VALUE storage (name vs integer) or how the JSON→C# generator emits an
  enum default. That's a gap this design must close.

## The core design forks (for the architect)

1. **Authoring surface for action PARAM VALUES** (the DD-1/DD-2 redesign): the user wants per-action nodes whose
   pins = the action's param DTO, with literal authoring (when used from a BTree graph) via a StructEdit-style
   property grid — the SAME grid HSM state params should use. So: should blueprint action-param literal authoring
   and HSM/BTree param authoring **unify on StructEdit (System A)** rather than NodeEdit pins (System B)? Enums
   come free if so.
2. **Blueprint data pins that are enum-typed** (independent of params — e.g. an enum value flowing on a wire):
   do we still need System B's `EnumPinEditor` wired (with an `IEnumValueProvider` reflecting project enums + a
   `StaticTypeRegistry` enum path), or are enums only ever authored as param VALUES (System A) and never as raw
   pin literals?
3. **Compiler enum-type acceptance (reflection-less):** how should a project enum FQN be accepted as a valid type
   by the `netstandard2.0` registries? Likely the JSON carries the underlying integer + FQN and the generator
   emits `(global::FQN)N` — needing only an "is a known enum FQN" check, not a member catalog. Is a
   **source-generated enum catalog** (scan project enums → emit FQN+size list the generators consume) the
   intended mechanism, or a curated list, or "trust the JSON FQN + emit a cast"?
4. **Reconciling the two render models for Category-2:** the blackboard DD says the Category-2 Variables panel
   renders from the JSON model (no reflection), yet StructEdit's enum combo is reflection-based. For an enum
   variable in a Category-2 blackboard, where does the editor get the member list — reflect the (loaded) enum
   type at editor time (allowed; the enum TYPE exists even if the generated struct shouldn't be reflected), or
   carry members in JSON? (Likely: reflect the enum type itself — it's a project type, not the generated struct.)

## Proposed direction (my recommendation, pending architect)
- **Unify param/DTO-field authoring on StructEdit (System A)** for HSM, BTree, AND blueprint action params:
  one reflection-based property grid (net8.0) that already handles enums, vectors, FixedString, nested DTOs.
  First concrete step = **wire the `InspectorWindow` StructEdit render-loop stub** → unblocks HSM/BTree param
  editing AND enum fields in one move (addresses DD-2 + enums for two subsystems).
- **Blueprint:** redesign ChannelCommand into per-action nodes (DD-1) whose params are authored via the same
  StructEdit grid in Details (not inline pins). Enum param fields then render via System A.
- **Compiler:** store enum param values in JSON as integer (+ FQN); generator emits `(global::FQN)N`. Add a
  minimal "enum FQN is a valid unmanaged type (size = underlying)" acceptance to the type registries — ideally
  fed by a source-generated enum catalog so it stays in sync without reflection.
- **System B (NodeEdit `EnumPinEditor`):** wire only if raw enum-typed pin literals are actually needed
  (fork #2); otherwise leave it and rely on System A for param values.

## Focused architect questions (to relay)
Context to give the architect: "Generators are netstandard2.0/reflection-less; StructEdit (ComponentEditDrawer)
already does reflection-based enum combos at net8.0 and HSM/BTree facets are wired to it but the InspectorWindow
render loop is a stub; blueprint pins use a separate NodeEdit editor system where EnumPinEditor is unwired; the
Blackboard DD says Category-2 panels render from JSON, not struct reflection."

Q1. Is the intended single authoring surface for **action/state PARAM VALUES** (blueprint per-action nodes,
    HSM state actions, BTree actions) the **StructEdit reflection property grid**, with the NodeEdit inline-pin
    editors reserved for raw data-flow pin literals only? (i.e. should we unify on StructEdit and wire the
    InspectorWindow render-loop stub as the shared path?)
Q2. For enum-typed fields in a **Category-2 blackboard variable / param DTO**, is the editor expected to **reflect
    the project enum type** at edit time to get the member list (the enum TYPE, not the generated struct), with
    the chosen value persisted to JSON as an integer? Or should enum members be carried in JSON?
Q3. How should project **enum types be made known to the reflection-less generators/compilers** so generated code
    can emit an enum literal — a source-generated enum catalog (FQN + underlying type), or just store the integer
    + FQN in JSON and emit `(global::FQN)N` with a lightweight "known enum FQN" validation? Does the blueprint
    `StaticTypeRegistry` need full enum entries, or only acceptance of the FQN as an unmanaged type?
Q4. Is replacing the **`InspectorWindow` StructEdit render-loop stub** (so HSM/BTree facet + param DTO fields,
    including enums, actually render/edit) the intended next step, and is there an existing planned task/slice for
    it we should align to (e.g. in the Blackboard slice plan §15)?
Q5. Does the **ChannelCommand→per-action-node redesign** (DD-1: one node per action, pins/fields = the action's
    param DTO, authored via StructEdit) align with the intended channel/action model, and should the
    `IChannelCommandCatalog` (ChannelType+ActionId+ParamsTypeFqn) drive the per-action palette + DTO field set?
