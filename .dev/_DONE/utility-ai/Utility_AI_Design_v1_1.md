# Utility AI (Decision Scoring) — Design v1.2

Consolidated design from the brainstorming sessions between the project owner and Claude.
This is the **architecture document**; editor round-trip and source-generator details are
deferred to follow-on docs once the core shape here is ratified (mirroring how EQS started
as a single architecture doc and grew the editor/compiler docs afterward).

> **Changelog v1.1 → v1.2** (resolves the four design-review questions of 2026-05-28):
> - **PREREQ expanded:** the original single-field `MaxAmmo` cache becomes a six-item Phase-0
>   bundle — see [`PREREQ_Phase0_Bundle.md`](./PREREQ_Phase0_Bundle.md). New items: multi-mount
>   weapon entities (P0.2), `MaxTrackedTargets` raised to 16 (P0.3), `UnitRoster.Add`/`IndexOf`
>   helpers (P0.4), `Blackboard1024.Project<T>` helper (P0.5), `UtilityTestWorld` test helper (P0.6).
> - **EQS multi-sensor resolution (§6.6):** an agent that consumes multiple EQS templates uses
>   the engine's existing **child-entity-per-sensor** pattern (each child carries `EqsSensor` +
>   `EqsCognitiveBuffer` + `PartMetadata.ParentEntity = self`). `EqsTopScore("CoverQuery")` resolves
>   the child whose `EqsSensor.BlueprintId` matches the requested template hash, then reads that
>   child's buffer. Rewrites the "one buffer per agent" implication of v1.1.
> - **§8.1 invariant corrected:** the v1.1 assertion was tautological (`a <= b || b <= a`). The
>   real invariant is `MaxTrackedTargets <= TopN`; with the P0.3 raise, both are 16, threat
>   ranking is non-truncating, and squad assignment over a 16-member roster fits exactly.
> - **§6.4 weapon enumeration:** per-mount entities (P0.2) make Weapon Selection a real candidate
>   scorer over a real list. The starter pack's `WeaponSelectionDecision` ships in Slice 1.
> - **§10.1 projection:** uses the new `Blackboard1024.Project<T>(ref bb)` helper (P0.5);
>   the existing raw `Unsafe.As<Blackboard1024, T>` form remains valid.
> - **Reader bindings (§6) honest about the position component:** distance is derived from
>   `Fdp.Toolkit.Geographic.Components.Position` (the real component), not an invented
>   `WorldPosition`. LOS-proxy reads `TargetMemory.Modalities[i]` bitmask (`Visual` bit set).
> - **`WeaponState` real shape acknowledged:** `Ammo` + `CooldownSecondsRemaining` + `MuzzleVelocity`
>   today; `MaxAmmo` added by P0.1.

> **Changelog v1.0 → v1.1** (historical):
> - Q-2 resolved (§10.1): squad uses commander `Blackboard1024` + projected `ThreatMatrixAssignmentState`.
> - Q-3 resolved (§8.1): now superseded by v1.2's corrected invariant.
> - Prerequisite added (§6.7): `MaxAmmo` cache (now P0.1).
> - Input readers mapped to real components.

This document describes the Utility AI decision-scoring layer for the HROT/FDP engine. It is
a **Brain-resident** layer that scores competing options — *which target to eliminate, which
weapon to fire, whether to take cover / flee / advance* — and selects the best one. It sits
beside the three existing AI authoring systems (FastBTree, FastHSM, Blueprint) and is consumed
by them rather than replacing any of them.

---

## 1. Goals and scope

### 1.1 The gap this fills

The engine's three decision systems are all **structural / Boolean**:

- **BTree** picks the leftmost passing branch (priority order).
- **HSM** picks via hand-authored transitions and guards.
- **Blueprint** wraps both.

None of them score a *continuous* fit between an option and the current situation. Expressing
"retreat is the right move *only when* health is low *and* we're outnumbered *and* good cover
exists" requires nested selectors with threshold conditions that must be constantly retuned.
Utility AI removes that brittleness: each option is scored from weighted **considerations**,
each consideration is a raw input mapped through a **response curve** to a 0–1 utility, the
considerations are aggregated into a single score, and the highest-scoring option wins.

### 1.2 What it covers

- **Threat ranking** — score each known contact; rank "who to eliminate first."
- **Weapon / effector selection** — score each weapon against a chosen target; pick the most
  effective.
- **Combat posture selection** — score a fixed authored set of postures (cover / flee /
  advance-and-attack / suppress / hold / regroup …) and pick one.
- **Group fire coordination** — the squad's virtual-leader entity allocates targets across
  members using the same threat-scoring core, writing assignments into the shared blackboard;
  individual members may **veto** under self-preservation.

### 1.3 What it explicitly does **not** cover

- **Spatial candidate selection** (cover points, flanking positions, retreat points). That is
  **EQS**, and it stays Muscle-side. Utility consumes EQS results as inputs; it never
  re-implements position scoring. (See §2.2.)
- **Pathfinding, LOS, navmesh reachability** — Muscle-owned, reached only indirectly through
  EQS results already sitting in the Brain's `EqsCognitiveBuffer`.

### 1.4 Scale and agents

Target scale matches the rest of the Brain cognitive pipeline: utility runs in the Brain tick,
synchronously, with a per-consideration trace budget that scales to the same agent counts the
Brain already sustains (thousands of cognitively-active agents). Supported agent types are the
same as EQS: infantry (highest fidelity), ground vehicles, flying, naval.

---

## 2. Core mental model

### 2.1 Why Brain-local, not EQS-shaped

EQS earns its Brain→Muscle→Brain round-trip because its scoring tests need *physical* world
data — navmesh reachability, raycast LOS, cover geometry, spatial-hash neighbours — all
Muscle-owned. Utility's inputs are different:

| Decision | Dominant inputs | Where they live |
|---|---|---|
| Threat ranking | threat level, faction, last-seen, target weapon/health estimate, LOS flag | **Brain** (`TargetMemory`); LOS flag already in `EqsCognitiveBuffer` |
| Weapon selection | own ammo, weapon stats, cooldowns, range bands vs. target type | **Brain** entirely |
| Combat posture | own health, own ammo, enemy-strength estimate, "is good cover available?" | **Brain**, except the cover-availability score which arrives pre-computed from EQS |

The Muscle-flavoured inputs (is there cover, can I reach it, can I see the target) are already
on the Brain as flags and scores inside `EqsCognitiveBuffer`, produced by a perception/EQS query
that ran independently. Shipping Brain-only data (ammo, health, threat memory) *down* to Muscle
just to rank it and ship the answer back would add a frame of latency and wire traffic for no
benefit. **Utility is therefore a Brain-resident library, not a node subsystem, and not an EQS
solver.**

### 2.2 EQS is an input, not the engine

The relationship is one-directional and clean:

```
  EQS (Muscle)  ──result──▶  EqsCognitiveBuffer (Brain)  ──read as one consideration──▶  Utility (Brain)
```

"Take cover" becomes attractive because the EQS cover query already returned a Top-K with a high
best-score. Utility reads that score; it does not select the point. The point is EQS's job and
stays EQS's job.

### 2.3 What is reused from EQS (machinery, not solver)

- The **response-curve vocabulary**: `Linear | InverseLinear | Threshold | Bell | Step`
  (extended in §5.3).
- The **scoring-mode enum** shape: `WeightedProduct | WeightedSum`.
- The **consideration → curve → aggregate** pipeline structure.
- The **`[…Definition(AssetId)]`** stable-GUID + FNV-1a→`int` runtime-ID discipline.
- The **`[InlineArray]` Top-K buffer** pattern (and its defensive-copy trap, §8.2).
- The **hot-reload** classification (`StructureHash` / `ParamHash`, soft/hard).

None of the Muscle-side solver, DDS wire protocol, or async state machine is reused. Utility is
synchronous Brain code.

---

## 3. Architecture overview

Three pieces over one scoring core, all Brain-resident:

| Piece | Shape | Output |
|---|---|---|
| **Scoring core** | `consideration → curve → aggregate` | a single 0–1 score for one option |
| **Candidate scorer** | core run over a *dynamic* list (targets, weapons) | ranked Top-N |
| **UtilitySelector** | core run over a *fixed authored set* (postures) | one winning option |

Above the three sits the **group layer**: the leader entity runs a greedy assignment pass over
the (member × target) score matrix and writes per-member assignments into the shared blackboard.

```
┌─────────────────────────── Brain node ───────────────────────────┐
│                                                                   │
│  Leader entity (virtual)                                          │
│    └─ ThreatMatrixAssignmentSystem (greedy, focus-fire bias)      │
│         └─ writes per-member assignment → Blackboard1024          │
│            (ThreatMatrixAssignmentState via Unsafe.As)            │
│                                                                   │
│  Member entity                                                    │
│    ├─ ThreatRankingScorer    (candidate scorer)                   │
│    │     reads TargetMemory + EqsCognitiveBuffer flags            │
│    │     + "assigned target" consideration (from shared bb)       │
│    ├─ WeaponSelectionScorer   (candidate scorer)                  │
│    ├─ CombatPostureSelector   (UtilitySelector)                   │
│    │     reads scorer outputs + EQS cover score + self state      │
│    └─ surfaced to BTree / HSM / Blueprint as nodes (§7)           │
│                                                                   │
│  Scoring core (shared)  +  Decision trace ring buffer (§9)        │
└───────────────────────────────────────────────────────────────────┘
        ▲ reads (one consideration among many)
        │
  EqsCognitiveBuffer  ◀── EQS result events ◀── EQS solver (Muscle)
```

---

## 4. The scoring core

### 4.1 Mental model

An **option** carries a set of **considerations**. Each consideration is:

1. a named **input reader** (parameterized by context) producing a raw float,
2. a **normalizer** mapping the raw value into 0–1 (owned by the reader, §6.2),
3. a **response curve** mapping normalized input to a 0–1 utility,
4. a **weight**.

The option's score aggregates its considerations per the option's `ScoringMode`. Across options,
highest score wins (with an optional small tie-break, §4.5).

### 4.2 Canonical structs

```csharp
public readonly struct UtilityConsideration
{
    public readonly ushort InputId;        // FNV-1a of the [UtilityInput] reader name
    public readonly InputContext Context;  // Self | Target | Leader | Candidate
    public readonly ResponseCurve Curve;   // see §5
    public readonly float Weight;          // 0..1, used by WeightedSum; product mode treats as exponent (§5.4)
    public readonly InputParams Params;    // packed 16-byte struct, discriminated by InputId
}

public readonly struct UtilityOption
{
    public readonly ushort OptionId;       // stable id within the set (posture id, or 0 for dynamic candidates)
    public readonly ScoringMode Mode;      // WeightedProduct (default) | WeightedSum
    public readonly UtilityConsideration[] Considerations;
}

public readonly struct UtilityDecisionDef
{
    public readonly int BlueprintId;       // FNV-1a of AssetId GUID
    public readonly ulong StructureHash;   // shape of options + considerations
    public readonly ulong ParamHash;       // weights + curve params only
    public readonly string DebugName;      // diagnostics only; not hashed
    public readonly DecisionKind Kind;     // ThreatRanking | WeaponSelection | PostureSelect
    public readonly UtilityOption[] Options; // fixed set for PostureSelect; template for candidate kinds
}
```

For **candidate scorers** (`ThreatRanking`, `WeaponSelection`) the `Options` array holds a single
*template* option that is evaluated once per dynamic candidate, with `InputContext.Candidate`
bound to each contact/weapon in turn. For **`PostureSelect`** the `Options` array holds the fixed
authored postures.

### 4.3 Aggregation — product with compensation (default)

The default `ScoringMode` is `WeightedProduct`, chosen because combat decisions are gate-heavy:
"no ammo," "can't reach," "can't see" must drive an option's score to ≈ 0, which a sum cannot do.

Raw product of N sub-1.0 terms unfairly penalizes options with more considerations. We apply
Dave Mark's **compensation factor** so options with differing consideration counts compare fairly:

```
modificationFactor = 1 - (1 / n)          // n = number of considerations
makeUpValue        = (1 - rawProduct) * modificationFactor
finalScore         = rawProduct + (makeUpValue * rawProduct)
```

Each consideration's contribution is `curve(input)` raised to its weight (weight as exponent in
product mode — weight 0 → term is 1.0 / no effect; weight 1 → full effect), so weights remain
meaningful under multiplication.

### 4.4 Aggregation — weighted sum (escape hatch)

Per-option flag `ScoringMode.WeightedSum` computes `Σ(wᵢ · curve(inputᵢ)) / Σwᵢ`. Provided for
options where additive feel is genuinely wanted; it has **no hard gates**, so authors using it
take responsibility for any required gating via a `Threshold`/`Step` curve that returns 0.

### 4.5 Selection and tie-break

The scorer returns options sorted by `finalScore` descending. For `PostureSelect`, a small
configurable **hysteresis bonus** is added to the currently-active posture to prevent flip-flop
(a common Utility-AI failure mode), and an optional epsilon random tie-break breaks exact ties.
Candidate scorers return the full ranked Top-N (capped, §8.1) with no hysteresis.

---

## 5. Response curves

### 5.1 Reused from EQS

`Linear`, `InverseLinear`, `Threshold`, `Bell`, `Step` — same semantics as `EqsScoringCurve`.

### 5.2 Added for utility

Utility decisions benefit from a richer curve set than spatial scoring:

- `Logistic` (sigmoid) — soft threshold; "becomes urgent fairly suddenly around X."
- `Quadratic` / `InverseQuadratic` — accelerating / decelerating response.
- `PiecewiseLinear` — author-defined control points for hand-tuned shapes.

### 5.3 Curve struct

```csharp
public readonly struct ResponseCurve
{
    public readonly CurveKind Kind;
    public readonly float Slope;       // m
    public readonly float Exponent;    // k
    public readonly float XShift;      // b
    public readonly float YShift;      // c
    // PiecewiseLinear stores control points in a side table keyed by CurveId
}
```

All curves clamp output to [0, 1]. The parameterization (`m`, `k`, `b`, `c`) follows the
standard `output = m·(x − b)^k + c` family used by IAUS tooling, so the visual curve editor
(follow-on doc) maps directly onto four sliders plus the piecewise side-table.

### 5.4 Weight-as-exponent in product mode

In `WeightedProduct`, a consideration contributes `curve(input)^weight`. This keeps a low-weight
consideration from dominating while preserving the gate property: if `curve(input)` is ~0, any
positive exponent still yields ~0, so gates hold regardless of weight.

---

## 6. Inputs (B1 — fixed catalog, parameterized)

### 6.1 The `[UtilityInput]` reader

A consideration names a registered reader, discovered by source-gen exactly like
`[BTreeAction]` / `[HsmAction]` / `[EqsTemplate]`:

```csharp
[UtilityInput(Name = "AmmoFraction")]
public static float AmmoFraction(in UtilityInputCtx ctx)
{
    ref readonly var ws = ref ctx.ReadWeaponState(ctx.Self);     // WeaponState (real component)
    return ws.MaxAmmo > 0 ? Math.Clamp((float)ws.Ammo / ws.MaxAmmo, 0f, 1f) : 0f;
}

[UtilityInput(Name = "DistanceToContext")]
public static float DistanceToContext(in UtilityInputCtx ctx)
    => ctx.NormalizedDistance(ctx.Self, ctx.Context);      // normalized by reader-owned max range
```

`AmmoFraction` depends on `WeaponState.MaxAmmo`, which **does not exist in v236** and must be added
as a spawn-time cache — see §6.7 and the companion `PREREQ_WeaponState_MaxAmmo_Cache.md`. The
`MaxAmmo > 0` guard makes the reader safe (and fully gating) for any weapon spawned before that
change lands.

`UtilityInputCtx` carries the `EntityRepository`, the `Self` entity, the resolved `Context`
entity (`Self | Target | Leader | Candidate`), the active `EqsCognitiveBuffer`, and the
`InputParams` packed struct. Readers are unsafe function pointers at runtime (same mechanism as
HSM action thunks), so the tick path is pointer-call fast with no reflection.

**Real-component mapping (v236, v1.2-corrected).** The catalog readers bind to these actual components:

| Reader | Reads | Notes |
|---|---|---|
| `AmmoFraction` / `WeaponHasAmmo` / `WeaponReadiness` | `WeaponState` (`Ammo`, `MaxAmmo`, `CooldownSecondsRemaining`, `MuzzleVelocity`) | `MaxAmmo` is P0.1; readiness uses cooldown; `MuzzleVelocity` exists today but is not read by Utility |
| `HealthFraction` / `ContactHealthFraction` | `Fdp.Toolkit.Combat.Components.Health` (`Current`, `Max` floats) | `Current / Max`, clamped |
| `DistanceToContext` | `Fdp.Toolkit.Geographic.Components.Position` (`Vector3 Value`) on both endpoints; normalized by reader-owned max range | the *position* is the geographic one; `TargetMemory` stores **last-known** positions in `PositionsX/Y` for known contacts |
| `ContactThreatLevel` | `TargetMemory.ThreatScores[i]` (decay-tracked, insertion-sorted) | populated by `TargetMemory.AddOrUpdateTarget(...)`; index 0 = highest threat |
| `HasLineOfSight` | derived: `TargetMemory.Modalities[i] & (byte)SensorModality.Visual` | no first-class LOS field; the `Visual` modality bit being set is the proxy for "currently visible" |
| `IsAssignedTarget` | the commander's projected `ThreatMatrixAssignmentState` (looked up via `UnitSubordinate.Commander` → `Blackboard1024.Project<ThreatMatrixAssignmentState>`) | §10.1 |
| `EqsTopScore` / `EqsResultCount` | the **child sensor entity**'s `EqsCognitiveBuffer` (resolved by `EqsSensor.BlueprintId` match) | §6.6 |
| `EnemyStrengthRatio` | derived: sum of `TargetMemory.ThreatScores` (or acquired-target `Health`) vs. own strength | §6.4 derived reader |
| `WeaponEffectivenessVsTarget` / `WeaponRangeBandFit` | the candidate mount's `WeaponMountInfo.EffectiveRange` vs. target distance and armor | requires P0.2 multi-mount infra |

Utility never actuates a weapon: once a target is chosen, the BTree/HSM writes
`CombatConstants.ActionIdAimAndFire` into `WeaponChannel.ActiveAction` with packed `AimAndFireParams`.
Utility reads `WeaponState`; actuation writes `WeaponChannel`. The two stay separate.

### 6.2 Normalization is the reader's job

Every reader returns a value already in (or clamped to) **0–1**. This centralizes "what's the
sensible range for this input" in one place, keeps curves portable across decisions, and lets
the debug overlay show a meaningful normalized number. This is the decisive advantage over a
property-path system, where normalization would leak into every consideration.

### 6.3 Parameterized contexts avoid catalog sprawl

Following the EQS context lesson, readers take a context rather than baking the target into the
name: one `DistanceToContext` with `Context ∈ {Self, Target, Leader, Candidate}` instead of
`DistanceToTarget` / `DistanceToLeader` / `DistanceToCandidate`. Keeps the catalog small and the
dropdown legible.

### 6.4 Derived inputs are first-class

The interesting inputs — *threat level*, *weapon effectiveness*, *enemy-strength estimate* — are
computations over several fields, so they are registered C# readers, not field reads:

```csharp
[UtilityInput(Name = "ContactThreatLevel")]
public static float ContactThreatLevel(in UtilityInputCtx ctx) { /* weapon × proximity × LOS × faction */ }

[UtilityInput(Name = "WeaponEffectivenessVsTarget")]
public static float WeaponEffectivenessVsTarget(in UtilityInputCtx ctx) { /* range band × armor match × ammo */ }
```

This is exactly where a property-path system would force you back into C# anyway — so the fixed
catalog loses nothing here and keeps everything legible and fast.

### 6.5 Reserved seam: `Custom`

A single `Custom(propertyPath, min, max)` reader is **reserved** for one-off field reads via the
existing `SearchPredicateDto` / `PropertyPath` infrastructure, but is **not implemented in
Slice 1**. The named catalog stays the default; the seam exists so the flexibility of B2 is
available later without re-architecting.

### 6.6 EQS results as inputs (v1.2 — child-entity resolution)

The codebase places **one `EqsSensor` + one `EqsCognitiveBuffer` per entity**. An agent that runs
multiple EQS queries (e.g. one cover query, one retreat query) uses the engine's existing
**child-entity-per-sensor** pattern: each query is a child entity carrying its own
`EqsSensor` (with a distinct `BlueprintId`) and `EqsCognitiveBuffer`, linked back via
`PartMetadata.ParentEntity = self`. `EqsResultUpdateSystem` already dispatches result events to
the right child by matching `LocalChildIndex` against `PartMetadata.InstanceId`.

The Utility readers therefore resolve a sensor child entity at read time:

```csharp
[UtilityInput(Name = "EqsTopScore")]   // best Top-K score for a named template; 0 if none/stale
public static float EqsTopScore(in UtilityInputCtx ctx)
{
    // ctx.Params.BlueprintId is the FNV-1a-32 of the EQS template name (e.g. "CoverQuery").
    // The hash is computed at gen time and packed into InputParams (no per-tick string work).
    if (!ctx.TryFindEqsChild(ctx.Self, ctx.Params.BlueprintId, out var child)) return 0f;
    ref readonly var buf = ref ctx.ReadEqsCognitiveBuffer(child);
    if (!buf.IsReady || buf.Count == 0) return 0f;
    return buf.GetTop().Score;
}
```

`TryFindEqsChild(owner, blueprintId, out child)` walks the owner's `PartMetadata` children once
per agent per tick (≤ a few entities; squad-leader scopes excepted), caching the resolved child
handle in the per-entity `UtilityResultBuffer.SensorChildCache` (small fixed array, sized to the
number of authored sensors per agent — typically 2–4). Cache invalidation is one bit per slot
flipped when an `EqsSensor.Epoch` change is observed.

This is the entire mechanism by which "good cover is available" feeds posture selection — no
special-casing in the scoring core, just a sensor-resolution helper at the reader boundary.

> **Author UX note (Editor DD):** the input picker shows EQS templates by display name; the
> emitted code carries the `BlueprintId` hash, not the string. Renaming a template updates the
> hash (`AssetId` is the stable id, not the display name) and the analyzer surfaces the change
> via the cross-reference refactor path (`SubElementKind.UtilityInput`).

### 6.7 Prerequisites — the Phase-0 bundle

The Utility layer needs six small codebase changes outside its own assemblies before Phase-1 code
can compile against live state rather than invented helpers. All six are spec'd as a single Phase-0
batch in [`PREREQ_Phase0_Bundle.md`](./PREREQ_Phase0_Bundle.md):

- **P0.1** — `WeaponState.MaxAmmo` cached at spawn (the original v1.1 prereq; ammo readers).
- **P0.2** — Multi-mount weapon entities (each mount as a child entity with `WeaponState` +
  `WeaponMountInfo` + `PartMetadata`). Makes `WeaponSelectionDecision` a real candidate scorer.
- **P0.3** — `PerceptionConstants.MaxTrackedTargets` raised from 4 to 16 so threat ranking is
  non-truncating against the Utility Top-N cap.
- **P0.4** — `UnitRoster.Add` / `UnitRoster.IndexOf` zero-alloc helpers.
- **P0.5** — `Blackboard1024.Project<T>(ref bb)` helper wrapping `Unsafe.As<,>`.
- **P0.6** — `UtilityTestWorld` Brain-only test scaffolding (replaces the v1.1 starter pack's
  invented `TestRepository.CreateBrainOnly`).

P0.1 and P0.2 are the only ones that touch production data paths (spawn + TKB translator);
P0.3 affects two struct sizes but no behavior; P0.4, P0.5, P0.6 are pure-add helpers.

---

## 7. Integration with the three authoring systems

The scorer is exposed as a **selector primitive** the existing systems call into, not a fourth
peer subsystem.

### 7.1 BTree — `UtilitySelectorNode`

A smarter `Selector`: instead of returning the first child that succeeds, it scores each child's
attached consideration set and ticks the highest-scoring child. Re-scores on a configurable
cadence; integrates with `ObserverSelector` semantics so a higher-scoring option can abort a
running lower-scoring branch. Authored as a node referencing a `UtilityDecisionDef` of
`Kind = PostureSelect`.

### 7.2 HSM — utility transition arbiter

A transition whose guard is "this state has the highest utility among a candidate set." Lets an
HSM use explicit states for entry/exit semantics while delegating *which* state to enter to the
scorer. Surfaces as an `[HsmGuard]`-shaped arbiter bound to a `UtilityDecisionDef`.

### 7.3 Blueprint — `ScoreDecisionNode` / `ReadRankedResultNode`

Two nodes mirroring the EQS Blueprint nodes:

- `ScoreDecisionNode` — runs a named `UtilityDecisionDef`, outputs the winning `OptionId` (or
  the ranked Top-N handle for candidate kinds).
- `ReadRankedResultNode` — reads rank `i` of a candidate decision (entity/weapon + score),
  paralleling `ReadEqsResultNode`.

### 7.4 Candidate-scorer access from any system

Threat ranking and weapon selection produce ranked results into a Brain-side
`UtilityResultBuffer` component (Top-N, §8.1). Any of the three systems reads it synchronously,
exactly as they read `EqsCognitiveBuffer`.

---

## 8. Storage

### 8.1 `UtilityResultBuffer`

Top-N ranked results for candidate decisions, stored as a fixed-size inline array (Top-N capped
at **16**, mirroring `EqsTargetPool` / `EqsCognitiveBuffer`). Per-entry: candidate handle (entity
or weapon id) + final score + winning posture id where applicable.

**Cap invariant (v1.2 — corrected).** The Top-N cap is **16**, matched to two real engine
capacities:

- **Squad assignment** — `UnitRoster.Capacity == 16` (hardcoded). A Top-16 buffer evaluates an
  entire squad's (member × target) matrix with **zero truncation**, by construction.
- **Threat ranking** — `TargetMemory` capacity is `PerceptionConstants.MaxTrackedTargets`. P0.3
  raises that from 4 to 16, so the perception cap is now equal to the Utility Top-N. Threat
  ranking is **non-truncating** in the common case (perception sees fewer than 16) and exactly
  bounded in the saturating case (perception sees 16).

The invariant the scorer asserts at registration:

```csharp
// Threat ranking is non-truncating only if perception tracks at most as many contacts
// as the Utility scorer ranks. A future raise of MaxTrackedTargets must be matched by
// raising UtilityConstants.TopN (and resizing UtilityResultBuffer's inline array).
Debug.Assert(PerceptionConstants.MaxTrackedTargets <= UtilityConstants.TopN,
    $"Perception tracks {PerceptionConstants.MaxTrackedTargets} contacts but Utility " +
    $"ranks only {UtilityConstants.TopN}. Raise UtilityConstants.TopN or accept silent " +
    "truncation of the lowest-threat tail.");
```

(The v1.1 disjunctive form was tautological and is removed.)

### 8.2 The `[InlineArray]` mutation trap (carried over from EQS)

Indexing directly into a C# 12 `[InlineArray]` field through a `ref struct` emits a defensive
`ldobj` copy; writes are silently lost. All write paths must cast to `Span<T>` first:

```csharp
// WRONG — silent mutation loss:
buffer.Results[i] = entry;

// RIGHT:
Span<UtilityResultEntry> results = buffer.Results;
results[i] = entry;
```

Baked into the design now, not discovered later.

---

## 9. Debug — "why did it pick this?" (Slice 1)

This is a first-class deliverable, not deferred. Given the engine's debug-tooling emphasis it is
the killer feature of the layer.

### 9.1 Decision trace

Each scored decision optionally emits a trace into a per-entity ring buffer
(`UtilityTraceWorkingMemory1024`, sibling to `BTreeTraceWorkingMemory1024` /
`HsmTraceWorkingMemory1024`). Per option, per consideration it records: `InputId`, raw value,
normalized value, curve output, weight, and the option's running aggregate and final score, plus
the selected `OptionId` and the runner-up margin.

### 9.2 Designed-in, not retrofitted

The core writes trace entries inline during scoring when a per-entity `UtilityDebugFlags`
component is present (gated like `BehaviorDebugFlags`). Tracing off → near-zero cost (a flag
check). Retrofitting a trace after the fact would require re-running scoring out of context with
stale inputs, so the buffer is structured from the first line of code — the EQS `[InlineArray]`
lesson applied to diagnostics.

### 9.3 Surfacing

- **Inspector panel** (Brain-side, ImGui, reusing the predicate-infrastructure host pattern):
  a per-entity table — options as rows, considerations as columns, cells showing
  `raw → norm → curve`, with the running aggregate and the winner highlighted and the runner-up
  margin shown. Reads "AmmoFraction: 0.20 → 0.15" legibly *because* inputs are named and
  pre-normalized (§6.2).
- **GizmoMap overlay**: a world-space label over the agent showing the chosen option and its
  score, layer-masked like other AI overlays, transportable over the existing gizmo DDS topic
  so a remote operator station can see live decisions.

### 9.4 Group-decision trace

The leader's assignment pass (§10) writes its (member × target) matrix and the resulting greedy
assignment into the same ring-buffer family, so "why was member 3 told to shoot target B" is
inspectable alongside member 3's own veto reasoning.

---

## 10. Group fire coordination

### 10.1 The leader entity and shared blackboard (v236 — reuses existing infrastructure)

A squad already has a **commander entity** in the engine; the utility layer adds no new component
for it. The mapping:

- **Hierarchy** — the commander carries a `UnitRoster` component (fixed-capacity list of
  subordinate handles, `Capacity == 16`); each subordinate carries a `UnitSubordinate` component
  pointing back via `UnitSubordinate.Commander`.
- **Shared memory** — the commander carries the existing `Blackboard1024` component (a 1024-byte
  unmanaged block). The engine's convention is to project that block into a typed mutable struct
  via `Unsafe.As` (the same pattern `HillAttackMutableState` uses at
  [`HillAttackCommanderNodes.cs:48`](../../Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs#L48)).
  P0.5 adds a `Blackboard1024.Project<T>(ref bb)` thin wrapper so callers can write
  `ref var s = ref Blackboard1024.Project<ThreatMatrixAssignmentState>(ref bb)` instead of the raw
  `Unsafe.As` chain. The raw form remains valid; the helper is purely additive.
- **Assignment state** — the utility layer defines an unmanaged `ThreatMatrixAssignmentState`
  struct (per-member assigned-target handles + scores + focus-fire counters) and projects it onto
  the commander's `Blackboard1024`. The leader writes assignments into it; each member reads its
  own slot through `UnitSubordinate.Commander` and `UnitRoster.IndexOf` (P0.4).

The v1.0 `SharedSquadBlackboard` component is **removed** — it was an invention that duplicated
`Blackboard1024`. Fire coordination is still the leader running an **assignment pass**, not each
member independently maximizing (which would dogpile one target).

> Sizing note: `ThreatMatrixAssignmentState` must fit in 1024 bytes. At 16 members, that is 64
> bytes/member — ample for a target handle (8B), score (4B), and flags. If a future layout needs
> more, it shares the block with other projected states by offset, per existing `Blackboard1024`
> convention; confirm no other system already claims the same offset range on commander entities.

### 10.2 Greedy assignment with focus-fire bias

`ThreatMatrixAssignmentSystem` (Brain, runs on the leader) builds the (member × target) matrix
by running the **same threat-scoring core** for each (member, target) pair — iterating members via
`UnitRoster` and targets via the commander's perceived `TargetMemory` — then assigns greedily:

1. Compute all (member, target) scores into the matrix.
2. Apply a **focus-fire bias**: once a target has ≥ k assigned shooters, further assignments to
   it are damped (configurable), and once a target's predicted incoming exceeds its survivability
   estimate, it is treated as consumed so the squad doesn't over-commit.
3. Sort pairs by score, assign highest, mark shooter consumed (and target per the bias), repeat.
4. Write the resulting per-member assignments into `ThreatMatrixAssignmentState` on the
   commander's `Blackboard1024`.

Greedy is O(n·m log(n·m)); with `UnitRoster.Capacity == 16` the matrix is at most 16×16 and well
within budget, avoiding the weight of an optimal (Hungarian) solver. Optimality buys little when
targets and scores churn every cognitive tick anyway.

### 10.3 Authority model — leader proposes, member vetoes

The leader writes each member's assigned target into `ThreatMatrixAssignmentState`. Each member's
`ThreatRankingScorer` reads its slot via `UnitSubordinate.Commander` and carries a **high-weight
"AssignedTarget" consideration** that strongly biases the member toward the assignment. But because
scoring is multiplicative, the member's **self-preservation considerations** (own health near zero,
no escape, overwhelming local threat) can drive the "engage assigned target" option toward zero, so
the member **breaks off** — the veto. The leader proposes; the member's own utility can override.
No member is forced to suicide on an assignment.

### 10.4 Assignment as one input, not an order

Crucially the assignment is modeled as a *consideration*, not an imperative command. This keeps
the whole system in one consistent scoring paradigm and makes the veto fall out naturally from
the math rather than requiring a separate override protocol.

---

## 11. Authoring

### 11.1 C# is the source of truth

Like BTrees: utility decisions are authored as C# definitions carrying a stable GUID.

```csharp
[UtilityDecision(
    AssetId = "b71c-44a2-9e08-...",
    DisplayName = "Combat posture",
    Kind = DecisionKind.PostureSelect,
    Category = "Tactical/Posture")]
public sealed class CombatPostureDecision : IUtilityDecisionDefinition
{
    public static void Build(IUtilityDecisionBuilder b) => b
        .Option(Posture.TakeCover, Mode.WeightedProduct, o => o
            .Consider(In.HealthFraction(Ctx.Self),        w: 0.8f, Curve.InverseLinear)
            .Consider(In.EqsTopScore("CoverQuery"),       w: 1.0f, Curve.Linear)
            .Consider(In.EnemyStrengthEstimate(),         w: 0.6f, Curve.Logistic))
        .Option(Posture.AdvanceAndAttack, Mode.WeightedProduct, o => o
            .Consider(In.HealthFraction(Ctx.Self),        w: 0.7f, Curve.Linear)
            .Consider(In.AmmoFraction(Ctx.Self),          w: 0.9f, Curve.Threshold)
            .Consider(In.EnemyStrengthEstimate(),         w: 0.7f, Curve.InverseLinear))
        .Option(Posture.Flee, Mode.WeightedProduct, o => o
            .Consider(In.HealthFraction(Ctx.Self),        w: 1.0f, Curve.InverseQuadratic)
            .Consider(In.EqsTopScore("RetreatQuery"),     w: 0.8f, Curve.Linear));
}
```

`Build` must be **pure and deterministic** — no reading `EntityRepository`, singletons, or
non-deterministic APIs — same rule as `[EqsTemplate].Build`, enforced by an analyzer diagnostic.
Runtime variation comes from the inputs at tick time, never from `Build`.

### 11.2 Visual editing from day one (round-trip)

A `UtilityFluentEmitter` converts the editor model → C# source, mirroring `BTreeFluentEmitter` /
`HsmFluentEmitter`. Because the authored vocabulary is closed and enumerable (catalog inputs,
enum contexts, enum curves with four numeric params), the round-trip is **lossless** without an
expression parser — the editor dropdown *is* the input catalog, and emission is just writing
names and numbers. This is the concrete reason §6 chose the fixed catalog (B1) over property
paths: it makes day-one lossless visual editing easy. The visual curve editor maps the four curve
params onto sliders plus a piecewise control-point widget. (Editor mechanics → follow-on doc.)

### 11.3 Hot reload

Inherited wholesale from the engine pattern via `AiHotReloadCoordinator`:

- **Soft reload** (`ParamHash` changed — weights / curve params only): live decisions pick up new
  numbers next tick; no state reset.
- **Hard reset** (`StructureHash` changed — options / considerations added/removed): result
  buffers for affected decisions are zeroed and re-evaluated fresh next tick.
- Build failures or missing input-reader names abort the reload; live ALC continues unchanged.

### 11.4 Starter pack

Hand-written C# definitions shipped as documentation-by-example and runtime test fixtures:

1. `ThreatRankingDecision` — rank contacts by `ContactThreatLevel × proximity × LOS × assigned-bias`.
2. `WeaponSelectionDecision` — rank weapons by `WeaponEffectivenessVsTarget × ammo-gate × range-band`.
3. `CombatPostureDecision` — the posture set above.
4. `LeaderAssignmentDecision` — the threat-scoring definition the leader runs per (member, target).

---

## 12. Source structure (proposed)

```
Fdp.Toolkits/Utility/
├── Core/
│   ├── UtilityConsideration.cs        // struct (§4.2)
│   ├── UtilityOption.cs
│   ├── UtilityDecisionDef.cs
│   ├── ResponseCurve.cs               // §5
│   ├── ScoringMode.cs                 // WeightedProduct | WeightedSum
│   ├── Aggregator.cs                  // product-with-compensation + sum (§4.3/4.4)
│   └── UtilityScorer.cs               // the core tick path
├── Inputs/
│   ├── UtilityInputAttribute.cs
│   ├── UtilityInputCtx.cs
│   ├── InputContext.cs                // Self | Target | Leader | Candidate
│   └── StandardInputs.cs              // catalog: Ammo/Health/Distance/Threat/WeaponEff/Eqs* …
├── Components/
│   ├── UtilityResultBuffer.cs         // [InlineArray(16)] Top-N (§8)
│   ├── UtilityDebugFlags.cs
│   └── ThreatMatrixAssignmentState.cs // unmanaged; projected onto commander Blackboard1024 (§10.1)
├── Diagnostics/
│   ├── UtilityTraceRecord.cs
│   ├── UtilityTraceWorkingMemory1024.cs
│   └── IUtilityTraceLogEmitter.cs
├── Group/
│   └── ThreatMatrixAssignmentSystem.cs   // leader greedy assignment over UnitRoster (§10)
├── Authoring/
│   ├── UtilityDecisionAttribute.cs
│   ├── IUtilityDecisionDefinition.cs
│   ├── IUtilityDecisionBuilder.cs
│   └── StarterPack/                       // §11.4
└── Integration/
    ├── UtilitySelectorNode.cs             // BTree (§7.1)
    ├── UtilityTransitionArbiter.cs        // HSM (§7.2)
    └── Blueprint/ ScoreDecisionNode.cs, ReadRankedResultNode.cs  // (§7.3)
```

(Source-gen registrar — `UtilityInputRegistrar.g.cs`, `UtilityDecisionCatalog.g.cs` — parallels
the FBT/HSM analyzers; details in the source-generator follow-on doc.)

---

## 13. Open questions for the architect review

- **Q-1. Hysteresis location.** §4.5 puts posture hysteresis in the selector. Should the bonus be
  authored per-decision (a field on `UtilityDecisionDef`) or a global tunable? Leaning
  per-decision with a global default.
- **Q-4. Re-score cadence.** Should the BTree `UtilitySelectorNode` re-score every tick or on a
  decimated cadence (e.g. with perception/EQS at ~10 Hz)? Leaning decimated + event-driven
  re-score on significant input change, to match the cognitive pipeline's rhythm.
- **Q-5. Weight-as-exponent ergonomics.** §5.4 makes weight an exponent in product mode but a
  linear coefficient in sum mode. Acceptable that the same authored number means different things
  per mode, or do we want two separate fields? Leaning keep one field, document clearly, show both
  interpretations in the debug overlay.

### Resolved by the v236 codebase review (v1.1, historical)

- **Q-2.** Leader entity reuse — commander `Blackboard1024` + projected `ThreatMatrixAssignmentState`. (§10.1)
- **Q-3.** Candidate cap vs. `TargetMemory` size — *superseded by v1.2*; see §8.1 corrected invariant.

### Resolved by the 2026-05-28 review (v1.2)

- **EQS multi-sensor pattern.** Child-entity-per-sensor; reader resolves by `EqsSensor.BlueprintId`. (§6.6)
- **Weapon Selection scope.** Per-mount entities (P0.2) keep Weapon Selection in Slice 1.
- **§8.1 invariant.** Tautological disjunction replaced with `MaxTrackedTargets <= TopN`; P0.3 raises perception cap to 16.
- **Phase-0 bundle.** Six prereq items in [`PREREQ_Phase0_Bundle.md`](./PREREQ_Phase0_Bundle.md).

---

*End of Utility AI architecture design v1.2. Follow-on docs: Utility Editor (visual curve editor,
round-trip), Utility Source-Generator (registrar emission, analyzer diagnostics), Phase-0 bundle.*
