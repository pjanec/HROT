<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: the whole file. This is THE design record for what the Inspector's
  "STATIC PARAMETERS" panel belongs to - the action-parameter BINDING model. Section 2
  is the ruling (per-field REJECTED, whole-DTO APPROVED); section 3 is node-owned
  variables; section 4 is the runtime.
stale-below: nothing.
known-rot: section 4 step 3 says the scenario/mission JSON overlay wins. TRUE, and it
  was NOT true of the generated managed-asset path until BP-275 (Batch 70, BTree) and
  BP-292 (Batch 74, HSM). See DESIGN_Parameter_Model.md section 3.2, whose own
  correction on that point is now folded as HISTORY. The document is correct today.
known-conflict: section 3.4 names auto variables `_auto_{VisualId:N}`; that is what
  B-2's Promote gesture does (BTreePickerDrawers.cs:246). The LATER composed-AiPrimitive
  path (E2) names its pair `bpParams` / `bpWorkingState` instead - a different gesture,
  not a drift.
-->
# Blackboard Authoring — Addendum v3: Action-Parameter Authoring & Node-Owned Variables

> **Status:** Addendum to `Blackboard_Authoring_Detailed_Design.md` (the "DD"). Architect-reviewed + approved
> (2026-06-06). **Authoritative where it refines the DD; the DD otherwise stands.**
> **Amends:** DD §11 (per-node binding) and §4 (Variables panel). Adds the **node-owned / auto-managed variable**
> concept and the **action-parameter authoring** UX for BTree/HSM action nodes.
> **Audience:** AI designers (see §5 "How to author action parameters") and implementers (see §3, §6).
> **Companion editor work:** SE1/SE2 (the shared `InspectorWindow` StructEdit facet render + per-asset picker
> drawers) are the authoring surface this addendum builds on.

---

## 1. Why this addendum exists

During the action-node design work we explored letting designers bind/author **each parameter field
independently** on a BTree/HSM action node (e.g. `Target` ← variable A, `Speed` ← static, `Destination` ←
variable B). That **per-field model was rejected by the architect** because it violates the execution kernel's
zero-allocation, contiguous-memory invariant (see §2). This addendum records the **approved model** and the new
**node-owned variable** mechanism that gives designers the convenient "set the values right on the node" feel
**without** breaking that invariant.

It also captures a clarification the original DD left implicit: how an AI designer actually *sets* an action's
parameter values (static vs dynamic), and how that maps to the runtime.

---

## 2. The approved model (and the rejected one)

### 2.1 REJECTED — per-field binding on action nodes
A regular action node may **not** bind individual DTO fields to different sources. The kernel projects an
action's parameters as a single `ref TValue` over **one contiguous, pre-packed byte slice** in the blackboard
(`Unsafe.As` at a fixed bin-packed offset). Scattering fields across different variables would force a per-tick
temporary-struct allocation + field-by-field copy — destroying the zero-allocation guarantee.

### 2.2 APPROVED — whole-DTO binding (reaffirms DD §11.6)
A regular **action node binds its WHOLE parameter DTO to exactly ONE blackboard variable** of that DTO type, via
the single `ExpressionTargetField`. The `[BlackboardFieldPicker]` is **type-filtered** (DD §11.2): it shows only
variables whose type matches the action's `DtoType` (from `IActionSchemaExporter`).

### 2.3 Two ways to supply the values
- **Static parameters** → author them as the bound variable's **default** (`DefaultValueJson`), edited via
  StructEdit (enums→combos, etc.). They are **baked into the generated `ParseParamsDelegate`** and applied once,
  at behavior **assignment** (see §4).
- **Dynamic parameters** (must change while the behavior runs, or be shared) → the value lives in the bound
  variable and is updated at runtime by:
  - **Approach A — whole-DTO aliasing** (DD §7): two places share the *same* variable → both receive a
    `ref TValue` to the same byte slice (true zero-copy sharing).
  - **Approach B — field-level sync** (DD §8): **Subtree nodes only** — a generated orchestrator copies values
    in before the sub-tree ticks and out after. (Field-level sync remains a Subtree-only mechanism; it is *not*
    available on plain action nodes.)

---

## 3. NEW: Node-owned / auto-managed variables

The friction with §2.2 is "variable sprawl": if every action needs its own variable just to hold static values,
the designer would hand-create dozens of variables. We solve this with **node-owned (auto-managed) variables**,
created in-context via the existing **"+ Promote to new variable"** affordance (DD §11.3).

### 3.1 What they are
A node-owned variable is a **normal blackboard variable in every downstream respect** — it lives in the asset
JSON `BlackboardBlockDto.Variables`, is bin-packed (§6), and is materialized by the generated
`ParseParamsDelegate`. The **only** difference is **editor presentation + lifecycle**: it is marked
auto-managed, presented as belonging to its owning node, and not hand-managed by the designer.

This mirrors the architecture's existing treatment of **Subtree/Approach-B allocations** (editor-managed
variables hidden from general authoring).

### 3.2 The `IsAutoManaged` flag (persisted)
Add a boolean **`IsAutoManaged`** to:
- `BlackboardVariableDto` (persisted into the asset JSON `Blackboard` block), and
- the editor model `BlackboardVariableEntry`.

Everything downstream of the JSON (incremental generator, bin-packer, `ParseParamsDelegate`) ignores the flag and
treats the variable as a standard master variable.

### 3.3 Creation
When a designer clicks **"+ Promote to new variable"** in the action's type-filtered picker:
1. A new variable of the action's `DtoType` is created with `IsAutoManaged = true`.
2. The action node's `ExpressionTargetField` is bound to it.
3. The designer edits its default values (the static params) via StructEdit, in the Inspector — never leaving the
   node's context.

### 3.4 Naming / identity
Auto-name as **`_auto_{VisualId:N}`** (BTree) or **`_auto_{StableId:N}`** (HSM):
- The `_auto_` prefix keeps it a valid C# identifier (the variable becomes a generated struct field; identifiers
  can't start with a digit).
- The `VisualId`/`StableId` guarantees uniqueness within the asset **and** stability across saves/reloads, so the
  reference catalog (`{AssetId}::{VariableName}`) and refactor service resolve it reliably.

### 3.5 Lifecycle — auto-delete with the node
When the owning action node is **deleted**, the command sink (`BTreeCommandSink` / `HsmCommandSink`) **removes the
node-owned variable** from the blackboard list and triggers a **re-pack** (so its 100-byte inline memory isn't
orphaned). This mirrors the Subtree-allocation lifecycle ("adding/removing a Subtree node adds/removes its
allocation").

### 3.6 Presentation (Variables panel)
`VariablesPanelControl` filters entries where `IsAutoManaged == true` **out of the main "Defined Variables"
list** and renders them in a **read-only, dimmed "Node-Owned Allocations" sub-group** (or behind a "show auto
vars" toggle). The designer experiences "values on the node"; the panel stays clean.

### 3.7 Interactions with existing machinery (all handled)
- **Unused-variable diagnostics (DD §12):** never false-positives — the node's `ExpressionTargetField` gives the
  auto-var a reference count of 1 while the node lives, and §3.5 deletes it when the node dies.
- **Bin-packing (DD §6):** identical to any master variable (inline tier, spill to heavy as needed).
- **Approach-A aliasing (DD §7):** **EXCLUDE** node-owned variables from the "Defined Variables" **drop-target**
  list — a sub-tree must not alias a node-private variable. (UI filter only.)
- **Cross-region conflict validator (DD §9):** no false positives — a node-owned var has exactly one writer (its
  owning action) by construction.
- **Reference catalog / refactor service:** work unchanged (stable `_auto_{id}` name).

---

## 4. Runtime path (unchanged kernel; recorded for clarity)

At behavior **assignment** (`BehaviorIngressSystem`), the generated `ParseParamsDelegate` runs **exactly once**:
1. instantiate the DTO,
2. apply the **editor-authored static defaults** (from the node-owned/bound variable's `DefaultValueJson`),
3. overlay any scenario/mission-command JSON parameters (**runtime override wins**),
4. zero-allocation `Unsafe.Write` of the DTO into the entity's bin-packed blackboard slot.

Thereafter the action reads its `ref TValue` directly from that slot each tick.

### 4.1 Static-vs-dynamic timing — surface this to designers (one-line tooltip)
The **authoring gesture is unified with Blueprints** (set values where the node is), but the **runtime timing
differs by host**, by design:
- **Blueprint** channel command: re-writes its params from the node's pins **every time it fires**.
- **BTree/HSM** action (static): applied **once, at behavior assignment** (baked into `ParseParamsDelegate`) — not
  re-applied per tick.

So, for a BTree/HSM action: a **static** field = "config, fixed when this behavior is assigned"; a value that must
**change while running** → bind the DTO to a (shared) variable that something else updates (Approach A/B). The
Inspector should show a one-line tooltip to this effect so designers aren't surprised.

> **Mutable working state (local / shared variables).** The "shared variable that something else updates" above is
> formalized in `BTree_AiActionParameterBinding_Detailed_Design.md §4.4`: variables carry a **role** (`input` param vs
> `state`) and, for state, a **scope** (`Node` = local, `Behavior`/`Entity` = shared). State reaches the action either as
> a Mode-1 `ref` arg (its own scoped slot) or via the Mode-2 `GetShared<T>/GetSharedRW<T>(entity, scope)` accessor
> (another entity's / a group's slot). This is the general mechanism; §3's node-owned variable is the `Node`-scoped case.

---

## 5. How an AI designer authors action parameters (workflow)

**BTree / HSM action node:**
1. Place the action node; select it. The shared **Inspector** shows the node's facet (SE1/SE2): the action
   picker + a **type-filtered "Parameter variable"** dropdown.
2. **To give it static values (the common case):** open the dropdown → click **"+ Promote to new variable"**. A
   node-owned variable is created and bound automatically. Edit its fields right there (Destination, Speed, an
   enum via a combo, etc.). Done — no panel-switching, no manual variable bookkeeping. (Tooltip reminds you these
   are applied when the behavior is assigned.)
3. **To use a shared/dynamic value:** pick an existing compatible variable from the dropdown instead of promoting.
   Whatever updates that variable at runtime (another node via Approach A/B, mission command, etc.) drives the
   action's params live.
4. The node-owned variable appears (dimmed, read-only) under **"Node-Owned Allocations"** in the Variables panel,
   not cluttering your hand-authored variables. Deleting the node removes it automatically.

**Blueprint action node (for contrast):** parameters are **data-IN pins** on the node — set a literal inline or
wire a value; re-applied every time the node fires. (No blackboard variable involved.)

---

## 6. Implementation checklist (BB1 batches)

- **B-1 — Type-filtered picker:** `[BlackboardFieldPicker]` consults the action's `DtoType` (`IActionSchemaExporter`)
  → show only compatible variables; `(no compatible variables)` + Promote affordance otherwise. (DD §11.2)
- **B-2 — Promote-to-new-variable + `IsAutoManaged`:** add `IsAutoManaged` to `BlackboardVariableDto` +
  `BlackboardVariableEntry`; Promote creates an `_auto_{VisualId:N}` variable of the action's DtoType, sets
  `IsAutoManaged=true`, binds `ExpressionTargetField`. (DD §11.3)
- **B-3 — StructEdit default editing:** edit the (node-owned or shared) variable's `DefaultValueJson` via the SE1
  StructEdit surface (enums/vectors/etc.).
- **B-4 — Node-owned presentation + lifecycle:** `VariablesPanelControl` filters `IsAutoManaged` into a dimmed
  read-only "Node-Owned Allocations" group; exclude from Approach-A alias drop-targets; `BTreeCommandSink`/
  `HsmCommandSink` auto-delete + re-pack on owning-node delete.
- **B-5 — Static-vs-dynamic tooltip** in the Inspector (per §4.1).
- **Codegen / bin-pack:** the generator's `ParseParamsDelegate` baking + `BlackboardBinPacker` already treat the
  variable normally (no `IsAutoManaged` awareness needed downstream of the JSON).

---

## 7. One-line summary
Action nodes bind their **whole** parameter DTO to **one** blackboard variable; "**+ Promote to new variable**"
gives the blueprint-like "values on the node" feel by auto-creating a **node-owned variable** (hidden, dimmed,
auto-deleted with the node) whose default holds the static params — preserving the engine's contiguous,
zero-allocation memory model while keeping authoring simple and consistent for designers.
