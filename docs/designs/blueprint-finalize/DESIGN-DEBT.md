# Blueprint editor — design debt & discrepancies (deferred)

Captured 2026-06-06 from running-editor review. These are NOT yet scheduled; recorded so they aren't lost.
Current focus is the enum mechanism (see ENUM-DESIGN work); these wait.

> **⚠ DD-1 UPDATED (architect, 2026-06-06):** the original proposal below (per-action nodes whose params live in
> a StructEdit Details facet) is **REJECTED for Blueprints** — it breaks data-flow wiring. Blueprint action params
> MUST stay as dynamically-projected data-IN pins (catalog `ParamsTypeFqn` → `NodePinSchema.GetCanonicalPins`).
> What survives of DD-1: (a) the action-dropdown-in-Details is fine as STATIC METADATA selection (channel+action),
> NOT param editing; (b) the user's "a separate node per action" desire is reframed as a **palette / action-catalog**
> question (one node-kind with per-action palette entries vs N node-kinds) and the broader **behavior-action node
> duality** (BTree-bind-to-blackboard vs Blueprint-pins) — moved to **ACTION-NODE-DESIGN.md** (still open).

## DD-1 — The generic "ChannelCommand" node concept is wrong (original; see UPDATE above)
**Symptom (user, live editor):** The single `ChannelCommand` node lets you pick *any* action from *any* channel
via a Details-panel dropdown (Locomotion/MoveTo, Locomotion/FollowRoute, Weapon/..., Interaction/...). But the
param pins it then projects ("Destination", "ArrivalRadius", "Speed", ...) only make sense for the chosen action
(e.g. locomotion MoveTo). One generic node spanning all channels/actions is the wrong model.

**Desired design (user):**
- **Per-action nodes**, not one generic ChannelCommand. Each action is its own node type whose **pins correspond to
  that action's parameter DTO** (e.g. a `MoveTo` node with Destination/ArrivalRadius/Speed pins; a `FollowRoute`
  node with its own params).
- **Selecting the action in the Details panel makes no sense** — the action IS the node. Picking it from the node
  palette (categorized by channel) is the right UX; the node's pins are then fixed by the action's DTO.
- Channels (Locomotion / Weapon / Interaction / ...) become **palette categories**, each containing its actions.

**Open questions for design time:**
- Source of truth for the action→DTO mapping (the `IChannelCommandCatalog` already knows ChannelType + ActionId +
  ParamsTypeFqn — extend it to drive per-action palette entries + pin projection from the DTO fields).
- How action params are authored when the value is a literal vs wired (see DD-2).
- Migration of any existing generic ChannelCommand assets (Loco1 etc. are throwaway; low concern).

## DD-2 — Action parameter authoring: pins vs a StructEdit property grid (cross-cuts HSM)
When an action node is used **from a BTree graph** (not data-wired), its parameters need to be **set as literal
values**. The user suggests a **StructEdit property grid** for the param DTO — the **same UI HSM state nodes
should use** for their action/activity params.

**Current state to confirm/design:**
- HSM state nodes' param editing via StructEdit is **"unwired now"** (user) — needs a proper solution.
- Want **one** property-grid solution that serves BOTH: blueprint action nodes (literal param authoring) AND HSM
  state-node params. Avoid two divergent mechanisms.
- This is closely tied to the inline-pin-default story: inline pin editors render on the canvas, but inline pin
  DEFAULTS are not consumed by the compiler yet (`Stage3_Normalize.MaterializeDefaultPinLiterals` is a no-op stub
  for ALL types). A StructEdit property grid for action params would need a real persistence + compile path for
  those literal values.

## DD-3 — Rare ChannelCommand node collapse on inline-editor touch (residual)
**Status:** BATCH-UX1 fixed the reproducible collapse (ApplyPinIds wasn't passing the channel catalog → exec-only
pins stamped → pass-0 short-circuit). The user reports it **still happened ONCE** afterward, now **very hard to
reproduce**. So a residual path remains (possibly a different rebuild/edit ordering, or a transient projection
miss). Not gone. Re-investigate if it recurs more reproducibly; keep a watch. Likely related to the
`node.Pins` fast-path vs slow-path (JSON-loaded `Pins:[]`) interaction in `BlueprintGraphModel.Rebuild` /
`NodePinSchema.GetCanonicalPins` pass-0.

## DD-4 — Inline pin defaults are not compiled (Stage3 stub) — cross-cutting
`Stage3_Normalize.MaterializeDefaultPinLiterals` is a no-op for ALL types: inline pin default values (int, float,
FixedString, etc.) render + persist in `Node.PinDefaults` but never reach the generated code. Whatever literal
authoring solution we pick (inline editors and/or DD-2's StructEdit grid) needs a real Stage3 materialization
path. Affects every literal-default type, not just the new ones.
