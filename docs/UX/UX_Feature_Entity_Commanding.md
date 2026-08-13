# Feature design — entity commanding (right-click orders)

> **Design for [UXI-32](UX_Issues.md#uxi-32) · drafted 2026-08-13.** Implements
> [Q29](Architect_Question_29_Entity_Commanding.md), **architect-accepted** by
> [ruling 48](UX_RESUME_INTERACTION.md). Rulings **37-50** are binding here.
> **Status: ✅ designed — no open decisions.**

![the commanding chain](img/uxi32_commanding.svg)

## 0. Prior art ([rule 6](UX_Issues.md#rules) · [rule 6e](UX_RESUME_INTERACTION.md))

🔒 **Index-first pass, per [rule 6e](UX_RESUME_INTERACTION.md).** `search_graph` surfaced two things
repeated grepping had not: a **complete prior design document** for this exact feature, and a commander
BTree node already publishing the event this design routes.

| Exists? | What | Bearing |
|:--:|---|---|
| ✅ | 📐 **`.dev/_DONE/tactical-intent/DESIGN.md`** (328 L) + `design-talk.md` (502 L) — the shipped design of the tactical-intent pipeline | ⭐ **this design must extend it, not restate it.** Its stated goals already include *"reuses `[BehaviorContract]` and `BehaviorUiRegistry` for UI discovery of generic intent DTOs **with no new rendering code**"* |
| ✅ | **`AssignTacticalIntentEvent { Entity, string IntentId, string JsonParams }`** | the choke point — already *"one id + a JSON argument"* |
| ✅ | **Complementary authority gates** — `TacticalIntentResolutionSystem` (owner) / `TacticalIntentEgressTranslator` (→ `TacticalIntentRequest`) | any node may issue; routing is free |
| ✅ | **Pass-through fallback** — an `IntentId` with no mapper is treated as a **direct behavior name** | ⭐ **most orders need no mapper at all** |
| ✅ | **11 `[BehaviorContract]` param DTOs** — the real per-category vocabulary | §1 |
| ✅ | `BehaviorCatalog.GetValidBehaviors(long tkbType)` — per-TKB-type behavior list by reflection; **already used by ExCon and the Editor** | the fallback source (§4.3) |
| ✅ | `BehaviorSchemaDiscovery.AutoRegister` → `BehaviorUiRegistry` — schema-driven parameter forms, already wired into `MissionPanel` | [ruling 42](UX_RESUME_INTERACTION.md)'s deferred work is **not greenfield** |
| ✅ | **`GizmoInteractionBatch`** carries `int ActionId` **and** `[DdsManaged] string? PayloadJson` | ⭐ **seam-law 22** — the argument channel exists and is left null for `MenuAction` |
| ✅ | `ITkbStorageStrategy` / `ITkbDatabase` — the 5-level TKB stack | ⭐ **seam-law 23** — §4 composes it |
| 🔴 | `ITkbHotReloadEvents` — cache-invalidation contract, **subscriber but no publisher** | ⭐ **seam-law 24** — §4.4 |
| 🔴 | **`CommanderNodes.Action_IssueTacticalIntent`** (`:66-90`) — publishes the event to a subordinate with a **hardcoded `"DefendArea"`** and `// TODO: resolve IntentId from a registered intent-type lookup table` | ⭐ **the same lookup this design builds** — §2.3 serves both |

## 1. ⭐ The three orders that are actually on screen already have behaviors

`MoveHere · Engage · Stop` are the only three of the nine ids a user can reach today
([Q29 §A](Architect_Question_29_Entity_Commanding.md) — the other six sit behind ExCon menu strategies
whose `SetStrategy` has zero callers). **All three map onto registered production behaviors:**

| Menu id | Behavior | Registered | Params DTO |
|---|---|---|---|
| `MoveHere` | **`MoveToLocation`** | `CgfCuratedBehaviorRegistrar.cs:57` + resolver `:124` | `MoveToLocationParamsJsonDto` (AllMilitary \| Civilian) |
| `Engage` | **`FireAtTarget`** | `:92` + resolver `:127` | `FireAtTargetParamsJsonDto` (AllMilitary) |
| `Stop` | **`Idle`** | `:110` | `IdleParamsJsonDto` (AllMilitary) |

⇒ 🔒 **[Ruling 39](UX_RESUME_INTERACTION.md)'s placeholder behaviors are needed only for
`Repair · Reinforce · Resupply · Transfer`** — and those are **not on screen**, so they can be deferred
without any visible gap. **The shippable slice is the three that already work.**

### The full declared vocabulary — 11 contracts, not 2 mappers

⚠ **Q29 said *"the whole production order vocabulary is two mappers"*.** That is true of
`ITacticalOrderMapper`, and **understates the vocabulary**: mappers are only needed for *translation*.

| Behavior | Category |
|---|---|
| `MoveToLocation` | AllMilitary + **Civilian** |
| `FireAtTarget` · `FollowRoute` · `Idle` · `DefendArea` | AllMilitary |
| `ConvoyEscort` · `WanderMilitary` | MilitaryApc |
| `InfantryCombat` · `JoinFormation` | Infantry |
| `Ambush` | Insurgent |
| **`PlatoonHillAttack`** | **Commander** |

⭐ **`BehaviorCategory.Commander` already exists and already has a member** — [ruling 45](UX_RESUME_INTERACTION.md)'s
formation story is real, not aspirational.

## 2. The design

### 2.1 🔒 One action id, one JSON argument

Per [ruling 41](UX_RESUME_INTERACTION.md) — **no `GlobalActionIds` value per behavior.**

```jsonc
{ "id": 300, "label": "Move Here",
  "args": "{\"intent\":\"MoveToLocation\",\"params\":{\"speed\":\"normal\"}}" }
```

| | |
|---|---|
| 🔒 **`GlobalActionIds.IssueTacticalIntent = 300`** — one new constant, forever |
| ✅ The handler is *deserialize → publish `AssignTacticalIntentEvent { IntentId, JsonParams }`* | the destination shape needs no adaptation |
| ✅ `ITacticalOrderMapper.TryMap(self, repo, jsonParams, out …)` takes the payload **verbatim** | end-to-end once the middle is filled |

### 2.2 Filling the argument channel — 8 hops, 1 already done

| # | Hop | Change |
|--:|---|---|
| 1 | `ContextMenuItemDto` | **+ `args`** (`[JsonPropertyName("args")]`, null-ignored — compact by default) |
| 2 | 🔴 `ImGuiMenuRenderer:83,89` — holds the DTO, calls `onAction?.Invoke(item.Id)` | widen the callback to carry the item's `args` |
| 3 | `GizmoMenuActionEvent` | **+ `ArgsJson`** |
| 4 | ✅ **`GizmoInteractionBatch.PayloadJson`** | ⭐ **already exists** — `WriteMenuAction` just stops leaving it null |
| 5 | `ContextActionTriggered` | **+ `ArgsJson`** (already a managed class with a string) |
| 6 | `ContextActionInvokedDto` | **+ `ArgsJson`** — the ExCon DTO |
| 7 | 🔴 `GlobalActionRequestedEvent` — **blittable, `Pack = 1`** | ⚠ **cannot gain a string.** Add a **managed sibling** `GlobalActionRequestedManagedEvent { ActionId, Target, ArgsJson }`; the blittable path stays for parameterless actions |
| 8 | `GlobalActionHandler(view, target)` | an **args-carrying overload**; existing handlers unchanged |

⚠ **`JsonEntityContextMenuHandler.AddElement` is a second, hand-rolled parser** of the same JSON
(`id`/`label`/`enabled`/`separator`/`children`). **It must learn `args` too**, or inspector-issued orders
silently lose their parameters.

### 2.3 🔒 The intent lookup — one table, two consumers

[Ruling 41](UX_RESUME_INTERACTION.md)'s payload carries an **intent *name***, so the ordinal→name table
the commander node asks for is the same table:

| Consumer | Today | After |
|---|---|---|
| **Menu handler** | — | reads `intent` from the payload |
| 🔴 **`CommanderNodes.Action_IssueTacticalIntent`** | `const string intentId = "DefendArea";` + `// TODO: resolve IntentId … keyed by p.IntentTypeOrdinal` | resolves through the same registry |

⇒ ⭐ **Fixing the menu fixes the commander node's TODO** — one lookup, not two.

### 2.4 🔒 Menu content comes from TKB, with `BehaviorCatalog` as the fallback

Per [rulings 43 and 46](UX_RESUME_INTERACTION.md).

```csharp
[TkbDescriptor("AI.CommandSet")]
public record CommandSetDto
{
    public List<CommandEntryDto> Commands { get; init; } = new();
}
public record CommandEntryDto
{
    public string IntentId    { get; init; } = "";   // matches a mapper's TargetIntentId, or a behavior name
    public string Label       { get; init; } = "";
    public string DefaultParams { get; init; } = ""; // JSON, may be empty
}
```

| | |
|---|---|
| ✅ **Backward-compatible by construction** — unknown keys are skipped, missing fields fall to defaults (`FdpJsonOptionsRegistry.DefaultRelaxed`, no `UnmappedMemberHandling`) |
| ✅ **Read at L3+L4** — `TryGetByType(tkbType, out template)` → `template.GetDescriptor<CommandSetDto>()`. ⭐ **No ECS translator, so ExCon works with no ECS world** |
| 🔒 **Fallback**: no descriptor ⇒ `BehaviorCatalog.GetValidBehaviors(tkbType)`, which ExCon and the Editor already call |
| 🔒 **Absent, never greyed** ([rulings 47, 49](UX_RESUME_INTERACTION.md)) — an order this entity or host cannot take **is not in the menu** |

### 2.5 🔒 Issue = publish, and nothing else

```csharp
bus.PublishManaged(new AssignTacticalIntentEvent {
    Entity = target, IntentId = entry.IntentId, JsonParams = mergedParams });
```

| | |
|---|---|
| 🔒 **Never write `BehaviorState`** — `BehaviorIngressSystem` is its sole writer |
| 🔒 **Never touch `MissionPlanQueue`** — [ruling 38](UX_RESUME_INTERACTION.md): a right-click is an *immediate* order, not a plan edit |
| ✅ **Routing is free** — owner resolves, non-owner forwards. **CGF resolves locally** (it holds `BehaviorState`); everyone else forwards |
| ⚠ **Two-frame latency**, documented and accepted by the prior design |

### 2.5b 🔒 The order cancels the mission plan — as **one** operation

> **User, 2026-08-13:** *"immediate order should cancel the mission plan first."*

⇒ [Ruling 50](UX_RESUME_INTERACTION.md). This removes the transient-order risk entirely: the plan cannot
resume control because it no longer exists.

🔴 **But it must not be built as *abort, then order*.** Verified ordering hazard:

| `BehaviorIngressSystem.Execute()` | reads |
|---|---|
| `:53` | `AssignBehaviorEvent` ← the order |
| `:172` | **`ClearBehaviorEvent`** ← `CMD_ABORT_ALL` |

⇒ **the clear runs LAST within a frame.** An abort and an order arriving together would leave the entity
**brain-dead** (`ActiveBehaviorHash = None`) with the operator's order **silently discarded**. It happens
to work today only because the abort is **1 hop** and the intent is **2** — an accident of hop counts, not
a guarantee.

🔒 **So: one operation, not two.**

```csharp
public sealed class AssignTacticalIntentEvent {
    public Entity Entity;
    public string IntentId   = "";
    public string JsonParams = "";
    public bool   CancelsMission;      // ⬅ new
}
```

`TacticalIntentResolutionSystem` — already the owner-side choke point, already authority-gated — does
both in one place when the flag is set:

| | |
|---|---|
| 1 | `MissionPlanQueue` → `PhaseCount = 0, CurrentPhase = 0, PhaseElapsedSeconds = 0` (the exact `CMD_ABORT_ALL` shape, `MissionControlExecutionSystem.cs:222-228`) |
| 2 | `ActiveMissionPlan` → null, and `SmartEgressUtil.MarkDirty` so the change replicates |
| 3 | publish the `AssignBehaviorEvent` **as usual** |
| 🔒 | **No `ClearBehaviorEvent` is published** ⇒ the `:53`/`:172` hazard cannot arise |

| | |
|---|---|
| ✅ **`MissionAdapterSystem` will not fight it** — it fires on *phase change*, and `PhaseCount = 0` means there are no phases |
| ⚠ **Non-owning nodes need nothing extra** — the flag rides the event, and `TacticalIntentRequest` carries it to the owner, where the same one-operation path runs |
| 🔴 **The plan is DISCARDED, not paused** — there is no resume. Restoring it means re-committing from `MissionPanel` or reloading the scenario. ⚠ **This is the consequence to surface in the UI**, and it is a stronger warning than the transient-order one it replaces: ordering one unit **destroys its authored plan** |

### 2.6 Parameters — defaults from TKB, the rest from the click

| Source | Example |
|---|---|
| **TKB `DefaultParams`** | `MoveToLocation` speed profile |
| **The click itself** | `MoveHere` → world point |
| **A follow-up pick** ([UXI-07](UX_Feature_Tool_Model.md)'s modal tool, as *Mark Target* already does) | `Engage` → target entity |

🔒 **Merge order: TKB defaults ← click-time values.** The merged JSON is what travels in `args`.

## 3. Acceptance

| # | Case | Cls |
|---|---|:--:|
| 32.1 | A menu item's `args` survives **menu → click → handler** locally | H |
| 32.2 | 🔒 …and over **DDS** — an IG terminal click delivers `args` via `GizmoInteractionBatch.PayloadJson` | H |
| 32.3 | A menu item with **no** `args` behaves exactly as today — back-compat guard | H |
| 32.4 | 🔒 **One** `GlobalActionIds` value serves **all** orders; adding an order adds **no** id | H |
| 32.5 | The **blittable** `GlobalActionRequestedEvent` path is unchanged for parameterless actions | H |
| 32.6 | `JsonEntityContextMenuHandler` passes `args` through — the second-parser guard | H |
| 32.7 | Issuing an order publishes **`AssignTacticalIntentEvent`** and **writes no ECS component** | H |
| 32.8 | 🔒 An order with `CancelsMission` **empties `MissionPlanQueue`** (`PhaseCount = 0`) and **nulls `ActiveMissionPlan`** | H |
| 32.8b | 🔒 **No `ClearBehaviorEvent` is published** by that path — the `:53`/`:172` ordering guard | H |
| 32.8c | 🔒 After the order, `MissionAdapterSystem` publishes **nothing** — the plan cannot resume | H |
| 32.8d | The cancel + assign are **atomic from the operator's view**: the entity is never left brain-dead | H |
| 32.9 | On the **owning** node the intent resolves locally; on a **non-owner** it leaves as `TacticalIntentRequest` — one publish, two routings | H |
| 32.10 | An order **replaces** the active behavior (`BrainBTreeState`/HSM reset) | H |
| 32.11 | `MoveHere → MoveToLocation`, `Engage → FireAtTarget`, `Stop → Idle` reach their **registered** behaviors | H |
| 32.12 | An `IntentId` with **no mapper** resolves by **pass-through** to the behavior of that name | H |
| 32.13 | Menu content for an entity comes from its **TKB `AI.CommandSet`** | H |
| 32.14 | 🔒 No descriptor ⇒ **`BehaviorCatalog`** fallback, not an empty menu | H |
| 32.15 | 🔒 An order the entity cannot take is **absent** — not greyed, no reason ([rulings 47, 49](UX_RESUME_INTERACTION.md)) | H |
| 32.16 | 🔒 A **multi-selection** shows only orders valid for **every** selected entity | H |
| 32.17 | A fan-out over 12 entities publishes **12** intents; mixed ownership routes each correctly | H |
| 32.18 | TKB defaults merge with click-time params, **click wins** | H |
| 32.19 | 🔒 **`CommanderNodes.Action_IssueTacticalIntent` resolves its `IntentId` from the registry**, not the hardcoded constant | H |
| 32.20 | A **commander** entity's menu offers `BehaviorCategory.Commander` orders; a rifleman's does not | H |
| 32.21 | 🔒 TKB loads **from disk**; a missing/unreadable source falls back to the hardcoded catalog **and logs which won** ([ruling 46](UX_RESUME_INTERACTION.md)) | H |
| 32.22 | 🔒 A TKB reload **publishes `TkbDescriptorChangedEvent`** and subscribed caches invalidate | H |
| 32.23 | 🔒 **ExCon builds the same menu with no ECS world** — L3 + L4 only, no translator | H |
| 32.24 | SimHost's strict cluster-transition load still **fails loudly** on a missing staged TKB | H |
| 32.25 | **Map**: right-click a unit in CGF → order it → the unit visibly changes behavior | I |
| 32.26 | **IG terminal**: the same order issued remotely reaches the CGF brain | I |
| 32.27 | Mixed selection (tank + civilian) → only universally-valid orders appear | I |

**27 H · 3 I · 0 V.**

## 4. Build order

| # | Step | Why first |
|--:|---|---|
| 1 | **TKB loader**: shared disk-first + fallback + `TkbPostLoad`, called from `HrotEnvironment.CreateTkb()`; publish `TkbDescriptorChangedEvent` | everything downstream reads TKB; ⭐ one change reaches four hosts, and ExCon gains a `TkbDatabase` |
| 2 | **`AI.CommandSet` descriptor** + `BehaviorCatalog` fallback | the menu's data source |
| 3 | **Argument channel** — the 8 hops (§2.2) | the mechanism, independently testable with an existing action |
| 4 | **`IssueTacticalIntent` handler** + intent registry | joins 2 and 3 |
| 5 | **Bind `MoveHere`/`Engage`/`Stop`**; retire their menu-JSON constants | the visible payoff, no new behaviors needed |
| 6 | `CommanderNodes` TODO → registry | free once 4 lands |
| 7 | Placeholder behaviors for `Repair`/`Reinforce`/`Resupply`/`Transfer` | ⚠ **only if they are ever put on screen** — they are not today |

## 5. 🔒 Out of scope

| | |
|---|---|
| **Menu generated from all supported behaviors** | 🔒 [ruling 42](UX_RESUME_INTERACTION.md) — needs the parameter-authoring design; this slice ships **TKB-declared** commands with **default** params |
| **`MissionPanel` UX redesign** | [UXI-33](UX_Issues.md#uxi-33) — pair it with the above; both need *"how does an operator supply behavior parameters?"* |
| **Mission editing / plan mutation** | works today; whole-plan replace is accepted ([ruling 40](UX_RESUME_INTERACTION.md)) |
| **`CMD_APPEND_TASK` / `CMD_INSERT_TASK`** | dead verbs; ⚠ **nothing here needs them** |
| **TKB over the network** | 🔒 [ruling 46](UX_RESUME_INTERACTION.md) — later, and the L1 seam keeps it a local decision |
| **What `Repair`/`Resupply`/`Reinforce` mean tactically** | ⚠ unverified whether they have any meaning in the current combat/health model |
| Multi-select mechanics | [UXI-24](UX_Feature_Multi_Select.md) — **prerequisite** for 32.16-32.17 |

## 6. Risks

| | |
|---|---|
| 🔴 **Order** | UXI-24 (fan-out) and [UXI-23](UX_Feature_Map_Parity.md) (one dispatch path) precede binding. ⚠ **Step 1 before everything** — a menu that reads TKB before TKB loads shows the fallback and looks correct |
| ⚠ **Touching `GizmoMap.Presentation` touches the IG production terminal** | [ruling 20](UX_RESUME_INTERACTION.md); the change is additive (a previously-null field) and 32.3 is the guard |
| ⚠ **`PayloadJson` becomes dual-purpose** — `StructUpdate` **and** `MenuAction` | disambiguated by `Kind`, which the reader already switches on. **Document it beside the bit7/bit0 comment** |
| 🔴 **An order DESTROYS the entity's authored plan** | §2.5b, [ruling 50](UX_RESUME_INTERACTION.md). Replaces the milder transient-order risk with a sharper one: there is **no resume**. **This is the one UX risk that is not a code risk** — it needs a visible cue, and arguably a confirmation when the entity has a non-empty plan |
| ⚠ **`BehaviorId`s are stable forever and the registry must be complete before frame 1** | ⇒ allocate ids deliberately even for placeholders (guide `:469-471`) |
| ⚠ **ExCon has no `ITkbDatabase` today** | step 1 gives it one; until then 32.23 fails and its menu falls back |
