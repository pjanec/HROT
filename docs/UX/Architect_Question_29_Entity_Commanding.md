# Architect Question 29 — entity commanding: what a map "order" actually is

> **For [UXI-32](UX_Issues.md#uxi-32) · opened 2026-08-13. Status: ◐ open — decisions pending.**
> Raised by the user, 2026-08-13: *"the actions like MoveHere, Engage, Stop, Properties, Teleport, Repair,
> Reinforce, Resupply, Transfer are unresolved and need a dedicated design pass. **The only supported way
> of commanding entities now is via a mission having a list of conditional behaviors to perform.** This is
> not ExCon-only, must be equally supported by the CGF subsystem (who owns the entity brain)."*
>
> ⇒ This **supersedes [UXI-23 §5](UX_Feature_Map_Parity.md)**, whose A/B/C options all assumed the nine ids
> were ordinary actions with missing handlers. They are not.
> ⚠ [Correction 33](UX_Tasks_Detail.md#corrections) records why my *"keep them ExCon-only"* lean was wrong.
>
> Format follows [Q28](Architect_Question_28_Map_Layers.md): decision-shaped sub-questions, each with
> options, a recommended lean, and the reuse-vs-build tradeoff.

![the commanding chain](img/uxi32_commanding.svg)

## 0. Verified ground truth — the commanding chain, end to end

**Every link below was read, not inferred.**

| Stage | What | Where |
|--:|---|---|
| 1 | `MissionControlCommand { EntityId, eMissionCommandType, MissionPlan?, TaskId, BaseVersion }` — *"wrapper for a mission plan **or imperative**"* | `Hrot.Core/Network/Commands.cs:27-34` |
| 2 | `MissionControlExecutionSystem` applies it. ⚠ Requires a **live** `EntityRepository`; waits **10 frames** for the entity then NAKs `EntityNotFound` | `Hrot.Common/Systems/MissionControlExecutionSystem.cs:42-46,87-90` |
| 3 | `MissionPlanQueue` — **≤ 8 phases**, `DataPolicy.NoSave` | `Fdp.Toolkits/Behavior/Components/MissionComponents.cs:140-163` |
| 4 | `MissionDirectorSystem` evaluates the phase trigger and publishes the transition | `Fdp.Toolkits/Behavior/Systems/MissionDirectorSystem.cs:27-41` |
| 5 | `MissionAdapterSystem` detects the phase change and publishes an **`AssignTacticalIntentEvent`**. 🔒 It *"intentionally does **not** mutate `BehaviorState` or `BrainBlackboard` directly"* | `Hrot.CGF/Systems/MissionAdapterSystem.cs` |
| 6 | ⭐ **`AssignTacticalIntentEvent { Entity, string IntentId, string JsonParams }`** — the choke point | `Fdp.Toolkits/Behavior/Events/AssignTacticalIntentEvent.cs:23-43` |
| 7 | **Complementary authority gates** — `TacticalIntentResolutionSystem` runs `if (!HasAuthority<BehaviorState>) continue`; `TacticalIntentEgressTranslator` runs `if (HasAuthority<BehaviorState>) continue` and writes `TacticalIntentRequest` | `TacticalIntentResolutionSystem.cs:93-95` · `TacticalIntentEgressTranslator.cs:72` |
| 8 | `ITacticalOrderMapper.TryMap(self, repo, jsonParams, out AssignBehaviorEvent)` — or **pass-through**, treating `IntentId` as a behavior name | `ITacticalOrderMapper.cs:23-55` |
| 9 | `BehaviorIngressSystem` (**Input** phase) — 🔒 **the sole `BehaviorState` writer** | guide `:472-474` |

### ⭐ The ECS tier keeps **both** representations — authored *and* executable

⚠ **An earlier draft of this document said the wire DTO was strictly richer than the runtime. That was
wrong** — it missed the managed component. `MissionControlExecutionSystem` writes **both**, in the same
block (`:182-186`):

```csharp
repo.SetComponent(entity, queue);                                    // executable, ≤8, ints + enum
repo.SetManagedComponent(entity, new ActiveMissionPlan { Plan = domainPlan });  // authored, unbounded, strings
SmartEgressUtil.MarkDirty(repo, entity, EntityMissionDescriptorOrdinal);
```

| | `ActiveMissionPlan` — **authored** | `MissionPlanQueue` — **executable** |
|---|---|---|
| kind | **managed** component (`DomainMissionPlan.cs:45-49`) | unmanaged, ECS-chunk (`MissionComponents.cs:140`) |
| plan | `List<DomainMissionTask>` — **unbounded** | **≤ 8** phases, `[InlineArray]` |
| step | `string BehaviorName`, `string BehaviorParams`, `string ExecutingEngine` | `int BehaviorId` |
| condition | **`List<DomainMissionTrigger>`**, each `{ string Type, string Params }` | **one** enum + **one** float |

⇒ 🔒 **The authored surplus is not lost — it is retained beside the projection.** The real question
becomes *which* one is authoritative when they disagree, and whether the operator is told (§D).

⚠ `BehaviorName` crosses as a **string** while the runtime uses an **int**, and the engine forbids the
obvious conversion: *"`BehaviorId`s are stable forever, never reused… **never `string.GetHashCode()`** —
use `BehaviorIds` + `TryGetId`"* (guide `:469-471`).

### What is actually registered today

| | |
|---|---|
| **`ITacticalOrderMapper` implementations, registered in production** | **two**: `DefendArea`, `HullDownAttack` — registered identically by **CGF** (`CgfSubsystem.cs:321-323`) and the **Editor** (`EditorSubsystem.cs:863-865`) |
| Defined but **never registered** | `ForceManeuverMapper`, `ClearForceManeuverMapper` (`Fdp.Toolkits/Squad/Mappers/`) |
| Of the **nine** menu ids | **zero** are a registered intent, a mapper, or a behavior name |

### 🔴 Of the five imperative verbs, **two are dead**

| Verb | Producers | Handler |
|---|---|:--:|
| `CMD_REPLACE_MISSION` | **four** — IG (`IgApplication.cs:3019`, `MiniExConPanelState.cs:249`), Editor (`EditorMissionService.cs:145`), ExCon (`MissionEditorService.cs:107`), SimHost (`SimHostVisualization.cs:283`) | ✅ |
| `CMD_JUMP_TO_TASK` | `MissionPanel.cs:152` | ✅ |
| `CMD_ABORT_ALL` | ⭐ **ExCon's ORBAT panel** (`OrbatPanel.cs:309`) | ✅ |
| 🔴 `CMD_APPEND_TASK` | **none** — the enum member and one `[DdsCase]` attribute, nothing else | ❌ falls to `default:` → `NotSupported` (`:243-245`) |
| 🔴 `CMD_INSERT_TASK` | **none** | ❌ same |

⇒ **There is no "add one task to a plan" operation today.** Every editor rewrites the whole plan.

### ⭐ And the authoring panel already exists — shared, and not where the brain is

| | |
|---|---|
| **`MissionPanel`** — **815 lines**, a full task/trigger editor (add · delete · move · edit behavior + params · edit trigger type + params · Commit / Force-Commit / Jump / Abort) | `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` — an **engine-level shared panel** |
| Hosted by | **ExCon** (`ExConSubsystem.cs:357`) and the **Editor** (`EditorSubsystem.cs:1582`) |
| 🔴 **Not hosted by** | **CGF** — the node that owns the brain — nor SimHost, nor IG |
| ⭐ And the Editor already chains map selection into it | *"Synchronise map selection to the `MissionPanel` using Network ID"* (`EditorSubsystem.cs:1651`) — **the pattern CGF needs already exists, one host over** |

⇒ ⭐ **Seam-law instance 21.** The user's *"must be equally supported by CGF"* is, in large part,
**hosting a panel that is already shared** — not building a mission UI.

⭐ **The spine of every answer below:** stage 6 is a **single choke point that any node may publish to**,
already authority-routed and already network-transparent. A mission phase and an operator's right-click
can converge on it. **The work is to feed it, not to invent a path.** ⇒ **seam-law instance 20.**

---

## A. The nine ids are **three different kinds**, not one — 🎯 split them first

| Kind | Ids | Why it is different |
|---|---|---|
| **Tactical orders** | `MoveHere` · `Engage` · `Stop` | change what the entity *does* ⇒ must enter at stage 6 and end at a behavior |
| **Sustainment / logistics** | `Repair` · `Reinforce` · `Resupply` · `Transfer` | change entity **state or structure**, not behavior. `Transfer` may also mean re-parenting under `UnitRoster` (**cap 16 subordinates**, `UnitRoster.cs:32`) |
| **Not commands at all** | `Properties` · `Teleport` | `Properties` = *open the inspector*, a pure UI action. `Teleport` = a **pose write**, which [UXI-29](UX_Feature_Authority_Aware_Writes.md) already owns |

| | Option | Consequence |
|--:|---|---|
| **A1** | 🎯 **Split into the three kinds and route each to its own existing mechanism** | `Properties`/`Teleport` bind **immediately** under [UXI-23](UX_Feature_Map_Parity.md)/[UXI-29](UX_Feature_Authority_Aware_Writes.md) — they were never blocked. Only the first two kinds wait for this design |
| **A2** | Treat all nine uniformly as "orders" | forces `Properties` through the cognitive tier, which is absurd, and hides that two of the nine are already solvable |

**Lean: A1.** ⭐ It shrinks the open question from nine ids to **seven**, and unblocks two today.

### ⚠ And five of the nine are never even **emitted**

`Teleport` · `Repair` · `Reinforce` · `Resupply` · `Transfer` live on ExCon's `Admin` / `DamageControl` /
`Logistics` menu strategies (`ContextMenuLogic.cs:182-192`). 🔴 **`SetStrategy` has zero callers** — only
its own definition (`:81`) and the interface declaration (`IContextMenuLogic.cs:28`) — and
`_currentStrategy` defaults to `Standard` (`:40`). ⇒ **only `Properties` is ever emitted** of those six,
and it dead-ends at `GlobalActionDispatchSystem`'s silent no-op like the rest.

⇒ ⚠ **The register's *"43% of the vocabulary is inert"* ([UXI-23 §1](UX_Feature_Map_Parity.md)) understated
it**: five of those ids are not merely unhandled, they are **unreachable UI**. Deciding their fate costs
nothing today because nothing shows them.

## B. Does an operator right-click produce a **mission** or an **intent**?

Stage 6 accepts both; the question is which one a menu click should build.

| | Option | Consequence |
|--:|---|---|
| **B1** | **Direct `AssignTacticalIntentEvent`** — the click publishes an intent, bypassing `MissionPlanQueue` | ✅ one event, immediate, already authority-routed. 🔴 **Invisible to the mission**: `MissionAdapterSystem` will re-publish the phase's own intent on the next phase change and silently override the operator |
| **B2** | 🎯 **A mission mutation** — the click edits the entity's plan | ✅ **matches the user's statement literally**; the order is durable, inspectable and survives a phase change. 🔴 **But the natural verb does not exist**: `CMD_APPEND_TASK` has **no producer and no handler** and returns `NotSupported`. So B2 today means **read-modify-`CMD_REPLACE_MISSION`** — a whole-plan rewrite per order, with the `BaseVersion` conflict window that implies. ⚠ Costs a round-trip, and ⚠ **the 8-phase cap becomes user-visible** |
| **B2′** | B2, **after implementing `CMD_APPEND_TASK`** | ⭐ the enum member, the DDS union case and the neutral DTO all already exist — this is a handler arm, not a protocol change. 🔴 **Architect ruling needed on whether the verb was left unimplemented deliberately** |
| **B3** | Both — intent for "right now", mission edit for "from now on" | ⚠ two vocabularies for one gesture; the operator must understand the difference. Defensible only if the UI names it (*"Move here now"* vs *"add to plan"*) |

**Lean: B2**, because the user's sentence is a statement about the *supported* model, not a preference.
⚠ **But B1 is what `Stop` most naturally is** (`CMD_ABORT_ALL` is a plan verb, so even `Stop` has a B2
form) — 🔴 **this is the central question of the whole design and I do not want to guess it.**

## C. Where does the **order vocabulary** live?

Today an intent id is a bare `string` matched against a per-node `TacticalIntentMapperRegistry`.

| | Option | Consequence |
|--:|---|---|
| **C1** | 🎯 **Reuse `GlobalActionIds` + a declared id → `IntentId` binding** | ✅ the menu already speaks `GlobalActionIds`; ✅ [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)'s `EntityActionDescriptor` is exactly where such a binding belongs. ⚠ Needs one mapper per order |
| **C2** | Make the menu emit intent **strings** directly and drop the int ids for orders | ✅ no binding layer. 🔴 loses `GlobalActionIds`' compile-time safety and splits the menu into two id spaces |
| **C3** | Derive the vocabulary from the **registered mappers** — the menu shows what the node can actually do | ⭐ **self-consistent by construction; an order can never be inert** (the failure [UXI-23 §1](UX_Feature_Map_Parity.md) found). ⚠ Menu content becomes node-dependent, which may surprise |

**Lean: C1 for the binding, plus C3 as the *enablement* rule** — declare centrally, but grey out an order
whose mapper is not registered on the owning node. That is exactly
[UXI-24 §2](UX_Feature_Multi_Select.md)'s `isEnabled` axis, if that reconciliation is accepted.

## D. Authored vs executable — which one is the truth?

Both live on the entity (§0). An unbounded authored plan projects into **8** phases, and a task's
**list** of JSON-parameterised triggers projects into **one enum + one float**.

| | Question | Options |
|--:|---|---|
| **D1** | When they disagree, **which is authoritative**? | (a) the executable queue — the plan is a display artefact · (b) 🎯 **the authored plan** — the queue is a derived cache, re-projected on change |
| **D2** | What happens to the **surplus**? | (a) drop + warn — already the documented cap behaviour (*"excess tasks dropped + Warn"*, guide `:183`) · (b) 🎯 **reject at the boundary** for user-authored plans, so the operator learns *before* committing · (c) widen the ECS side |
| **D3** | Is a **multi-trigger task** meaningful at all? | the authored form allows N triggers; the runtime evaluates one. Is that OR, AND, or *"first wins"* — or is the wire form simply over-modelled? |

⚠ **D2(c) is the one I would not take**: it touches a hot cognitive component whose `[InlineArray]`
carries a documented defensive-copy trap (guide `:105-112`). Demand-driven caution says leave the runtime
alone unless the architect says it is under-specified.

**Lean: D1(b) + D2(b) for user-authored plans**, keeping drop-and-warn for machine-generated ones.
🔴 **D3 is a runtime-semantics ruling, not a UI one, and I will not guess it** — it decides whether the
authoring panel should even offer a second trigger.

## E. Who may issue an order — and does the map need to know?

🔒 **Stage 7's complementary gates already answer this**, and the answer is *"any node"*:
the owner resolves, a non-owner forwards as `TacticalIntentRequest`. ⇒ **CGF, SimHost, Editor, IG and
ExCon are peers on this channel by construction** — which is precisely the user's *"not ExCon-only"*.

| | Question | Lean |
|--:|---|---|
| **E0** | 🔴 **Does CGF host `MissionPanel`?** | 🎯 **It must, per the user's ruling** — and this is the cheapest part of the whole issue: the panel is a shared 815-line component two other hosts already construct, and the Editor's map-selection→panel sync (`EditorSubsystem.cs:1651`) is the pattern to copy. ⚠ **CGF is also the only host where the order resolves with no network hop**, since it holds `BehaviorState` authority |
| **E1** | Does the **Editor** issue orders, given [ruling 22](UX_RESUME_INTERACTION.md) *"Editor owns all"*? | 🎯 **Yes, and it is already wired** — the Editor builds the same mapper registry as CGF (`:863-865`), hosts `MissionPanel`, and its `EditorMissionService` publishes `MissionControlIntent` on the **local bus, no DDS** (`EditorMissionService.cs:145`) |
| **E2** | Does **ReplayBrowser**? | 🎯 **No** — read-only host. The order set is *hidden*, not disabled: a replay cannot accept commands in principle ([UXI-23 §3.3](UX_Feature_Map_Parity.md)) |
| **E3** | Does the map surface need per-node knowledge of who owns the brain? | 🎯 **No.** ⭐ This is the same conclusion [UXI-29](UX_Feature_Authority_Aware_Writes.md) reached for attribute writes: publish the event, let the gates route it |

## F. Parameter capture — an order is not complete at the click

| Order | Needs |
|---|---|
| `MoveHere` | a **world point** — available from the right-click itself |
| `Engage` | a **target entity** — a second pick |
| `Stop` | nothing |
| `Repair` · `Resupply` · `Reinforce` | a **source/amount**, or nothing if the rule is implicit |
| `Transfer` | a **destination unit** — an ORBAT pick, not a map pick |

| | Option | Consequence |
|--:|---|---|
| **F1** | 🎯 **Reuse the existing interactive-pick tool model** — *Mark Target* already does `await _mapPickAdapter.PickEntityAsync()` (`EditorSubsystem.cs:1464`) | ✅ **the pattern exists and ships**; ✅ [UXI-07](UX_Feature_Tool_Model.md)'s modal-tool stack and [ruling 13](UX_RESUME_INTERACTION.md)'s progress/cancel obligations already cover a pending pick |
| **F2** | A parameter dialog per order | ⚠ heavier, and wrong for `MoveHere`, whose parameter *is* the click |

**Lean: F1**, with the JSON payload built by the same code that formats `JsonParams` today.
⚠ **`ExecutingEngine` on `MissionTask` is unexplained** — I could not determine what values it takes or
who reads it. **Flagging rather than guessing.**

## G. Multi-select — an order to twelve entities

[UXI-24 §3.5](UX_Feature_Multi_Select.md) already defines `PerEntity` vs `Selection` fan-out.

| | |
|---|---|
| ⭐ **`AssignTacticalIntentEvent` is per-entity by signature**, so a `PerEntity` fan-out is N events and needs nothing new | consistent with `GlobalActionRequestedEvent` |
| ⚠ **But a mission edit (B2) is not obviously per-entity** — *"order twelve units"* is twelve **whole-plan rewrites** (§B2, since append does not exist), each gated by an optimistic-concurrency check: `if (intent.BaseVersion > 0 && intent.BaseVersion != currentVersion) → VersionConflict` (`MissionControlExecutionSystem.cs:145-149`) | 🔴 **Verified: partial failure is reachable** — 9 commit, 3 NAK on a stale version. [UXI-24 §3.5](UX_Feature_Multi_Select.md)'s *"one ECB, atomic"* rule **cannot** hold across a network round-trip |
| **Question** | is a partially-applied multi-entity order acceptable, with per-entity feedback — or must it be all-or-nothing? |

**Lean: accept partial, report precisely.** All-or-nothing across N independent network commits needs a
distributed transaction, and the cluster already has one (`2PC`, `CreateUpdateDeleteEntityAck`) — 🔴 but
reusing it for orders is a significant claim I will not make without a ruling.

## H. Formation / hierarchy orders — in or out?

`UnitRoster` (**≤16 subordinates**), `JoinFormationExecutor` (*"null while the executor is a stub"*,
`CgfLogicPack.cs:106-107`) and the unregistered `ForceManeuverMapper` suggest a squad-level command layer
that is partly built.

**Lean: out of scope for the first pass** — but 🔴 **`Transfer` and `Reinforce` sit exactly on this line**,
so the architect should say whether they are entity orders or hierarchy edits before either is bound.

---

## Summary — what I need ruled

| # | Question | My lean |
|--:|---|---|
| **A** | Split the nine into orders / sustainment / not-commands? | **A1 — yes**, and bind `Properties` + `Teleport` now. ⚠ Five of the nine are currently **unreachable UI**, so the decision is cheap |
| **B** | 🔴 Does a right-click order produce a **mission mutation** or a **direct intent**? | **B2**, per the user's statement — 🔴 **the load-bearing question**, and it needs **B2′** because `CMD_APPEND_TASK` is unimplemented |
| **C** | Where does the order vocabulary live? | **C1** binding + **C3** enablement |
| **D** | 🔴 Authored plan vs executable queue — which is the truth, and what happens to the surplus? | **D1(b)** authored is truth · **D2(b)** reject user-authored overflow · 🔴 **D3 (multi-trigger semantics) I will not guess** |
| **E** | Who may issue? | the gates already answer it — **any node**. 🎯 **CGF must host `MissionPanel`**, which is a shared component two hosts already construct |
| **F** | Parameter capture | **F1** — reuse the interactive-pick tool |
| **G** | 🔴 Multi-entity orders: partial or atomic? | **partial, precisely reported** — the version-conflict NAK is verified, so partial failure is not hypothetical |
| **H** | Formation / hierarchy orders | **out**, but rule on `Transfer` / `Reinforce` |

⚠ **Unverified, flagged rather than guessed:** `MissionTask.ExecutingEngine`'s value space and consumer;
whether `Repair`/`Resupply`/`Reinforce` have any runtime meaning at all in the current combat/health
model; whether `CMD_APPEND_TASK`/`CMD_INSERT_TASK` were left unimplemented deliberately or abandoned.

⚠ **One premise did not survive checking.** A census reported `CmdAppendPersonalWaypoint` as a
*"fully production-wired, mission-independent order path"*, which would contradict the user's
*"only supported way"*. **Its consumer is wired; its producer is not** — the publisher sits inside
`IgApplication.OnCanvasWorldClick`, which has **zero production callers**
([UXI-31](UX_Issues.md#uxi-31), [Correction 34](UX_Tasks_Detail.md#corrections)). ⇒ **the user's premise
holds**: missions are the only operator-reachable commanding path.
