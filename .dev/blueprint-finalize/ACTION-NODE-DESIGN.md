# Behavior-action nodes — duality & authoring (DESIGN CONVERGED)

## ✅ ROUND-3 RESOLVED (architect + user, 2026-06-06) — generalized behavior-action node

**Decision (user agrees): behavior actions are standalone, multi-tick NODES — NOT `FunctionCall` (CLR) calls.**
A behavior action's channel-poking is an implementation detail; the action is identified by itself, not a channel.

- **One generalized "behavior-action invocation" node** is the universal dispatcher for ALL actions — channel
  commands AND non-channel `[SharedAiAction]`/`[BTreeAction]`/`[HsmAction]`/AiPrimitives. The existing
  `ChannelCommandNode` class is **repurposed/generalized under the hood** to be this node (it gains a non-channel
  action identity alongside the channel `(ChannelType, ActionId)`).
- **Palette** = one entry per action from the **unified catalog (AN3)** spanning `IChannelCommandCatalog` +
  `ActionSchemaExporter`. Non-channel actions are **named by their FQN** (`{Namespace}.{Type}.{Method}`); no
  channel in their identity. (AN4 currently emits only the channel-command subset — generalize it.)
- **Immutable + dynamic pins** (D-B): drop an action → bake its identity (FQN, or channel+actionId) → immutable;
  `NodePinSchema.GetCanonicalPins` projects one data-IN pin per `ParamsTypeFqn` field (enum fields per AN6).
- **`FunctionCall` is explicitly the WRONG primitive for behavior actions.** FunctionCall = pure Library utilities
  / synchronous instance methods, lowered to direct inline C# calls. Behavior actions have a state-machine
  signature `(entity, ECS context, params DTO) -> NodeStatus` (Success/Failure/**Running**), and must be lowered
  via `BehaviorRegistry`/`HsmActionDispatcher` context-injection + routing, natively supporting suspension
  (`NodeStatus.Running`, like `WaitForChannel`). FunctionCall stays for math/pure/instance calls only.

**What this re-scopes (tasks added — see TASK-DETAIL Phase 5C):**
- AN4/AN5 (channel-command palette + immutable drawer) are the **channel SUBSET** of the generalized node — done.
- **AN7 (editor):** generalize the node + palette to non-channel actions — node carries an action FQN; palette
  emits ActionSchemaExporter entries (named by FQN); NodePinSchema projects pins from the non-channel action's
  `ParamsTypeFqn`; drawer shows the action identity read-only.
- **AN8 (compiler, LARGE):** lower a non-channel behavior-action invocation node in a Blueprint —
  `(self, ctx, paramsDTO) -> NodeStatus` via `BehaviorRegistry`, with Success/Failure exec routing and
  Running/suspend (mirroring the channel-command + WaitForChannel latent path). New emit path.
- **Enum sample:** a sample behavior action with an enum-typed param (the live enum-editor test vehicle).

## ✅ ROUND-2 RESOLVED (architect + user, 2026-06-06) — authoritative

**AQ1 — per-action nodes:** Keep a **single generalized node kind** (`ChannelCommandNode`); the **palette is
populated per-action** from a **unified behavior-action catalog** (spanning `IChannelCommandCatalog` + the action
schema = hardcoded `[BTreeAction]`/`[HsmAction]`/`[SharedAiAction]` + blueprint `AiPrimitive`s). Dropping a
per-action palette item bakes its `(Channel, ActionId)` / action id into the node; `NodePinSchema` projects pins
from `ParamsTypeFqn`. Action selection becomes **create-time-only / immutable** — the `ChannelCommandNodeDrawer`
renders ChannelType/ActionId as **read-only labels** once the node exists (no editable dropdown). **No JSON
migration** (ChannelType/ActionId already persisted).

**AQ2 — blueprint-authored actions:** Surface via the normal **compile + hot-reload** cycle: AiPrimitive(Params +
hostings) → generated backing structs + thunks → `[BlueprintRegistrar]` registers into `BehaviorRegistry` →
post-reload `ActionSchemaExporter`/`IChannelCommandCatalog` reflect the compiled assembly → appear identically to
hardcoded actions in all palettes + StructEdit grids. **Canonical identity = the generated type/method FQN**
(`{Namespace}.{Type}.{Method}`), NOT the blueprint AssetId (runtime uses short name + FNV-1a hash). DTO discovery
is **only after build+reload** — accepted (Quick Reload <100ms + auto catalog rebuild); no pre-build JSON-schema
path.

**AQ3 — ParamsType drift:** **Dynamic re-projection on reload + Stage-2 validation diagnostics**; no pin
versioning/migration. Pins are never persisted (`SaveActiveBlueprintCommand` swaps Pins→[]), so they can't go
stale; on load/reload `NodePinSchema` re-projects from the current `ParamsTypeFqn`, binding connected pin GUIDs
from link `From/ToPinId` and minting deterministic GUIDs for new ones. If the DTO drifted (renamed/deleted/retyped
field), Stage-2 `V_ChannelCommandReferences` emits a validation diagnostic (error badge) → designer rewires. The
compiler is the safety net.

## Implementation roadmap (NOT started — awaiting user go + priority)
**Already implemented (verified — no work):** channel CQRS (ChannelCommandNode start, WaitForChannel, ActionInstanceId
cancel, channel.Status poll); projection-only save + dynamic re-projection on load; `V_ChannelCommandReferences`
Stage-2 validation (existence confirmed; may need a drift-specific diagnostic message); `ActionSchemaExporter` +
`IChannelCommandCatalog`; AiPrimitive hostings + `[BlueprintRegistrar]`; FixedString types (committed 2bc9ae11).

**To build:**
- **B1 — Immutable action selection (SMALL).** `ChannelCommandNodeDrawer`: read-only ChannelType/ActionId labels
  on an existing node (selection moves to palette). Removes the chameleon hazard. No JSON migration.
- **B2 — Unified action catalog + per-action palette (MEDIUM).** Facade over `IChannelCommandCatalog` +
  `ActionSchemaExporter`; emit one palette entry per action (preset channel/actionId or action FQN) over the
  single node kind. Delivers "one action = one node" + fixes the original channel-node UX complaint.
- **B3 — Blueprint enum data pins (MEDIUM).** `IEnumValueProvider` (reflect project enums) + register
  `EnumPinEditor` (System B) + `StaticTypeRegistry` enum-FQN acceptance (unmanaged, size=underlying) +
  `BlueprintPinModel.ParseValue` enum case (persist int).
- **B4 — Stage3 default-literal materialization (MEDIUM, cross-cutting; DD-4).** Make inline pin defaults (incl.
  enum + FixedString) actually compile (currently a no-op stub) — emit `(global::FQN)N` for enums, FixedString
  ctor for strings. Without this, authored literals render/persist but don't reach generated code.
- **B5 — InspectorWindow StructEdit wiring (MEDIUM, foundational).** Un-stub the render loop → BTree/HSM facet
  editing + **enum combos come free** (ComponentEditDrawer reflection). Blackboard Slice 1.5 foundation.
- **B6 — BTree/HSM per-param binding (LARGE).** Extend facets to project the action DTO's fields; per-field
  `[BlackboardFieldPicker]` (type-filtered) + static literals + sub-tree sync sub-panel. Blackboard Slice 1.5.

**Enum-focused path:** blueprint enums = **B3 (+B4 to compile)**; BTree/HSM enums = **B5** (StructEdit renders
enum combos for free once the loop is wired). B6 is param-binding (bigger), not required just to get enum *fields*
rendering.

---

## 🔄 RESHAPED (user's own architect Q, 2026-06-06) — verified against code

The user's blueprint idea (start an action non-blocking, cancel it, wait for completion + exit code, poll status)
maps onto the **CQRS channel model** — and most of it is **already implemented** (lead-verified, not just claimed):

**Blueprint action execution = VERIFIED IMPLEMENTED:**
- **Start (non-blocking) + dynamic pins:** `ChannelCommandNode` — select channel + ActionId; `NodePinSchema`
  projects one data-IN pin per `ParamsTypeFqn` member; generated code writes params + ActionId to the channel
  ECS component and does `ActionInstanceId++`, then continues. (Seen in the MoveToAndFire golden.)
- **Wait for completion:** `WaitForChannelNode` → latent lowering via `BlueprintLatentCursor.ResumeAt`; resumes
  when the channel `Status` → Success/Failure. (`WaitForChannel` + `BlueprintLatentCursor` exist across the
  compiler; the golden emitted the `__phase` state machine + `Status` checks.)
- **Cancel / preempt:** re-issue a `ChannelCommandNode` to the same channel (different action / Idle) →
  `ActionInstanceId++` is the preemption token; the muscle dispatcher aborts the prior behavior.
- **Poll status:** read the channel component's `Status` (`Fbt.NodeStatus`) anytime.
  *(Verified: `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs` — each channel struct has
  `ActiveAction (ushort)`, `ActionInstanceId (uint)`, `Status (NodeStatus)`.)*

**Hardcoded vs blueprint-authored actions = unified (mechanism verified, surfacing partly to-build):** an
`AiPrimitive` blueprint with `intent=Action` + `hostings=[BTreeAction/HsmAction/...]` generates a backing Params
struct + registers a thunk into `BehaviorRegistry` alongside hardcoded C# actions; both then appear identically in
BTree `StructEdit` pickers and project data-IN pins in the blueprint `ChannelCommandNode`. (Compiler hostings
exist; cross-editor *palette surfacing* of blueprint-authored actions is the part to confirm/build.)

**BTree/HSM param authoring = DESIGNED, NOT YET BUILT (Blackboard Slice 1.5):** the architect described the
*target* — StructEdit renders the action DTO's fields; each field is set static OR bound to a blackboard variable
via a type-filtered `[BlackboardFieldPicker]`; whole-subtree params via Approach A alias / Approach B sync. But
**current code** has only `BTreeActionFacet { MethodFqn, ExpressionTargetField (single), Comment, ... }` — no
per-DTO-field projection — and the `InspectorWindow` render loop is stubbed. So this requires: wire the Inspector
stub + extend the facet to project the action's DTO fields + per-field picker + the sync sub-panel. Substantial.

### User decisions (settled 2026-06-06)
- **D-A — Handle = channel.** ≤1 active action per channel; the channel IS the action handle. (Locomotion/
  Weapon/Interaction run concurrently, one action each.) No per-action concurrent handles on one channel.
- **D-B — One action = one node, action baked at creation, pins immutable.** In Blueprints, the designer picks a
  **concrete action from a per-action palette**; the placed node's data-IN pins are **baked from that action's
  ParamsType and are not changeable afterward** (no runtime action-swap — that would orphan already-wired pins,
  the "chameleon" hazard). Internally the implementation may remain a single node-kind keyed by an immutable baked
  action id; the user-facing node is concrete/unchangeable.

### What's converged vs still genuinely open
- **Converged + implemented:** blueprint start/wait/cancel/status via channels; dynamic pins from ParamsType;
  enum mechanism (ENUM-DESIGN §RESOLVED).
- **Converged + to-build:** wire InspectorWindow→StructEdit; per-param static/blackboard-var binding for BTree/HSM
  (Blackboard Slice 1.5); enum FQN acceptance in StaticTypeRegistry + System B EnumPinEditor wiring; Stage3
  default-literal materialization (DD-4) so authored literals actually compile.
## Architect questions — round 2 (to relay, with the updated ENUM-DESIGN + ACTION-NODE designs)
Context for the architect: *D-A and D-B above are user decisions. Verified current state: blueprint channel
start/wait/cancel/status (ChannelCommandNode + WaitForChannel + ActionInstanceId + channel.Status) is implemented;
the current `ChannelCommandNode` picks its action via the Details panel (mutable); BTree/HSM per-param StructEdit
binding is designed (Blackboard DD §11) but NOT built (BTreeActionFacet = MethodFqn + single ExpressionTargetField,
InspectorWindow render stubbed).*

- **AQ1 — Per-action immutable nodes (validate D-B + mechanics).** We want one Blueprint node per concrete action,
  with the action fixed at creation and pins baked from its ParamsType (no runtime action-swap). (a) Does this fit
  the architecture? Preferred mechanism: **per-action palette entries over a single `ChannelCommandNode` kind with
  an immutable baked (channel, actionId)** vs **distinct node kinds per action**? (b) The current `ChannelCommandNode`
  selects its action mutably in the Details panel — should action selection become **create-time-only/immutable**,
  and how should any existing assets migrate? (c) Should the **per-action palette entries** be generated from the
  `IChannelCommandCatalog` (channel commands) and the action schema (other behavior actions)?

- **AQ2 — Blueprint-authored action surfacing & identity.** A behavior action may be authored as an `AiPrimitive`
  blueprint (Params + hostings) instead of hardcoded C#. (a) How does such an action become available as a palette
  node in the OTHER editors (BTree/HSM/other Blueprints)? (b) What is its **canonical identity** in the action
  catalog and in consuming nodes' references — the blueprint **AssetId** or the **generated Params/thunk type FQN**?
  (c) Its Params DTO is generated to C# only at build, so is the action discoverable/typed **only after a
  build+reload** (ActionSchemaExporter reflecting the compiled assembly), and is that latency the accepted model —
  or is there a JSON-schema path to surface its params pre-build?

- **AQ3 — Baked pins vs ParamsType drift.** Given D-B bakes a node's pins from the action's ParamsType at creation,
  how should we handle the action's ParamsType **changing later** — a blueprint-authored action's Params edited
  in-tool, or a hardcoded DTO changed in C#? Options: re-project pins on reload preserving compatible wires/literals
  by name; a validation diagnostic on mismatch; explicit pin versioning. What is the intended reconciliation
  (sharper for blueprint-authored actions, whose DTO is editable inside the tool)?

---

**Status: design discussion. No implementation.** Builds on the settled decisions in ENUM-DESIGN.md
(§RESOLVED) and must integrate with `docs/blueprints/Blackboard_Authoring_Detailed_Design.md` (the action-DTO
discovery §10, per-node binding §11, aggregation §5).

## The problem (user-raised, 2026-06-06)
A **behavior action** (e.g. MoveTo, FollowRoute, AimAndFire) is a named unit of behavior with a **parameter DTO**.
Internally it may drive a channel — but channel-poking is an *implementation detail*, not the authoring concept.
The user wants **one node per available behavior action**, and notes the action must surface **differently per
host graph**:

- **In a Blueprint** (data-flow graph): the action node must expose **a data-IN pin per parameter** (so dynamic
  values can be wired in). → settled: dynamic pin projection from `ParamsTypeFqn` (ENUM-DESIGN §RESOLVED, architect Q5).
- **In a BTree** (no data-flow pins): the action node has **no param pins**; its parameters must be **connected to
  blackboard variables** (or set to literals) via the Inspector. → StructEdit Inspector + Blackboard DD §11 binding.
- **In an HSM** (state action/activity): same as BTree.

And an action can be defined **two ways, both required**:
- **Hardcoded** — a C# method (`[BTreeAction]`/`[HsmAction]`/`[SharedAiAction]`, or a channel-command catalog
  entry); its ParamsType is the first `ref` parameter.
- **Visually authored via Blueprints** — an AiPrimitive blueprint that declares `Params` + a hosting
  (`BTreeAction`/`BTreeCondition`/`HsmActivity`/`HsmGuard`/`BlueprintCall` — these hostings already exist in the
  compiler). Its Params DTO is generated to C# at build.

## The emerging unifying model (to validate with architect)
**One concept: a "behavior action" = { Id, Category/Channel, ParamsTypeFqn, valid-host-contexts, source }.**
The single source of truth for an action's *parameters* is its **ParamsType DTO**. Per host:

| Host | Param surface | Binding/value | Mechanism |
|------|---------------|---------------|-----------|
| Blueprint | data-IN pins (one per DTO member) | wire data-flow; literal via System B inline editor | `NodePinSchema.GetCanonicalPins` reflects `ParamsTypeFqn` |
| BTree | Inspector fields (no pins) | bind each field → blackboard var, or literal | StructEdit facet + Blackboard DD §11 |
| HSM | Inspector fields (no pins) | bind each field → blackboard var, or literal | StructEdit facet + Blackboard DD §11 |

A unified **action catalog** would enumerate all actions (hardcoded attrs + channel catalog + blueprint
AiPrimitives) and drive **per-action palette entries** in each editor (filtered by valid host). That satisfies
"one node per action" without necessarily creating N node *classes* (could be one node-kind + per-action palette
entries that preset the action id).

## Open questions for the architect (next round — focused, with context)
Context to provide: *settled that Blueprint params = dynamic data-IN pins (System B for literals) and BTree/HSM
params = StructEdit Inspector bound to blackboard vars; the compiler already supports AiPrimitive hostings
(BTreeAction/BTreeCondition/HsmActivity/HsmGuard/BlueprintCall); IChannelCommandCatalog exists for blueprint
channel commands; the Blackboard DD §10 reflects [BTreeAction]/[HsmAction]/[SharedAiAction] into an action schema
and §11 binds action-DTO fields to blackboard variables.*

- **QA1 — Unified catalog?** Is there meant to be a **single behavior-action catalog** spanning channel commands +
  hardcoded `[SharedAiAction]`/`[BTreeAction]`/`[HsmAction]` + blueprint-authored AiPrimitives, driving per-action
  palette entries across all three editors? Or do the subsystems intentionally keep separate catalogs
  (`IChannelCommandCatalog` for blueprint channels; the §10 schema for BTree/HSM)? What is the intended single
  abstraction for "an invokable action with a ParamsType"?

- **QA2 — Blueprint node granularity.** For "one node per action," is the intended model **one node-kind
  (e.g. the existing `ChannelCommandNode`, possibly generalized) with per-action palette entries** that preset the
  action id and drive dynamic pin projection — or genuinely **distinct node types per action**? (The dynamic-pin
  projection from `ParamsTypeFqn` works either way; this is about palette/registry structure + UX.)

- **QA3 — Channel-command identity.** Should the blueprint `ChannelCommandNode` be **generalized into a single
  "behavior-action invocation" node** (channel commands becoming one category among `[SharedAiAction]`s usable in
  blueprints), since channel-poking is an internal impl detail? Or do channel commands remain a distinct node
  concept separate from other behavior actions?

- **QA4 — BTree/HSM channel-action invocation.** How is a channel-driving action invoked from a BTree/HSM today vs
  intended — via a hardcoded `[SharedAiAction]`/`[BTreeAction]` method whose body pokes the channel (params bound
  to blackboard vars in the Inspector), or is a first-class "invoke channel action" BTree/HSM node intended? Does
  the same action's ParamsType drive both the blueprint pins and the BTree/HSM Inspector binding?

- **QA5 — Blueprint-authored actions surfacing.** When a designer authors an AiPrimitive blueprint (Params + a
  hosting), how does it become available as a node in the OTHER editors' palettes, and how does its Params DTO
  (generated at build) get reflected for pins (blueprint) / Inspector binding (BTree/HSM)? Is the post-build/reload
  reflection latency (Blackboard DD v2.1 aggregation note — schemas visible only after a reload bakes them) the
  accepted mechanism? Should the unified action catalog include blueprint-authored actions discovered from
  blueprint assets, and what is their canonical identity (AssetId? generated type FQN?)?

- **QA6 — Param literal vs blackboard-var in BTree/HSM.** Confirm the BTree/HSM Inspector offers, per action-param
  field, BOTH (a) bind-to-blackboard-variable and (b) set-literal-constant — and that this is exactly the
  Blackboard DD §11 per-node binding surface that the InspectorWindow StructEdit wiring will render.
