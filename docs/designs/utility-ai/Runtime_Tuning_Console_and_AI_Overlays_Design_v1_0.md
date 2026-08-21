# Runtime AI Tuning Console & AI Debug Overlays — Design v1.0

Consolidated design from the brainstorming sessions between the project owner and Claude.

This document covers **two features that share one substrate**: a **runtime AI tuning console**
(live-edit AI knobs without recompiling) and **AI-specific debug overlays** (perception cones,
EQS scoring, target memory, and the Utility "why did it pick this?" view rendered in-world). They
are designed together because both ride the existing **GizmoMap** rails — the same
`StructInspector` primitive, focus-arbitrated `IStatefulGizmo` lifecycle, and DDS transport that
already drive `LayerControlGizmo` today. Tuning *is* a StructEdit-backed gizmo; overlays *are*
gizmo sources. Combining them means one transport, one focus model, one operator surface.

Prereq reading: GizmoMap Contracts/Example (the `StructInspector` + `IStatefulGizmo` pattern),
AI Editor Shared Infra §12–§13 (`DebugState.Flags` / `TraceBufferLifecycleSystem`), and the
Utility AI architecture doc §9 (the decision trace these overlays surface).

---

## 1. Goals and scope

### 1.1 Why these two, together

- **Tuning console.** AAA AI work is 80% balance iteration: perception ranges, utility weights and
  curves, cooldowns, EQS refresh rates, squad focus-fire caps. Today every change is an edit →
  recompile → relaunch cycle, or at best a hot-reload of an authored asset. Operators (designers,
  test engineers, the architect during a live exercise) need to turn knobs **in a running cluster**
  and see the effect immediately.
- **Debug overlays.** "Why is this agent doing that?" is unanswerable from logs alone. The data —
  FOV cones, LOS rays, EQS scored candidate points, target-memory aging, the utility decision
  breakdown — is all in the ECS but invisible. Rendering it in-world over the agent turns a
  guessing game into an observation.

They share a substrate because the **overlay shows you the problem and the console lets you fix it
without leaving the view** — select an agent, see its utility decision overlay, open the tuning
panel for that decision's weights, drag a curve, watch the overlay update next tick. One loop.

### 1.2 What's in scope

- A **tuning registry** of named, typed, live-editable parameters with provenance and bounds.
- A **TuningConsoleGizmo** — a `StructInspector`-backed panel (one or many) for editing them.
- **Write paths** that respect the Brain/Muscle split and the engine's deterministic discipline.
- A family of **AI overlay gizmo sources**: perception, EQS, target memory, utility decision,
  squad assignment.
- **Per-entity enablement** through the existing `DebugState.Flags` path, so overlays are
  off-by-default and near-zero-cost when off.
- **DDS transport** so a remote ExCon/operator station drives tuning and sees overlays on a live
  cluster.

### 1.3 What's out of scope

- **Persisting tuned values back to C# source.** The console edits *runtime* state. Promoting a
  good tuning back into the authored asset is a separate manual step (export, §9.4) — the console
  is not an asset editor and never writes `.cs`.
- **Structural edits.** Adding a consideration or an option is asset authoring (the Utility editor),
  not tuning. The console edits values within an existing structure only.
- **Replacing the visual editors.** Overlays are observation; authoring stays in the editors.

---

## 2. The shared substrate — why GizmoMap already does most of this

`LayerControlGizmo` is the existence proof. It:

- holds editable state (a 256-bit layer mask),
- emits a `StructInspector` primitive built from a `ComponentEditService` (StructEdit.Reflection),
- receives commits via `OnStructUpdate(json)` and applies them,
- emits a `MainMenuBinding` so an operator can open it,
- and runs identically in-process (`LocalGizmoTransport`) or across the cluster
  (`DdsGizmoTransport`), with interactions forwarded back over the `GizmoInteractionBatch` topic.

Everything the tuning console needs is in that list. The console is `LayerControlGizmo`
generalized from "one layer mask" to "any registered tuning struct," and the overlays are
additional `IGizmoSource` emitters feeding the same buffer.

```
            ┌──────────────────────── Operator station (ExCon / viewer) ────────────┐
            │  GizmoViewerFrontend renders consumer buffer                          │
            │     • overlays (cones, EQS points, utility bars)                      │
            │     • TuningConsoleGizmo StructInspector panel(s)                     │
            │  user drags a curve / edits a weight  ─┐                              │
            └────────────────────────────────────────┼──────────────────────────────┘
                         ▲ primitives (DDS)           │ GizmoInteractionBatch (DDS)
                         │                             ▼
            ┌────────────┴───────────── Brain / Muscle nodes ──────────────────────┐
            │  IGizmoSource emitters:                                               │
            │    PerceptionOverlaySource, EqsOverlaySource, TargetMemoryOverlay,    │
            │    UtilityDecisionOverlay, SquadAssignmentOverlay                     │
            │  TuningConsoleGizmo (IStatefulGizmo)                                   │
            │    OnStructUpdate(json) → TuningRegistry.Apply → write path (§5)       │
            └────────────────────────────────────────────────────────────────────────┘
```

---

## 3. The tuning registry

### 3.1 A tunable is a named, bounded, owned value

```csharp
public readonly struct TuningKey
{
    public readonly uint Id;            // FNV-1a of the dotted name
    public readonly string Name;        // "perception.fovDegrees", "utility.CombatPosture.TakeCover.eqsWeight"
}

public sealed class Tunable
{
    public TuningKey Key;
    public TuningKind Kind;             // Float | Int | Bool | Enum | Curve
    public float Min, Max;              // bounds (from [EditRange] or registration)
    public TuningScope Scope;           // Global | PerNodeRole | PerEntity | PerSquad
    public TuningOwner Owner;           // Brain | Muscle  — which node authoritatively holds it
    public Func<float> Read;            // current live value
    public Action<float> Write;         // applies to live state (validated, clamped)
    public string Provenance;           // where it came from (asset id, system name)
}
```

### 3.2 What registers tunables

Three sources, all converging on one registry:

1. **`[Tunable]`-attributed fields** on systems/config structs, discovered by source-gen (mirrors
   `[UtilityInput]` / `[BTreeAction]` discovery). Example: `EqsSolverModule.EqsHz`,
   `AutonomousPerceptionModule.FovDegrees`.
2. **Utility decisions**, registered automatically: every weight, curve param, and hysteresis on a
   loaded `UtilityDecisionDef` becomes a tunable keyed
   `utility.<DecisionName>.<Option>.<Consideration>.<field>`. This is the high-value set — it makes
   the entire utility layer live-tunable for free, and the overlay (§7.4) points straight at it.
3. **`GlobalDebugSettings`** existing fields (`DebugLayerMask`, `MaxGizmoFrameMs`,
   `AutoEnableAiTracing`) — folded in so the one console covers them too.

### 3.3 Grouping and discovery

Tunables are organized by dotted namespace into a tree the console renders as collapsible groups:

```
perception/        fovDegrees, refreshHz, losMaxRange, contactTtlSeconds
eqs/               eqsHz, budgetMs, maxAccurateRaycasts, topKThreshold
utility/
  CombatPosture/   TakeCover.eqsWeight, Flee.healthCurveK, hysteresisBonus, …
  ThreatRanking/   …
squad/             focusFireCap, assignmentBiasWeight
recorder/          keyframeInterval
```

The operator filters by substring (the `LayerControlGizmo` inspector already shows the search
pattern works).

---

## 4. The TuningConsoleGizmo

### 4.1 It is a generalized `LayerControlGizmo`

```csharp
public sealed class TuningConsoleGizmo : IStatefulGizmo
{
    public const long AnchorId = 9001L;          // stable registration key (LayerControl is 9999)
    public const int  OpenActionId = 260;        // "Tools > AI Tuning Console..."
    public bool RequiresExclusiveFocus => false; // coexists with overlays and other gizmos

    void ToggleEditor();                          // show/hide the StructInspector panel
    void OnStructUpdate(string json);             // parse → TuningRegistry.Apply (§5)
    void UpdateAndDraw(float dt, IGizmoDrawBuilder b);  // emit StructInspector + MainMenuBinding
}
```

Per frame it emits a `MainMenuBinding` ("Tools > AI Tuning Console…") and, when open, a
`StructInspector` primitive whose `EditDocument` is built from the **currently-selected tuning
group** projected into a flat DTO. Commits arrive as JSON via `OnStructUpdate` exactly like
`LayerControlGizmo` — the same StructEdit.Json round-trip, the same
`GizmoInteractionBatch` transport.

### 4.2 The DTO is synthesized, not a fixed struct

`LayerControlGizmo` edits a fixed layer DTO. The tuning console edits a **synthesized** DTO: the
selected group's tunables become fields, with `[EditRange(Min,Max)]` carried from the `Tunable`
bounds so StructEdit surfaces sliders with the right limits. `StructEdit.Reflection` builds the
`EditDocument`; the synthesized type is generated per group (cached by group hash). Curve tunables
expand into their `m,k,b,c` sub-fields, so a curve is editable as four bounded scalars in the same
panel — and the same numbers the Utility editor's curve panel shows (consistent mental model).

### 4.3 Scoped editing

A tunable's `Scope` controls what the edit targets:

- **Global** — one value, applies everywhere (e.g. `recorder.keyframeInterval`).
- **PerNodeRole** — applies on nodes of a role (e.g. `eqs.eqsHz` only matters on Muscle).
- **PerEntity** — the operator first selects an entity (via the existing gizmo pick/selection path),
  then edits that entity's override (e.g. force one agent's `perception.fovDegrees` for debugging).
- **PerSquad** — targets a squad's leader entity / shared blackboard (e.g.
  `squad.focusFireCap`).

`EditScope.ForField` (StructEdit) is used for per-entity single-field edits, matching the existing
"focused editing" guidance.

### 4.4 Multiple panels

Because the console is just a gizmo with a non-exclusive focus, an operator can open several —
one pinned to perception, one to a specific utility decision — and arrange them, same as any other
gizmo windows. Each is a `TuningConsoleGizmo` instance with a different selected group and a
distinct `AnchorId` offset.

---

## 5. Write paths — respecting CQRS and determinism

This is the part that is *not* free from GizmoMap and needs care. A live edit must reach the
authoritative owner and apply without breaking the deterministic frame.

### 5.1 The owner decides the path

- **Brain-owned tunable** (utility weights, threat params, perception cognitive thresholds): the
  edit applies on the Brain node. If the operator is on ExCon, the commit travels over
  `GizmoInteractionBatch` to the Brain, where `TuningConsoleGizmo.OnStructUpdate` runs.
- **Muscle-owned tunable** (`eqs.eqsHz`, raycast caps, navmesh sample density): the edit must reach
  Muscle. It travels as a **tuning command** over the same orchestration/command rails used for
  other cross-node config, *not* by mutating a Brain copy and hoping it replicates.

The `Tunable.Owner` field makes this routing mechanical: the registry on each node only exposes
write for tunables it owns; a commit for a non-owned tunable is forwarded as a command to the
owner. This mirrors the engine's existing principle that config flows Brain→Muscle as replicated
component data / commands, results flow back as events.

### 5.2 Apply on a frame boundary, never mid-tick

A commit does not write live state immediately. `TuningRegistry.Apply` enqueues the change; the
registry drains the queue at a **fixed point in the frame** (top of frame, before systems read
config), so no system sees a value change underneath it mid-tick. This preserves the determinism
the engine depends on and matches how `PerspectiveCoordinatorSystem` drains UI events at frame top.

### 5.3 Validation and clamping

`StructEdit` reads `[EditRange]` into metadata but does **not** clamp (documented engine behavior).
So `TuningRegistry.Apply` clamps to `[Min,Max]` and runs any registered validator before writing,
rejecting out-of-range commits with a console notice rather than corrupting state.

### 5.4 Determinism / replay honesty

A live tuning change is a **non-deterministic external input** — it breaks bit-exact replay if
silently applied. Two rules:

1. Every applied tuning change is **recorded into the Flight Recorder** as a discrete
   `TuningChangeEvent` (key, old, new, wall-tick, operator id), so a replay re-applies the same
   change at the same frame and stays deterministic.
2. In **Deterministic (lockstep) mode**, tuning changes route through the orchestrator so all nodes
   apply them on the same agreed frame, exactly like other synchronized state transitions. In
   Continuous mode they apply on the next local frame boundary.

This is the single most important correctness constraint in the design: **tuning is an input to be
recorded, not a side-channel mutation.**

### 5.5 Hot-reload interaction

When an authored asset hot-reloads, its tunables are re-registered. A live tuning override on a
value the reload didn't change is preserved; a reload that changes structure (`StructureHash`)
drops overrides for removed fields (with a console notice). This reuses the utility hot-reload
soft/hard classification.

---

## 6. Overlay architecture

### 6.1 Overlays are gizmo sources gated by `DebugState.Flags`

Each overlay is an `IGizmoSource` that, per frame, queries entities whose `DebugState.Flags` has
the relevant overlay bit and emits primitives for them. This reuses the **exact** mechanism the
editor's observer mode uses (`TraceBufferLifecycleSystem`, AI Shared Infra §13): a single flag,
set in bulk or per-entity, drives attachment of the heavier machinery and the overlay emission.

```csharp
[Flags]
public enum AiOverlayFlags : ushort
{
    None            = 0,
    Perception      = 1 << 0,   // FOV cone, LOS rays, sensor ring
    TargetMemory    = 1 << 1,   // known contacts, aging, threat value
    Eqs             = 1 << 2,   // scored candidate points, top-K highlight
    UtilityDecision = 1 << 3,   // per-option bars, winner, consideration breakdown
    SquadAssignment = 1 << 4,   // leader→member→target assignment lines
    Channels        = 1 << 5,   // active locomotion/weapon/interaction action
}
```

These extend the existing `BehaviorDebugFlags` family rather than inventing a parallel system.
Off-by-default; near-zero cost when off (a flag check in the source's query filter).

### 6.2 Budget honored

Overlay emission respects `GlobalDebugSettings.MaxGizmoFrameMs` — the existing per-frame gizmo
budget. A frame that would blow the budget sheds the lowest-priority overlays first (Channels
before UtilityDecision before Perception), so a heavily-observed scene degrades gracefully rather
than tanking the sim.

### 6.3 Layer-masked

Each overlay emits on a distinct `LayerMask256` layer, so the operator toggles overlay families on
and off through the **existing `LayerControlGizmo`** — no new visibility UI. (Another reuse: the
layer control already exists and already transports.)

---

## 7. The overlay catalog

### 7.1 Perception overlay

For each flagged entity: the FOV cone (from `AutonomousPerceptionModule` parameters), the sensor
ring (max range), and the batched LOS rays from the last perception pass (green = clear,
red = blocked), plus a small label with refresh age. Makes "why didn't it see the enemy" obvious —
the enemy is outside the cone, or the LOS ray is red.

### 7.2 Target memory overlay

Draws each contact in `TargetMemory` as a marker over its last-known position, with: a staleness
fade (older contacts dimmer), a threat-value bar, and a line from the agent to the contact colored
by faction. Shows what the agent *believes*, which may differ from ground truth — the core of most
"bad AI" bug reports.

### 7.3 EQS overlay

For a flagged entity with an active `EqsSensor`, draws the candidate points from
`EqsCognitiveBuffer`: each point colored by score (heatmap), the Top-K highlighted, the winner
ringed, and gated/rejected candidates shown faded with their gate reason on hover. This is the
spatial twin of the utility overlay and directly supports the cover/flank/retreat tuning loop.
(The plumbing — `EqsCognitiveBuffer` Top-K with per-result flags — already exists; the overlay
just renders it.)

### 7.4 Utility decision overlay — "why did it pick this?" in-world

The headline integration with the Utility AI layer (§9 of that doc). For a flagged entity it reads
`UtilityTraceWorkingMemory1024` and draws, anchored over the agent:

```
        ┌─ entity 42 · CombatPosture ─────────────┐
        │ TakeCover   ▮▮▮▮▮▮▮▮ .81  ◀ WIN (+.09)  │
        │ Advance     ▮▮▮▮▮▮   .72                │
        │ Flee        ▮▮▮▮     .41                │
        │ Suppress    ▮▮▮      .33                │
        │ Hold        ▮▮       .20                │
        │ ── TakeCover considerations ──          │
        │  HealthFraction  0.35 →curve 0.65 ×0.8  │
        │  EqsTopScore     0.85 →curve 0.85 ×1.0  │
        │  EnemyStrength   0.60 →curve 0.71 ×0.6  │
        └─────────────────────────────────────────┘
```

This is the same breakdown the editor's live-preview shows (Utility wireframes §2) and the same the
trace test asserts (StarterPack §5) — three views, one data source. Clicking the decision name
opens the TuningConsoleGizmo focused on that decision's group (§3.2 item 2), closing the
observe→tune loop in one gesture.

### 7.5 Squad assignment overlay

Anchored on the leader entity: draws a line from the leader to each member, and from each member to
its **assigned** target (solid) and to the target it is **actually engaging** (dashed). When the two
diverge, the member has vetoed (Utility §10.3) — the overlay makes the veto visible, and a label
gives the dominant self-preservation consideration that caused it.

---

## 8. Enablement UX

### 8.1 Selecting what to observe

Three granularities, all through existing paths:

- **Per-entity:** operator picks an entity (gizmo selection), toggles overlay families via a context
  menu action (`ToggleAiTrace` / `ToggleAiTraceLog` already exist as action ids 251/252; we extend
  with per-family toggles). Sets `DebugState.Flags` on that entity.
- **Per-asset:** "observe all entities running CombatPosture" — reuses
  `BeginObservingAsset` (AI Shared Infra §13.2), which sets the flag on all matching entities and
  future spawns.
- **Global:** `GlobalDebugSettings.AutoEnableAiTracing` already stamps trace on every AI entity at
  genesis; we extend it with an overlay-family mask so "show me everything" is one switch (subject
  to the frame budget, §6.2).

### 8.2 The combined loop

The intended operator workflow, and the reason the two features are one doc:

1. Something looks wrong with an agent. Operator selects it; enables Utility + EQS overlays.
2. The utility overlay shows `Flee` winning when it shouldn't — `EnemyStrengthRatio` curve is too
   aggressive.
3. Operator clicks the decision name → TuningConsoleGizmo opens on `utility.CombatPosture`.
4. Operator drags `Flee.enemyStrengthCurveK` down. The change records to the Flight Recorder (§5.4),
   applies next frame boundary (§5.2).
5. The overlay updates next tick; `Advance` now wins. Loop closed without a recompile.
6. When satisfied, operator exports the tuning delta (§9.4) to hand back into the C# asset.

---

## 9. Source structure & integration points

```
Hrot.Diagnostics/Hrot.Diagnostics.Tuning/
├── TuningKey.cs, Tunable.cs, TuningScope.cs, TuningOwner.cs
├── TuningRegistry.cs                 // register, group, apply-queue, clamp/validate, drain
├── TuningAttribute.cs                // [Tunable] field marker (source-gen discovered)
├── TuningChangeEvent.cs              // recorded to Flight Recorder (§5.4)
├── UtilityTuningBinder.cs            // auto-registers every loaded UtilityDecisionDef field (§3.2)
└── Gizmos/
    └── TuningConsoleGizmo.cs         // IStatefulGizmo, StructInspector-backed (§4)

Hrot.Diagnostics/Hrot.Diagnostics.Overlays/
├── AiOverlayFlags.cs                 // extends BehaviorDebugFlags family (§6.1)
├── PerceptionOverlaySource.cs
├── TargetMemoryOverlaySource.cs
├── EqsOverlaySource.cs
├── UtilityDecisionOverlaySource.cs   // reads UtilityTraceWorkingMemory1024 (§7.4)
├── SquadAssignmentOverlaySource.cs
└── OverlayBudgetArbiter.cs           // honors MaxGizmoFrameMs, sheds by priority (§6.2)
```

Integration points (all existing, all reused):

| Reused thing | Used for |
|---|---|
| `StructInspector` primitive + `ComponentEditService` | the tuning panel UI |
| `IStatefulGizmo` / `GizmoInteractionManager` | console + overlay lifecycle, focus |
| `IGizmoTransport` (Local / Dds) | in-process and cross-cluster operation |
| `GizmoInteractionBatch` topic | commit forwarding from a remote operator |
| `DebugState.Flags` / `TraceBufferLifecycleSystem` | per-entity overlay enablement |
| `LayerControlGizmo` / `LayerMask256` | toggling overlay families |
| `GlobalDebugSettings` | folded into the console; budget + auto-enable |
| `UtilityTraceWorkingMemory1024` | the utility decision overlay's data |
| Flight Recorder event capture | recording tuning changes for replay honesty |

---

## 10. Test strategy

- **TuningRegistry**: apply clamps to bounds; out-of-range rejected; apply-queue drains at frame top
  only (assert no mid-tick value change visible to a probe system); owner routing forwards
  non-owned commits.
- **Replay honesty (the critical test)**: record a session with a mid-run tuning change; replay;
  assert the change re-applies at the same wall-tick and the post-change frames are bit-identical.
- **Gizmo round-trip**: `OnStructUpdate(json)` from a synthesized group DTO applies the right
  tunables (reuse GizmoMap's headless 30-frame harness).
- **Overlay gating**: an entity without the flag emits zero overlay primitives; with the flag, the
  expected count; budget arbiter sheds lowest-priority family when over `MaxGizmoFrameMs`.
- **Utility overlay fidelity**: the bars/considerations rendered match
  `UtilityTraceWorkingMemory1024` for a fixture entity (shares fixtures with Utility StarterPack §5).
- **Cross-node**: in DDS mode, a commit on the viewer reaches the Brain and applies; a Muscle-owned
  tunable edited from ExCon reaches Muscle (forwarded command path).

---

## 11. Open questions

- **T-1. Operator identity & permissions.** Should tuning be gated (only certain operator roles may
  write)? `TuningChangeEvent` records an operator id; whether to *enforce* is a policy call. Leaning:
  record always, enforce via a simple role flag in Slice 2.
- **T-2. Curve editing in StructInspector.** §4.2 expands a curve to four scalars. A proper
  drag-handle curve widget (like the Utility editor) inside a gizmo `StructInspector` is richer but
  needs a custom StructEdit field editor. Leaning: four scalars in Slice 1, custom widget in Slice 2;
  the numbers are identical either way.
- **T-3. PerEntity override lifetime.** When does a forced per-entity override (§4.3) expire — on
  entity destroy only, or a TTL? Leaning: on destroy, with a "clear all overrides" console button.
- **T-4. Overlay anchoring in 3D vs 2D map.** The utility/squad overlays are text-heavy; they read
  well on the 2D tactical map but may clutter a 3D viewport. `PipelineTarget` (Map2D / Viewport3D /
  NodeGraph) already exists on primitives — do we restrict text-heavy overlays to Map2D by default?
  Leaning: yes, with an opt-in for 3D.
- **T-5. Does the tuning registry need a snapshot/restore ("revert all to authored")?** Useful after
  a wild tuning session. Leaning: yes — capture authored defaults at registration, expose
  "revert group" and "revert all." Cheap; high operator value.

---

*End of Runtime AI Tuning Console & AI Debug Overlays design v1.0. Related: Utility AI architecture
(the decision trace surfaced by §7.4), AI Editor Shared Infra §12–13 (the flag/lifecycle path
reused by §6), GizmoMap (the substrate).*
