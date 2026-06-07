# Build Order — Utility AI + Runtime Tuning Console + AI Overlays v1.0

> **Status:** Implementation sequencing plan. Spans three designed feature sets — Utility AI
> (Architecture v1.1, Starter Pack v1.1, Source Generator v1.1, Editor v1.2), the Runtime Tuning
> Console & AI Overlays (v1.0), and the Curve-Editor-in-StructEdit guide (v1.1) — plus the
> WeaponState MaxAmmo prerequisite.
> **Audience:** Implementation lead sequencing the work; reviewers checking dependency soundness.
> **Drives:** A six-phase interleaved order that builds Utility AI and the tuning/overlay tooling
> *together* rather than serially, so the shared substrate (trace buffer, source generator, curve
> widget, GizmoMap) is built once and the live observe→tune loop comes online before the heavyweight
> visual editor.
> **Doesn't cover:** The feature designs themselves (see the referenced DDs) — only their ordering and
> the dependency rationale.

---

## 1. Why interleave instead of build-then-bolt-on

The individual DDs present Utility AI as a self-contained tower (runtime → generator → editor) and the
tuning console + overlays as a separate combined document. Read literally, that implies: finish all of
Utility AI, then build the tuning/overlay tooling on top.

That serial reading is suboptimal because **three artifacts are shared dependencies of both feature
sets**, and a fourth — the live observe→tune loop — is far more valuable early than late:

1. **The decision trace** (`UtilityTraceWorkingMemory1024`) feeds the editor's live preview, the
   in-editor debug view, *and* the in-world utility overlay. It is a shared root, not a Utility-only
   detail.
2. **The source generator** (`In.*` accessors + registrars) gates both the editor's catalog browser
   and the tuning console's auto-registration of weights/curves as tunables.
3. **The curve widget** (`CurveWidget.Draw`) is the editor's centerpiece *and*, wrapped as a StructEdit
   drawer, the tuning console's Slice-2 UI. Build-it-twice is the explicit trap the Curve-Editor guide
   warns against.
4. **The observe→tune loop** (see an agent's decision overlay, tune a weight, watch it change) is the
   point of the whole tooling effort — and it can exist with *scalar* tuning as soon as the runtime
   and generator are done, long before the visual editor.

Interleaving pulls these shared pieces forward and builds the two halves of the debugging instrument —
the overlay (observe) and the console (act) — as a pair, since each validates the other.

---

## 2. The dependency graph (hard edges)

Edges that *must* be respected, regardless of ordering preference:

```
  WeaponState.MaxAmmo ─────────────► ammo readers (weapon/posture decisions)
                                            │
  Scoring core + TRACE BUFFER ──────────────┼──► everything observational
        │                                   │
        ├──► Source generator (In.* + registrars)
        │          │
        │          ├──► Editor catalog browser
        │          └──► Tuning console auto-registration of tunables
        │
        ├──► Curve widget (standalone) ──┬──► Editor card inspector
        │                                └──► Tuning console Slice 2 (via StructEdit drawer)
        │
        └──► TRACE ──┬──► Editor live preview / in-editor debug
                     └──► In-world utility overlay (GizmoMap)

  GizmoMap substrate ──┬──► AI overlays
                       └──► Tuning console (StructInspector + GizmoInteractionBatch)
```

The two non-obvious edges that drive the interleave: **the trace buffer is a shared root** (three
consumers across both features), and **the curve widget is a shared leaf** (two consumers across both
features). Build the root first and the leaf before either UI consumer.

---

## 3. The six-phase order

### Phase 0 — Prerequisite bundle (was: single `MaxAmmo` field)

Updated 2026-05-28: the original single-field `MaxAmmo` prereq expanded into a **six-item bundle**
after the v236 codebase review surfaced four more discrepancies. The bundle is now a real chunk of
work (sequenced deliberately, not parallel-and-forgotten). See [`PREREQ_Phase0_Bundle.md`](./PREREQ_Phase0_Bundle.md).

Items:
- **P0.1** `WeaponState.MaxAmmo` cached at spawn (the original).
- **P0.2** Multi-mount weapon entities (child entities with `WeaponState` + `WeaponMountInfo` + `PartMetadata`).
- **P0.3** `PerceptionConstants.MaxTrackedTargets` raised from 4 to 16.
- **P0.4** `UnitRoster.Add` / `IndexOf` zero-alloc helpers.
- **P0.5** `Blackboard1024.Project<T>(ref bb)` helper wrapping `Unsafe.As<,>`.
- **P0.6** `UtilityTestWorld` Brain-only test scaffolding.

P0.1 and P0.2 touch production data paths (spawn + TKB translator); P0.3 affects two struct sizes
but no behavior; P0.4/P0.5/P0.6 are pure-add helpers. The bundle ships as one batch.

**Exit criteria:** all six items in `main` and tested green; a single integration test instantiates
`UtilityTestWorld`, spawns a multi-mount agent, populates a leader's `Blackboard1024` via
`Project<T>`, adds 16 contacts, and reads back through each new API.

---

### Phase 1 — Runtime core + trace buffer (headless)

The foundation both feature sets stand on. No UI, no gizmos.

- `UtilityScorer`, the aggregation math (product-with-compensation + weighted-sum), the curve
  evaluation functions, `UtilityConsideration` / `UtilityOption` / `UtilityDecisionDef`.
- `UtilityResultBuffer` (`[InlineArray(16)]`, with the span-cast write discipline).
- **`UtilityTraceWorkingMemory1024` baked in from the first line** — per-option, per-consideration
  `raw → normalized → curve → weighted → aggregate`, winner + runner-up margin. This is *designed in,
  never retrofitted* (Architecture §9.2); building it now is what makes Phases 4 and 5 cheap.
- The greedy `ThreatMatrixAssignmentSystem` over the commander `Blackboard1024` /
  `ThreatMatrixAssignmentState` projection.

**Validated by:** the Starter Pack integration tests (v1.1) against a fake Brain-only world — the four
named decisions, the gating behavior, the wounded-member veto, and the **trace assertions** (which
double as the regression guard for the observational features built later).

**Exit criteria:** all starter-pack tests green; trace buffer populated and asserted; zero UI.

> Why first: the trace is the shared root of every observational surface in both features. If it isn't
> present and correct here, the editor preview, the in-editor debug view, and the in-world overlay all
> have nothing to read.

---

### Phase 2 — Source generator + analyzer

Makes decisions authorable in C# and self-discovering.

- `UtilityInputGenerator` → `UtilityInputRegistrar.g.cs` + `UtilityInputAccessors.g.cs` (`In.*`).
- `UtilityDecisionGenerator` → `UtilityDecisionCatalog.g.cs` + per-decision `.Id` constants + the
  best-effort manifest.
- `UtilityAuthoringAnalyzer` (the `UT####` diagnostics; purity check copying `EqsTemplatePurityAnalyzer`).
- The `[UtilityRegistrar]` startup discovery handshake.

**Validated by:** the hash-parity test (gen-time vs. runtime FNV, exact codebase formula, with pinned
vectors — the critical silent-failure guard); generator snapshot tests; one fixture per diagnostic.

**Exit criteria:** the starter-pack decisions compile, self-register, and validate clean; hash parity
proven.

> **Milestone — end of Phase 2: a working, authorable, testable Utility AI with zero UI.** Everything
> after this is observation and tuning, which is where the two feature sets merge.

---

### Phase 3 — The curve widget, standalone

Build `CurveWidget.Draw` as the host-agnostic ImGui function (Curve-Editor guide Step 2): plot,
draggable handles, the `m,k,b,c` numeric fields (locked ones greyed per kind), piecewise control-point
editing, the optional test-fixture input marker, the optional comparison overlay (opt-in via
`CurveWidgetOptions`). It evaluates the *actual runtime curve function* so it cannot drift from
Phase 1.

Built **before** either UI consumer, with **no StructEdit and no Utility-editor dependency**, so the
same function serves both the editor card inspector (Phase 5) and the tuning console Slice 2 (Phase 6).

**Validated by:** a small standalone ImGui harness — handle drag updates `m,k,b,c`; plotted output
equals the runtime curve at sample points; piecewise points stay x-sorted and clamped.

**Exit criteria:** the widget runs in a throwaway harness, host-agnostic, matching runtime math.

> Why here: it is the single most-shared piece of UI in the cluster. Building it as a standalone
> artifact now is what prevents the "build the curve editor twice" trap.

---

### Phase 4 — AI overlays + Tuning console Slice 1 (together)

**The heart of the interleave.** Both ride the GizmoMap substrate, both gate on `DebugState.Flags`,
and together they form the observe→tune loop. Build them as a pair.

Overlays (each an `IGizmoSource` gated by `AiOverlayFlags`, budget-honoring, layer-masked):
- **Utility decision overlay** — reads the Phase-1 trace, renders the "why did it pick this" bars +
  consideration breakdown in-world.
- Perception, target-memory, EQS, squad-assignment overlays.

Tuning console Slice 1:
- `TuningConsoleGizmo` (generalized `LayerControlGizmo`), `TuningRegistry`, `UtilityTuningBinder`
  auto-registering the Phase-2 decisions' weights/curves/hysteresis as tunables.
- **Curves rendered as the four `m,k,b,c` scalars** (Slice 1 — the visual widget comes in Phase 6).
- Frame-top apply via the enqueue/drain discipline; **`TuningChangeEvent` recorded to the Flight
  Recorder** (the determinism-honesty rule — built now, not deferred).
- Owner-routing (Brain vs. Muscle tunables).

**Validated by:** overlay-gating tests (no flag → zero primitives); the replay-honesty test (recorded
tuning change re-applies at the same wall-tick, post-change frames bit-identical); gizmo round-trip in
the headless harness.

**Exit criteria — the payoff:** select an agent → utility overlay shows its decision → open the console
→ drag a weight (scalar) → overlay updates next tick. **The observe→tune loop works, with scalar
tuning, before the visual editor exists.** You can now balance real AI live.

> Why together: the overlay is how you *see* whether a tune worked; the console is how you *act* on
> what the overlay shows. Two halves of one instrument; each is only half-testable without the other.
> They also share one GizmoMap integration, so building them apart means doing that integration twice.

---

### Phase 5 — The Utility editor (card-table)

The largest single piece, deliberately after the observational tooling because its surfaces reuse
artifacts that already exist and are proven.

- The `ManagedWindow` card-table host (following the `BlackboardAuthoringWindow` pattern).
- Option/consideration cards, the input catalog browser (from Phase 2's `In.*`), the inspector.
- **Live preview** and **in-editor debug** — both read the Phase-1 trace via a throwaway Brain-only
  repo running the real scorer (throttled to ~10 Hz, matching runtime cadence).
- The curve inspector **calls the Phase-3 `CurveWidget` directly.**
- `UtilityFluentEmitter` — the lossless round-trip; the partial-manifest read-only rule.
- Comparison integration (sanitizer + tuning-diff fast lane).
- The four shared-infra additions (`AssetKind` +Utility, inspector dispatch arm,
  `UtilityTraceLaneProvider`, `SubElementKind` +UtilityInput).

**Validated by:** the emitter round-trip (model → emit → parse → reflect → equal; emit → re-emit →
byte-identical); live-preview-equals-runtime; hot-reload classification (Cosmetic/Soft/Hard).

**Exit criteria:** designers can author decisions visually with a lossless C# round-trip; preview and
debug agree with runtime (same trace source).

> Why after Phase 4: the editor's preview and debug views consume the trace (Phase 1), the catalog
> (Phase 2), and the curve widget (Phase 3) — all already built and proven. Building the editor last
> among the surfaces means it assembles existing parts rather than inventing them.

---

### Phase 6 — Tuning console Slice 2 + cross-surface polish

Upgrade the console from scalars to the visual widget, and wire the two tools together.

- Wrap `CurveWidget` as the two StructEdit plugins — `UtilityCurveFieldEditor` (`ICustomFieldEditor`,
  collapses to one `EditNodeKind.Custom` node) and `UtilityCurveFieldDrawer` (`IImGuiFieldDrawer`,
  delegates to the widget). Register both on the console's `ComponentEditService` /
  `ComponentEditDrawer`. The console's scalar curves become the visual editor with two registrations.
- **Piecewise translate-on-apply:** managed `DynamicArray` edit on the console side → fixed-size
  blittable buffer at frame-top apply, clamping overflow with a warning.
- The **editor↔console bridge:** clicking a decision name in the utility overlay opens the console on
  that decision's tuning group (the one-gesture observe→tune loop, now with the visual curve editor).
- Overlay budget tuning, snapshot/restore ("revert group" / "revert all" to authored defaults),
  per-entity override lifetime (clear on destroy + manual clear-all).

**Validated by:** the console's curve edits produce the same `UtilityCurve` JSON a scalar edit would
have; piecewise round-trips through translate-on-apply; revert restores authored values.

**Exit criteria:** the in-world console has the full visual curve editor; editor and console show
identical curve numbers; the observe→tune loop is one gesture end to end.

---

## 4. What combining buys, concretely

| Benefit | Serial order would… | Interleaved order does… |
|---|---|---|
| **Trace built once, three consumers** | risk building the editor's preview data path, then duplicating it for the overlay | treat the trace as the shared root from Phase 1; all three surfaces read it |
| **Observe→tune loop early** | arrive only after the entire visual editor (Phase 5+) | arrive at Phase 4 with scalar tuning — balance real AI while the editor is still being built |
| **Curve widget once** | tempt building it inside the editor, then retrofitting into the console | build it standalone at Phase 3; both UIs consume the same function |
| **One GizmoMap integration** | integrate twice (overlays, then console) | integrate once at Phase 4, shared by both |
| **Each tool validates the other** | leave overlay and console each half-testable until both exist | build them as a pair; the overlay shows whether a tune worked |

The structural shift from the DDs' implied order: **the trace buffer is pulled forward as an explicit
shared root, the curve widget is inserted as a standalone phase before either UI consumer, and
overlays + scalar tuning are built before the visual editor** so the live loop comes online early.

---

## 5. The one fork that would change this order

The order above optimizes for **getting the live observe→tune loop running early** (Phase 4 before
Phase 5). It assumes that balancing real AI live — with scalar tuning initially — is more valuable to
you sooner than designers authoring decisions in the visual editor.

If instead the priority is **designers in the visual editor as soon as possible**, swap Phases 4 and 5:
build the editor first (it only needs Phases 1–3), then overlays + tuning. The dependency graph permits
either; nothing in Phase 5 depends on Phase 4, and nothing in Phase 4 depends on Phase 5. They share
only their common ancestors (trace, generator, curve widget), all built by Phase 3.

Everything else in the sequence is fixed by hard dependencies:

- Phase 0 (six-item prereq bundle, v1.2) gates everything else. Not parallel.
- Phase 1 (core + trace) is the root — unmovable.
- Phase 2 (generator) must follow Phase 1 and precede any authoring/tuning UI.
- Phase 3 (curve widget) must precede both UI consumers (Phases 4–6).
- Phase 6 (console Slice 2) must follow both Phase 3 (the widget) and Phase 5 (if the editor↔console
  bridge is wanted; the StructEdit wrap itself only needs Phase 3).

So the only genuine choice is the Phase 4 ↔ Phase 5 swap, driven by whether *live tuning* or *visual
authoring* is the earlier priority.

---

## 6. Phase summary

| Phase | Deliverable | Gates | Key validation |
|---|---|---|---|
| **0** | Phase-0 bundle (6 items) | all Phase-1+ utility work | bundle integration test green |
| **1** | Scoring core + **trace buffer** + assignment | everything observational | starter-pack tests + trace assertions |
| **2** | Source generator + analyzer | authoring + tuning registration | hash parity (pinned vectors); snapshot tests |
| **3** | Standalone `CurveWidget` | both UI consumers | handle↔param sync; matches runtime math |
| **4** | Overlays + tuning console Slice 1 | the observe→tune loop | replay-honesty; gating; **loop works (scalar)** |
| **5** | Utility editor (card-table) | visual authoring | emitter round-trip; preview==runtime |
| **6** | Console Slice 2 + bridge + polish | full visual in-world tuning | curve JSON parity; piecewise translate-on-apply |

---

*End of Build Order v1.0. References: Utility AI Architecture v1.1, Starter Pack v1.1, Source Generator
v1.1, Editor v1.2; Runtime Tuning Console & AI Overlays v1.0; Curve-Editor-in-StructEdit v1.1;
WeaponState MaxAmmo prerequisite.*
