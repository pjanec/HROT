# Utility AI — Editor Detailed Design v1.2

> **Status:** Detailed design. Follow-on to the Utility AI architecture (v1.1), the Source Generator
> & Analyzer DD (v1.1), and the Editor Wireframes. Derived from `AI_Editor_Shared_Infrastructure.md`,
> `BTree_Editor_NodeEditor_Host_Design.md`, `HSM_Editor_NodeEditor_Host_Design.md`,
> `Blackboard_Authoring_Detailed_Design` (the closest precedent — see below), and the
> `Visual_Asset_Comparison_Detailed_Design`.

> **Changelog v1.1 → v1.2** (resolves the open questions):
> - **E-1 → fixed small set (3–4 candidates) for Slice 1** (§7, §13). Author-variable deferred.
> - **E-2 → locked curve params shown-disabled, not hidden** (§5.2, §13). Teaches the model; no
>   layout jump on `CurveKind` swap.
> - **E-3 → live preview throttled to 10 Hz** (§7.2, §13), matching the cognitive/EQS cadence — a
>   *fidelity* decision, not just a perf one: previewing faster than the runtime thinks would
>   misrepresent in-game responsiveness.
> - **E-4 → authoring new `[UtilityInput]` readers stays out of scope** (§6, §13). Inputs are
>   engineer-owned code; the editor consumes the closed `In.*` set, never creates it.

> **Changelog v1.0 → v1.1** (incorporates the v236 editor review):
> - **Reframed from "structural departure" to "follows `BlackboardAuthoringWindow`" (§1, §3.3).** The
>   `ManagedWindow` card-table (instead of a NodeEditor host) and the partial-manifest read-only rule
>   are *not* novel — `BlackboardAuthoringWindow` already establishes both: a custom ImGui layout in a
>   `ManagedWindow` wired to `EditorSelectionStore`/`IRefactorService`, and a read-only lockout
>   (`StructParseFailed` / `SpanCaptureFailed`) when its parser can't safely round-trip. The Utility
>   editor is a second instance of that established pattern, not an exception to the three-host norm.
> - **Shared-infra additions pinned to named extension points (§11):** `AssetKind` enum (+`Utility`),
>   a `UtilityTraceLaneProvider : ITraceLaneProvider` (mirroring `BTreeTraceLaneProvider`), and
>   `SubElementKind` enum (+`UtilityInput`). No vague "add a case."
> **Audience:** Implementation agent and human reviewer.
> **Drives:** The visual editor for Utility AI decisions — a fourth editor sitting on
> `Hrot.Editor.AiShared`, sharing its selection bus, emitter contract, inspector dispatch, hot-reload
> classification, debug session hierarchy, and comparison pipeline.
> **Doesn't cover:** The scoring runtime (architecture DD), the generators/analyzer (source-gen DD),
> the in-world tuning overlay (Runtime Tuning Console & Overlays DD). The curve *math* is owned by
> the architecture DD §5; this doc owns the curve *editing UI*.
> **Companion code lives in:** `Hrot.Utility.Editor/` (references `Hrot.Editor.AiShared`, which it
> does not modify except for the small shared additions called out in §11).

---

## 1. Scope and the non-graph host pattern

### 1.1 A fourth editor, following the `BlackboardAuthoringWindow` shape

The Utility editor is the fourth consumer of `Hrot.Editor.AiShared`, joining Blueprint, BTree, and
HSM. It reuses, unchanged: `EditorSelectionStore`, the `IFluentCSharpEmitter<TAsset>` contract and
its deterministic-output rules, the `HROT_EDITOR_GENERATED` marker and ownership model, the
`[…Layout]`-method convention for editor-only data, the Cosmetic/Soft/Hard hot-reload classifier,
the `IAiDebugSession` / `IAiTraceObserver` split, the Asset Browser, the `IRefactorService`, and the
Visual Asset Comparison pipeline.

It differs from Blueprint/BTree/HSM in one respect — and that difference is **already an established
pattern in the engine, not a new exception.** The other three editors are NodeEditor hosts because
they author graphs. A utility decision is **not a graph** — it is a table of options × considerations
with no edges and no execution flow (Wireframes §1). Forcing it onto a node canvas would map a flat
table onto `NodeId`/`LinkId`/pin structures and accrue large tech debt for no benefit.

The engine already authors non-graph AI surfaces this way: **`BlackboardAuthoringWindow` is a custom
ImGui layout inside a `ManagedWindow`** that still integrates with the shared `EditorSelectionStore`
and `IRefactorService`. The Utility editor is a second instance of that exact shape — a
`ManagedWindow` with a card-table layout that implements the canvas-independent shared contracts
(selection, emitter, debug session, inspector dispatch, refactor) and simply does not implement
`IGraphModel` / `IGraphCommandSink` / `INodeCatalog`. So this is not "three graph editors plus one
oddball"; it is "the engine's two host shapes — NodeEditor for graphs, `ManagedWindow` for tabular
authoring — with Utility joining Blackboard in the latter."

### 1.2 What this editor owns

- `UtilityDecisionAsset` — the in-memory editor model (§3).
- The **card-table window** — options, considerations, inspector, live-preview strip (§4).
- The **curve editor** — the four-param + piecewise widget (§5). This is the centerpiece.
- The **input catalog browser** — populated from the generated `In.*` accessors (§6).
- The **test-fixture system** — shared with CI, driving live preview (§7).
- `UtilityFluentEmitter : IFluentCSharpEmitter<UtilityDecisionAsset>` — the round-trip (§8).
- The **utility debug overlay panel** and `IUtilityDebugSession` (§9).
- The **comparison integration** — sanitizer + the tuning-diff fast lane (§10).

---

## 2. Topology — where it sits

```
NodeEditor (generic graph lib)         Hrot.Editor.AiShared
  ▲ used by Blueprint/BTree/HSM           selection · emitter contract · inspector
  │                                       debug hierarchy · hot-reload · comparison
  │                                              ▲
  │                                              │ references (contracts only)
Blueprint.Editor  BTree.Editor  Hsm.Editor       │
  (NodeEditor hosts)                       Hrot.Utility.Editor
                                             ManagedWindow card-table host
                                             (NOT a NodeEditor host)
                                             consumes generated In.* + manifest (source-gen DD §7)
```

The Utility editor depends on the source generator's two tooling artifacts: the `In.*` accessor set
(the catalog browser's entries) and the best-effort decision *manifest* (structure for the
comparison fast lane), both defined in the Source Generator DD §7 and §4.2.

---

## 3. The editor model

### 3.1 `UtilityDecisionAsset`

The in-memory model the window mutates and the emitter serializes. It mirrors the runtime
`UtilityDecisionDef` (architecture §4.2) but in an editor-friendly, mutable, `VisualId`-bearing form
— the same relationship `BehaviorTreeAsset` has to `BehaviorTreeBlob`.

```csharp
public sealed class UtilityDecisionAsset : IEditableAsset   // shared marker
{
    public Guid AssetId;                      // stamped by editor; FNV-1a-32 at runtime (shared identity model)
    public string DisplayName;
    public DecisionKind Kind;                 // ThreatRanking | WeaponSelection | PostureSelect
    public string Category;
    public float HysteresisBonus;             // per-decision (Q-1)
    public List<OptionModel> Options;         // single template option for candidate kinds
    public List<FixtureRef> Fixtures;         // §7
    // editor-only, persisted via [UtilityLayout] (§8.3): card order, collapsed state, pinned fixture
    public UtilityLayoutData Layout;
}

public sealed class OptionModel
{
    public ushort OptionId;                   // posture id; 0 for candidate-template
    public string Name;
    public ScoringMode Mode;                  // WeightedProduct | WeightedSum
    public List<ConsiderationModel> Considerations;
    public string VisualId;                   // stable id for deterministic emit + comparison annotation
}

public sealed class ConsiderationModel
{
    public string InputName;                  // resolves to In.<InputName>; validated by UT0120
    public InputContext Context;              // Self | Target | Leader | Candidate
    public InputParamsModel Params;           // sensor name etc. (editor-side, typed)
    public ResponseCurveModel Curve;          // §5
    public float Weight;
    public string VisualId;
}
```

`VisualId` on options and considerations is what makes emission deterministic (shared rule §6.2:
sorted traversal by stable id) and what the comparison feature annotates against (§10) — exactly how
BTree nodes carry `VisualId` for the same two purposes.

### 3.2 Loading

Opening an asset:

1. The shared Asset Browser lists `UtilityDecisionAsset` files via `IAssetCatalog` (type-filter chip
   "Utility," added to the shared browser per §11).
2. On open, the editor reads the `.cs` file. If the `HROT_EDITOR_GENERATED` marker is present →
   editable; absent → read-only with "Duplicate as Editable Copy" (shared ownership model §6.3).
3. Structure comes from the **manifest** (source-gen DD §4.2) when fully extractable; for `partial`
   manifests (a `Build` using loops/helpers), the editor falls back to reflecting the built
   `UtilityDecisionDef` from the loaded assembly — the same fallback the comparison fast-lane uses.
   A `partial`-sourced asset opens **read-only** with a banner ("This decision's `Build` uses
   constructs the editor can't round-trip; view-only"), because emitting it would discard the
   author's loop/helper structure. This protects the C#-source-of-truth guarantee.

This read-only-on-unparseable rule is the **same UX the engine already uses for tabular AI
authoring**: `BlackboardAuthoringWindow` shifts to `StructParseFailed` / `SpanCaptureFailed` and
locks read-only when its parser hits a field it can't safely round-trip (e.g. a multi-line
declaration), precisely so the emitter never wipes hand-authored C#. The Utility editor's
`partial`-manifest banner is the same mechanism with a utility-specific trigger — an established
convention, not a new safeguard.

---

## 4. The card-table window

### 4.1 Layout

Per Wireframes §1: a two-pane card table. Left/center: option cards, each a small table of
considerations. Right: the inspector for the selected consideration (the curve editor lives here).
Bottom: the live-preview strip. Top: decision-level fields (Kind read-only after creation, default
mode, hysteresis).

The window is a `ManagedWindow` (shared window manager), drawn with ImGui (same UI stack as the
shared Inspector and the GizmoMap `StructInspector`). It is **not** a NodeEditor host; it owns its
own draw loop for the table.

### 4.2 Selection integration

The window subscribes to `EditorSelectionStore`. Selecting a consideration sets a
utility-sub-selection on the store so the shared Inspector (if open) can also show the consideration
facet, and so the debug overlay (§9) knows which consideration to highlight. Selecting an entity in
the world (shared selection) drives the live preview to that entity's real state instead of a fixture
(§7.4) — the observe-the-real-agent path.

### 4.3 Editing operations and undo

All edits go through a command stack so undo/redo is uniform with the other editors. Commands:
`AddOption`, `RemoveOption`, `RenameOption`, `SetOptionMode`, `AddConsideration`,
`RemoveConsideration`, `SetInput`, `SetContext`, `SetParam`, `SetWeight`, `SetCurve*`,
`ReorderConsideration`, `SetHysteresis`. Each command records the inverse for undo. The card table
reorders are cosmetic for scoring (product/sum are order-independent) but are preserved in emit order
so diffs stay stable (shared determinism rule).

### 4.4 Validation surfacing

The editor runs the same `UtilityAuthoringAnalyzer` rules (source-gen DD §6) live, debounced, and
shows diagnostics inline on the offending card/row with a toolbar count — the Wireframes §8
treatment. Because the analyzer is the authority, the editor calls into the shared diagnostic
descriptors rather than re-implementing the checks, so editor-time and compile-time validation never
diverge.

---

## 5. The curve editor (the centerpiece)

The architecture DD §5 owns the curve math (`CurveKind`, the `m,k,b,c` family, piecewise side-table).
This section owns its editing UI. It is the feature with the most novel surface, because the other
three editors have no equivalent.

### 5.1 Anatomy

Per Wireframes §4: a kind dropdown, a plotted curve with draggable handles, four numeric fields
(`m,k,b,c`), and — for `PiecewiseLinear` — a control-point editor that replaces the slider row.

```
┌─ Curve: InverseLinear ▾ ───────────────────────────────┐
│  [kind ▾]                                               │
│  ┌─ plot (0..1 × 0..1) ─────────────────────────────┐  │
│  │ draggable handles map to m (slope) and b (xshift) │  │
│  │ vertical marker = current test-fixture input      │  │
│  └───────────────────────────────────────────────────┘  │
│  m[ ] k[ ] b[ ] c[ ]      input=0.35 → output=0.65       │
└─────────────────────────────────────────────────────────┘
```

### 5.2 Handle ↔ parameter mapping

Each `CurveKind` exposes a small set of draggable handles that map to its meaningful params; the
numeric fields and the handles are two views of the same values (dragging updates the fields and
vice versa). The mapping per kind:

| CurveKind | Handles | Locked params |
|---|---|---|
| Linear / InverseLinear | endpoint handles → `m`, `c` | `k=1`, `b` from left endpoint |
| Threshold / Step | a single x-position handle → `b`; height → `c` | `m`, `k` fixed by kind |
| Bell | center → `b`, width → `k`, height → `c` | `m` |
| Logistic | midpoint → `b`, steepness → `k` | `m`, `c` |
| Quadratic / InverseQuadratic | curvature handle → `k`, offset → `b` | `m`, `c` |
| PiecewiseLinear | per-point handles (add/drag/delete) | none — points are the data |

The plot evaluates the *actual runtime curve function* (architecture §5.3) so what the designer sees
is exactly what the runtime computes — no separate preview math that could drift. This is the same
discipline the live-preview strip follows (§7).

**Locked params are shown disabled, not hidden** (E-2). When a `CurveKind` fixes a param (e.g.
`m`,`k` for `Step`), the four numeric fields all stay visible with the locked ones greyed and
non-editable. Two reasons: it teaches the underlying `output = m·(x − b)^k + c` model organically —
the designer sees which knobs each kind exposes — and it keeps the inspector layout fixed so swapping
the `CurveKind` dropdown doesn't make fields appear and disappear and shift the pane.

### 5.3 The test-fixture marker

A vertical line on the plot shows where the current fixture's input value for this consideration
lands, with the resulting output labeled. Dragging a curve handle moves the output readout live, so
the designer tunes against a concrete scenario rather than in the abstract. Switching fixtures (§7)
moves the marker.

### 5.4 PiecewiseLinear editing

Selecting `PiecewiseLinear` swaps the slider row for a point editor: click-to-add, drag-to-move,
right-click-to-delete control points, each clamped to [0,1]×[0,1]. Points serialize to the curve
side-table (architecture §5.3) and emit as an array argument in the fluent C# (§8). Points are kept
x-sorted on every edit so emission is deterministic and evaluation is monotone-walkable.

### 5.5 Curve overlay (comparison)

When the comparison feature is active (§10) and a consideration's curve changed, the curve editor
draws **both** old and new curves on the same axes (Wireframes §7.2), so the designer sees the shape
delta, not just the numbers. For differing piecewise point-counts, old and new are drawn as separate
polylines with a legend (the deferred W-4 detail, resolved here as "two polylines, no point-pairing").

---

## 6. Input catalog browser

### 6.1 Populated from generated `In.*`

The "add consideration" / "change input" picker (Wireframes §3) is populated **from the generated
`In.*` accessor set** (source-gen DD §7) discovered by reflection over the loaded editor-target
assembly, grouped by the `[UtilityInput].Category`. The dropdown is literally the catalog — this is
the structural reason day-one visual editing is lossless (Wireframes §9): the editor can only author
inputs that exist, and emission is just the accessor name.

### 6.2 Context and param sub-controls

Each catalog entry advertises its `AllowedContexts` (from the attribute) and its parameter shape.
Picking an input that requires a context shows a context dropdown limited to the allowed set; picking
a parameterized input (e.g. `EqsTopScore`) shows its param sub-control (a sensor-name dropdown
populated from the EQS template registry). Choosing a disallowed context is impossible by
construction, so `UT0121` can't be tripped from the editor — the UI enforces what the analyzer checks.

### 6.3 Cross-assembly inputs

Because custom `[UtilityInput]` methods can live in referenced assemblies (source-gen DD §6.2/G-4),
the browser reflects over all loaded assemblies' `In.*` partials, not just the primary one. An input
defined upstream appears in the picker the same as a built-in.

### 6.4 The reserved `Custom` entry

Shown disabled with a "not available yet" tooltip (Wireframes §3), signposting the architecture
§6.5 future seam without implying it works. No accessor is emitted for it (source-gen DD G-3).

---

## 7. Test fixtures & live preview

### 7.1 Fixtures are shared with CI

A fixture is a fabricated entity-state snapshot (health, ammo, contacts, EQS scores) plus an
**expected winner**. These are the *same* fixtures the integration tests use (StarterPack §0/§3.2):
the editor loads them, the live-preview strip evaluates the decision against them, and a mismatch
between actual and expected shows a ✗ — the identical assertion CI makes. Editor and CI share the
fixture files so a tuning change that breaks a named scenario is visible in both places.

### 7.2 Live evaluation uses the real scorer

The live-preview strip (Wireframes §1) and every "live" number in the cards run the **actual
`UtilityScorer`** against the selected fixture, not a reimplementation. This is non-negotiable: a
separate editor-side scoring path could drift from runtime and make the preview lie. The editor
constructs a throwaway Brain-only `EntityRepository` (the `TestRepository.CreateBrainOnly` helper),
seeds the fixture, and runs one scoring pass with tracing on, reading back the per-consideration
breakdown (§9 trace) to fill the cards.

**Re-evaluation is throttled to ~10 Hz** (E-3), not run per editor frame. This is a fidelity choice
before it is a performance one: the cognitive pipeline and EQS solvers run asynchronously at ~10 Hz,
so a decision in-game only re-scores at that cadence. Previewing per-frame (e.g. 60 Hz) would show
the designer a responsiveness the agent will never actually have — exactly the kind of editor-lies
this section exists to prevent. Throttling the preview to the runtime's thinking rate keeps the
editor honest and incidentally avoids burning CPU in the editor loop; 10 Hz still reads as
instantaneous to the eye. (Against a live selected entity, §7.4, the same 10 Hz applies.)

### 7.3 Fixture editing

"[edit fixture…]" (Wireframes §5) opens a small struct editor (the shared StructEdit drawer) over the
fixture's state; "save as new fixture" writes it back as a CI fixture file, closing the
author→regression loop (W-1, resolved here as: editable, writes back to the shared fixture corpus).

### 7.4 Preview against a live entity

When a real entity is selected in the world (shared selection), the preview switches from fixture to
that entity's live state, evaluated each editor frame. This is the bridge to the in-world tuning loop
(Runtime Tuning DD §8.2): observe a real agent's decision in the editor, tune, watch it change.

---

## 8. The round-trip emitter

### 8.1 `UtilityFluentEmitter`

Implements the shared `IFluentCSharpEmitter<UtilityDecisionAsset>` contract and obeys every shared
deterministic-output rule (§6.2 of shared infra): stable ordering by `VisualId`, sorted `using`s,
4-space indent, fixed blank-line policy, lowercase `D`-format Guids, no timestamps, the
`HROT_EDITOR_GENERATED` marker + AssetId header.

It emits the `[UtilityDecision]` attribute, the class, and the fluent `Build` body:

```csharp
// HROT_EDITOR_GENERATED — manual edits to this file will be overwritten by the AI editor on next save.
// AssetId: 3c6f9e42-5d10-6f3a-ac23-posture0000001
namespace Hrot.AI.Utility.Decisions;

[UtilityDecision(
    AssetId = "3c6f9e42-5d10-6f3a-ac23-posture0000001",
    DisplayName = "Combat posture",
    Kind = DecisionKind.PostureSelect,
    Category = "Tactical/Posture",
    HysteresisBonus = 0.08f)]
public sealed class CombatPostureDecision : IUtilityDecisionDefinition
{
    public static void Build(IUtilityDecisionBuilder b) => b
        .Option(Posture.TakeCover, Mode.WeightedProduct, o => o
            .Consider(In.HealthFraction(Ctx.Self),  w: 0.8f, Curve.InverseLinear)
            .Consider(In.EqsTopScore("CoverQuery"), w: 1.0f, Curve.Linear)
            .Consider(In.EnemyStrengthRatio(),      w: 0.6f, Curve.Logistic))
        // ... emitted in VisualId order
        ;
}
```

### 8.2 Why the round-trip is lossless

Each model element maps to exactly one deterministic C# fragment (Wireframes §9), and crucially the
authored vocabulary is **closed**: inputs are `In.*` accessor names (not expressions), contexts and
curves are enum members, curve params are four floats (plus a piecewise float array). There is no
free-form expression to serialize or parse, so emit → Roslyn-parse → reflect → compare is exact. The
shared emitter self-test mode (§6.2) runs this round-trip on the utility starter-pack corpus in CI.

### 8.3 Editor-only data via `[UtilityLayout]`

Card order, collapsed/expanded state, the pinned fixture, and the per-consideration last-selected
flag are editor-only. Following the shared "no sidecar files" rule (§7 of shared infra), they are
emitted as an opt-in `[UtilityLayout]` static method in the same file, ignored by the runtime and the
generators, read back by the editor on load. A layout-only change classifies as **Cosmetic**
(§8.5) — the runtime never sees it.

### 8.4 Curve params and emission precision

Floats emit with round-trip precision (`R` format) so a loaded-then-saved curve is bit-stable. This
matters for the Cosmetic/Soft classification: re-saving without touching a curve must not flip a
`ParamHash` due to float formatting.

### 8.5 Hot-reload classification

The editor reuses the shared Cosmetic/Soft/Hard classifier (§17 of shared infra) with utility's
hashes:

| Tier | Utility trigger | Effect |
|---|---|---|
| **Cosmetic** | `[UtilityLayout]` change only (card order, collapsed state, pinned fixture) | runtime unaffected |
| **Soft** | weight or curve-param change (`ParamHash` differs, `StructureHash` same) | hot-patch decision params; running selections keep hysteresis state |
| **Hard** | option or consideration added/removed, input changed, mode changed (`StructureHash` differs) | bump Generation; result buffers for the decision reset next tick |

The hashes are the **runtime** ones from `ToDef()` (source-gen DD §4.4), not the best-effort manifest
hashes — the authoritative classification path stays on robust runtime computation. The
`HotReloadStatusIndicator` pill shows the tier, including the instance-reset count for Hard.

---

## 9. Debug — the in-editor decision view

### 9.1 `IUtilityDebugSession`

Derives from the shared `IAiDebugSession` (§12 of shared infra), giving the utility editor the same
attach/detach, observe-asset, and entity-highlight vocabulary as the other three. What differs is the
"step" semantics: a utility decision is evaluated atomically (no sub-stepping), so the session
exposes **per-evaluation breakpoints** ("break when this decision next evaluates on the selected
entity") and a **decision-history scrubber** rather than step-into/over/out.

### 9.2 The decision inspector

When attached and an entity is selected, the editor reads `UtilityTraceWorkingMemory1024`
(architecture §9) for that entity and renders the full breakdown the Wireframes §2 and the in-world
overlay (Runtime Tuning DD §7.4) show: options ranked with bars, the winner and runner-up margin, and
per-consideration `raw → normalized → curve → weighted` for the selected option. This is the same
data the live preview uses (§7.2) — three surfaces (editor preview, in-editor debug, in-world
overlay), one trace source, guaranteeing they agree.

### 9.3 Observer mode

Reuses `TraceBufferLifecycleSystem` (§13 of shared infra): "observe all entities running this
decision" sets `DebugState.Flags` to attach the utility trace buffer on matching entities and future
spawns, so the inspector lights up without per-entity manual toggling.

---

## 10. Comparison integration

### 10.1 Reuses the Visual Asset Comparison pipeline

Per Wireframes §7, the editor plugs into the existing comparison pipeline (sanitize C# → user-shuttled
LLM → annotate). A `UtilityComparisonSanitizer` (sibling to the BTree/HSM/Blueprint sanitizers, living
in `Hrot.Utility.Editor/Comparison/`) strips `[UtilityLayout]` and presentation data, emits the
canonical fluent C# in `VisualId` order, and preserves `VisualId`s for annotation correlation.
Annotations land on **option cards and consideration rows** (not canvas nodes), with the same
outline/badge vocabulary the comparison DD defines.

### 10.2 The tuning-diff fast lane

The utility-specific shortcut (Wireframes §7.2): when the two versions have equal `StructureHash` and
differing `ParamHash` — detected via the source-gen manifest hashes for the fast pre-check, confirmed
against runtime hashes — the editor offers an **instant, offline tuning diff** (no LLM). It lists each
changed weight and curve param with old→new and a delta arrow, and selecting a row overlays both
curves in the curve editor (§5.5). The LLM path is reserved for structural changes where semantic
interpretation helps. The choice is automatic: structure-equal → tuning diff; structure-differ → LLM
comparison.

### 10.3 Sanitizer determinism

The comparison DD's byte-stability property (`sanitize(file) == sanitize(file)`) must hold for the
utility sanitizer, so a no-op comparison yields an empty diff. The `R`-format float emission (§8.4)
and `VisualId` ordering are what make this hold.

---

## 11. Required additions to shared infrastructure

The Utility editor is mostly additive, but it needs four small shared touches (each a minor,
backward-compatible extension, owned by the shared-infra maintainer). The v236 review pinned each to
an exact extension point:

1. **`AssetKind` enum + `Utility`.** The `AssetKind` enum currently has `Blueprint`, `BTree`, `Hsm`;
   add `Utility` and register `UtilityDecisionAsset` with `IAssetCatalog`, which gives the Asset
   Browser its filter chip (§3.2). One enum value + one icon.
2. **Inspector dispatch arm.** The shared `InspectorWindow` already delegates sub-pane rendering by
   active-asset type; add the `UtilityDecisionAsset` → consideration-facet drawer arm (§4.2). The
   Utility editor supplies the facet struct; shared infra just adds the dispatch case.
3. **`UtilityTraceLaneProvider : ITraceLaneProvider`.** The `TraceTimelineWindow` is driven by
   `ITraceLaneProvider` implementations (e.g. `BTreeTraceLaneProvider` injecting node-execution
   lanes). A `UtilityTraceLaneProvider` appends decision-scoring evaluations to the shared timeline
   the same way — no change to the timeline window itself, just one more provider registered.
4. **`SubElementKind` enum + `UtilityInput`.** The `SubElementKind` enum defines what
   `IRefactorService` can rename across the ecosystem (`ActionFqn`, `ConditionFqn`,
   `BlackboardVariable`, …). Add `UtilityInput` so `IRefactorService.PreviewRename` finds and updates
   every `.Consider(In.OldName(...))` reference across all saved decisions when an engineer renames a
   `[UtilityInput]` method. Decision `AssetId` reuses the existing asset-reference kind. This is
   exactly the cross-asset refactor the shared layer exists for.

None of these modify existing behavior; they extend registries the shared layer already exposes for
the three current editors.

---

## 12. Test strategy

- **Emitter round-trip** (the critical one) — for the starter-pack corpus: model → emit → Roslyn-parse
  → reflect → assert structural equality with the original model; and emit → re-emit → assert
  byte-identical (determinism). Run in the shared emitter self-test harness.
- **Lossless on reload** — open a `.cs`, edit one weight, save, reload; assert only that weight
  changed and classification is Soft.
- **Partial-manifest read-only** — a decision whose `Build` uses a loop opens read-only with the
  banner; assert no emit path is reachable for it.
- **Curve UI fidelity** — dragging a handle updates `m,k,b,c` and the plotted output equals the
  runtime curve function at sample points; piecewise points stay x-sorted; output clamps [0,1].
- **Live preview = runtime** — the preview's per-consideration numbers equal a direct `UtilityScorer`
  run on the same fixture (proves no drift); a fixture's expected-winner mismatch surfaces ✗.
- **Catalog browser** — picker entries equal the generated `In.*` set including a cross-assembly
  input; disallowed contexts are unselectable (UT0121 unreachable from UI).
- **Hot-reload classification** — layout-only → Cosmetic; weight change → Soft; option added → Hard,
  with correct instance-reset count.
- **Comparison** — structure-equal versions trigger the tuning-diff fast lane (no LLM); structure-
  differ versions produce a sanitized export; sanitizer is byte-stable across runs.
- **Refactor** — renaming a `[UtilityInput]` updates all `.Consider(In.…)` references across decisions
  via the shared `RefactorService`.

The starter-pack decisions are the canonical editor fixtures (as they are for the generator and the
comparison feature), so all three tooling layers exercise the same assets.

---

## 13. Resolved questions (editor review)

All four resolved; recorded here as decisions with their rationale.

- **E-1. Candidate-template preview shape — RESOLVED: fixed small set (3–4) in Slice 1.** A
  `ThreatRanking`/`WeaponSelection` fixture supplies a fixed handful of candidates; the preview shows
  the ranked list over them (§7). A small representative sample is enough to validate the ranking math
  and product-mode gating, and squad/Top-K caps are 16 anyway, so nothing larger is needed to prove
  the UX. Author-variable candidate counts are deferred to a later slice.
- **E-2. Locked curve params — RESOLVED: shown-disabled, not hidden** (§5.2). All four `m,k,b,c`
  fields stay visible with the kind-locked ones greyed. Teaches the `m·(x−b)^k+c` model and keeps the
  inspector from reflowing when the `CurveKind` dropdown changes.
- **E-3. Live-preview cost — RESOLVED: throttle to ~10 Hz** (§7.2). Matches the cognitive/EQS async
  cadence, so the preview reflects real in-game responsiveness rather than hallucinating 60 Hz
  snappiness; also avoids editor-loop CPU burn while still reading as instant.
- **E-4. Editor-authored `[UtilityInput]` readers — RESOLVED: out of scope** (§6). Inputs are
  engineer-owned code; the editor consumes the closed, generator-discovered `In.*` set and never
  creates it. Stubbing C# from the editor would cross data-authoring into code-authoring and threaten
  the lossless round-trip guarantee. The engineer/designer ownership split (engineer owns
  inputs/code, designer owns curves/tuning) is intentional and load-bearing.

---

*End of Utility AI Editor DD v1.2. This completes the Utility AI document set: Architecture (v1.1),
Starter Pack (v1.1), Source Generator & Analyzer (v1.1), Editor (v1.2), plus the WeaponState MaxAmmo
prerequisite. All open questions across the set are resolved. The Runtime Tuning Console & AI
Overlays DD shares this feature's trace and tuning surfaces.*
