# Architect question #20 — component COLLECTION element WRITE (fixed-capacity, self, unmanaged only)

**Status: ✅ APPROVED (architect, 2026-08-04) — A1+amendment · B1-as-composed (1-tick lag documented) · C1 ·
D + G1 accessor mediation; G1–G7 confirmed, FC-1b generator approved.** The write counterpart to Q#17/#18
(component collection READ, shipped) and the collection-shaped sibling of Q#16 (scalar component WRITE,
approved). Cleared to build FC-0 → FC-1 → FC-1b.
See `Blueprint_Fixed_Collections_Design.md` (umbrella) + `Fixed_Collections_RESUME.md`.

## The need

A designer-facing node family to **mutate the elements of a fixed-capacity collection field on the entity's own
ECS component** — `SetAt` / `Add` / `InsertAt` / `RemoveAt` / `Clear` / `Resize` over an `[InlineArray(N)] Items +
int Count` field. Today blueprints can **read** such a field (CA-07 consumer nodes: `ForEach` / `ItemGet` /
`ItemCount` / `Contains` / `Find`) but there is **no write op** — the emitter only writes *scalar* component fields
(`__wc.Field = value`, `StatementEmitter.cs:374`). Writing "my component's list" still requires a hand-authored C#
action. This is the verified omission FC-1 fills.

**Scope of THIS question — the *component* home only.** The blueprint-variable list home (Q#19, private to the
instance) and the action-DTO home (by-ref in C#) are **independent** slices with their own decisions; they do not
inherit these rulings. This doc is only about writing a collection field that lives on a *shared ECS component*.

## What is already settled (inherited, not re-asked)

These are approved and carry over verbatim — the collection case must stay inside them:

| Q#16 ruling (scalar write) | Applies to collection element write as |
|---|---|
| **Self only** — no cross-entity write; RW binds to `self` | element writes bind to `self`; the node exposes **no `Target`/`Entity` pin** |
| **`[BlueprintWritable]` opt-in** gate on the component type | a collection field is writable only if its component is `[BlueprintWritable]` (plus Q20-A below) |
| **Unmanaged → direct `GetComponentRW<T>(self)`**, write-if-present, in-place | same fetch; element mutation through the same `ref` |
| **Managed → whole-replace via ECB; per-field managed FORBIDDEN** (snapshot aliasing) | **managed (`ManagedMember`, e.g. `List<T>`) collections are NOT element-writable** — see Q20-C |
| **Tick order safe:** `BlueprintTickSystem` `[UpdateBefore]` the dispatchers, system outputs excluded by policy | unchanged — element writes are still in-phase, self, before readers |

**The read side introduced a hazard the write side must NOT inherit.** CA-07 reads may target **any** component on
**any** entity (a `Target` pin). Writes are self-only. So the collection-write node must **not** reuse the read
picker's cross-entity Target surface — it is a self-write node that happens to address a collection field.

## What exists today (reuse map)

| Piece | Mechanism | Reuse for collection write |
|---|---|---|
| Scalar self-write | `IrOp_SetComponent` → `ref var __wc = ref GetComponentRW<T>(self); __wc.F = v;` (`StatementEmitter.cs:372`) | same `GetComponentRW` fetch + HasComponent guard; the **assignment target** changes from a scalar field to a collection element |
| Collection READ accessors | `RenderCollectionAccessors` (CA-07d): `CuratedStatic` → `global::{Fqn}(comp[,i])`; `ManagedMember` → `IReadOnlyList<T>` local | the **write** accessor is new (`SetAt`/`Add`/…); `CollectionKind` already discriminates curated vs managed |
| Overflow-returns-false + diagnostic | designed for the blueprint-variable list (Q#19) | same contract: `Add`/`InsertAt` on a full list return `false` + `DebugProbe` diagnostic, never silent |
| `[InlineArray]` element access | `.NET 8` inline-array indexer / `AsSpan` | **mandate the `Span<T>` write pattern** (see Robustness) — the naïve `ref`+indexer path can silently lose writes |

**Most machinery is reuse** (self fetch, `[BlueprintWritable]` gate, `CollectionKind`, overflow contract). The
genuinely new decisions are the *element-write* gate granularity (Q20-A), the managed exclusion (Q20-C), and the
capacity/existence contract (Q20-D).

---

## Q20-A — Is `[BlueprintWritable]` on the component enough, or does a *collection field* need its own opt-in?

A scalar `[BlueprintWritable]` write assigns one field. An element write mutates **shared, sequence-valued** state
whose *contents/ordering* a system may own even when the struct is nominally "behavior-writable" (e.g. a roster a
system re-sorts, a ring buffer a system compacts). Two curation granularities:

- **A1 — Component-level gate only.** If the component is `[BlueprintWritable]`, all its fields — scalar *and*
  collection — are writable. Simplest; matches the scalar rule exactly; author opts the whole component in.
- **A2 — Field-level opt-in for collections** (`[BlueprintWritable]` on the component **plus** a per-field marker,
  or a `[BlueprintWritableCollection]` on the field). Lets an author expose scalar intent while keeping a
  system-managed sequence read-only on the same component.

**Claude's lean: A1** (component-level, matching Q#16) for v1 — a component author who marks a type writable is
already declaring "behaviors may mutate this"; collection fields are not special enough to warrant a second marker,
and A2 can be added later without breaking A1. **Ask the architect:** is there a component whose scalar fields are
safe-to-write but whose collection field is system-owned sequence state — i.e. a real case that demands A2 now?

## Q20-B — Tick-ordering for *sequence* mutation (confirm Q16-B still holds)

Q16-B established `BlueprintTickSystem` runs in Simulation, `[UpdateBefore]` the dispatchers, so writing
behavior-owned intent is race-free. Element writes are still in-phase, in-place, on self.

- **B1 — The Q16-B guarantee covers element writes unchanged** (a mutated `Items`/`Count` is read after the
  behavior, same as a mutated scalar).

**Claude's lean: B1.** **Confirm** there is no additional hazard specific to *structural* mutation of a shared
component's sequence mid-tick (e.g. another Simulation-phase system iterating the same `Items` between the write
and the dispatcher). If such a reader exists for a writable collection, that component simply stays out of the
writable set — same policy lever as scalar.

## Q20-C — Managed (`ManagedMember`) collections: read-only, confirming the per-field-managed forbiddance

CA-07d added `ManagedMember` collections — a `List<T>`/`IReadOnlyList<T>`/`T[]` on a **class** component, read via
an `IReadOnlyList<T>` local. Q#16 **forbids per-field managed writes** (a managed component is shallow-copied by
reference into snapshots → field-mutation corrupts recorded history / Flight Recorder / background threads).

- **C1 — Managed collections are READ-ONLY from blueprints.** Element writes are emitted only for **`CuratedStatic`
  (unmanaged `[InlineArray]`) collections**; a write node bound to a `ManagedMember` collection is rejected at
  validate-time (a new `BPxxxx`, the collection analog of Q#16's per-field-managed rejection). A managed collection
  is mutated only by whole-component ECB replace (Q#16 W2) — never element-wise.

**Claude's lean: C1** — it is the exact collection projection of the approved aliasing rule; anything else
reintroduces the snapshot-aliasing corruption Q#16 closed. **Confirm** no exception is wanted (e.g. a managed
component explicitly marked "not snapshotted").

## Q20-D — Write mechanics, capacity, and existence

Direct `GetComponentRW<T>(self)` (per Q16-D), then mutate the `[InlineArray]` element(s):

- **Op family:** `SetAt(i,v)` (indexed write over `[0,Count)`), `Add(v)`, `InsertAt(i,v)` (shift up), `RemoveAt(i)`
  (shift down), `Clear()`, `Resize(n)` (grow default-fills, shrink drops tail). Matches the blueprint-variable list
  ops so the designer's node-level UX is identical across homes (the standing UX requirement).
- **Overflow:** `Add`/`InsertAt` past capacity `N` → **return `false` + `DebugProbe` diagnostic, drop the write**;
  never silent, never throw. (Same contract as Q#19.)
- **Existence:** write-if-present (Q16-D). Component absent on self → graceful fail (`Found`-style / `NodeState`),
  **no implicit ECB add**.
- **`Count` coherence:** all ops keep `Count` in `[0,N]`; `SetAt` is valid only within `[0,Count)` (does not grow
  `Count` — use `Add`/`Resize` to extend), OOB index → false + diagnostic.

**Claude's lean:** direct RW + the full op family + false-on-overflow + write-if-present, exactly mirroring Q16-D
and Q#19 so all three homes share one vocabulary. **Confirm** the op set and the "`SetAt` does not grow `Count`"
rule (vs. a Godot-style auto-extend), and that `Resize` shrink need not re-zero dropped tail bytes for correctness
(it must for snapshot determinism — see Robustness).

---

## Robustness & correctness (design notes — build mandates, not architect asks)

- **`[InlineArray]` silent-mutation-loss trap (MANDATORY).** `GetComponentRW`'s own doc warns that
  `ref var q = ref GetComponentRW<T>(...); q.Buf[0] = x;` can copy the inline buffer to a temp and **lose the
  write**. All element writes MUST go through the `Span<T>` pattern (`MemoryMarshal.CreateSpan` / the inline-array
  `AsSpan`) that writes through the `ref` to real component storage. **Gate: an InlineArray write round-trip test**
  (write element → re-read component → value present) is required before FC-1 lands, for every home.
  > **FC-0 empirical correction (2026-08-04):** the warned trap **does not reproduce** on the current toolchain
  > (.NET SDK 8.0.4xx) — a naive element write through a `ref` local (or ref-returning receiver) LANDS; the only
  > reproducible loss mode is the missing-`ref` **value copy**, which is not InlineArray-specific.
  > `EntityRepository.GetComponentRW`'s ldobj claim appears stale. The convention above **stays mandated**
  > (Q#5-C off-graph rule, readonly-read defensive copies, value-copy hazard confined inside `ref`-receiver
  > accessors, generator uniformity), and two FC-0 tests pin the measured compiler behavior in both directions
  > (`FixedCollectionOpsTests.NaiveRefLocalWrite_CurrentToolchain_Lands` / `ValueCopyWrite_IsLost`).
- **Determinism on shrink/remove.** `RemoveAt`/`Resize`-shrink/`Clear` must **re-zero** the vacated tail slots
  (not just lower `Count`) so byte-for-byte memcpy snapshots/record-playback stay identical regardless of prior
  contents. (`Count`-only shrink leaves stale bytes → nondeterministic hashes.)
- **Self-only, no Target pin.** The node binds `GetComponentRW<T>(self)`; it exposes **no** cross-entity Target pin,
  unlike the CA-07 read picker. Validate-time rejects any wired entity source (the collection-write analog of
  Q#16's cross-entity rejection).
- **Multi-op ordering.** Distinct write nodes on the same component each fetch their own `ref` inside a HasComponent
  guard (the `SetShared`/Q#16 model); a chain of `Add`s is exec-ordered by wires, each in-place — no whole-struct
  clobber.
- **Stale/removed field:** reuse the CA-07 reflector → null → `NodeState.Error` red node + build failure; baked
  data preserved.

## Proposed build order (gated on this question)

1. **FC-0 foundation** — canonical write-accessor convention (`Span<T>` pattern) + the mutation-op **IR family** +
   `CollectionKind` write-backing + the InlineArray write round-trip test + the real `DebugProbe` overflow hook.
   Extend `BpCollectionDemoOps` (the CA-07 demo accessor) with the mutators as the reference implementation.
2. **FC-1** — component-collection write nodes: palette + self-bound picker (writable set, **no Target**) + drawer,
   `CuratedStatic`-only (managed rejected, Q20-C), validate-time gates (non-writable, cross-entity, managed,
   existence, OOB), `NodeState.Error` stale-ref.
   Gate: clean build → **Generators 184/184 byte-identical** → new lowering + round-trip tests green.

Blueprint-variable writes (Q#19) and action-DTO recognition are **independent** slices — not chained off FC-1.

## Architect-role review pass (2026-08-04) — verdicts + gaps found

*(Claude played the architect against the code — every claim re-verified at the cited line. The NotebookLM
pass can still override; record its deltas in "Architect answers" below as usual.)*

### Verdicts on the asks

| Ask | Verdict |
|---|---|
| **Q20-A** | **A1 approved, with a free A2 amendment.** Gate = component-level `[BlueprintWritable]` **AND** curated *write accessors existing* for that (component, field). Under Q#5-C accessor mediation (G1) the graph can only mutate through curated statics — an author who ships read accessors only has made that collection read-only **per field** with zero new vocabulary. The "system-re-sorted roster" case is served: don't write its mutators. No `[BlueprintWritableCollection]` attribute. |
| **Q20-B** | **B1 approved as policy — but the load-bearing engine fact is currently FALSE in the compositions blueprints actually run in** (G2). Plus a collection-specific sibling hazard Q#16 never had: same-graph mutation during iteration (G3). |
| **Q20-C** | **C1 approved, no exception.** No "not-snapshotted" managed marker exists; inventing one now is speculative vocabulary. ManagedMember collections stay read-only: new `BPxxxx` + write-picker exclusion. |
| **Q20-D** | **Approved:** the 6-op family (= Q19-C), `SetAt` does not grow `Count` (= Q19-F F1), false-on-overflow, write-if-present. **Corrected by:** emit shape (G1), zeroing policy (G6), out-pin contract (G5). |

### Gaps / flaws found (each with the fix)

**G1 (blocker) — the proposed emit contradicts Q#5-C: raw element mutation must stay OFF-graph.**
Q20-D says "mutate the `[InlineArray]` element(s)" after `GetComponentRW`, and Robustness mandates the `Span<T>`
pattern "for all element writes" — but for `CuratedStatic` collections the standing ruling (Q#5-C, reaffirmed by
Q#17's reality check) keeps raw fixed/inline-array access out of generated graph code; reads already lower to
curated accessor calls. **Fix:** writes lower to curated **write-accessor statics** — `Ops.Add(ref __wc, v)` on
the guarded `ref` local, mirroring the read emit — and the `Span<T>` mandate relocates *inside* the hand-written
accessors (compiler can't see it → enforced by the accessor recipe + the per-accessor round-trip test gate,
optionally a Roslyn analyzer later). **The doc's actual missing design surface is the write-accessor
convention.** Proposed: `[BlueprintCollectionWrite(typeof(C), "Field", op)]` on statics with pinned shapes —
`bool Add(ref C c, Elem v)` · `bool SetAt(ref C c, int i, Elem v)` · `bool InsertAt(ref C c, int i, Elem v)` ·
`bool RemoveAt(ref C c, int i)` · `void Clear(ref C c)` · `bool Resize(ref C c, int n)` — discovered by the
`ComponentFieldReflector.TryReflectCollections` pipeline; a partial set is legal (the palette offers what
exists → per-op curation for free, reinforcing the Q20-A amendment).

**G2 (blocker, engine fact) — the Q16-B ordering guarantee is not honored where blueprints run today.**
`BlueprintTickSystem` declares `[UpdateBefore]` the three dispatchers (`BlueprintTickSystem.cs:17-19`), but
`EditorSubsystem.cs:889` **appends** `bpTick` after ALL sim systems — and `CgfLogicPack.cs:160` puts the
`ActionDispatchModule` dispatchers inside that very list. Module-group order is array position
(`ChannelArbitrationSystem.cs:10-12`), and `EditorHarness.cs:223` says outright that the `[UpdateBefore]`
ordering "is not re-applied inside the group". So the *actual* contract in the editor compositions is
**write-visible-next-tick** (deterministic one-tick lag), not "dispatcher reads it the same tick". Not a
corruption risk, but Q#16/Q#20 sell writes on a guarantee the composition doesn't deliver. **Fix (FC-1 gate):**
splice `bpTick` before the dispatch systems in both compositions **or** add a composition-order assertion/test;
until then, document one-tick-lag as the real contract.

**G3 (major, new hazard class) — mutation during iteration is undefined AND wire-dependent.**
Verified in `StatementEmitter.cs:461-474`: `IrOp_ForEach`'s loop bound is **hoisted once** iff the `Count`
out-pin is wired, else **re-evaluated each pass**; the roster is a `ref readonly` view of live chunk memory. A
`RemoveAt` inside a `ForEach` body over the same collection therefore behaves differently by wiring:
unwired-Count → live-shrinking bound (silently skips successors); wired-Count → stale bound → `ItemGet` reads
vacated slots. Scalar writes (Q#16) never invalidated an iteration — this class is new. **Fix:** validator
diagnostic (start as a warning) when a collection-write node targeting the same baked
(`ComponentTypeFqn`, field) sits inside a `ForEach` body iterating that collection; document "a collection is
read-only while being iterated" as the designer rule. Statically checkable because the binding is author-time.

**G4 (decision the doc skips) — binding shape: collection in-pin vs picker-bound.**
Q17-A committed the Unreal collection-pin UX, and Unreal's array *writes* also take the array pin; Q20 silently
makes writes picker-bound ("no Target pin") — right instinct on safety, but an unstated UX asymmetry.
**Ruling: prefer the collection in-pin for writes too**, with (a) validate-time self-only enforcement on the
*producer* (the wired `GetComponent` must have `Target` unwired — author-time visible, closes R2a structurally,
generalizes BP2062 to consumers) and (b) defense-in-depth: emit always binds `self`, never a wire-derived
entity. Fallback if the producer check proves awkward in FC-1: picker-bound is acceptable, but then brand the
nodes as the `SetComponent` statement family, not collection-pin consumers.

**G5 (minor contract holes in Q20-D).**
- Out-pins unspecified → **one `Ok` bool** (component present AND op applied); the `DebugProbe` diagnostic
  distinguishes cause (absent / full / OOB). Mirrors scalar `Written` + Q19's bool.
- A failed op (e.g. full list) has already fetched `GetComponentRW` → chunk-version bump on a no-op. Accept for
  v1 (matches scalar; avoiding it costs a pre-check RO fetch) — document it.
- CuratedStatic capacity `N` is invisible to the editor (only the accessor knows it) → optional capacity
  const/accessor in the convention for budget/diagnostic UX. Nice-to-have, not gating.

**G6 (cross-home inconsistency) — two different zeroing policies were approved.**
Q19 settled **zero-on-GROW** ("only a grow-after-shrink must re-zero"; `ListResize` zero-fills the grown
range); Q20's Robustness mandates **re-zero-on-SHRINK**. Both give "tail reads default", but they produce
different byte images for identical op sequences across homes and double work if merged blindly. The
determinism justification is also overstated: a deterministic sim writes deterministic stale bytes, and no
production byte-level world hash exists (only test-side `ComputeStateHash`); the real byte-image consumers are
the debugger's whole-repo keyframes (`SubTickSnapshotRecorder`) and any future rebaseline/JIP byte compare.
**Ruling: ONE invariant for ALL homes, pinned in FC-0 — "slots ≥ `Count` are always `default(T)`".** Mutators
zero vacated slots (`RemoveAt` tail slot, `Clear`, `Resize`-shrink); grow then needs **no** fill (the blob is
already zero; Q19's grow-fill demotes to a debug assert). Stronger property: `Contains`/`Find` can never match
stale-tail garbage even under a `Count` bug, and the byte image is canonical.

**G7 (test-shape flaw) — the FC-0 reference implementation cannot exercise R3.**
`BpCollectionDemo.Values` is an `unsafe fixed int[4]` buffer (`BpCollectionDemo.cs:28`) — the `ldobj`
defensive-copy trap is an `[InlineArray]` phenomenon and does not exist for `fixed` buffers. "Extend
`BpCollectionDemoOps` with mutators + round-trip test" would pass even with the naïve unsafe pattern and prove
nothing about R3. **Fix:** FC-0 must add an `[InlineArray]`-backed demo component (or convert the demo) so the
round-trip gate actually bites; keep one `fixed`-buffer accessor set too — both idioms exist in the wild
(`UnitRoster`).

### Scope steers (RESUME step 2 / review R5)

- **`GetShared`/`SetShared` list slots + list-typed `asset.Parameters`: OUT for v1**, with explicit
  validate-time rejection + a diagnostic naming the supported homes (not a silent gap). Add to the Q#19-E scope
  table.
- **Enforcement-point note (Q20-A):** as with Q#16/CA-04, the `[BlueprintWritable]`+accessor gate is
  **editor-primary** (`BlueprintWritableAttribute.cs:20-28` — netstandard2.0 Stage2 cannot reflect
  attributes); Stage2 checks stay structural. Hand-edited JSON bypasses the attribute gate but not the
  structural ones — accepted precedent, restate it in FC-1's tracker.

## G1 resolution (decided 2026-08-04) — write-accessor convention + generated ops class

The missing design surface from G1, now pinned. Two layers: a **convention** (what an ops class looks like,
hand-written or generated) and a **generator** that mechanizes it.

### The convention

New attribute (Fdp.Core, sibling of `[BlueprintCollection]`/`[BlueprintCollectionItem]`):

```csharp
public enum BlueprintCollectionOp { Add, SetAt, InsertAt, RemoveAt, Clear, Resize }

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BlueprintCollectionWriteAttribute(
    Type component, string collection, BlueprintCollectionOp op) : Attribute { }
```

Pinned signatures (discovered by the `ComponentFieldReflector.TryReflectCollections` pipeline; the write node
bakes one `WriteAccessorFqn` per op, exactly like `CountAccessorFqn`/`ItemAccessorFqn`):

| Op | Signature | Contract |
|---|---|---|
| `Add` | `bool Add(ref C c, Elem v)` | false on full |
| `SetAt` | `bool SetAt(ref C c, int i, Elem v)` | `[0,Count)` only — never grows `Count` |
| `InsertAt` | `bool InsertAt(ref C c, int i, Elem v)` | shift up; false on full / `i > Count` |
| `RemoveAt` | `bool RemoveAt(ref C c, int i)` | shift down; **zero the vacated slot** (G6) |
| `Clear` | `void Clear(ref C c)` | **zero `[0,Count)`** (G6), `Count = 0` |
| `Resize` | `bool Resize(ref C c, int n)` | `n ≤ N`; **shrink zeroes dropped tail** (G6); grow needs no fill |

Rules: `ref C` receiver (mutates real chunk storage through the `GetComponentRW` ref); every element access
inside goes through the **`Span<T>` cast** (`((Span<Elem>)c.Buf)[i] = v` — the R3 mandate lives HERE, verified
by the FC-0 round-trip test); mutators own `Count` and the G6 tail-always-default invariant; the graph never
touches `Count` or the buffer directly. **A partial set is legal** — the palette offers exactly the ops that
exist (per-op, per-field curation with no extra vocabulary; the Q20-A amendment). Capacity stays a public
`const int Capacity` on the component — the editor reads it reflectively for budget/diagnostic UX.

Emit shape (mirrors the Q#16 guarded write; raw buffer access stays off-graph, per Q#5-C):

```csharp
var __t8 = false;
var __t7 = wv.HasComponent<global::…PatrolPlan>(__self);
if (__t7)
{
    ref var __wc7 = ref wv.GetComponentRW<global::…PatrolPlan>(__self);
    __t8 = global::…PatrolPlanCollectionOps.SetAt(ref __wc7, __t3, __t5);   // __t8 = "Ok" out-pin
}
```

### The generator (FC-1b) — dev writes ONE attribute, not ~60 lines of trap-prone code

A Roslyn **source generator** emits the ops class (`{Component}CollectionOps`, own file, deterministic name)
for every field marked:

```csharp
[BlueprintWritable]
public struct PatrolPlan
{
    public const int Capacity = 8;
    [InlineArray(Capacity)] public struct WaypointBuffer { private Entity _e0; }

    public int Count;

    [BlueprintCollectionField(nameof(Count))]        // ← the only thing the dev writes
    public WaypointBuffer Waypoints;
}
```

- **Explicitly NOT auto-triggered** on "any `[InlineArray]` in a `[BlueprintWritable]` DTO": (a) the logical
  Count field is a sibling and not inferable (capacity ≠ count, Q#17 reality check; ambiguous with two
  collections); (b) auto-emitting mutators would delete the per-field writability gate (the A2 hazard);
  (c) bespoke-semantics collections (dedup, `UnitRoster` compaction) must keep hand-written ops.
- **Knobs on the attribute:** `Access = ReadOnly` (read accessors only) · `Ops = Add | Clear` (partial write
  set). Default = read pair + full write set.
- **Generator diagnostics:** missing/non-`int` count field · non-blittable element · buffer on a managed
  (class) component (Q20-C) · a hand-written `[BlueprintCollection*]` accessor already exists for the same
  (component, field) → **hand-written wins, generator emits nothing** (the bespoke escape hatch is automatic).
- Generated code compiles into the game assembly → editor reflection discovery and the netstandard2.0 baked-FQN
  compiler path are **unchanged**; the emitter can't tell generated from hand-written. R3 enforcement gets
  stronger: the round-trip test validates the generator's template once, not every future hand-written class.
- **Relation to the locked "no source generator v1" decision** (umbrella doc): NOT a contradiction — that
  ruling covered generating the *collection type* (blocked: can't augment a non-`partial` struct; ~3 lines
  anyway). This generates the free-standing *accessor class* — a new type in a new file, the sweet spot for
  generators. The umbrella doc is amended accordingly.

### Build-order deltas (fold of G2/G3/G6/G7 + this resolution)

1. **FC-0** — hand-written reference ops on a **new `[InlineArray]`-backed demo component** (G7 —
   `BpCollectionDemo`'s `fixed` buffer cannot exercise the R3 trap; keep a `fixed`-buffer accessor set too,
   both idioms exist) + the InlineArray write round-trip test + the mutation-op IR family + the real
   `DebugProbe` overflow hook + the **tail-always-default invariant** (G6) pinned as the canonical zeroing
   rule for ALL homes.
2. **FC-1** — component-collection write nodes per the convention above; **collection in-pin binding with
   validate-time producer self-check** (G4: wired `GetComponent` must have `Target` unwired; emit binds `self`
   regardless); iteration-mutation validator warning (G3); **composition-order assertion/test** for
   `BlueprintTickSystem` vs the dispatchers (G2) — or the `bpTick` splice fixed.
3. **FC-1b** — the `[BlueprintCollectionField]` source generator (template = the FC-0 reference, proven by the
   same round-trip test).

## Architect answers (received)

*(NotebookLM architect pass, 2026-08-04, relayed by the user — the review-pass verdicts G1–G7 and the G1
resolution were reviewed and confirmed wholesale. Caveat noted by the user: the architect is itself an AI
reading the docs/code; its restatements were cross-checked against the code before recording. One
doc-inconsistency surfaced by the cross-check: `Blueprint_List_Variables_Design.md` §3 still carried the
pre-review `SizeReliable=true` — now annotated as superseded by its §F3.)*

- **Q20-A — A1 + amendment APPROVED.** Component-level `[BlueprintWritable]` gate; field-level curation (A2)
  falls out for free from accessor mediation — an author who provides no write accessors for a system-owned
  sequence has kept it read-only.
- **Q20-B / G2 — CONFIRMED as analyzed.** The composition reality is `BlueprintTickSystem` runs **after** the
  dispatchers (array order), so the actual contract today is **write-visible-next-tick** (1-tick lag).
  Document that as the contract, unless the `bpTick` splice is fixed as part of the FC-1 gate (choice left to
  FC-1).
  > **FC-1·G2 outcome (2026-08-04): the splice was FIXED** — `BlueprintRuntimeWiring.SpliceIntoSimulation`
  > inserts the tick before its `[UpdateBefore]` targets in both compositions; the same-tick Q#16-B contract
  > now holds by construction (pinned by `BlueprintTickSpliceTests`). The 1-tick-lag documentation path is moot.
- **Q20-C — C1 APPROVED.** `ManagedMember` collections strictly read-only from blueprints; write nodes bound
  to a managed collection rejected at validate-time (per-field managed mutation corrupts snapshots via
  reference aliasing — Q#16); managed mutation only via whole-component ECB replace.
- **Q20-D + G1 — APPROVED.** The 6-op family, write-if-present, false-on-overflow. **Strictly enforced:** raw
  element mutation and `[InlineArray]` Span casts stay OFF-graph — the emitter outputs curated write-accessor
  calls (`Ops.Add(ref __wc, v)`), and the `Span<T>` trap mitigation lives exclusively inside the accessors.
  **FC-1b `[BlueprintCollectionField]` source generator APPROVED** (freestanding static class in a new file —
  avoids the constraint that blocked generating the struct type).
- **G3 — MANDATED.** Validate-time diagnostic (starting as a warning) flagging a collection-write node
  targeting the same component+field inside a `ForEach` body over that collection.
- **G4 — MANDATED.** Writes use the **collection in-pin** (UX symmetry with reads); validate-time enforces the
  producer's `Target` is unwired (self-only).
- **G6 — PINNED for ALL homes.** Universal zeroing invariant: slots `>= Count` are always `default(T)`;
  `RemoveAt`/`Clear`/`Resize`-shrink zero vacated slots immediately; grow never fills.
