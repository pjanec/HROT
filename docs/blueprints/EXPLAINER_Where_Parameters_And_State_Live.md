# Where parameters and state actually live — all hosts, one picture

> **Why this exists.** The question *"is every input variable in the 100-byte blackboard?"* has a
> one-word answer (**yes**) and a five-part explanation. This is the explanation, measured on
> `HEAD` (`2026-08-16`), not inferred.
>
> ⭐⭐ **Headline: most of the unification we were designing already exists as a design record** —
> 📄 [`Behavior_Parameter_Resolver_Detailed_Design.md`](Behavior_Parameter_Resolver_Detailed_Design.md)
> (`2026-07-13`), with a gap list `G1`–`G7`. This document does **not** redesign it; it maps it onto
> what the code does today.

---

## 1. The storage map

![storage map](EXPLAINER_Storage_Map.svg)

| region | holds | size | who writes it |
|---|---|---|---|
| **`BrainBlackboard.BehaviorParameters`** | ⭐ **every input, every host, every tier** | **100 B**, shared by all actions on the entity | `BehaviorIngressSystem`, once at activation |
| `Blackboard1024.Memory` | AiPrimitive / shared-AI **state** | 1024 B, `StructureHash` @0, state @8 | the action itself, every tick |
| `BlueprintBlackboard{1024,4096,16384}` | ⭐ **Instance blueprint state — the allocatable one** | payload **928 / 3936 / 16368 B**, 4 slots | `BlueprintInstanceService.AttachToEntity` |
| a managed heavy component | `[SharedAiHeavyAction]` managed state | unbounded (a class) | the action itself |

⭐ **All four are `[DataPolicy(NoSave)]`.** Nothing here is serialised — inputs are **re-supplied at
each activation**, which is why the tier question is about *addressing*, not persistence.

### The three things people get wrong

| | |
|---|---|
| ⛔ *"blueprints keep inputs in allocated space"* | **No.** `asset.Parameters` has **one emitter in the entire compiler** — `AiPrimitiveEmitter.EmitParamsStruct`. `InstanceEmitter` never emits them, and `BP1031` refuses them outright. ⇒ **a blueprint has inputs only when it IS a BTree/HSM action**, and they land in the same 100 bytes |
| ⛔ *"going heavy moves the params"* | **No.** `EmitHeavySharedAiAdapter` emits **both**: params from `bb.BehaviorParameters`, heavy state from the component. ⭐ **The heavy tier extends STATE, never INPUT** |
| ⛔ *"the 100 bytes are per node"* | **No.** One region **per entity**, carved into disjoint offsets by every action on it. The cap is enforced three times, the last a runtime `throw` |

---

## 2. How inputs get supplied

![supply path](EXPLAINER_Supply_Path.svg)

⭐⭐ **`BP1031`'s *"nothing supplies them at spawn"* is true only of the `Instance` dispatch** — which
is attached via `BlueprintInstanceService`, not installed through behavior ingress. Everything
reached by `AssignBehaviorEvent` **is** supplied, and has been since before this programme started.

⇒ ⭐ **If a tactical intent installs a blueprint the way it installs a BTree/HSM behavior, the supply
path already exists and `BP1031`'s stated reason evaporates.** That is a smaller change than the rail
makes it look — but it is a *design* question (who supplies them, and when), not a rail to delete.

---

## 3. The three data shapes — and there is no "Param" role

From the resolver design §3.2, verbatim:

> **Vocabulary note.** The variable **role** enum is `{ Input, State }` — there is no separate
> "Param" role. `Input` *is* the parameter role.

| shape | authored? | populated | lifetime |
|---|---|---|---|
| **authored DTO** | ✅ yes | deserialized from JSON — a transient parse buffer | parse-time only |
| **usable params** — `Role=Input` | no | the resolver writes them (identity ⇒ just the deserialize) | resolved **once at activation** |
| **working state** — `Role=State` | no | zero-init at provisioning | **reset at activation**; `Scope` = `Node`/`Behavior`/`Entity` |

⭐ **One shape by default.** The authored DTO is an auto-generated mirror of the `Input` variables;
two shapes appear only on divergence (a `PickableGeoPoint` the designer clicks vs. the Cartesian the
tree reads). ⇒ ⭐⭐ **"a single struct-typed input" is the degenerate case of the multi-field model,
not a rival to it.**

---

## 4. One region, two layout authorities — the whole difference

| | who declares the shape | who assigns offsets | moving a field |
|---|---|---|---|
| **compiler-owned** — blueprint-as-AiPrimitive, JSON-authored behaviors | authored declarations, multi-field | `EmitParamsStruct` + `FieldLayout`, versioned by `StructureHash` | ✅ a versioned recompile |
| **human-owned** — `[SharedAiAction(typeof(Dto),"Field")]` | a hand-written DTO, one field per action | the human, by field order | ⛔ **silently breaks every saved binding** — the offset is *in* the registry key, `Method@byteOffset` |

⭐⭐ **The storage is already unified. Only the layout authority differs.** That is the real content
of the "reserved input variable" idea — and it needs **no new type-authoring machinery**: a struct
type is already selectable as a variable's type via `U-8` discovery of `[BlackboardDtoStruct]`.

⚠ **The one blocker is `S5`.** The parameter combo reads `EditorOfferableTypeIds` — **18 hardcoded
primitives, no structs** — while the variable modal reads primitives **plus** discovered structs.
⇒ **a variable can be struct-typed today; a parameter cannot.**

---

## 5. HSM parallel regions

![hsm parallel storage](EXPLAINER_Hsm_Parallel_Storage.svg)

**The kernel is genuinely concurrent.** `ActiveLeafIds[4]`, and `ProcessActivityPhase` walks every
region and every leaf's ancestor chain in one tick. Per-region storage exists — `ActiveLeafIds`,
`TimerDeadlines`, `HistorySlots` — but it holds **bookkeeping, never action data**.

🔴 **The addressing has no region in it.** The slot key is `hash(methodName @ compileTimeOffset)`,
resolved through one shared `ActionTable`, projected at a static offset in the **one**
`BrainBlackboard` the entity has. All four action slots — Entry / Exit / Activity / Timer — dispatch
identically. ⇒ **two concurrently-active regions running the same action write the same bytes.**

⭐ **BTree does not have this problem** — it provisions per-scope partition slots via
`ResolveStatefulSlotKey` over `Node`/`Behavior`/`Entity`.

### Two findings this raises

⚠ **Described, not numbered — the coordinator allocates no ids (rule 3).**

| | measured |
|---|---|
| **HSM `Role`/`Scope` have no runtime wiring** | `HsmEmitCore` + `HsmBridgeEmitCore` contain **0** `Role`/`Scope` references; `BTreeBridgeEmitCore` contains **45**. `HsmBlackboardVariableDto` persists both faithfully. ⇒ **authoring metadata the HSM runtime never reads** |
| **The two guards for exactly this collision never fire** | `HsmValidator` rules **8** (`ConcurrentStatefulSubtree`) and **8b** (`ConcurrentSharedScopeKey`) are errors with correct messages, but both depend on injected resolvers defaulting to `_ => false` / `_ => Empty`, and **both production call sites use the default ctor**. Only unit tests pass real resolvers |

⭐ **Neither is a dead rule to delete.** The XML doc says *"Production should wire this"* — this is
**unfinished wiring**, and the `.dev/` rule applies: the design record says what it is for.

---

## 5b. Instance dispatch — what it is, and why `BP1031` is not a wall

⛔ **Not a debug feature.** Three attach paths, two of them production: `BlueprintMaterializationSystem`
(`Hrot.SimHost`, `SystemPhase.Input`) resolves `InitialBlueprintsIntent` from scenario state and
pre-provisions the tier; `BlueprintEventIngressSystem` attaches/switches by event; the editor's
`RunBlueprintOnEntityCommand` is authoring convenience. Ticked by `BlueprintTickSystem`.

| | **Instance blueprint** | **BTree/HSM behavior** |
|---|---|---|
| verb | **attached** | **assigned** |
| how many per entity | ⭐ **several** — 4 slots (1024) … 16 (16384) | ⭐ **one active** |
| comes from | scenario state, at load | a command / tactical intent |
| state home | its own partition slot, keyed `blueprintId + StructureHash` | the shared 1024 / 100 B regions |
| boots to | `InitDefault` | zero-init, after the resolver writes inputs |

⇒ An Instance is **a script component bolted onto an entity**; a behavior is **what the entity is
currently doing.** That is why one stacks and the other is exclusive.

### ⭐⭐ The supply slot exists — deferred, not absent

```csharp
/// <summary>Per-variable overrides. Null/empty in MVP (see Design §6).</summary>
public Dictionary<string, object>? Overrides { get; init; }
```

📄 **`.dev/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` §6** — *"Variable overrides (MVP:
assignment-only; door left open) … The format is forward-compatible so overrides drop in without a
change."* ⭐⭐⭐ **And the stated blocker:** *"**Deferred because** the authoring UX ('where is a
per-instance override edited?') is unsettled."*

⇒ ⭐⭐ **That is the UX Track C is designing right now.** The variable Details panel *is* the answer
to that question.

⭐ **Note what the design chose:** per-variable **overrides on ordinary `Variable`s** — *not* a
`Parameter` tier. So:

| host | supply seam | when |
|---|---|---|
| behavior | JSON → resolver → `Role=Input` bytes | at **activation** |
| Instance blueprint | `Overrides` → after `InitDefault`, by descriptor lookup | at **attach** |

⇒ ⭐⭐⭐ **Both reduce to *"a variable with an externally supplied initial value."*** Neither host
needs a `Parameter` tier to get it. **`BP1031` therefore reads as *"Instances express inputs as
overridable variables, not as a Parameters list"*** — the cleaner side of the one-cell model, not a wall.

### ⭐⭐ The concrete blocker on Instance parameters — **identity, not storage**

⛔ **Instances do NOT have the HSM collision.** Every attach takes its own allocated, zeroed payload
slot from a real allocator (free list + bump, per-slot `StructureHash`) ⇒ **several blueprints running
at once on one entity is already correct.** ⭐ **Blueprints and BTree are the template HSM is missing** —
not the other way round.

🔴 **But slot identity is `blueprintId` ALONE**, and attach is **idempotent** on it —
`TryFindExistingTier` ⇒ `AlreadyAttached`, a no-op:

```csharp
slot.BlueprintId = blueprintId;     // the whole identity
```

⇒ ⭐⭐⭐ **Parameterless scripts are fine; parameterised ones are not.** The moment a script takes
arguments you want **the same script twice with different arguments** — two `Patrol` instances on
different waypoints — and that is impossible **by construction** today.

⇒ **The real work in "Instance blueprints take parameters" is widening slot identity from
`blueprintId` to `(blueprintId, instanceKey)`.** ⭐ **That IS the same shape as the HSM defect** —
*one key where there should be several* — which is why the two feel like one problem. They are one
problem **at the identity layer**, and two different problems at the storage layer.

⚠ **`InstanceVersion` is not a free hook for this.** It is already load-bearing: bumped on hard reload
and compared against `BlueprintLatentCursor.InstanceVersion` to invalidate stale latent resumes — the
blueprint twin of `BehaviorState.InstanceId`.

### ⚠ Open, and genuinely the architect's

| | |
|---|---|
| **should intent install blueprints at all?** | merging the two lifecycles. The shipped design keeps them apart — intent assigns behaviors, scenario attaches blueprints |
| ⭐ **is a behavior "an Instance limited to one per entity"?** | ⛔ **Not as it stands.** The behavior side carries a **monotonic preemption token** (`InstanceId`, driving `ChannelArbitrationSystem` to invalidate a superseded behavior's in-flight commands), **parse-before-commit atomicity**, brain/`SimTier` coupling, and *"assignment ≡ activation, deactivation is brain death"* — the rule that makes **resolve-once** safe. ⇒ ⭐⭐ **Exclusivity is not a cardinality constraint; it is what preemption is defined against.** A unification runs the other way: **blueprint dispatch would have to GROW a preemption and atomic-swap story**, not intent shrink into it |

---

## 6. What this means for `W8` / `D2`

| | |
|---|---|
| **`D2` — which `DeclarationKind`?** | ⭐ **Largely dissolved.** The resolver design already rules there is no "Param" role: `Input` *is* the parameter role, and inputs stay in the params region. `D2` reduces to *"let the compiler own the layout"* rather than *"move to the heavy tier"* |
| **inputs-in-1024** | ⛔ **not needed for the unification.** The decoupling motive is answered by generating the layout; the *size* motive survives only as an **opt-in overflow tier** |
| **the real work** | `S5` (one picker, so parameters can be struct-typed) · the `G1` split of deserialize from resolve · and for HSM, wiring `Role`/`Scope` at all |

⇒ ⭐⭐⭐ **The unification that covers all hosts while still allowing hardcoded DTOs is: one params
region, one `{Input, State}` role model, and the compiler owning the layout — with a hand-written
`[BlackboardDtoStruct]` remaining legal as a declaration's *type*.** Most of it is designed already.

---

## Sources

`FieldLayout` · `AiPrimitiveEmitter` · `InstanceEmitter` · `EmissionContext` · `Stage2_Validate`
(`BP1031`) · `BehaviorComponents` · `BehaviorConstants` · `BehaviorRegistry` · `BehaviorIngressSystem`
· `BTreeActionGenerator` · `HsmActionGenerator` · `HsmKernelCore` · `HsmValidator` ·
`BlueprintInstanceService` · `BlueprintBlackboardPartitions` · `VariableCreateModal` ·
`ParameterRowsView` · `BlackboardTypeChoiceBuilder` · `VariablesPanelControl`
📄 `Behavior_Parameter_Resolver_Detailed_Design.md` · `Blackboard_Authoring_Addendum_v3` §4
(architect-approved `2026-06-06`) · `.dev/_DONE/tactical-intent/DESIGN.md` · `DESIGN_U12_Rails.md`
