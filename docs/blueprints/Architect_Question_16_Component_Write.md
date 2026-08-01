# Architect question #16 — self ECS component WRITE (per-field unmanaged / whole-replace managed)

**Status: 🟡 DRAFT — for architect.** Reopens the "writes go through actions only" policy for the **self-write**
case. Counterpart to Q#15 (component READ). The user has approved the *shape* below; the open items are the
**writable-component policy** and the **tick-ordering/authority** guarantees only the architect can rule on.

## The need

A designer-facing node to **write an entity's own ECS component** — the write counterpart to Q#15's read.
Motivating gap: a behavior with a **custom, behavior-owned state component** currently has no blueprint way to
write it — every write requires a hand-authored C# action. Channel commands cover the *known* intent writes
(`MoveTo`→`LocomotionChannel`, `AimAndFire`→`WeaponChannel`); nothing covers "write my own component field."

**User decisions (fixed — shape approved):**
- **Self only.** Cross-entity write stays out (like `SetShared`); coordination is self-write + peer-read (Q#15).
- **Unmanaged → per-field write** (`GetComponentRW<T>(self).Field = value`; unwired fields preserved).
- **Managed → whole-component replace** via ECB (`SetManagedComponent(self, freshInstance)`); **per-field managed
  is forbidden** (snapshot aliasing — Q#15's follow-up rule).
- **Gated** to a curated **writable-component set** (not every component); validate-time rejects non-writable
  components, cross-entity Target, and per-field managed writes.

## Why a gate at all — the crux (authority, not self-vs-other)

Self-writes are not unsafe *per se*; the hazard is **which** component. Two classes:
- **Behavior-owned *intent*** (`LocomotionChannel`, `WeaponChannel`, a behavior's own state struct) — safe: the
  owning system reads it *later* in the frame. This is what channel commands already write.
- **System-owned *outputs*** (`Transform`, physics/velocity, `NavigationStatus.Result`) — a *system* is the sole
  writer; a blueprint stomping these mid-tick fights that system → nondeterminism.

So **reads may expose all components (Q#15/D1); writes must expose only the safe (intent) subset.** Defining that
subset + its ordering guarantee is the architect decision.

## What exists today (reuse map)

| Piece | Mechanism | Reuse |
|---|---|---|
| Unmanaged component write | `IrOp_GetComponent` (RW) → `GetComponentRW<T>(entity)`; `ChannelCommandLowering` writes `GetComponentRW<Channel>(self).Field = …` | the exact per-field write shape already emitted by channel commands |
| Per-field "unwired preserved" | `SetShared` multi-pin → `TrySetSharedField` writes only wired fields, leaves the rest | same model → write only wired component fields (no whole-struct clobber) |
| Managed replace | ECB `AddManagedComponent`/`SetManagedComponent` (store by reference, fresh instance) | the sanctioned managed write |
| Managed read API | `view.GetManagedComponentRO<T>` + baked `IsManaged` flag (Q#15/F2) | reuse `IsManaged` to pick unmanaged-RW vs managed-ECB emit |
| Discovery + stale-ref | Q#15 component/field reflector + `FunctionCall`-style `NodeState.Error` on a removed component | same reflector (filtered to the writable set) + same red-error handling |
| Aliasing rule | Q#15 follow-up: managed components shallow-copied by ref into snapshots → **never field-mutate** | forces managed = whole-replace |

**Most of the node is reuse** (per-field write shape, `IsManaged`, reflector, stale-ref, self pin). The genuinely
new decisions are the writable-set policy (Q16-A) and the ordering contract (Q16-B).

---

## Q16-A — Writable-component POLICY (the crux)

Which components may a blueprint write? Reads are all-components (safe); writes must be curated.

- **A1 — Opt-in marker** `[BlueprintWritable]` on components (mirrors `[BlackboardDtoStruct]` / `[BlueprintEvent]`
  discovery). Explicit, safe-by-default, self-documenting; a component author declares "behaviors may write this."
- **A2 — Editor allowlist/catalog** (central list of writable component types).
- **A3 — All components the entity *owns*** (any component present on self). *Maximum reach; highest risk — lets a
  designer stomp system outputs like `Transform`.*

**Claude's lean: A1** (marker) — curated, opt-in, matches existing discovery patterns, keeps system outputs
unwritable unless deliberately opened. **Ask the architect:** is a `[BlueprintWritable]`-style marker the right
curation, or is there an existing notion of "behavior-writable / intent" components to key off?

## Q16-B — Tick-ordering / authority guarantee

A write during `BlueprintTickSystem` (Simulation phase): for which components is a behavior write **safe** w.r.t.
the systems that read/write them? (Intent components are read *after* the behavior; system outputs are written
*by* a system — writing those races.)

- **B1 — The writable set (A) is exactly the "read-after-behavior intent" components; system outputs are excluded
  by policy**, so ordering is safe by construction.
- **B2 — Additional phase/barrier constraints** the architect specifies.

**Claude's lean: B1** — but this is an **engine-authority fact only the architect can confirm** (which components
are read-after-behavior vs system-written, and whether the Simulation-phase timing holds).

## Q16-C — Managed write mechanics (ECB whole-replace)

Managed write = replace the whole component with a fresh instance via the ECB (`SetManagedComponent(self, fresh)`).

- Confirm **ECB from a blueprint tick** is the sanctioned managed-write path, and it's **deferred** (played back at
  the ECB sync point) — any determinism concern with a behavior issuing a managed add/set?
- Confirm **per-field managed write is forbidden** (must whole-replace) — matches the aliasing rule.

**Claude's lean:** ECB whole-replace, deferred; per-field managed rejected at validate-time. **Confirm** the ECB
add/set-from-tick contract.

## Q16-D — Unmanaged write mechanics (direct RW vs ECB) + existence

- **Direct `GetComponentRW<T>(self)` field-write** (as channel commands do — immediate, in-place, bumps
  version/dirty) — or should even unmanaged writes go through the ECB for ordering?
- **Existence:** RW requires the component to already exist. Write-if-present only (error/`Found`-style signal if
  absent), or add-if-absent (ECB add)?

**Claude's lean: direct `GetComponentRW<T>(self)`** (precedented by channel commands; immediate, deterministic
in-phase), **write-if-present** (no implicit add). **Confirm** vs an ECB-mediated write.

---

## Robustness & multi-pin (design notes — reuse, not new asks)

- **Multi-pin, no ordering hazard:** per-field writes assign only **wired** fields on the `ref` (unwired preserved)
  — the `SetShared` model. No whole-struct clobber, order-independent (each field an independent in-place
  assignment). This avoids the #13 write-ordering concern.
- **Stale/removed component:** reuse Q#15 — reflector returns null → `NodeState.Error` red node + tooltip; build
  fails (Roslyn CS0246) so it can't run; baked data preserved, never silently dropped.
- **Discovery:** reflect the writable set at editor startup (per A1, `[BlueprintWritable]`-marked types), same
  pipeline as the read picker.

## Proposed build order (post-answers)

1. **Slice W1 (gated on A/B/D):** self **unmanaged** per-field write to a **writable-set** component
   (`GetComponentRW<T>(self).F = v`, wired-only), palette + picker (writable set) + drawer, `NodeState.Error`
   stale-ref, validate-time gates (non-writable, cross-entity, existence).
2. **Slice W2 (gated on A/C):** self **managed** whole-replace write via ECB (`SetManagedComponent(self, fresh)`),
   `IsManaged` flag; per-field managed rejected.

## Architect answers

*(record here once relayed)*
- Q16-A:
- Q16-B:
- Q16-C:
- Q16-D:
