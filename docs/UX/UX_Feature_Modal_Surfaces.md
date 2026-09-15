<!--STATUS
state: LIVE
build-state: NOT-BUILT
verified: 2026-08-28 (coordinator source scan)
current-answer: NOT-BUILT (design only). No ModalManager/ConfirmationSpec/IProgressSink in source.
-->
# Feature design — modal surfaces: confirmation + activity progress

> **Design for [UXI-16](UX_Issues.md#uxi-16) and [UXI-27](UX_Issues.md#uxi-27) · drafted 2026-08-13.**
> Paired at the user's direction — they share one vehicle, and designing it twice is the duplication this
> programme keeps finding. Implements [rulings 13, 29](UX_RESUME_INTERACTION.md) and
> [API §6c](UX_Interaction_API.md#6c-progress-reporting--part-of-the-design-per-the-ruling).
> **Status: ❌ NOT-BUILT (design only) — no `ModalManager`/`ConfirmationSpec`/`IProgressSink` in source.**

## 0. Prior art ([rule 6](UX_Issues.md#rules) · [rule 6e](UX_RESUME_INTERACTION.md))

🔒 **Index-first pass.** `search_graph` on `.*(Modal|Dialog|Confirm|StatusBar|Progress|Toast).*` surfaced
the decisive asymmetry immediately.

| Exists? | What | Adoption |
|:--:|---|---|
| ✅ | **`StatusBarManager`** (145 L) — a **delegate registry**: `RegisterSection(id, sortOrder, renderDelegate, perspective?)`, sorted render loop, duplicate-id replace, perspective filter, `Height` property. Unit-tested (203 L) | ⭐ **6 production registrations** — ClusterRunner ×2, CGF, SimHost, Editor ×2 |
| ✅ | `MessageLogStatusBarSection` · `ClusterTimeControlStatusBarSection` · `TimeControlStatusBarSection` | three working sections to copy |
| 🔴 | **No modal registry of any kind** | **28 `BeginPopupModal` calls across 19 production files** |
| 🔴 | `WindowManager` itself hand-rolls the idiom **twice** (About, Settings) — `:504-533` | the shell that *owns* `StatusBarManager` did not get the same treatment for modals |
| 🔴 | Four Blueprint modals — `ItemRenameModal`, `VariableCreateModal`, `FunctionCreateModal`, `CustomEventCreateModal` — **with no shared base class** | each re-implements open-request → `OpenPopup` → `BeginPopupModal` → Escape → close |
| ✅ | `IProgressSink { Report(float? fraction, string? message) }` — **already specified** by [API §6c](UX_Interaction_API.md) | 🔴 no implementation, no surface |

⇒ ⭐ **Seam-law instance 25, in an unusually clear form: the same shell grew a registry for the status bar
and never grew one for modals.** UXI-27's status-bar half is a **registration**; the modal half is the
only real mechanism to build — and it serves both issues.

## 1. What each issue actually needs

| | [UXI-16](UX_Issues.md#uxi-16) — confirmation | [UXI-27](UX_Issues.md#uxi-27) — activity progress |
|---|---|---|
| Trigger | a destructive action | a long-running action |
| Surface | **modal** | 🔒 **modal if `Exclusive`, status bar otherwise** ([ruling 13](UX_RESUME_INTERACTION.md)) |
| Needs | ✅ a modal host | ✅ the same modal host **+** a status-bar section |
| Exists today | ❌ nothing | ⚠ **half** — the status bar registry, with nothing registered |

⇒ 🔒 **One `ModalManager`, two consumers.** Build it once, mirroring `StatusBarManager` line for line.

## 2. The design

### 2.0 🔴 Who is allowed to ask — the layering that everything else depends on

> **User, 2026-08-13:** *"Confirmations are allowed for interactive sessions only. The infrastructure must
> by default allow for headless operations. Requests coming from an interactive session should be marked
> as such and need to be resolved on the interactive node where the user is sitting **before** the
> headless and unconfirmable request is sent to the infrastructure. Note we are building a distributed
> system — ExCon might live on a different node than SimHost and CGF who actually perform the
> operations."*

⇒ [Ruling 53](UX_RESUME_INTERACTION.md). ⚠ **An earlier draft of this design put the gate on the
executing side** — *"`Destructive` ⇒ confirmation, by construction"*, evaluated where the handler runs.
**In a cluster that is wrong twice over** ([Correction 41](UX_Tasks_Detail.md#corrections)): it would try
to raise a modal on **SimHost or CGF**, where no operator is sitting, and it would **block every
MCP-driven operation** on a dialog nobody can answer.

🔒 **The gate belongs to the dispatcher, not the handler.**

```
interactive node (ExCon / Editor / IG operator)        executing node (SimHost / CGF)
──────────────────────────────────────────────         ── applies; §2.0c decides ─────
 intent to act
   → descriptor says Destructive
   → confirmation modal          ← the ONLY place a human is asked
   → confirmed
   → emit request  ──────────DDS──────────▶  apply unconditionally
                                             (never asks · never blocks)
```

| | |
|---|---|
| 🔒 **What crosses the wire is already authorized** | a receiver has no confirmation code path at all — not a suppressed one |
| 🔒 **Headless is the default, not a mode** | MCP, scripts, replay and tests dispatch through a context with **no confirmation host**, so nothing can prompt. Unconfirmable **by construction** |
| ⭐ **This is why the marker matters** | the *same* descriptor is dispatched from a UI and from MCP. The **dispatch context** — not the action, not the request — decides whether a human is asked |

```csharp
public interface IActionDispatchContext
{
    bool                IsInteractive { get; }   // false ⇒ headless: never prompt, never block
    IConfirmationHost?  Confirmation  { get; }   // null when headless
    IProgressSink       Progress      { get; }   // log sink when headless
}
```

| Dispatcher | `IsInteractive` | Confirmation | Progress |
|---|:--:|---|---|
| ExCon / Editor / IG operator UI | ✅ | `ModalManager` | modal or status bar (§2.3) |
| **MCP / Debug API** (`DebugApiHost`, ~49 tools on `feat/ai-debug-api`) | ❌ | **none** — proceeds | **log sink** |
| Scenario script · replay · tests | ❌ | none | log sink |

| | |
|---|---|
| 🔒 **Headless proceeds; it does not refuse** | *"the infrastructure must by default allow for headless operations"*. ⚠ A stricter policy (refuse destructive ops without an explicit `--force`) is **deliberately not built** — note it as a future option, not a default |
| 🔒 **But it is recorded** | a headless destructive dispatch **logs what it would have asked**, at the origin. That is the audit trail replacing the dialog |
| 🔴 **`Destructive` therefore declares a *requirement*, not a behaviour** | the interactive dispatcher honours it with a modal; the headless dispatcher honours it with a log line. **Neither the descriptor nor the handler knows which** |
| ⚠ **This generalises past deletes** | [ruling 53](UX_RESUME_INTERACTION.md) names *"`ClearsPriorIntent`… and so deletes and others"* — every confirmation in the programme routes through this gate, including [UXI-32](UX_Feature_Entity_Commanding.md)'s plan-destroying order |

⚠ **Verified gap:** requests carry a `RequestId` (Guid) but **no origin or interactivity marker**
(`Hrot.Core/Network/Commands.cs`). ⭐ **None is needed on the wire** — the resolution happens before
emission, so the marker is a property of the *local dispatch path*, not of the message. **Do not add a
field to the DDS contracts for this.**

### 2.0b 🔒 How the origin knows **what** to ask — it does not. The knowing node tells it.

> **User, 2026-08-13:** *"ExCon can see the mission state, but it should not need to be able to in order
> to handle the confirmations. **ExCon needs to be dumb in this aspect.** The checks need to run on the
> node which 'knows'. The confirmation type etc. needs to come from there — ExCon is just the one who
> shows it to the user."*

⇒ [Ruling 55](UX_RESUME_INTERACTION.md). ⚠ **An earlier draft mirrored the clear providers with
origin-side *describers* reading replicated state** — which made the origin smart and carried three
residual constraints. **Replaced** ([Correction 42](UX_Tasks_Detail.md#corrections)); all three vanish.

```
interactive node (dumb)                     CGF — the knowing node (§2.0c)
───────────────────────                     ──────────────────────────────
 user picks a destructive action
   → Preflight(action, target) ──DDS──▶   run aspect providers over LIVE state
                                            build ConfirmationSpec, or null
   ◀────────── ConfirmationSpec? ──────
   → null ⇒ dispatch straight through
   → else render it VERBATIM, ask the human
   → confirmed → emit request ──DDS──▶    apply unconditionally
```

#### One registry, on one node

🔒 [Ruling 52](UX_RESUME_INTERACTION.md)'s provider gains a second capability instead of acquiring a
mirror image:

```csharp
public interface IIntentAspectProvider          // registered ONLY where the feature runs
{
    string       AspectId { get; }
    ImpactNote?  DescribeImpact(Entity e, ISimulationView view);  // null ⇒ nothing to warn about
    void         ClearFor(Entity e, EntityRepository repo);       // ruling 52
}
```

| ⇒ every constraint [ruling 54](UX_RESUME_INTERACTION.md) had to accept | now |
|---|---|
| warnings could be **stale** (replicated state) | ✅ **gone** — computed on the authoritative node at the moment of asking |
| an aspect whose state is **not replicated** could not warn | ✅ **gone** — the origin never reads aspect state at all |
| **cross-node id drift** was unverifiable | ✅ **gone** — describe and clear are the same object, on the same node |

#### The transport already exists

⭐ **`ICommandGateway`** is already a *neutral* async request/response seam from ExCon/Editor to CGF —
`Task<int> CreateEntityAsync`, `Task<MissionCommitResult> SendMissionControlRequestAsync`. Pre-flight is
**one more method on it**, not a new mechanism:

```csharp
Task<ConfirmationSpec?> PreflightAsync(ActionPreflightCommand cmd, CancellationToken ct = default);

public sealed record ConfirmationSpec(          // neutral DTO, beside the other commands
    string Title, IReadOnlyList<string> Lines, int Severity,
    IReadOnlyList<ConfirmButtonSpec> Buttons);
```

| | |
|---|---|
| 🔒 **The origin renders it verbatim and names nothing** | no aspect vocabulary, no mission vocabulary, no branching on content |
| ✅ **`null` ⇒ no dialog** — the *"should not always ask"* answer, decided by the node that can actually tell |
| ⭐ **The Editor is not a special case** | it owns everything in-process, so the same call resolves locally with no network hop. **One code path, not two** |
| 🔒 **Headless never pre-flights** | MCP/script/replay dispatch the authorized request directly ([ruling 53](UX_RESUME_INTERACTION.md)). ⚠ The origin still **logs** what it skipped |
| ⚠ **Latency is hidden** | only actions that declare they may need confirmation pre-flight, and the round trip happens exactly when a dialog is about to appear. **Non-destructive right-clicks never pay it** |

### 2.0c 🔒 The knowing node is **CGF** — one endpoint, not "whoever executes"

> **User, 2026-08-13:** *"the central node that decides on whether/what to confirm is the CGF subsystem."*

⇒ [Ruling 56](UX_RESUME_INTERACTION.md). ⚠ **§2.0's diagram said *"executing node (SimHost / CGF)"***, which
reads as though pre-flight could land on either. **It cannot.** 🔒 **The applying node and the deciding node
are different questions** — a gizmo write may apply on SimHost, but *what to warn about* is CGF knowledge.

| Verified | |
|---|---|
| ⭐ **The transport already says so** | `ICommandGateway.cs:6` — *"Neutral interface for mission-control commands from **ExCon/Editor to CGF**"*. **All four existing methods target CGF**, so `PreflightAsync` adds a method to a seam that already has exactly one peer |
| ⭐ **The whole intent stack is one pack** | `CgfLogicPack.cs:123-127,149-154` composes `MissionControlExecutionSystem`, `MissionAdapterSystem`, `TacticalIntentResolutionSystem`, `MissionControlModule`, `CognitiveRuntimeModule` ⇒ `MissionPlanQueue`, `ActiveMissionPlan` and `BehaviorState` are **all CGF-side** |
| ✅ **Two production composition roots** | `CgfSubsystem.cs:324` and `EditorSubsystem.cs:866`. SimHost builds the pack **only in test harnesses** (`SimHostInstance.cs:294`, `CgfLogicPackTests`) |
| ✅ **Delete is CGF-served too** | `Hrot.Network.NED/CGF/NedCgfEntityLifecycleAdapters.cs:91-98` consumes `DeleteEntityRequest` ⇒ the **other** confirmable action ([§2.2](#22-confirmation--uxi-16)) resolves against the same node |

| ⇒ what this settles | |
|---|---|
| 🔒 **One pre-flight endpoint** | no routing table, no "which node owns this aspect" lookup, no fan-out/merge of several `ConfirmationSpec`s |
| 🔒 **`IIntentAspectProvider` is registered on CGF only** | it is not a cross-host interface. A host without CGF registers nothing and pre-flights nothing |
| ⭐ **The Editor stays one code path** | it composes `CgfLogicPack` in-process, so the same call resolves locally with **no network hop** — confirmed by the two roots above |
| ⚠ **The one thing to watch** | an aspect owned by **neither** CGF nor the origin (a future SimHost-only or ExCon-only warning) has no home under this rule. **Do not pre-emptively generalise** — when such an aspect appears, it either registers on CGF or the rule gets revisited then |

#### ⚠ One decision this forces: what if pre-flight fails or times out?

| | Option | |
|--:|---|---|
| **a** | proceed silently | 🔴 no — a destructive act would lose its guard precisely when the cluster is unhealthy |
| **b** | refuse | ⚠ blocks legitimate work over a transport hiccup |
| **c** | 🎯 **ask a generic confirmation** — *"Cannot verify the impact of this action (no response from the owning node). Proceed?"* | ✅ honest, and we were already about to interrupt the user |

🔒 **(c) interactively, proceed-and-log headlessly.** ⚠ **Fails toward asking, never toward silence.**

### 2.0d 🔒 How pre-flight **travels** — bus event + translator, never a network call in the handler

> **User, 2026-08-13:** *"on CGF the pre-flight network request should be translated to a local FDP bus
> event, this one handled by the dispatcher, response sent back as a local FDP bus event, this translated
> to a network response and sent to the origin. Pre-flight query sent on a networkless Editor goes via FDP
> bus event only. On origin nodes, where the FDP event bus and translator infrastructure exist, this is
> similar… **this is a proven concept widely used.**"*

⇒ [Ruling 57](UX_RESUME_INTERACTION.md). ⚠ **§2.0b showed `PreflightAsync` on `ICommandGateway` as though
that were the transport** ([Correction 43](UX_Tasks_Detail.md#corrections)). It is a **caller-facing
façade**; the transport is four hops through the bus.

**The template is verified, not proposed** — mission control already does exactly this:

| Hop | Proven at |
|---|---|
| DDS → **bus** | `MissionControlIngressTranslator.cs:77-83` — `repo.Bus.PublishManaged(MissionControlIntent)`. Doc: *"the only class that reads DDS wire messages for mission control"* |
| bus → **handler on the tick thread** | `MissionControlExecutionSystem` |
| handler → **bus** | publishes `MissionControlAckEvent` — *"without any DDS dependency"* |
| bus → **DDS** | `MissionControlAckEgressTranslator.cs:53` — `view.ReadEvents<MissionControlAckEvent>()` → writes the ack |
| correlation | `RequestId` (Guid) on both messages; origin side correlates via `DdsCommandClient<Req,Ack>` (`NedCommandGateway.cs:60-75`) |

⭐ **Second instance, and it is already two-phase**: entity lifecycle — `DeleteEntityRequestSystem.cs:70-74`
drains a neutral `IEntityDeletionRequestSource`, answers through `IEntityAckSink`, emits **`InProgress`
immediately**, then registers with `EntityRequestFinalizationSystem` for the final ack. All
`[UpdateInPhase(SystemPhase.Input)]`.

```
ORIGIN (ExCon / IG / Editor)                    CGF
────────────────────────────                    ───
 UI publishes PreflightRequested ──▶ egress xlat ──DDS──▶ ingress xlat
                                                            │ publishes PreflightIntent
                                                   tick ▼   ▼
                                            dispatcher system runs IIntentAspectProvider
                                                            │ over LIVE ECS state
 UI ◀── bus event ◀── ingress xlat ◀──DDS── egress xlat ◀────┘ publishes PreflightResultEvent
```

| | |
|---|---|
| 🔴 **The hard reason, not just consistency** | the providers read **live ECS state**, and only the tick thread may. A DDS callback resolving pre-flight inline is exactly the **async-handler-touching-ECS** violation the engine rules forbid — the standing *"no two async handlers write to ECS; there are command buffers for that"* constraint |
| ⭐ **The Editor drops the two outer hops** | networkless ⇒ **no translator is registered**, the request and result events never leave the bus, and the **dispatcher system is byte-identical**. This is what makes [§2.0b](#20b)'s *"one code path, not two"* literally true rather than aspirational |
| ✅ **ExCon qualifies as a bus origin** | `ExConSubsystem.cs:182,186` constructs an `FdpEventBus` (plus an observer bus). ⚠ [Ruling 16](UX_RESUME_INTERACTION.md)'s *"ExCon is DDS-only, no ECS"* is about **components**, not the bus — do not read it as excluding ExCon from this pattern |
| ⚠ **The façade may stay awaitable** | [ruling 15](UX_RESUME_INTERACTION.md) already has handlers on the tick thread that **yield**, so a `Task`-shaped `PreflightAsync` over a bus round trip is consistent. 🔒 **But it must be a bus-level correlation helper, not `DdsCommandClient`** — otherwise the Editor path drags in DDS |
| ⚠ **Seam-law watch — do not copy a third time** | the request→bus→system→bus→ack chain is **hand-rolled per command family**: mission control and entity lifecycle each declare their own source/sink/events/translators. ⚠ *I verified two families; I did not enumerate all of them.* Pre-flight would be the **third** ⇒ prefer generalising the pair over adding a third copy |

### 2.1 🔒 `ModalManager` — mirror `StatusBarManager`, do not invent

Same file neighbourhood (`Fdp.Presentation/ImGui/WindowManager/`), same registry idiom, same test shape.

```csharp
public sealed class ModalManager
{
    // Queued, not immediate: ImGui popups must be opened outside BeginMainMenuBar
    // — the constraint WindowManager already documents at :504.
    public ModalHandle Show(ModalRequest request);
    public void        Render();       // called once per frame by WindowManager, like StatusBar
    public bool        IsAnyOpen { get; }
}

public sealed record ModalRequest(
    string            Title,
    string            Message,
    IReadOnlyList<ModalButton> Buttons,      // ordered; exactly one IsDefault, one IsCancel
    Action<string>?   OnResult      = null,  // the clicked button's id
    Func<float?>?     Progress      = null,  // non-null ⇒ render a progress bar
    Action?           OnCancel      = null); // non-null ⇒ Escape and window-close both cancel
```

| | |
|---|---|
| 🔒 **One modal at a time, queued** | ImGui popups do not nest usefully. A second `Show` **queues**; it does not stack or drop |
| 🔒 **Escape always resolves to the cancel button** — never "no answer" | matches [ruling 12](UX_RESUME_INTERACTION.md)'s centralised Escape |
| 🔒 **Closing the window == cancel** | the two existing hand-rolled modals already do this (`if (!open) CloseCurrentPopup()`); the registry makes it uniform |
| ⚠ **Headless-safe** — every ImGui call gated behind a current-context check | the Blueprint modals already establish this, and the acceptance cases run headless |

⚠ **The 19 existing modal sites are NOT migrated by this design** — that is a mechanical follow-up
([UXI-34](UX_Issues.md#uxi-34)). This design **adds** the host and uses it for the two new consumers.

### 2.2 Confirmation — [UXI-16](UX_Issues.md#uxi-16)

🔒 **[Ruling 29](UX_RESUME_INTERACTION.md):** a destructive action raises a modal **naming what will be
removed**; **Cancel removes nothing**.

| | |
|---|---|
| **Declared, not hand-written** | `EntityActionDescriptor` ([UXI-03](UX_Feature_Entity_Action_Vocabulary.md)) already carries `EntityActionGroup.Destructive`. ⇒ 🔒 **`Destructive` ⇒ the *interactive* dispatcher confirms** (§2.0) — no host writes a dialog, and none can forget one. ⚠ **Headless dispatchers proceed and log** |
| **Naming, bounded** | ⚠ [UXI-24 §6](UX_Feature_Multi_Select.md) warns about a 40 000-entity `Select All` → `Delete`. 🔒 **The count is exact; the naming is capped** — *"Delete 12 entities? Alpha-1, Alpha-2, Bravo-1 … and 9 more"* |
| **Cancel is total** | ⭐ **free, and already guaranteed**: [ruling 15](UX_RESUME_INTERACTION.md) gives every action a per-action ECB that is **dropped, never played back** on cancel. Cancel deletes nothing **because the buffer never flushes** — not because the handler remembered to check |
| ⚠ **The `Delete` key path must reach it** | [UXI-24 §3.6](UX_Feature_Multi_Select.md) converges the raw key and the menu item on one handler. **Without that convergence the key still deletes silently** — a hard dependency, not a nicety |

⭐ **Third consumer, already identified:** [UXI-32 §2.5b](UX_Feature_Entity_Commanding.md) — an order with
`ClearsPriorIntent` **destroys the entity's authored mission plan** with no resume. 🔒 **Confirm when the
target has a non-empty plan**, using the same machinery; silent otherwise.

### 2.3 Progress — [UXI-27](UX_Issues.md#uxi-27), the surface chosen by exclusivity

🔒 [Ruling 13](UX_RESUME_INTERACTION.md) / [API §6c](UX_Interaction_API.md), unchanged:

| Exclusivity | Surface | Cancel |
|---|---|---|
| `Exclusive` | **modal + progress bar** — it blocks everything, so it must say so | button in the dialog |
| `None` | **status-bar section: progress bar + cancel icon** | the icon |

**The status-bar half is one registration**, using the shape six sites already use:

```csharp
wm.StatusBar.RegisterSection("activity_progress", sortOrder: 50, section.Render);
```

| | |
|---|---|
| ✅ **`IProgressSink` binds to whichever surface the activity got** — the handler never knows which | API §6c's *"surface is chosen by exclusivity, not by the handler"* |
| 🔒 **Indeterminate is first-class** — `fraction == null` renders a marquee, not a stuck 0 % |
| **Several concurrent non-exclusive activities** | the section shows the **most recent**, with *"+N more"*; the count is exact. ⚠ **Not a list** — the status bar is one line, and [UXI-13](UX_Issues.md#uxi-13) is already about menu-bar overcrowding |
| 🔒 **Registration throws** when an `Exclusive` action declares no progress surface | API §6c; same fail-at-composition stance as `GlobalActionRegistry.Register` on duplicate ids |
| ⚠ **A cancelled activity must clear its surface** | otherwise a dead progress bar persists — the failure mode that makes progress UI worse than none |

### 2.3b 🔴 Progress across nodes — the gap §2.3 had, and its fix

⚠ **Asked by the user, 2026-08-13: *"is the progress notification designed to travel over a network from
the knowing node to the origin node?"*** — **it was not** ([Correction 44](UX_Tasks_Detail.md#corrections)).
§2.3's *"`Report()` is a plain field write; the surface reads it next frame"* is only true when handler and
surface share a node. For work running on CGF, **the origin's bar never moves**.

🔒 **Fix: the sink is the same interface on both nodes — only its binding differs.** That is [§2.3](#23)'s
own property (*"the handler never knows which surface it got"*, case 27.3) extended one hop:

| Node | `IProgressSink` binds to |
|---|---|
| **executing (CGF)** | a **status-egress sink** — `Report()` publishes a bus event; the egress translator writes it to DDS ([§2.0d](#20d)) |
| **origin, interactive** | the ingress translator turns it back into a bus event that drives the **modal or status-bar** section |
| **origin, headless** | **log sink**, unchanged ([ruling 53](UX_RESUME_INTERACTION.md), case O.4) |
| **Editor** | bus only, **no translators** — the existing single-node path, unchanged |

⭐ **Again, not a new mechanism**: `DeleteEntityRequestSystem.cs:70-74`'s **`InProgress` → final** ack is the
degenerate case of this stream — one intermediate sample, correlated by `RequestId`,
tracked by `EntityRequestFinalizationSystem`. **Widen the cadence, keep the shape.**

| 🔴 Three things the wire forces that a local field write never did | |
|---|---|
| ⚠ **Rate** | a per-frame `Report()` is **60 Hz of DDS traffic per activity**. 🔒 **Coalesce at the egress: latest-sample-wins, capped cadence (~10 Hz), and the terminal sample is ALWAYS sent** — a dropped 100 % leaves a bar stuck at 97 % forever |
| ⚠ **Cancel travels the other way** | the cancel button is at the origin, the work is on CGF ⇒ 🔒 **cancel is a second request correlated by the same `RequestId`**, not a local flag. Case 27.5 (*"cancelling clears the surface"*) must not clear the surface until the executing node **confirms** |
| ⚠ **Liveness** | if the executing node dies mid-activity the origin's bar hangs forever. 🔒 **Timeout ⇒ surface *"lost contact with the owning node"*** — the same fail-toward-telling-the-user stance as [§2.0b](#20b)'s case D.6, never a silent spinner |

### 2.4 ⚠ Where this sits in the frame

| | |
|---|---|
| `WindowManager.Render()` calls `StatusBar.Render()` today | 🔒 **`Modals.Render()` goes beside it**, and **after** the main menu bar — ImGui requires `OpenPopup` outside `BeginMainMenuBar`, which `WindowManager:504` already documents. ⭐ The queue in §2.1 exists for exactly this reason |
| ⚠ **Progress must not require the handler to be on a background thread** | 🔒 [ruling 15](UX_RESUME_INTERACTION.md): handlers stay on the tick thread and **yield**. `Report()` is a plain field write; the surface reads it next frame |

## 3. Acceptance

| # | Case | Cls |
|---|---|:--:|
| 16.1 | 🔒 A `Destructive` action raises a confirmation **without the host writing one** | H |
| 16.2 | 🔒 **Cancel removes nothing** — the ECB is dropped, verified by entity count | H |
| 16.3 | Confirm proceeds exactly once; the dialog closes | H |
| 16.4 | **Escape == Cancel**; closing the window == Cancel | H |
| 16.5 | The message names the entities and the **count is exact** | H |
| 16.6 | 🔒 Naming is **capped** — 40 000 selected produces a bounded string, not 40 000 names | H |
| 16.7 | 🔒 The **`Delete` key** path raises the same confirmation as the menu item | H |
| 16.8 | Deleting a **single** entity confirms too — [UXI-16](UX_Issues.md#uxi-16) is *"no `Delete` confirms in any of the five hosts"*, not *"multi-delete"* | H |
| 16.9 | 🔒 An order that would **destroy a non-empty mission plan** confirms; with an empty plan it does not | H |
| 27.1 | An `Exclusive` activity shows a **modal with a progress bar** | H |
| 27.2 | A non-exclusive activity shows a **status-bar section with a cancel icon** | H |
| 27.3 | 🔒 The handler calls only `IProgressSink.Report` and **cannot tell which surface** it got | H |
| 27.4 | `fraction == null` renders **indeterminate**, not 0 % | H |
| 27.5 | Cancelling from either surface **cancels the activity and clears the surface** | H |
| 27.6 | 🔒 Registering an `Exclusive` action with no progress surface throws at composition — ⚠ **only in an INTERACTIVE composition**; a headless node must not throw at startup | H |
| 27.7 | Two concurrent non-exclusive activities → most recent shown, **exact** *"+N more"* | H |
| 27.8 | A completed activity **removes its surface**; no dead progress bar survives | H |
| M.1 | 🔒 **One modal at a time** — a second `Show` **queues**, and is neither dropped nor nested | H |
| M.2 | The queued modal opens when the first resolves, in request order | H |
| M.3 | 🔒 `ModalManager` is **headless-safe** — every case above runs with no ImGui context | H |
| M.4 | `Modals.Render()` runs **outside `BeginMainMenuBar`** — the ImGui constraint guard | H |
| O.1 | 🔒 **A headless dispatch of a `Destructive` action proceeds without prompting** and completes | H |
| O.2 | 🔒 It **logs what it would have asked**, at the origin — the audit trail replacing the dialog | H |
| O.3 | 🔒 An **executing node never confirms**: applying a received request raises no modal and blocks on nothing, even when the action is `Destructive` | H |
| O.4 | 🔒 A headless context binds `IProgressSink` to a **log sink** — no modal, no status-bar section | H |
| O.5 | 🔒 **No DDS contract gains an origin field** — the wire message is identical whether the operation began interactively or headlessly | H |
| D.1 | 🔒 An entity with **nothing to lose** returns `null` ⇒ **no dialog** — the "should not always ask" guard | H |
| D.2 | 🔒 An entity **with** a mission plan returns a spec, and the dialog shows its lines **verbatim** | H |
| D.3 | 🔒 The origin **names no aspect and no feature** — it renders the spec blind; adding an aspect needs **no origin change** | H |
| D.4 | 🔒 The origin reads **no aspect state** — the describer runs on the executing node over **live** state | H |
| D.5 | 🔒 **Headless never pre-flights** — the authorized request goes straight out, and the skip is logged | H |
| D.6 | 🔒 **Pre-flight timeout ⇒ a generic confirmation** interactively; proceed-and-log headlessly. Never silent | H |
| D.7 | 🔒 **The Editor resolves pre-flight in-process** — same call, no network hop, one code path | H |
| D.8 | A non-destructive action **never pre-flights** — the latency guard | H |
| T.1 | 🔒 The pre-flight request reaches the dispatcher **as a bus event**, published by an ingress translator — the handler holds **no DDS type** | H |
| T.2 | 🔒 The result leaves as a **bus event**; the egress translator is the only class writing the wire message | H |
| T.3 | 🔒 **Editor**: with **no translators registered**, request and result never leave the bus and the dispatcher system is **byte-identical** | H |
| T.4 | 🔒 The dispatcher runs **on the tick thread** (Input phase) — no provider is invoked from a network callback | H |
| T.5 | Request and result correlate by `RequestId`; a late or duplicate result for a resolved id is **dropped, not rendered** | H |
| P.1 | 🔴 **Progress from a remote activity reaches the origin's surface** — the cross-node case §2.3 originally missed | H |
| P.2 | 🔒 The handler calls only `Report()` and **cannot tell** it is being surfaced one node away (27.3, across the wire) | H |
| P.3 | 🔒 **Egress coalesces**: a 60 Hz `Report()` produces a capped sample rate, **and the terminal sample is always sent** | H |
| P.4 | 🔒 **Cancel travels back** and the surface clears only on the executing node's confirmation — not optimistically | H |
| P.5 | 🔒 A remote activity that goes silent **times out into *"lost contact"***, never an eternal spinner | H |
| O.6 | An interactive dispatch that is **cancelled emits nothing** — verified on the receiving node, not just locally | H |
| 16.10 | **Editor**: delete a unit → confirm → it disappears; repeat with Cancel → it does not | I |
| 27.9 | **Editor**: *Mark Area Targets* shows real progress and can be cancelled mid-run | I |

**35 H · 2 I · 0 V.**

## 4. Build order

| # | Step |
|--:|---|
| 0 | **`IIntentAspectProvider.DescribeImpact` + `ConfirmationSpec` + `ICommandGateway.PreflightAsync`** (§2.0b) — testable headlessly with a fake provider, before any UI exists |
| 1 | **`ModalManager`** + `WindowManager.Modals` property, mirroring `StatusBarManager`/`.StatusBar`, with its own test file |
| 2 | **Confirmation** driven by `EntityActionGroup.Destructive` — closes UXI-16 for every host at once |
| 3 | **`IProgressSink`** implementation + the two surfaces; register the status-bar section |
| 4 | Composition-time validation (27.6) |
| 5 | ⚠ **Migrate the 19 hand-rolled modal sites** — [UXI-34](UX_Issues.md#uxi-34), mechanical, not this design |

## 5. 🔒 Out of scope

| | |
|---|---|
| Migrating the 19 existing `BeginPopupModal` sites | [UXI-34](UX_Issues.md#uxi-34) |
| The action vocabulary / `Destructive` group | [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) — **prerequisite** |
| Converging the `Delete` key with the menu item | [UXI-24 §3.6](UX_Feature_Multi_Select.md) — **prerequisite for 16.7** |
| Toast / transient notifications | not requested; the Blueprint editor's own design has its own §10.7 |
| Undo | ⚠ a confirmation is **not** an undo, and this design does not pretend otherwise |

## 6. Risks

| | |
|---|---|
| 🔴 **A modal on the wrong node** | ✅ **closed by §2.0** — the gate is at the origin, so an executing node has no confirmation path at all. ⚠ **This is the risk that would have shipped**: in a single-process test everything passes, and it only fails once ExCon and SimHost are on different machines |
| ⚠ **Headless proceeds silently on destructive work** | deliberate ([ruling 53](UX_RESUME_INTERACTION.md)) — but it means an MCP agent can delete 12 entities with no prompt. **O.2's origin-side log is the whole safety net**, so it is a requirement, not a nicety |
| 🔴 **Confirmation fatigue** | if every delete confirms, operators click through blindly and the guard is worse than useless. ⚠ **Scope is `Destructive` actions only** — and 16.8 deliberately keeps single-delete confirming, because [UXI-16](UX_Issues.md#uxi-16) is about *no host confirming at all* |
| ⚠ **A modal in a real-time simulation does not pause it** | the world keeps running behind the dialog. ⭐ For `Exclusive` progress that is the point; for a **confirmation** it means the named entities can die before Confirm is clicked ⇒ **the handler must tolerate a stale target set** — the ECB already drops cleanly, but the count shown may be wrong by then |
| ⚠ **One modal at a time is a real constraint** | a confirmation raised while an `Exclusive` progress modal runs will **queue**, which may read as a lost click. ⚠ Mitigated because `Exclusive` already blocks other actions ([API §5](UX_Interaction_API.md)) — so the case should not arise, and M.1 pins the behaviour if it does |
| ⚠ **Progress that never updates is worse than none** | 27.8 and 27.5 exist to stop dead surfaces surviving |
