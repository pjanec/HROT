<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: section 5 - the recommended answers. Nothing here is built.
stale-below: nothing.
known-rot: none.
known-conflict: none known. Section 3 records where DESIGN_Parameter_Model.md's
  resolver tier turns out to be unreachable for managed assets; that is a finding,
  not a disagreement between documents.
-->
# ⭐ Architect Question 41 — **can a blueprint drive a BTree node's parameters?**

> ⛔⛔ **NOT RELAYED.** The architect is generally unavailable (`2026-08-16` user ruling).
> ⭐⭐ **Resolved JOINTLY with the user: I analyse and RECOMMEND, the user APPROVES.**
> ⭐ **Every sub-question below carries a recommended answer.** Reply *"approved"*, or name the one
> you want changed.
>
> 📌 **Origin:** user, `2026-08-18` — *"what if i need to set the speed or destination from a
> blackboard? … how can i change this variable at runtime? via special action perhaps, for example a
> blueprint aiprimitive? how can such a blueprint access the parent asset's blackboard?"*

---

## 1. ⭐⭐ INVENTORY — **the enumeration, before the design** *(`R-74`)*

| # | query | total | what it found |
|---|---|---|---|
| ① | `search_graph(name_pattern=".*IHostVariableAccess.*")` | **4** | the file · the module · **the interface** · **one rail test asserting nothing implements it**. ⛔ **Zero implementers** |
| ② | `search_graph(name_pattern=".*(ParseParamsDelegate\|ResolveParams\|RegisterResolver\|TrySetShared\|TryGetShared).*")` | **9** | production: `BehaviorRegistry.RegisterResolver` · `BlueprintSharedState.{TryGetShared, TrySetShared, TrySetSharedField}`. The rest are tests, one doc section, and one unrelated `TryResolveParamsSize` |
| ③ | `grep RegisterResolver` *(non-test)* | **5 call sites** | **all in `CgfCuratedBehaviorRegistrar`** — `MoveToLocation`, `FollowRoute`, `FireAtTarget`, `HullDownAttackRun`, `PlatoonHillAttack` |
| ④ | `grep -i resolver` over `Hrot.Editor.*` / `*.BTree.Editor` / `*.Hsm.Editor` | **0 relevant** | every hit is an unrelated `Func` *(canvas context, section source, entry resolver)*. ⛔ **No editor surface for a parameter resolver exists** |
| ⑤ | `BTreeActionDelegateShape` members | **3 named + 1 numeric** | `ThreeParamReusable` · `FourParamFull` · `AiPrimitiveTickCore = 3` · *(2 = `ThreeParamReusableStateful`, DTO-only)* |

---

## 2. ⭐⭐⭐ THE CRUX — **two disjoint memories, and nothing plain crosses**

| memory | who writes | who reads |
|---|---|---|
| **`BrainBlackboard`** *(BTree/HSM params + variables, bin-packed, baked offsets)* | `ParseParams` at assignment · any node holding a `ref` · a `FourParamFull` action | every BTree action, by **baked offset** |
| **`BlueprintBlackboard{16384,4096,1024}`** *(blueprint WorkingState + shared state, partition slots by key)* | a blueprint's `TickCore` · `BlueprintSharedState.TrySetShared*` | anything with `world` + `self` + **the name** |

📐 **Measured — the composed thunk** *(`BTreeBridgeEmitCore.AppendReusableStatefulThunk`)*:

```csharp
TickCore(ref dto, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime)
//       ↑ ref into BrainBlackboard at ITS OWN baked offset
//              ↑ ref into a BlueprintBlackboard partition slot
```

⇒ ⭐⭐ **A blueprint AiPrimitive can write exactly two things: its own `Params` and its own
`WorkingState`.** ⛔ **It is handed no host blackboard and no name→offset map**, so another node's
variable is *reachable* (the component is on the same entity) but **not addressable**.
📌 **`Q33` §1.5.8 named this exactly: *"what is actually missing: addressing, not access."***

### ⭐ What ALREADY bridges the two — **and it is not nothing**

⭐⭐ **A `FourParamFull` action** receives `(ref TBB, ref BehaviorTreeState, ref TCtx, int)` — the
**whole `BrainBlackboard`** *and* `ctx.World`/`ctx.Self`. ⇒ it can call
`BlueprintSharedState.TryGetShared(world, self, "name", out v)` and write the result into any host
variable. ⛔ **It is hand-written C#, per case.**

---

## 3. ⛔⛔ A FINDING THE DESIGN DID NOT KNOW — **the resolver tier is UNREACHABLE for managed assets**

📄 `DESIGN_Parameter_Model.md` §3.2 lists *"resolve-at-entry from world state"* as a supply tier, and
`Q33` §1.5.8 calls it *"exactly what a resolver is for."* ⭐ **Both are true of CURATED behaviours.**

📐 **Measured `2026-08-18`, `BehaviorRegistry.ApplyResolverOverlay`:**

```csharp
if (def.ParseParams == null)          // ⛔ "a topology def that set its own ParseParams wins"
    def.ParseParams = overlay.Resolver;
```

⚠⚠ **A managed asset ALWAYS emits its own `ParseParams`** — since `BP-275` the emit guard is
*"≥1 packed variable"*, not *"≥1 default"*. ⇒ ⛔⛔ **`RegisterResolver` can never apply to an
editor-authored managed asset.** The two mechanisms are **mutually exclusive by construction**, and
the exclusion is silent — `RegisterResolver` succeeds and simply does nothing.

> ⭐⭐⭐ **So the honest answer to *"can I use a resolver for a non-blueprint action?"* is:
> not on any asset whose parameters you authored in the editor.** ⛔ **And there is no editor surface
> for one either** *(inventory ④)*. ⚠ **Resolvers are per-behaviour-NAME, code-registered, and there
> are five.**

---

## 4. ⭐ The design constraints that bind any answer

| id | binds |
|---|---|
| **`R-84`** | ⛔ **live binding across host↔child is DELIBERATELY OUT** — a hosted primitive resolves **once at activation**. ⚠ Sharing *within* one asset is live by construction; do not conflate |
| **`R-85`** | ⛔ **`IHostVariableAccess` is READ-ONLY by design** — *"a resolver never writes its host"*; a write path is **a second supply mechanism** *(ruling 9)* |
| **`R-82`** | ⛔ **whole-DTO binding only** — no per-field sources |
| **`R-24`** | ⛔ **cross-asset access by NAME, never a raw offset** — layout is `StructureHash`-versioned |
| **`R-65`** | ⚠ the blackboard component is **shared by three hosts at disjoint offsets** — a blind write clobbers a neighbour |

---

## 5. ⭐⭐⭐ THE SUB-QUESTIONS — **each with a recommended answer**

### `Q41-A` — May a blueprint WRITE a host BTree variable?

| | option | verdict |
|---|---|---|
| **A1** | ⛔ **No — publish/subscribe.** The blueprint writes a **named entity-scoped shared slot**; the BTree side reads it | ⭐⭐⭐ **RECOMMENDED** |
| **A2** | give `IHostVariableAccess` a write half | ⛔ **Reject** — `R-85` names this a second supply mechanism, and `R-84` rules the live channel out. Both would have to be reopened |
| **A3** | let it write only its own `Params` | ⚠ **Already true**, and it is a hack — `Params` is an input. ⛔ Do not bless it as the mechanism |

⭐ **Why A1:** it needs **no new supply mechanism**, it is **name-keyed** *(`R-24`)*, it **fails
closed** on hash drift, and it already ships — `TrySetShared` / `TrySetSharedField`, the latter
writing **one field** and leaving the rest alone. ⚠ **Blast radius: none.** It is a usage pattern.

### `Q41-B` — What carries the value onto the BTree side, per tick?

| | option | verdict |
|---|---|---|
| **B1** | a hand-written **`FourParamFull`** action per case | ⚠ **works today**, ⛔ **needs a programmer every time** |
| **B2** | ⭐ **ship ONE generic reusable node** — *"read shared slot `X` → write host variable `Y`"* — authored entirely in the editor | ⭐⭐⭐ **RECOMMENDED** |
| **B3** | alias: the consumer node binds the **same variable** the blueprint's `Params` occupies | ⚠ **mechanically works only when the two DTO types are IDENTICAL** *(the picker is type-filtered)*. ⛔ Too narrow to be the answer |

⭐ **Why B2:** it is the **round-out** of `B1` — same mechanism, made general and authorable — and it
turns *"a programmer per case"* into *"a node you drop."* ⚠ **It is one node, not a vocabulary.**
⛔ **Open sub-choice for the user:** does it write the target variable **whole** (`TryGetShared<T>` →
`Unsafe.Write`) or **one field**? ⭐ **Lean: whole**, mirroring `R-82`; the field variant only if a
concrete case needs it.

### `Q41-C` — Should resolvers become editor-assignable?

| | option | verdict |
|---|---|---|
| **C1** | ⭐ **Leave resolvers as code, and FIX THE SILENT EXCLUSION** — make a `RegisterResolver` against an asset that emits its own `ParseParams` **fail loudly**, or say so in a diagnostic | ⭐⭐⭐ **RECOMMENDED** |
| **C2** | a dropdown in the asset editor over `_resolversByName` | ⛔ **Reject for now** — §3 says it would be **inert** on exactly the assets that have an editor. ⚠ Revisit only if `C1` changes the precedence |
| **C3** | per-action-node resolvers | ⛔⛔ **Reject** — resolvers are per-BEHAVIOUR by construction; per-node is a new supply mechanism *(ruling 9)* |

⭐ **Why C1:** the defect is not *"no UI"*, it is **a mechanism that silently does nothing**. ⚠ That
is the `2026-08-16` silent-default pattern with a different shape — ⛔ **a caller that registered a
resolver believes it has one.** ⭐ **Cheapest honest fix: make the collision visible.**

### `Q41-D` — If a cross-memory read ships, keyed how?

⭐⭐⭐ **RECOMMENDED: by NAME, resolved through the existing slot-key + `StructureHash` guard**, exactly
as `TryGetShared` already does. ⛔ **Never a baked cross-asset offset** *(`R-24`)*. ⚠ **No sub-options
worth listing** — this one follows from the canon and is stated only so it is not re-decided.

---

## 6. ⭐ What this does NOT decide

| ⛔ | |
|---|---|
| **`E7a`** *(populating `IHostVariableAccess`)* | unchanged — still read-only, still resolve-once |
| **per-field binding** | still rejected *(`R-82`)* |
| **the `FourParamFull` shape** | stays; `B2` is built **on** it, not instead of it |
| **any HSM-side equivalent** | ⚠ the same two memories exist there; ⛔ **not measured for this question** |
