# Architect Question #33 — blueprint as a brain tier, and suspendable sub-behaviours

> **Coordinator, `2026-08-16`.** ⭐ **Relay to the architect.** Claude cannot reach it.
> **Blocks:** nothing currently dispatched — this is ahead of the queue, raised because the capability
> is user-requested and touches the behaviour lifecycle.
> ⛔ **`N = 33` taken across ALL active branches** (rule 3a); highest existing is `32`.
> 📄 Context: [`EXPLAINER_Where_Parameters_And_State_Live.md`](EXPLAINER_Where_Parameters_And_State_Live.md)

---

## 0. ⭐⭐⭐ Three USER RULINGS — settled, not open

| | ruling, `2026-08-16` |
|---|---|
| ⭐ **blueprint IS a brain tier** | *"blueprint should be brain tier **exactly to inherit behavior lifecycle**"* |
| ⭐⭐ **latent ≠ ended** | *"calling a delay node (latent) does not mean the behaviour has ended so **no brain death because of latent call**. The blueprint needs to **exit itself or be cancelled from outside** to enter brain death"* |
| ⭐⭐⭐ **tiers are NOT mutually exclusive** | *"there are behaviors combining **strategical HSM on top with tactical BTree or blueprint under it** (running as part of an HSM state)"* |

⇒ ⛔ **Do not re-litigate these.** The questions below are what they leave open.

---

## 1. The situation, measured on `HEAD` (`2026-08-16`)

### 1.1 ⭐ `BrainTier` is the ROOT interpreter — composition is a different axis

```csharp
public struct BehaviorState {
    public int  ActiveBehaviorHash;
    public uint InstanceId;   // preemption token, monotonic, wrapping
    public byte BrainTier;    // which interpreter INGRESS starts.  Hsm = 1, BTree = 2
}
```

Composition is **subtree hosting**, already in the authoring model:

```csharp
// When non-empty, this state acts as a "Subtree host" that runs an external
// behavior asset (BTree or nested HSM) identified by this GUID.
public Guid SubtreeAssetId;
```

⇒ *"blueprint as a tier"* and *"blueprint under an HSM state"* are **two mechanisms**, both needed.

### 1.2 🔴 Latent calls REQUIRE Instance dispatch — the composition path has no cursor

`FieldLayout.StateStructBase` is **8** for `AiPrimitive` and **16** for `Instance`, and the 16 is
*because* an Instance's state opens with the cursor:

```csharp
public BlueprintLatentCursor Cursor;   // ResumeAt(4) + WaitUntilTime(4) + InstanceVersion(4) + pad
```

⇒ ⭐⭐ **A blueprint hosted as a BTree/HSM action node CANNOT suspend.** It is a leaf that runs to
completion each tick. So *"a blueprint with delay nodes running as part of an HSM state"* is **not**
the existing AiPrimitive path with more wiring — it needs the **Instance** path hosted as a
sub-behaviour, **which does not exist in any host.**

### 1.3 ⚠ HSM's authoring model is ahead of its runtime — in four places

| | measured |
|---|---|
| `SubtreeAssetId` | read **only** by `HsmValidator`. FastHSM kernel: **0** mentions. HSM emitters: **0**. Shipped `.hsm.json` using it: **0** |
| `Role` / `Scope` | persisted on `HsmBlackboardVariableDto`; **0** references in either HSM emitter (`BTreeBridgeEmitCore` has **45**) |
| validator rules **8**/**8b** | correct errors for concurrent-region collisions; injected resolvers default to `_ => false`, and **both production call sites use the default ctor** ⇒ never fire |
| parallel regions | the kernel genuinely runs several leaves per tick, but the action slot key is `hash(method@compileTimeOffset)` with **no region in the path** ⇒ two concurrent regions running one action write the same bytes |

⇒ ⭐ **BTree and blueprints both provision per-scope / per-slot storage; HSM alone does not.**

### 1.4 The lifecycle machinery a tier inherits

`InstanceId` is monotonic and wrapping, bumped on assign/clear, and drives `ChannelArbitrationSystem`
to preempt a superseded behaviour's in-flight commands. Ingress **parses before it commits**, so a
failed parse leaves the entity wholly on its old behaviour. `BlueprintSlotEntry.InstanceVersion` is
the blueprint twin — bumped on hard reload, compared against the cursor to invalidate stale resumes.

---

## 2. The sub-questions

### Q33-A — ⭐⭐ What hosts a suspendable sub-behaviour, and who owns its cursor?

| | option | reuse vs build |
|---|---|---|
| **A1** | **the parent's slot** — the child's cursor is a field in the parent's state struct | ✅ **no allocator work**; parent exit is a plain field reset ⛔ the child's layout becomes part of the parent's `StructureHash` ⇒ **editing the child re-versions and zeroes the parent** |
| **A2** | ⭐ **the child gets its OWN partition slot**, keyed by `(parentAsset, hostSite)`; the parent holds a handle | ✅ ⭐⭐ **reuses the shipped allocator verbatim** — `TryAttach` already does free-list + bump + per-slot `StructureHash` + zero-on-attach ⛔ needs a key algorithm and a detach-on-exit rule |
| **A3** | **root-only first** — a suspendable blueprint may only be a ROOT behaviour; nesting uses non-latent AiPrimitive leaves | ✅ **satisfies the "behaviour script" requirement with the least new machinery** ⛔ **defers ruling 3's composition case**, which is the half the user named explicitly |

⚖️ **Coordinator's lean: A2, phased behind A3.** ⭐ A2 is the only option where the child's latent
state survives independent of the parent's layout, and it is **reuse, not build** — the partition
allocator is shipped, proven and already zeroes every slot at a single choke point. ⚠ **But A3 first
is honest sequencing**: the root case alone delivers *"a behaviour defined as a tickable blueprint
with latent calls"*, and it needs no nesting decision at all. ⛔ **A1 is a trap** — coupling the
child's layout into the parent's `StructureHash` means editing a sub-script wipes its parent's state.

### Q33-B — When the parent state exits while the child is mid-wait, what happens to the cursor?

| | option | reuse vs build |
|---|---|---|
| **B1** | ⭐ **cancel** — invalidate the cursor, zero the child's slot | ✅ matches ruling 2's *"cancelled from outside ⇒ brain death"* exactly ✅ **reuses `InstanceVersion` staleness**, which already invalidates cursors ⛔ a long wait restarts on re-entry |
| **B2** | **preserve** — re-entering the state resumes where it left off | ⭐ **HSM already has history semantics** (`HistorySlots[8]`) ⇒ a precedent exists ⛔ but "resume a 30-second wait started a minute ago" needs a rule for `WaitUntilTime` |
| **B3** | **authored per host site** — a checkbox on the hosting state | ✅ most expressive ⛔ **a third semantic to test**, and it must agree with HSM's own shallow/deep history |

⚖️ **Coordinator's lean: B1 as the default, B3 only if it can be made to MEAN the same thing as HSM
history.** ⭐ Ruling 2 defines brain death as exit-or-cancel, and a parent state exiting *is* an
external cancellation of its child. ⚠ **The open half is `WaitUntilTime`** — it is absolute sim time,
so any "preserve" answer must say whether a preserved wait keeps its original deadline or re-bases.

### Q33-C — Does a nested blueprint get its own preemption token, or share the root's `InstanceId`?

| | option | reuse vs build |
|---|---|---|
| **C1** | **share the root's `InstanceId`** — one token per entity | ✅ trivially consistent; any reassignment invalidates everything ⛔ **a parent state change cannot invalidate one child without invalidating all of them** |
| **C2** | ⭐⭐ **per-slot token — `BlueprintSlotEntry.InstanceVersion`** | ✅ ⭐⭐⭐ **the field EXISTS and already does exactly this job**: bumped on hard reload, compared against `BlueprintLatentCursor.InstanceVersion` to reject a stale resume ⇒ **pure reuse** ⛔ two token spaces to reason about |

⚖️ **Coordinator's lean: C2, with high confidence.** ⭐ This is the strongest reuse case in the
document — the mechanism is shipped, its semantics are already *"invalidate a suspended resume point"*,
and Q33-B's cancel is then **a version bump**, not new code.

### Q33-D — Does a parameterised script need more than one instance per entity?

Slot identity is `blueprintId` **alone**, and attach is idempotent on it (`AlreadyAttached`, a no-op).

| | option | reuse vs build |
|---|---|---|
| **D1** | **keep `blueprintId` identity** — one instance per script per entity, params per-entity | ✅ **zero change** ⛔ **two HSM states cannot host the same script with different params** — and that is ⚠ **the HSM parallel-region collision reappearing in a new place** |
| **D2** | ⭐ **widen to `(blueprintId, instanceKey)`** | ✅ removes the whole class ⛔ touches the slot table, `TryGetSlotOffset`'s hot-path scan, and every by-id lookup ⚠ **`InstanceVersion` is NOT free for this** — it is already the staleness token |

⚖️ **Coordinator's lean: D2 if nesting happens (A2), D1 if root-only (A3).** ⭐ The requirement only
bites when one entity runs the same script twice, which nesting is what makes possible.

### Q33-E — ⚠ Does this work include building HSM's missing runtime?

§1.3 measures four places where HSM's authoring model has no runtime behind it. **`SubtreeAssetId` is
one of them**, and *"blueprint under an HSM state"* cannot exist until HSM subtree hosting does.

| | option |
|---|---|
| **E1** | ⭐ **this work builds HSM subtree hosting**, because ruling 3 depends on it |
| **E2** | **HSM's gaps are deliberately phased** and belong to the HSM programme — this work does root-only (A3) and waits |
| **E3** | some of the four are **abandoned**, not deferred — say which |

⚖️ **Coordinator's lean: E2, then E1.** ⛔ **No lean on which of the four are abandoned — that is
exactly the question a grep cannot answer**, and this programme has twice deleted something a design
record said was wanted.

---

## 3. What would be most useful back

1. ⭐⭐ **Q33-E** — whether HSM's four gaps are phased or abandoned. **Everything about ruling 3
   depends on it**, and it is the one question no measurement can settle.
2. ⭐⭐ **Q33-A** — hosting and cursor ownership; A2-vs-A3 is really *"nesting now or later."*
3. **Q33-B** — cancel vs preserve, and ⚠ **what a preserved `WaitUntilTime` means.**
4. **Q33-C / Q33-D** — leans are strong; a nod or a correction is enough.
5. ⭐ **Any correction to §1.** ⚠ **§1.3 says HSM subtree hosting has no runtime. The user described
   HSM-over-BTree behaviours as existing** — if they run through a mechanism this sweep did not find,
   that changes A, D and E together.
