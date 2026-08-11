# Architect question #26 — One entity-action vocabulary across surfaces and modes

> **Drafted 2026-08-10 · awaiting the architect.** Claude cannot reach the architect; the user relays.
>
> **Context:** [UX_Current_UI_Architecture.md](UX_Current_UI_Architecture.md) (the evidence) and
> [UX_Cleanup_Path.md](UX_Cleanup_Path.md) (the staged proposal these questions gate).
>
> ⚠ **This supersedes [Q25-D](Architect_Question_25_Scenario_Authoring_Golden_Path.md), and Q25-F/F′ are
> moot** — the user withdrew the dedicated-editor-exe plan on 2026-08-10.

## Ground truth (verified against code)

| Fact | Evidence |
|---|---|
| **The execution layer is already unified.** All menu paths reach `GlobalActionRegistry` (`int id → handler`) via `GlobalActionDispatchSystem`; ids cross DDS | `GlobalActionRegistry.cs:15-27`, `GlobalActionIds.cs:10`, `ContextActionIngressSystem.cs:32-72` |
| **The authoring layer is fragmented three ways** — inspector lambdas, map JSON, hardcoded ORBAT items | `EditorSubsystem.cs:1425-1516` · `ContextMenuProjectorGizmo.cs` · `OrbatPanel.cs:290-314` |
| `IEntityContextMenuHandler` works and is used per host: Editor **4** providers, SimHost **1**, IG **1**, CGF **1**, ExCon **0** | `EntityInspectorPanel.cs:136`, `LambdaEntityContextMenuHandler.cs:21` |
| **`Center` and `Delete` are reimplemented three times with different behaviour** | Editor publishes `DestroyEntityCommand`; SimHost branches on `NetworkIdentity`, falls back to `_repo.DestroyEntity`, clears selection + inspector state |
| **Two independent parsers of the same JSON payload** | Editor's `JsonEntityContextMenuHandler.cs:74-120` vs `GizmoMap.Presentation/UI/ContextMenuAdapter.cs:106-156`. ⚠ **Corrected 2026-08-10:** `Hrot.IG.Systems.ContextMenuSystem` does **not** parse the JSON — it only stores the raw string on `ContextMenuState.MenuJson` |
| **No perspective filter exists on any menu**; the toolbar has one, the menu registry does not | `WindowManager.cs:569-636` vs `MainToolbarManager.cs:129-134` |
| The interaction core (selection, drag, gizmo execution) is **already shared** by Editor, SimHost and IG | `EditorSubsystem.cs:1287`, `SimHostVisualization.cs:250`, `IgApplication.cs:767` |

**User brief, 2026-08-10:** Editor, SimHost and CGF *"should share most capabilities — capability to
share, but inheriting optionally only what is necessary."* ⇒ **share the declaration, bind the
implementation per host.** IG's menu must remain **configurable over the network**; ExCon is **natively
mapless** and is the source of those remote menus.

## 🔒 Constraints added by the user, 2026-08-10 — these bound every answer below

| # | Constraint | Consequence |
|--:|---|---|
| 1 | **No higher-level concept leaks into a generic component.** The inspectors and other `Fdp.Presentation` panels must stay ignorant of editor / simhost / cgf concepts. Everything through a clean API | The generic side may know `IEntityActionProvider` and an **opaque context**. It must never know what a *mode* is, nor what any specific action *does* |
| 2 | **The three `Delete` implementations are probably not wrong.** The same action legitimately needs different handling per subsystem — *"the editor is networkless while all the other ClusterRunner subsystems use the network"* | ⇒ **Unifying the handlers is not the goal.** Handlers must stay **register-time parametrizable**. Unify the *declaration*, never force one implementation |
| 3 | **The same applies to tools** | A tool descriptor is shared; its activation is host-bound |
| 4 | **The network-defined JSON menu is IG's speciality — keep it a completely separate pipeline** | ⇒ **Q26-A resolves to A2**, not Claude's A1 lean. See below |
| 5 | ⚠ *"But a JSON-defined context menu might be a generic idea reused by many subsystems — needs more investigation"* | ✅ **Investigated** → [A″](#a2-json-as-a-generic-mechanism). Not unified today (3 look-alike schemas), and the blocker is that items are **closures, not data**. Resolves into the descriptor/binding split |
| 6 | **Perspective and mode may *both* affect the context menu** | ⇒ Q26-D is not either/or. See the two-axis resolution |

### ⭐ What constraint 2 changes — declaration vs binding

Claude's Stage-1 framing said unifying `Delete` *"forces a decision about which behaviour is correct."*
**That was wrong.** The divergence is structural — the editor is networkless by construction — so the
correct model separates **what an action is** from **how a host performs it**:

```
// Shared, declarative — identity and presentation only. Neutral project.
EntityActionDescriptor { Id, Label, Icon, Group, Order }

// Host-supplied at registration — the implementation, and any condition.
host.RegisterAction(EntityActions.Delete,
                    isApplicable: (e, ctx) => …,      // host's rule
                    execute:      (e, ctx) => …);     // host's handler
```

⇒ **One identity, label, icon and ordering** — so a user sees a consistent menu across modes and
surfaces ([UXR-85](UX_Requirements.md#uxr-85)). **N implementations**, each correct for its subsystem.
The three `Delete`s stay three handlers; only the *declaration* is shared.

⇒ **It also satisfies constraint 1 for free:** the descriptor is inert data and the handler is a
delegate the host owns, so the generic panel carries neither behaviour nor knowledge.

## Q26-A — Is one action vocabulary right, and how far does it reach?

- **A1 — One `IEntityAction` vocabulary, every surface.** Map, inspector and ORBAT all render the same
  provider-resolved list. The network/JSON menu becomes **one provider among several**.
  *Reuse:* `IEntityContextMenuHandler` generalised by two parameters; `GlobalActionRegistry` untouched as
  the backend. *Build:* the action/provider/context types + one adapter per surface.
  *Cost:* every surface's menu code changes at once.
- **A2 — Unify the local surfaces only**; leave the network/JSON menu a separate pipeline.
  *Reuse:* more. *Build:* less. *Cost:* keeps two parsers and two mental models; IG's menu stays unable
  to carry a locally-defined action.
- **A3 — Do nothing structural**; extract only a shared *item library* the existing lambdas call.
  *Reuse:* maximal. *Build:* minimal. *Cost:* solves the triplicated `Delete` but not the
  cross-surface-consistency requirement.

> ### ✅ RULED A2 by the user, 2026-08-10 — Claude's A1 lean is withdrawn
>
> *"The network-defined JSON menu is the IG speciality and has little to do with other subsystems — I
> would keep that a completely separated pipeline."*
>
> ⇒ Unify the **local** surfaces (map, inspector, ORBAT). **IG's network pipeline stays as it is.**
> The reasoning behind A1 — that leaving it separate keeps a seam we would reopen — does not apply once
> the two are recognised as *different features*, not two implementations of one. IG is a network
> terminal whose menu is authored remotely by ExCon; that is a distinct capability, not a variant.
>
> **Still true and still worth doing:** the Editor's `JsonEntityContextMenuHandler` remains a *local*
> provider in the unified chain — it consumes the JSON payload for the Editor's own surfaces. Only IG's
> `ContextMenuSystem` (the DDS round-trip) stays out.

> **Sub-question A′:** should **ORBAT rows** be in scope for stage 2, or follow later? Including them
> collapses ExCon's 434-line fork via the same mechanism; excluding them keeps the stage smaller.

### <a id="a2-json-as-a-generic-mechanism"></a>Sub-question A″ — is JSON-defined menu content a *generic* mechanism? ✅ investigated 2026-08-10

The user, same session: *"a JSON-defined context menu might be a generic idea reused by many
subsystems — needs more investigation."*

**Distinguish two things that are currently conflated:**

| | |
|---|---|
| **The transport** — a remote party defines the menu over DDS | 🔒 **IG-specific. Ruled out of scope above.** |
| **The format** — menu content expressed as data rather than code | ⚠ **Possibly generic.** Would let any subsystem declare items without a rebuild, and is a plausible answer to Q26-B |

⚠ **Not a question for this round.** It needs an investigation first: who would author such a file,
whether items can carry a handler binding (constraint 2) or only reference a registered action id, and
whether it duplicates what the descriptor+binding split already gives. **Do not design for it now** —
but do not choose an action-id scheme that would prevent it.

### ✅ INVESTIGATED 2026-08-10 — and the answer resolves *into* the descriptor/binding split

The user's lead was *"I forgot how unified the menu definition is — I guess quite a lot."*
**Measured: it is not unified at all.** Three independently-defined, look-alike schemas exist:

| | Serializer | Fields |
|---|---|---|
| ExCon `ContextMenuItem` | Newtonsoft | `id · label · icon · enabled · style · shortcut · separator` — **7** |
| `ContextMenuItemDto` (gizmo emitters) | System.Text.Json | the same 7 **plus** `tooltip · children · priority · checked` — **11** |
| `CanvasMenuUpdateSystem` | none — a **hand-typed string literal** | `[{"id":200,"label":"Measurement Tool"}]` |

They overlap only because one renderer happens to consume both. **No shared type, no shared serializer,
no contract** — `style` is silently dropped by every parser today, and ExCon **cannot express a submenu
at all** (`BeginSubmenu` is a no-op that flattens).

#### 🔴 The blocker is not the schema — it is that menu items are **closures, not data**

| Host | Items that close over host-local state |
|---|--:|
| SimHost | **4 of 5** — camera, `_selection`, `_gizmoSystem`, `_map` |
| CGF | **4 of 5** — same pattern |
| Editor | most, plus two that data **cannot** express at all |

The clearest case: the Editor's `$"Mark Target for {perceiverCount} Units..."` — the **label itself is
recomputed at menu-open time** from the live selection count, and the handler is an `async void` closure
awaiting an interactive map pick. No `id`/`label`/`enabled` schema can carry that.

#### ⇒ The resolution: this question is already answered by [constraint 2](#-constraints-added-by-the-user-2026-08-10--these-bound-every-answer-below)

**The declarative half can be data; the behavioural half cannot.** That is precisely the
descriptor-vs-binding split:

| | Can it be data? |
|---|---|
| `Id`, `Label`, `Icon`, `Group`, `Order` — the **descriptor** | ✅ yes |
| `isApplicable`, `execute` — the **binding** | ❌ no, and the user already ruled it must stay register-time parametrizable |

⇒ **"Unify the menu format among SimHost, CGF and Editor" = "share the descriptors"** — which is
[Stage 1](UX_Cleanup_Path.md#stage-1--name-the-vocabulary-no-surface-changes-yet), already the plan.
Whether descriptors later live in a **file** rather than in code is Q26-B's B3, and can be decided
afterwards without redoing anything.

⚠ **A pure JSON menu is the wrong target for these three hosts.** Chasing it would either force the
closures into a plugin-id indirection nobody asked for, or drop the runtime-computed items.

## Q26-B — Where does a "profile" live? — ⚠ the question was malformed

> ### The user asked what "profile" means and where it lives. Answering honestly: **it should not exist.**
>
> Claude introduced the term loosely, meaning *"the per-mode declaration of which windows, menu items,
> map layers, tools and actions are present."* Under
> [constraint 1](#-constraints-added-by-the-user-2026-08-10--these-bound-every-answer-below) that is the
> wrong shape — a profile object is a **higher-level concept that would have to be understood by the
> generic components** in order to be applied by them. That is precisely the leak the user ruled out.
>
> **The profile already exists, and it is not an artifact: it is the set of registrations a host
> performs in its own composition root.** Every subsystem already has one. Nothing new is required to
> "hold" it.
>
> ⇒ **The real question is not where a profile lives, but what a registration carries.** Today a
> registration carries a handler. It should also carry the *conditions* under which the item applies —
> which is what makes it register-time parametrizable per constraint 2.

**So B is restated: what does a registration carry?**

- **B1 — Handler only** (today). Conditions are hardcoded inside the handler's body.
  *Cost:* a host cannot vary applicability without editing the lambda; nothing is inspectable.
- **B2 — Handler + declarative conditions** — `RegisterAction(descriptor, isApplicable, isEnabled, execute)`,
  where the predicates receive the opaque context.
  *Reuse:* exactly today's `LambdaEntityContextMenuHandler` shape, plus parameters.
  *Build:* small. *Cost:* conditions are code, so still not designer-editable.
- **B3 — Data-declared sets** (a file per mode listing action ids).
  *Cost:* the drift problem, and it collides with A″ — do not decide this before that investigation.

> **Claude's lean: B2.** It is the minimum that satisfies constraints 1 and 2 together, it introduces no
> new concept for a generic component to understand, and it leaves B3/A″ open rather than pre-empting
> them. **Recommend also dropping the word "profile" from the programme's vocabulary** — it named
> something that turned out to be the composition root.

## Q26-C — Replace or wrap the int action ids?

`GlobalActionIds` are `int` and **cross DDS** (ExCon → IG).

- **C1 — Wrap.** Keep the int ids as the wire/execution vocabulary; `IEntityAction` carries one.
  *Reuse:* total; no protocol change. *Cost:* two id spaces coexist forever.
- **C2 — Replace with string ids**, mapping at the network boundary.
  *Reuse:* less. *Build:* a boundary map. *Cost:* protocol-adjacent risk for a cosmetic gain.
- **C3 — Replace outright**, changing the wire format.
  *Cost:* breaks ExCon↔IG compatibility. Rejected unless the architect sees a reason.

> ### ✅ RULED C1 by the user, 2026-08-10
>
> *"New action and menu infrastructure needs to be **compatible with the global action registry** rather
> than inventing something completely different."*
>
> ⇒ `GlobalActionRegistry` + `GlobalActionDispatchSystem` + the `int` `GlobalActionIds` are the
> **backbone to build on**, not a legacy layer to route around. The descriptor/binding split sits *above*
> them: a descriptor carries an action id, and registering a handler is registering with the existing
> registry. **No parallel dispatch mechanism.** C2 and C3 are closed.

## Q26-D — Is *perspective* the right profile key?

The user said the menu *"needs to change with the subsystem-derived perspective (cgf/editor/simhost/ig)"*.
But the two concepts are not the same thing — see
[§5b](UX_Current_UI_Architecture.md#5b-how-perspective-switching-actually-works):

| | Perspective | Mode |
|---|---|---|
| Scope | a window-set filter, switchable at runtime | fixed for the process (`--mode`) |
| Today | 10 exist; 5 are cluster roles, 4 are the editor's internal graphs | 5 UI modes |
| Note | the editor's BTree/HSM/Blueprint perspectives are **not** subsystems | `editor` cannot combine with the others |

**User input, 2026-08-10:** *"perspective vs mode — both might affect the context menus."* ⇒ not
either/or. The design question becomes **which mechanism carries each**, and constraint 1 answers it:

> ### ⭐ The two-axis resolution — each axis at its own layer
>
> | Axis | When it is decided | Mechanism | Who knows about it |
> |---|---|---|---|
> | **Mode** (editor / simhost / cgf / ig) | **composition time** — a process is only ever one mode | **which registrations the host performs at all** | only the subsystem's composition root |
> | **Perspective** (Scenario / BTree / HSM / Blueprint / …) | **runtime** — switchable | a **condition carried by the registration**, evaluated against the context | the host's predicate; the generic panel just passes the context through |
>
> **Why this is clean:** `CurrentPerspective` is already a `Fdp.Presentation` (`WindowManager`) concept,
> so a generic panel may carry it in an opaque context **without learning anything new**. `Mode` is a
> `ClusterRunner` concept and therefore **must never appear in a generic API** — and it does not need
> to, because a host that exists only in one mode expresses mode-dependence simply by what it registers.
>
> ⇒ Both axes affect the menu, as the user requires, but **no generic component learns a
> higher-level concept.**

⚠ **One prerequisite:** keying on perspective requires the perspective set to be **declared** rather
than emergent from window registration. Stage 3 needs that anyway to fix the silent restore bug
([§5b](UX_Current_UI_Architecture.md#5b-how-perspective-switching-actually-works)).

> ### ✅ ANSWERED by the user, 2026-08-10 — and the alternative is withdrawn
>
> *"The perspective seems to be the factor that further customizes the mode-defined/enabled
> capabilities."*
>
> ⇒ **Mode enables; perspective refines.** The two do not cross-cut, and a perspective can never add a
> capability the mode did not enable. That ordering is what makes the layering hold.
>
> ⚠ **Claude's suggested alternative — a broader "is the world running" axis — is withdrawn**, because
> its premise was wrong: *"the editor fully supports running, not just preparation"* (same session). The
> Editor plays, pauses and rewinds, so running is a state the Editor **has**, not a thing that
> distinguishes it from SimHost. ⇒ **"Is running" is an ordinary `isApplicable` condition on an action or
> tool, not a capability axis.** Two axes only.

## Q26-E — Is Stage 0 acceptable as a delete-only batch?

~1,800 lines of dead UI, including the `Hrot.UI.Common` project that **builds nowhere while owning the
namespace the live panels declare**.

- **E1 — Yes, ship deletion alone**, gated on a green build and suites, before anything else.
- **E2 — Fold deletions into the stage that touches each file.**
  *Cost:* the namespace trap stays live for months, and every later stage risks editing the dead copy.

> **Claude's lean: E1, emphatically.** The trap has even odds of wasting a session's work, and a
> delete-only batch is the cheapest thing in this plan to review and revert.

## Answers

*To be filled in after the architect round. Then update the matching
[UXD rows](UX_Design.md#3-design-decisions-uxd) and unblock the stages in
[UX_Task_Tracker.md](UX_Task_Tracker.md).*

| Question | Decision | Notes |
|---|---|---|
| **Q26-A** — one vocabulary, how far | ✅ **A2** | **ruled by the user 2026-08-10** — local surfaces unify; IG's network pipeline stays separate |
| **Q26-A′** — ORBAT in stage 2? | — | *including it collapses a 434-line fork* |
| **Q26-A″** — is JSON-defined menu content generic? | ✅ **investigated** | **The format is not unified today** (3 look-alike schemas), and the blocker is that items are **closures, not data** — SimHost 4/5, CGF 4/5. ⇒ resolves into the descriptor/binding split; "unify the format" = "share the descriptors" = Stage 1 |
| **Q26-B** — what a registration carries | — | *lean B2 — handler **plus** declarative conditions. ⚠ the original "where does a profile live" was malformed; see the block* |
| **Q26-C** — replace or wrap int ids | ✅ **C1** | **ruled by the user 2026-08-10** — build **on** `GlobalActionRegistry`; invent nothing parallel |
| **Q26-D** — perspective or mode as key | ✅ **both, ordered** | **user 2026-08-10:** mode *enables*, perspective *further customizes*. The "is running" alternative is withdrawn — the editor runs too |
| **Q26-E** — delete-only batch | — | *lean E1* |

### Settled by the user before the round — do not re-ask

| | |
|---|---|
| **Handlers stay per-subsystem** | The three `Delete`s are **not** a defect. Unify the *declaration*; keep implementations **register-time parametrizable**. Same for tools |
| **Why they legitimately differ** | The editor is **networkless**; every other ClusterRunner subsystem uses the network |
| **No leaking into generic components** | Inspectors and other `Fdp.Presentation` panels stay ignorant of editor/simhost/cgf concepts — clean API only |
| **The editor is not preparation-only** | It *fully supports running*. So prep-vs-live is **not** the Editor/SimHost axis, and "is running" is a per-action condition rather than a capability axis |
| **Build on the existing registry** | `GlobalActionRegistry` is the backbone. No parallel dispatch mechanism |
