# Opening prompt — asset variables, action parameters, and what the HSM analogue should be

> **To:** the Blueprint / BTree authoring session (the one that owns
> `BTree_AiActionParameterBinding_Detailed_Design.md`, `Blackboard_Authoring_Detailed_Design.md` and
> the variable-unification work).
> **From:** the HSM design session, branch `claude/hsm-visual-editing-9ngei4`.
> **Purpose:** settle how an HSM binds **parameters** to its actions and guards, and how **asset
> variables** feed them and receive results back — by reusing the BTree model wherever it fits and
> naming precisely where HSM differs.
> **This is an opening prompt for a long discussion, not a change request.** Nothing is being built.
> **Read first if you are new to the HSM side:** [Hsm_Integration_Map.md](Hsm_Integration_Map.md) ·
> [Hsm_Issues_Tracker.md](Hsm_Issues_Tracker.md)

---

## 0. Why we are asking you

The HSM audit found that **binding an action or guard to an HSM does not work at all today** — three
separate id spaces, parameters read out of live instance memory, and a picker that offers nothing.
Before proposing fixes we want the BTree/blueprint model as the reference, because:

- BTree already solved the identical problem and the architect already ruled on the hard parts
  (per-field binding **rejected**; whole-DTO binding **approved**).
- Whatever HSM does should be *the same mechanism*, not a parallel invention — the two hosts already
  share `BrainBlackboard`, `Blackboard1024`, `IActionSchemaExporter`, `BehaviorRegistry` and the
  `[BlueprintRegistrar]` masquerade.
- We would rather inherit your constraints than rediscover them.

**We are not asking you to design the HSM side. We are asking: is our reading of your model right,
and where does it stop applying?**

---

## 1. What we believe your model is — please correct us

Verified from your docs + code on 2026-08-14. **If any line here is wrong, that correction alone is
worth the round trip.**

| # | Our understanding | Source |
|---|---|---|
| 1 | Every blackboard datum is a typed named **variable** with a **role** (`input`/param vs `state`) and, for state, a **scope** (`Node` \| `Behavior` \| `Entity`) | ParamBinding DD §4.4 |
| 2 | **Per-field binding was rejected by the architect.** A node binds its **whole DTO** to exactly **one** variable, via a single `ExpressionTargetField` | Addendum v3 §2.1–2.2 |
| 3 | Reason: the kernel projects params as one `ref TValue` over a contiguous pre-packed slice (`Unsafe.As` at a bin-packed offset); scattering fields would force a per-tick temp struct + copy, breaking zero-alloc | Addendum v3 §2.1 |
| 4 | **Static** values → the bound variable's `DefaultValueJson`, baked into a generated `ParseParamsDelegate`, applied at **behavior assignment** | Addendum v3 §2.3 |
| 5 | **Dynamic** values → **Approach A**, whole-DTO aliasing: two nodes bind the same variable ⇒ same baked offset ⇒ true zero-copy sharing, so writes are visible to both | DD §7, Addendum v3 §2.3 |
| 6 | **Approach B**, field-level sync in/out via a generated orchestrator, is **Subtree-only** and explicitly *not* available on plain action nodes | Addendum v3 §2.3 |
| 7 | **Node-owned / auto-managed** variables (`IsAutoManaged`, "+ Promote to new variable") exist to avoid variable sprawl; downstream they are ordinary variables | Addendum v3 §3 |
| 8 | Params live in `BrainBlackboard.BehaviorParameters` (100 B inline, `MaxBehaviorParamByteSize`); overflow → `Blackboard1024` heavy tier | ParamBinding DD §2 |
| 9 | ⭐ The binding identity is a **key string carrying the offset**: `{MethodFqn}@{offset}`, and `{MethodFqn}@{offset}@{slotKey}` when stateful | `BTreeBridgeEmitCore.cs:460,488,622` |
| 10 | Stateful slot key = `FNV-1a(BehaviorAssetId, NodeVisualId)` — because `BlueprintId` alone cannot distinguish two nodes using the same primitive | ParamBinding DD §4.2 |
| 11 | **"BTree owns layout, blueprint provides `TickCore`"** — the generator *ignores* a blueprint's standalone `BTreeTick` and emits a per-node adapter projecting `Params` at the BTree-controlled offset | ParamBinding DD §3.2 |
| 12 | The editor bin-packer is **advisory**; the authoritative layout is the compiled struct (`Marshal.OffsetOf`), and `bool` needs `[MarshalAs(UnmanagedType.I1)]` or offsets silently drift | ParamBinding DD §2, §3.2 |

---

## 2. The HSM side — where it stands

Verified; details and citations in [Hsm_Integration_Map.md](Hsm_Integration_Map.md) §4.

**What is the same.** HSM entities carry `BrainBlackboard` and `Blackboard1024` already;
`HsmAsset` already has `BlackboardVariables` + `BlackboardTypeName`; `TransitionNode` and
`GlobalTransitionNode` already carry `ExpressionTargetField` (BB1 shipped that).

**What is different — and this is the crux.**

| | BTree | HSM |
|---|---|---|
| Bindings per carrier | 1 action per node | **4 per state** (Entry/Exit/Activity/Timer) + **2 per transition** (guard, effect) |
| Node identity | `NodeVisualId` | `StateNode.StableId` / `TransitionNode.VisualId` |
| Identity → id | key string `{Fqn}@{offset}` into the action registry | **`ComputeHash(name)`** — FNV-1a-32 of the name, truncated to `ushort` (`HsmFlattener.cs:385`) |
| How the callee gets the blackboard | `ref BrainBlackboard bb` **is a parameter** of the thunk | thunk gets `(void* instance, void* context, …)`; blackboard must be fetched from the entity via `HsmKernelBridge` |
| Concurrency | parallel nodes | **orthogonal regions genuinely run concurrently**, and the kernel already has a lane-conflict notion |
| `ExpressionTargetField` on states | n/a | **absent** — `StateNode` has none (`DEBT-BF-04`) |

**What is broken** (tracker rows): HSM-013 registration keyed on a GUID hash the blob never looks up ·
HSM-015 the generated thunk reads `Params` out of live instance memory · HSM-016 the JSON bridge
registers no-op stubs at invented ids · HSM-014 the picker offers only names already in the asset.

---

## 3. ⭐ Our central proposal — please shoot at it

**Adopt your key convention verbatim, because HSM action ids are hashes of an arbitrary string.**

```
editor writes the slot's action name as:   Ns.Type.Method@40
HsmFlattener hashes the whole string   →   StateDef.ActivityActionId = ComputeHash("Ns.Type.Method@40")
the generated bridge registers a thunk under the SAME hash, projecting the DTO at offset 40
```

If this holds it is unusually good news: **the offset rides inside the hashed identity, so no
`StateDef`/`TransitionDef` ROM change is needed** (both structs are full — 32 B with one spare byte,
and 16 B with none). HSM-013 and HSM-015 collapse into one fix, and it is the fix you already
shipped.

**Q-A. Is that sound, or does the offset-in-key convention depend on something BTree-specific we
have not noticed?**

---

## 4. The questions

Grouped. Each carries our lean so you can disagree with something concrete.

### Q-B — Per-slot binding on a state

A BTree node has one action; an HSM **state has four**. Options:

- **B1** — one `ExpressionTargetField` per slot: `OnEntryParam`, `OnExitParam`, `ActivityParam`, `TimerParam`, plus the transition's `GuardParam`/`EffectParam`. Faithful, but six new persisted fields and six picker sites.
- **B2** — one variable per *state*, shared by all four slots. Cheap, but forces all four actions to take the same DTO type — almost never true.
- **B3** — treat each slot as its own binding *carrier* with a synthetic id (`StableId` + slot kind), so the model is "bindings" rather than "fields on a state".

**Our lean: B1** for the authored surface, with B3's synthetic id underneath for slot-key
uniqueness. **Does that match how you would extend your own model?** This is `DEBT-BF-04`, which the
plan doc says needs a design decision rather than a guess.

### Q-C — Where a guard's parameters live

Your conditions bind params exactly like actions. An HSM guard is
`bool Guard(void* instance, void* context, ushort eventId)` — **no blackboard parameter**, and
`instance` is the HSM instance, not a blackboard. So the thunk must fetch `BrainBlackboard` from the
entity via the bridge and project at the baked offset.

**Q-C1.** Is fetching `BrainBlackboard` per guard evaluation acceptable, given guards are evaluated
**speculatively** across candidate transitions (possibly several per RTC iteration, up to 100
iterations)? **Q-C2.** Would you instead push a blackboard pointer into `HsmKernelBridge` once per
tick, so guards get it for free? *(Our lean: C2 — it is one extra pointer in a struct we already
build per entity per tick.)*

### Q-D — Write-back: how an action's results reach a variable

This is the user's explicit question, and where we are least sure of your intent.

As we read it, **Approach A aliasing is the write-back mechanism**: the action holds a `ref` to the
variable's bytes, so anything it writes *is* the variable, with no copy-back step. Approach B's
explicit in/out sync is Subtree-only.

- **Q-D1.** Is that right — is "write back" simply "the DTO **is** the variable, mutate it in place"?
- **Q-D2.** If so, is there any read-only enforcement? An HSM **guard** must be side-effect free
  (`SelectTransition` evaluates speculatively and discards losers), so a guard holding a mutable
  `ref` to a shared variable is a footgun your BTree conditions may not have. **Should HSM guards get
  a read-only projection, and does that break DTO-type equality with the action that shares the
  variable?**
- **Q-D3.** Two orthogonal HSM regions aliasing one variable are **genuinely concurrent** — worse
  than BTree parallel nodes. Our validator already has `CheckConcurrentSharedScopeKeys` and
  `CrossRegionBlackboardConflict` rules for exactly this. Is hard-erroring the right stance (matching
  your §4.3 fix 3 for concurrent stateful Subtrees), or is aliasing across regions legitimate when
  the writer is unique?

### Q-E — Working state and scope

Your §4.4 model is role × scope, with `Node` scope keyed `FNV-1a(BehaviorAssetId, NodeVisualId)`.

- **Q-E1.** For HSM, is the `Node`-scope analogue **per state**, or **per state-slot**? A state's
  Entry and Activity are different call sites — do they share working state or not?
  *(Our lean: per state-slot, keyed `FNV-1a(AssetId, StableId, SlotKind)`.)*
- **Q-E2.** `Behavior` and `Entity` scope look like they carry over unchanged. Agreed?
- **Q-E3.** HSM has a lifecycle BTree lacks: **a state can be exited and re-entered**, and history
  can restore it. Should `Node`-scoped state be **reset on entry**, **preserved across re-entry**, or
  **author-controlled**? *(Our lean: preserved, because OnEntry is the natural place to reset — but
  this is a real semantic choice and we would rather have your view.)*

### Q-F — The 100-byte budget under HSM concurrency

Inline params share a 100 B region. A BTree runs one leaf at a time; **an HSM parallel composite has
several active leaves simultaneously**, each with Activity params live in the same 100 B.

**Does the bin-packer's budget model already account for simultaneously-live bindings, or does it
assume one-at-a-time?** If the latter, HSM may exhaust the inline region far sooner and need the
heavy tier much earlier.

### Q-G — Ownership

HSM-013, HSM-015 and HSM-016 are **codegen** defects (`AiPrimitiveEmitter`, `CSharpEmitter`,
`HsmBridgeEmitCore`) surfacing as HSM authoring failures. **Whose lane are they?** We are happy to
hold them in the HSM tracker as *recorded, not owned*, if the fix belongs with the blueprint
compiler.

---

## 5. What would be most useful back

1. **Corrections to §1** — anything we have misread about your model.
2. **A yes/no on Q-A**, the offset-in-key proposal. Everything else depends on it.
3. **Your lean on Q-B and Q-D**, the two genuinely new shapes (multi-slot carriers, guard
   side-effect safety).
4. **A pointer to anything already designed for HSM** that we have not found — the ParamBinding DD
   is BTree-titled but its §4.4 variable model reads as host-agnostic, and we would rather adopt than
   re-derive.
5. **A view on whether this needs an architect round** before either session builds. Our instinct is
   yes for Q-B/Q-D/Q-E3, since `.claude/CLAUDE.md` requires an architect pass for non-trivial
   capabilities and `DEBT-BF-04` was explicitly flagged as *"needs an architect design decision, not
   an autonomous guess"*.

---

## 6. Explicitly not in scope here

Event authoring (HSM-009) · the initial-state model (HSM-003) · history modelling (HSM-010) ·
timers (HSM-012) · region persistence (HSM-004/005). Those are HSM-local and do not need you.

---

## Change log

| Date | Change |
|---|---|
| 2026-08-14 | Created as the opening prompt for the parameters/variables consultation. |
