# Utility AI — Visual Editing Wireframes v1.0

ASCII wireframes and explanations for the day-one visual editor (architecture doc §11.2). The
editor edits a model and emits C# via `UtilityFluentEmitter`; C# remains the source of truth. The
authored vocabulary is closed (catalog inputs, enum contexts, enum curves with four numeric
params), so the round-trip is lossless without an expression parser — every control below maps to
a dropdown, a number, or a curve handle.

This doc also covers **asset comparison** (§7), which reuses the existing Visual Asset Comparison
pipeline (sanitize C# → LLM → annotate canvas) with one utility-specific extension: a **tuning
diff** for weights and curves that doesn't need the LLM at all.

---

## 1. Editor anatomy

The Utility editor is a NodeEditor host like the BTree/HSM editors, but a utility decision is not
a graph — it's a **table of options × considerations**. So the host uses a **two-pane card layout**
rather than a free canvas: options as cards on the left/center, an inspector for the selected
consideration on the right, and a live preview strip.

```
┌─ Utility Decision Editor ─ CombatPosture.cs ─────────────────────────────────[Validate][Compare]─┐
│                                                                                                   │
│  Kind: PostureSelect          Default mode: WeightedProduct ▾      Hysteresis: [0.08]             │
│ ┌───────────────────────────────────────────────────────┐ ┌───────────────────────────────────┐ │
│ │ OPTIONS                                          [＋]  │ │ INSPECTOR — HealthFraction        │ │
│ │                                                        │ │                                   │ │
│ │ ┌─ AdvanceAndAttack ─────────── product ─ score .72 ─┐ │ │ Input:   HealthFraction      ▾    │ │
│ │ │ ● HealthFraction      Self     Linear      w0.7    │ │ │ Context: Self                ▾    │ │
│ │ │ ● AmmoFraction        Self     Threshold   w0.9    │ │ │ Curve:   InverseLinear       ▾    │ │
│ │ │ ● EnemyStrengthRatio  —        InvLinear   w0.8    │ │ │ Weight:  [0.80] ───●──────        │ │
│ │ │ ● HaveLiveTarget      —        Step        w1.0    │ │ │                                   │ │
│ │ └────────────────────────────────────────────[＋][⋯]┘ │ │  ┌── curve preview ───────────┐   │ │
│ │ ┌─ TakeCover ◀selected──────── product ─ score .81 ─┐ │ │  │1┤            ___            │   │ │
│ │ │ ● HealthFraction ◀     Self    InvLinear   w0.8    │ │ │  │ │       ____/               │   │ │
│ │ │ ● EqsTopScore(Cover)   —       Linear      w1.0    │ │ │  │ │   ___/                    │   │ │
│ │ │ ● EnemyStrengthRatio   —       Logistic    w0.6    │ │ │  │0┼/__________________        │   │ │
│ │ └────────────────────────────────────────────[＋][⋯]┘ │ │  │ 0      input         1      │   │ │
│ │ ┌─ Suppress ────────────────── product ─ score .33 ─┐ │ │  └────────────────────────────┘   │ │
│ │ │ …                                                  │ │ │  m[1.0] k[1.0] b[0.0] c[0.0]      │ │
│ │ └────────────────────────────────────────────[＋][⋯]┘ │ │                                   │ │
│ │ ┌─ Flee ───────────────────────────────────────────┐ │ │  [Test inputs ▾]  raw 0.35        │ │
│ │ │ …                                                  │ │ │  → norm 0.35 → curve 0.65         │ │
│ │ └────────────────────────────────────────────[＋][⋯]┘ │ │                                   │ │
│ │ ┌─ Hold ──────────────────────── SUM ─── score .20 ─┐ │ │  [Remove consideration]           │ │
│ │ │ …                                                  │ │ │                                   │ │
│ │ └────────────────────────────────────────────[＋][⋯]┘ │ │                                   │ │
│ └───────────────────────────────────────────────────────┘ └───────────────────────────────────┘ │
│ ┌─ LIVE PREVIEW (test fixture: "Hurt, cover available") ──────────────────────────────────────┐  │
│ │  AdvanceAndAttack .72   TakeCover ▮▮▮▮▮▮▮▮ .81 ◀WIN   Suppress .33   Flee .41   Hold .20      │  │
│ └─────────────────────────────────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────────────────────────────────────┘
```

**Why a table, not a graph.** A utility decision has no execution flow to draw — there are no edges,
no sequencing. Forcing it onto a node canvas would invent meaningless geometry. The card table maps
1:1 to the `UtilityOption[] × UtilityConsideration[]` structure and to the emitted fluent C#, which
keeps the round-trip trivial.

---

## 2. The option card

```
┌─ TakeCover ◀selected ──────────────────── product ▾ ── live score .81 ──┐
│  drag-handle  rename↵                                                   │
│  ┌──────────────┬─────────┬─────────────┬────────┬──────────┐          │
│  │ Consideration │ Context │ Curve       │ Weight │ live      │          │
│  ├──────────────┼─────────┼─────────────┼────────┼──────────┤          │
│  │ ● HealthFrac  │ Self    │ InvLinear   │ 0.80   │ 0.65 ▮▮▮ │ ◀sel     │
│  │ ● EqsTopScore │ Cover   │ Linear      │ 1.00   │ 0.85 ▮▮▮▮│          │
│  │ ● EnemyStrRat │ —       │ Logistic    │ 0.60   │ 0.71 ▮▮▮ │          │
│  └──────────────┴─────────┴─────────────┴────────┴──────────┘          │
│  aggregate(product+comp) = 0.81                              [＋ add]   │
└─────────────────────────────────────────────────────────────────────────┘
```

- **Mode dropdown** (per option, top-right): `product` or `SUM`. Reflects §4.3/4.4. The card header
  shows the chosen mode so the SUM-mode `Hold` fallback is visually obvious among product cards.
- **Live column** shows each consideration's curve output and the option's running aggregate against
  the currently-selected **test fixture** (§5). This is the editor mirror of the runtime trace — same
  numbers a designer will later see in the debug overlay.
- **Add consideration** opens the input picker (§3). **Drag-handle** reorders (cosmetic; order doesn't
  affect product/sum, but matches the emitted C# order for clean diffs).

---

## 3. The input picker (catalog, B1)

Adding or changing a consideration's input opens the **catalog browser** — the closed set of
`[UtilityInput]` readers, grouped by category. This is why visual editing is lossless: the dropdown
*is* the catalog, emission is just the name.

```
┌─ Add consideration ─────────────────────────────────────────┐
│  search: [ammo___________]                                   │
│ ┌─ Self state ───────────────────────────────────────────┐  │
│ │   HealthFraction            Self                        │  │
│ │   AmmoFraction          ◀   Self                        │  │
│ │   WeaponReadiness           Self                        │  │
│ ├─ Targeting ────────────────────────────────────────────┤  │
│ │   ContactThreatLevel        Candidate                   │  │
│ │   DistanceToContext         Self|Target|Leader|Candidate│  │
│ │   ContactHealthFraction     Candidate                   │  │
│ │   IsAssignedTarget          Candidate                   │  │
│ ├─ Effectors ────────────────────────────────────────────┤  │
│ │   WeaponEffectivenessVsTarget   Candidate × Target      │  │
│ │   WeaponRangeBandFit            Candidate × Target      │  │
│ ├─ EQS (read cognitive buffer) ──────────────────────────┤  │
│ │   EqsTopScore   [sensor: CoverQuery ▾]                  │  │
│ │   EqsResultCount[sensor: ______ ▾]                      │  │
│ ├─ Group ────────────────────────────────────────────────┤  │
│ │   EnemyStrengthRatio        —                           │  │
│ │   AllyAdvancingNearby       —                           │  │
│ ├─ Reserved (disabled in Slice 1) ───────────────────────┤  │
│ │   Custom(propertyPath…)     ✕ not available yet         │  │
│ └─────────────────────────────────────────────────────────┘  │
│                                            [Cancel] [Add ▸]   │
└──────────────────────────────────────────────────────────────┘
```

- Inputs that take a **context** show the allowed contexts; picking one is required.
- **Parameterized inputs** (e.g. `EqsTopScore`) expose their `InputParams` inline as a **template
  picker**, populated from the EQS template registry (one entry per `[EqsTemplate]`-attributed
  type). The picker stores the template's `BlueprintId` (FNV-1a-32 of its `AssetId`); the runtime
  reader resolves the matching **child sensor entity** by `EqsSensor.BlueprintId` (architecture
  v1.2 §6.6). The picker never traffics in raw strings — template renames flow through
  `IRefactorService` via `SubElementKind`.
- The reserved **`Custom`** entry is shown but disabled, signposting the §6.5 future seam without
  implying it works yet.

---

## 4. The curve editor

Selecting a consideration's curve cell opens the curve panel. The four params (`m,k,b,c`, §5.3) map
to draggable handles plus numeric fields; `PiecewiseLinear` swaps in a control-point editor.

```
┌─ Curve: InverseLinear ▾ ────────────────────────────────────┐
│  Kind: [InverseLinear ▾]   (Linear, InverseLinear, Threshold,│
│                             Bell, Step, Logistic, Quadratic, │
│                             InverseQuadratic, PiecewiseLinear)│
│  ┌──────────────────────────────────────────────────────┐   │
│  │1 ┤●__                                                 │   │
│  │  │   \__                                              │   │
│  │  │      \__          ← drag handles to set m,b        │   │
│  │  │         \__                                        │   │
│  │0 ┤            \_____________________●                 │   │
│  │  └────────────────────────────────────────           │   │
│  │  0                input                 1             │   │
│  └──────────────────────────────────────────────────────┘   │
│  slope m [-1.0]  exp k [1.0]  xshift b [0.0]  yshift c [1.0]  │
│                                                              │
│  ▸ overlay current test-fixture input as a vertical marker   │
│    input=0.35  →  output=0.65                                │
└──────────────────────────────────────────────────────────────┘
```

- The **test-fixture input marker** (vertical line) shows where the live input lands on the curve, so
  a designer tunes against a real scenario, not in the abstract.
- `PiecewiseLinear` replaces the slider row with add/drag/delete control points; points serialize to
  the curve side-table (§5.3) and emit as an array in the fluent C#.

---

## 5. Test fixtures & live evaluation

The **live score** everywhere in the editor is computed against a selected **test fixture** — the
same fabricated-world fixtures the integration tests use (StarterPack doc §0). The designer flips
fixtures to see how the decision responds across situations without launching the sim.

```
┌─ Test fixture ──────────────────────────────────────────────┐
│  [Healthy, outnumbering ▾]                                   │
│    • Healthy, outnumbering        → expect AdvanceAndAttack  │
│    • Hurt, cover available        → expect TakeCover    ◀    │
│    • Near-death, escape exists    → expect Flee              │
│    • Near-death, no cover/escape  → expect Hold              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ self.health 0.35   ammo 0.80   enemyRatio 1.3          │ │
│  │ Eqs CoverQuery .85 (3)   RetreatQuery .20 (1)          │ │
│  │ [edit fixture…]                                        │ │
│  └────────────────────────────────────────────────────────┘ │
│  Result: TakeCover .81  ✓ matches expectation                │
└──────────────────────────────────────────────────────────────┘
```

- Fixtures carry an **expected winner**; a mismatch shows a ✗ so the designer immediately sees a
  tuning change that broke a named scenario — the same assertion the CI integration test makes
  (StarterPack §3.2). Editor and CI share the fixture files.
- **[edit fixture…]** lets a designer fabricate a new situation; it can be saved back as a new
  integration-test fixture, closing the loop between authoring and regression testing.

---

## 6. Candidate-scorer decisions (threat / weapon)

Threat and weapon decisions have a single **template option** (architecture §4.2), so their editor
shows one card labeled "per-candidate," plus a fixture that supplies a *list* of candidates and shows
the **ranked result** instead of a single winner.

```
┌─ Utility Decision Editor ─ ThreatRanking.cs ─────────────────[Validate][Compare]─┐
│  Kind: ThreatRanking      Per-candidate option (evaluated once per contact)        │
│ ┌─ per-candidate ─────────────── product ─┐  ┌─ RANKED RESULT (fixture) ────────┐  │
│ │ ● HasLineOfSight  Candidate Step   w1.0 │  │ 1. contact#7  .88  ▮▮▮▮▮▮▮▮      │  │
│ │ ● DistanceToCtx   Candidate InvLin w0.7 │  │ 2. contact#3  .61  ▮▮▮▮▮▮        │  │
│ │ ● ContactThreat   Candidate Linear w1.0 │  │ 3. contact#5  .44  ▮▮▮▮          │  │
│ │ ● ContactHealth   Candidate InvLin w0.4 │  │ ─ contact#9  .00  (no-LOS gate)  │  │
│ │ ● IsAssignedTgt   Candidate Thresh w0.9 │  │                                  │  │
│ └─────────────────────────────────[＋][⋯]┘  └──────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────────────────┘
```

- The **gated** candidate (no LOS) is shown greyed at the bottom with its gate reason — the editor
  surfaces *why* something scored ~0, mirroring the runtime trace and making the product-mode gate
  behavior legible at authoring time.

---

## 7. Asset comparison (reuses Visual Asset Comparison)

Comparison answers "what changed between two versions of this decision." It reuses the existing
pipeline wholesale — **sanitize C# → user hands to LLM → paste response → annotate** — with utility
decisions plugged in as a new sanitizable asset kind, plus one utility-specific shortcut.

### 7.1 The reused path (semantic diff via LLM)

A `UtilityComparisonSanitizer` (sibling to the BTree/HSM/Blueprint sanitizers) strips presentation
noise and emits the canonical fluent C# in deterministic order. The export, LLM contract, and
re-import/annotation are unchanged from the Visual Asset Comparison DD. Annotations land on **option
cards and consideration rows** instead of canvas nodes:

```
┌─ Comparison Mode ─ CombatPosture.cs  (vs. yesterday) ───────────[exit]─┐
│ ┌─ TakeCover ─── ✏️ modified ─────────────────────────────────────┐    │
│ │ ● HealthFraction   Self   InvLinear  w0.8                       │    │
│ │ ● EqsTopScore(Cov) —      Linear     w1.0 → w0.6   ✏️ weight    │    │ ◀ blue outline + ✏️
│ │ ● EnemyStrengthRat —      Logistic   w0.6                       │    │
│ └─────────────────────────────────────────────────────────────────┘    │
│ ┌─ Suppress ─── ➕ added ──────────────────────────────────────────┐    │ ◀ orange outline + ➕
│ │ …                                                               │    │
│ └─────────────────────────────────────────────────────────────────┘    │
│ ┌─ SUMMARY ───────────────────────────────────────────────────────┐    │
│ │ Cover is now less reliant on EQS (weight 1.0→0.6); a new         │    │
│ │ Suppress posture was added covering ally-advance situations.     │    │
│ └─────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
```

### 7.2 The utility-specific shortcut (tuning diff, no LLM)

Utility decisions have something BTrees don't: most edits are **pure tuning** — weights and curve
params change while structure (options, considerations, inputs) stays identical. That diff is
mechanical and needs no LLM. When the sanitizer detects **`StructureHash` equal, `ParamHash`
differing** between the two versions (the same hashes the hot-reload classifier uses, architecture
§11.3), the editor offers a **local tuning diff**:

```
┌─ Tuning diff (structure identical — no LLM needed) ────────────┐
│  TakeCover / EqsTopScore(Cover)   weight   1.00 → 0.60   ▼ -40% │
│  Flee      / HealthFraction       curve k  2.00 → 1.50   ▼      │
│  AdvanceAndAttack / AmmoFraction  weight   0.90 → 0.95   ▲      │
│                                                                │
│  [overlay both curves on the curve editor]  [accept] [revert]  │
└────────────────────────────────────────────────────────────────┘
```

- This is the common case for balance passes and is **instant, offline, and exact** — the LLM path is
  reserved for structural changes (added/removed options or considerations, changed inputs) where
  semantic interpretation actually helps.
- The decision of which path to offer is automatic: `StructureHash` match → tuning diff;
  `StructureHash` differ → full LLM comparison. Both can be run; the tuning diff is just the fast lane.
- **Curve overlay**: selecting a tuning row draws old and new curves on the same axes in the curve
  editor, so a designer sees the shape change, not just the number.

### 7.3 What the sanitizer strips / keeps

| Kept (semantic) | Stripped (presentation) |
|---|---|
| Kind, default mode, hysteresis | card positions, pane sizes, scroll state |
| option ids/names, order | selected-fixture, last-opened fixture |
| considerations: input, context, params | live-score caches |
| curve kind + m,k,b,c (+ piecewise points) | editor color theme, collapsed/expanded state |
| AssetId, StructureHash, ParamHash | EditorMetadata block |

Determinism requirement (Visual Asset Comparison §10.3) holds: `sanitize(file) == sanitize(file)`
byte-identical, so a no-op comparison yields an empty diff.

---

## 8. Validation surfacing

Validation runs on every edit (debounced), like the Blueprint validator, with diagnostics shown
inline on the offending card/row and counted in the toolbar.

```
┌─ Flee ─── ⚠ 1 warning ──────────────────────────────────────┐
│ ● HealthFraction   Self   InverseQuadratic  w1.0            │
│ ● EqsTopScore(Ret) —      Linear            w0.8            │
│   ⚠ UT0210  Flee has an EQS gate but no fallback option     │
│            uses SUM mode — if all gates fail nothing wins.   │
└──────────────────────────────────────────────────────────────┘
```

Utility-specific diagnostics (proposed `UT` series, finalized in the source-gen follow-on):

- `UT0101` consideration references an unknown `[UtilityInput]` name.
- `UT0102` input requires a context but none set.
- `UT0103` parameterized input missing its param (e.g. `EqsTopScore` with no sensor).
- `UT0110` `Build` reads disallowed runtime state (analyzer; mirrors EQS purity rule).
- `UT0210` all options are product-mode with gates and no sum-mode fallback exists → possible
  "nothing wins" (warning; the StarterPack `Hold` option is the canonical fix).
- `UT0211` weight outside [0,1].
- `UT0220` `PostureSelect` decision has zero options.

---

## 9. Round-trip guarantee

Every control in this doc emits a deterministic fragment of the fluent builder, so editor → C# →
editor is lossless:

| Control | Emits |
|---|---|
| option card | `.Option(Posture.X, Mode.Y, o => o …)` / `.CandidateOption(...)` |
| consideration row | `.Consider(In.Name(Ctx.Z, params), w: N, Curve.K)` |
| curve params | the `m,k,b,c` arguments / piecewise array |
| hysteresis field | `HysteresisBonus = N` on the attribute |
| order | source order of `.Consider` / `.Option` calls |

Nothing in the editor requires data the C# can't carry. That is the structural reason B1 (fixed
catalog) was chosen over property-paths in the architecture doc — and the reason day-one visual
editing is achievable rather than a future aspiration.

---

## 10. Open questions

- **W-1.** Should the editor allow authoring **new test fixtures** that write back as CI integration
  fixtures (§5), or keep fixtures read-only in Slice 1? Leaning: editable, because the
  author→regression loop is high-value and cheap given fixtures are already shared with CI.
- **W-2.** Tuning-diff (§7.2) granularity — show every changed number, or threshold-filter tiny
  deltas (e.g. <2%)? Leaning: show all, with a "hide <2%" toggle.
- **W-3.** Do candidate-scorer fixtures (§6) need to let the author hand-place arbitrary contact
  counts, or is a fixed small set (3–4 contacts) enough for authoring? Leaning: fixed small set in
  Slice 1; arbitrary in the follow-on.
- **W-4.** Curve overlay (§7.2) for `PiecewiseLinear` with differing control-point counts — how to
  render old vs. new cleanly. Deferred to the editor follow-on doc.
