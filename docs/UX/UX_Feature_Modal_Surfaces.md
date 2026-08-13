# Feature design — modal surfaces: confirmation + activity progress

> **Design for [UXI-16](UX_Issues.md#uxi-16) and [UXI-27](UX_Issues.md#uxi-27) · drafted 2026-08-13.**
> Paired at the user's direction — they share one vehicle, and designing it twice is the duplication this
> programme keeps finding. Implements [rulings 13, 29](UX_RESUME_INTERACTION.md) and
> [API §6c](UX_Interaction_API.md#6c-progress-reporting--part-of-the-design-per-the-ruling).
> **Status: ✅ designed — no open decisions.**

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
──────────────────────────────────────────────         ──────────────────────────────
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

### 2.0b 🔴 How the origin knows **what** to ask — without knowing what it is asking about

> **User, 2026-08-13:** *"How will it work? If a user issues a new tactical intent via right-click from
> ExCon, how does ExCon know what to ask — and whether at all — if the mission plan is existing at all?
> ExCon (or any interactive node) should not inherently know there is a mission planner. It is decoupled
> even on the target executing node. This might need some more complicated approach."*

⇒ [Ruling 54](UX_RESUME_INTERACTION.md). **It does need one, and it is the mirror of what
[ruling 52](UX_RESUME_INTERACTION.md) already built.**

| Node | Registry | Role |
|---|---|---|
| **executing** (CGF) | `IIntentClearProvider` ([UXI-32 §2.5d](UX_Feature_Entity_Commanding.md)) | **performs** the clear |
| **interactive** (ExCon / Editor / IG) | 🆕 **`IIntentImpactDescriber`** — *same `AspectId`* | **describes** what would be lost |

```csharp
public interface IIntentImpactDescriber
{
    string      AspectId { get; }                     // pairs with the clear provider
    ImpactNote? Describe(IDerEntity entity);          // null ⇒ nothing to warn about
}
public sealed record ImpactNote(string Summary, int Severity);   // "Discards a 4-phase mission plan"
```

**The dispatcher's rule is one line, and it names nothing:**

```
notes = describers.Select(d => d.Describe(entity)).Where(n => n != null)
if (notes.Any() && ctx.IsInteractive)  → confirm, listing notes
else                                   → proceed  (headless, or nothing to lose)
```

| | |
|---|---|
| ✅ **"Whether to ask at all" is answered by the notes being empty** | an entity with **no** mission plan produces no note ⇒ **no dialog**. This is exactly the *"should not always ask"* the user asked for |
| ✅ **ExCon the subsystem stays ignorant** | ⭐ **the mission *module* is already hosted there** — `MissionPanel`, `ExConMissionWindow`, `MissionEditorService`. **It** registers the describer, as it already registers its window |
| ⭐ **And the state is already replicated** | `EntityMissionDescriptor` is set on ExCon's DER entity by `NedExConIngressTranslators.cs:322`; `MissionEditorService.cs:83` already reads it with `HasDescriptor<EntityMissionDescriptor>()`. **The describer is ~5 lines over data ExCon already receives** |
| ⭐ **`IDerEntity` is the right parameter** | it has the **same descriptor-bag API** as the ECS side (`HasDescriptor<T>` · `GetDescriptor<T>` · `GetAllDescriptorTypes`), so a describer compiles against a facade both an ECS host and a DDS-only host satisfy — 🔒 no `Entity`, honouring [ruling 16](UX_RESUME_INTERACTION.md) |

#### 🔴 Three residual constraints — named, not hidden

| | |
|---|---|
| ⚠ **A warning can be stale** | describers read **replicated** state, so the plan may change between the prompt and the apply. 🔒 **Acceptable because the confirmation is advisory** — the executor applies unconditionally either way, and [§6](#6-risks) already records that a modal does not pause the simulation. ⚠ **It must never become a precondition** |
| 🔴 **An aspect whose state is not replicated to the origin cannot warn** | ⇒ 🔒 **its owner must replicate a summary flag, or accept silent destruction.** That is a deliberate, stated cost — the alternative is a pre-flight probe round trip (below), which this design does **not** take |
| 🔴 **The two halves live on different nodes, so id drift cannot be statically checked** | a clear provider with no describer destroys silently; a describer with no clear provider cries wolf. ⇒ 🔒 **both sides reference one shared `AspectIds` constant class**, and each node gets a test asserting its registrations are drawn from it. ⚠ **This is a mitigation, not a proof** — cross-node pairing is unverifiable at compile time |

#### ⚖ The alternative I did **not** take

**A pre-flight probe** — origin sends *"what would this destroy?"*, executor answers, origin confirms, origin sends the real request. ⭐ Authoritative and needs no replication.

| | |
|---|---|
| 🔴 **Rejected**: a round trip on every right-click, a new message pair, timeout and retry handling — and **still racy**, because state can change between probe and apply |
| ⚠ **But it is the right escape hatch** if an aspect ever genuinely cannot replicate a summary. Recorded so the option is not lost |

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
| D.1 | 🔒 An entity with **no** mission plan produces **no note** ⇒ **no dialog** — the "should not always ask" guard | H |
| D.2 | 🔒 An entity **with** a plan produces a note, and the dialog lists it | H |
| D.3 | 🔒 The dispatcher **names no aspect** — it renders whatever notes come back; adding an aspect needs **no dispatcher change** | H |
| D.4 | 🔒 A describer compiles and runs against **`IDerEntity`** — proven by ExCon, which has no ECS world | H |
| D.5 | ⚠ **Zero describers registered ⇒ no dialog and no error** — a host that ships none degrades to silent-proceed | H |
| D.6 | 🔒 Every registration on both sides draws its id from the shared **`AspectIds`** constants — the drift mitigation | H |
| O.6 | An interactive dispatch that is **cancelled emits nothing** — verified on the receiving node, not just locally | H |
| 16.10 | **Editor**: delete a unit → confirm → it disappears; repeat with Cancel → it does not | I |
| 27.9 | **Editor**: *Mark Area Targets* shows real progress and can be cancelled mid-run | I |

**33 H · 2 I · 0 V.**

## 4. Build order

| # | Step |
|--:|---|
| 0 | **`IIntentImpactDescriber` + `AspectIds`** and the dispatcher rule (§2.0b) — testable headlessly with a fake describer, before any UI exists |
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
