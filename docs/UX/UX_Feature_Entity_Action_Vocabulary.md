# Feature design — the shared entity-action vocabulary

> **Design for [UXI-03](UX_Issues.md#uxi-03) · drafted 2026-08-10.**
> 📐 **API: [UX_Interaction_API.md](UX_Interaction_API.md) · ✅ Acceptance: [UX_Interaction_UseCases.md](UX_Interaction_UseCases.md)**
> The API contract lives in the first — this doc keeps the
> evidence and the rulings; that one holds the types, the arbitration order and the threading model.
> **Status: ✅ designed — ready to break into `UXT` tasks.**
>
> Implements [UXR-89](UX_Requirements.md#uxr-89) and the descriptor/binding split ruled in
> [Q26](Architect_Question_26_Entity_Action_Model.md) (A2 · B2 · C1 · D). This is
> [Stage 1](UX_Cleanup_Path.md#stage-1--name-the-vocabulary-no-surface-changes-yet).

## 0. Prior art — ✅ re-verified 2026-08-10 against the [Seam Inventory](UX_Seam_Inventory.md)

| Proposed type | Does it already exist? |
|---|---|
| `EntityActionDescriptor` · `EntityActionGroup` · `EntityActionExecution` · `EntityActionRegistry` · `EntityActionContext` | ❌ **nothing.** The only `ActionDescriptor` in the repo is FastBTree's behaviour-tree record (`Fbt.Compiler/BTreeSchema.cs`), unrelated |
| The declaration + binding split | ✅ **exists** — `SharedContextMenuPopulator` + `IEntityActionController`. This whole design. See below |
| The **execution** backbone | ✅ **exists** — `GlobalActionRegistry` (4 prod; Editor · SimHost · ReplayBrowser). Built on, per Q26-C1 |

> ### ⚠ Refinement the re-check produced — the map already has a shared *rendering* path
>
> This design says no map surface uses `IEntityContextMenuHandler`. **Still true.** But the map is not
> path-less: menu content is emitted as JSON into a gizmo binding and rendered by a **shared** adapter.
>
> | | prod | Adopted by |
> |---|--:|---|
> | `ContextMenuAdapter` (GizmoMap) | 9 | CGF · IG · SimHost |
> | `CanvasContextMenuGizmo` — `[GizmoProjector]`, empty-canvas menu | 6 | CGF · Editor · IG · SimHost |
>
> Its own doc comment: *"`DebugGizmoLayer` … resolves the JSON from the intern map and opens the popup via
> `ContextMenuAdapter` — **identical to how entity menus are resolved**."*
>
> ⇒ **No change to this design** — the map stays [UXI-04](UX_Issues.md#uxi-04)'s scope. But UXI-04's map
> half is now *"emit the registry's items into the existing `ContextMenuBinding`"*, **not** *"build a map
> menu path"* — which is exactly what the unadopted `MapContextActionController` (0 consumers) was for.

## ⭐ The finding that rewrites this issue

**The mechanism already exists, was built deliberately, is unit-tested — and exactly one subsystem uses
it. The one without a map.**

| Piece | What it is | File | Used by |
|---|---|---|--:|
| `SharedContextMenuPopulator` | the shared **declaration** — labels, order, separator | `Hrot.Presentation/Menus/SharedContextMenuPopulator.cs:18` | **ExCon only** |
| `IEntityActionController` | the **binding** port — 7 methods, host-implemented | `Hrot.Presentation/Facades/IEntityActionController.cs` | **1 adapter** |
| `JsonContextMenuBuilder` | an `IContextMenuBuilder` that emits JSON instead of ImGui | `Hrot.ExCon/Adapters/JsonContextMenuBuilder.cs:20` | ExCon only |

Its declared list is **byte-for-byte the Editor's**, written twice, independently:

```
SharedContextMenuPopulator:37-51   Center on Entity · Rename... · Edit Shape · Edit Route · Rotate · ── · Delete
EditorSubsystem.cs:1427-1450       Center on Entity · Rename... · Edit Shape · Edit Route · Rotate · ── · Delete
```

⇒ **[The seam law](UX_Current_UI_Architecture.md) again, in its sharpest form yet:** the seam was built,
the convention it encodes is followed by every host, and adoption stopped at one. So UXI-03 is **not
"invent a vocabulary"** — it is *"adopt the one that exists, after removing the two things that stop the
other four from adopting it."*

### 🔴 Why the other four cannot adopt it — measured, not guessed

| # | Blocker | Evidence |
|--:|---|---|
| 1 | **`long entityId` is a *network* id.** The Editor is networkless by construction — the user's ruling shows up here as the concrete reason the shared code is unshareable | `IEntityActionController` — every method takes `long entityId` |
| 2 | **The port is fat and fixed.** Adding an action edits an interface every host must implement. ExCon's own adapter is already **3 no-ops out of 7** (Rename, Measure, Rotate) | `ExConEntityActionAdapter.cs` |
| 3 | **Conditions are positional parameters** — `hasEditablePolyline`, `hasRoutePlan`, `tkbType`. A new condition changes the signature for everyone | `PopulateEntityMenu(long, long, bool, bool, …)` |
| 4 | **Submenus silently flatten** — `BeginSubmenu(label) => this` | `JsonContextMenuBuilder.cs` |

**All four are exactly what Q26-B's per-item registration removes.** ✅ No new concept is required.

## The duplication being paid for

| Logical action | Independent declarations | Where |
|---|--:|---|
| **Center** | **9** | Editor `:1439` · SimHost `:168` · CGF `:592` · IG `:1206` · ReplayBrowser `:217` · ExCon ORBAT `OrbatPanel.cs:295` · ExCon legacy `ContextMenuLogic.cs:176` · `SharedContextMenuPopulator:37` · `ContextMenuProjectorGizmo` ×4 permutations |
| **Delete** | **10** | same spread — **including one cased `"DELETE"`** (`ContextMenuLogic.cs:178`) |
| **Rotate** | 5 | Editor, SimHost, CGF each re-implement the same `EntityRotatorGizmo` injection |
| `Toggle AI Trace` | 2, **divergent** | Editor inlines the patch; SimHost delegates to `AiTraceContextMenu.PublishToggle` |
| `CenterOnEntity` **handler** | 2, **divergent** | Editor publishes `CenterOnEntityCommand`; ReplayBrowser calls `Camera.FocusOn` directly |

⚠ **The divergent *handlers* are in scope only as a finding.** Per the user's ruling, N implementations
stay N implementations — this design unifies the **declaration** and nothing else.

## The design

### 1. Descriptor — inert, shared, no behaviour

```csharp
public enum EntityActionGroup     { View, Edit, Destructive }   // fixed order; call order within a group
public enum EntityActionExecution { PerEntity, Selection }

public sealed record EntityActionDescriptor(
    int                   Id,          // a GlobalActionIds value — the existing dispatch key (Q26-C1)
    string                Label,
    EntityActionGroup     Group,
    EntityActionExecution Execution   = EntityActionExecution.PerEntity,
    bool                  CancelsModalTool = false); // ⬅ added by UXI-07, 2026-08-10
```

> ### ⬅ `CancelsModalTool` — added by [UXI-07](UX_Feature_Tool_Model.md), 2026-08-10
>
> Dispatching an action with `CancelsModalTool = true` **clears the subsystem's modal tool stack**; a
> `false` action (e.g. a *recenter map* shortcut fired mid-drag) **leaves the tool armed**. The flag
> belongs to the action because the action *causes* the effect — an earlier draft put the inverse flag on
> the tool ([Corrections 20](UX_Tasks_Detail.md#corrections)).
>
> ⚠ **Renamed from `StealsFocus`** the same day — it named a mechanism, not the consequence, and the
> consequence is what a registration needs to declare.
>
> 🔒 **There is no `SuspendsModalTool` counterpart, and cannot be.** Suspension needs a matching resume
> point that only the handler knows, so it is **scoped, not declared** — the handler calls
> `ctx.Tools.PushModal(...)` and disposes it. See
> [UXI-07](UX_Feature_Tool_Model.md#-resolved--cancel-is-declarable-suspend-is-not).
>
> ⚠ **Default `false`** — also today's behaviour, so it is additive and non-breaking.

**`Group` is evidence, not taste.** The sequence *view → edit → destructive, separator before Delete* is
already written identically in `SharedContextMenuPopulator:37-51` **and** `EditorSubsystem.cs:1427-1450`
**and** three other hosts. Nothing enforces it today; it survives only because each host writes its whole
menu in one closure, and it breaks the moment two providers contribute. The enum encodes what is already
true. ❌ **No numeric priority** — `ContextMenuItemDto.Priority` is documented inert (Q26-B).

### 1b. Concurrency — ⭐ **borrowed, not invented**

> **User, 2026-08-10:** *"If actions are async, more can run in parallel. They need similar machinery —
> a flag to force-cancel other running actions, or an action marked exclusive… this must have some usual
> already proven UI resolution; let's take inspiration from other editors and avoid reinventing the
> wheel."*

#### The prior art, and what each one settles

| Product | Mechanism | What it settles for us |
|---|---|---|
| **AutoCAD** *(since the 1980s)* | **Transparent commands** — a command prefixed `'` (`'zoom`, `'pan`) runs **inside** a running command and returns to it. Declared at command definition | ⭐ **Exactly the user's *"recenter the map mid-tool"* case, solved decades ago** — and solved with a **registration-time flag on the command**, which is the shape we already reached |
| **Blender** | **Modal operators** return `RUNNING_MODAL` and keep receiving events until `FINISHED`/`CANCELLED`; the app keeps a **modal handler stack**; operators carry flags (blocking, cursor-grab, undo) | ✅ independently validates **both** the [modal stack](UX_Feature_Tool_Model.md#-the-modal-tool-stack--user-requirement-2026-08-10) **and** registration-time flags |
| **Qt** | Modality is a **three-level enum** — non-modal / window-modal / application-modal — never a bool | ✅ validates `ToolModality` as an enum, and gives the shape for action exclusivity |
| **Reactive streams** (RxJS et al.) | The canonical **named** answer to *"another arrives while one is running"*: **merge** (concurrent) · **switch** (cancel previous, latest wins) · **exhaust** (ignore the new, first wins) · **concat** (queue) | ⭐ **The user's two proposed flags are two of these four.** Adopting the named set costs the same and stops us inventing terms |
| **VS Code** | Long operations run with **progress + a cancel affordance**, not a lock | 🔒 the *obligation* that comes with blocking: if you block, you must show why and offer a way out |

#### ⇒ Two axes, both with established names

```csharp
public enum ActionConcurrency { Concurrent, Restart, Drop, Queue }  // same action dispatched again
public enum ActionExclusivity { None, Exclusive }                   // blocks OTHER actions while running
```

| | Reactive name | Meaning here |
|---|---|---|
| `Concurrent` | merge | default — several may run |
| `Restart` | switch | the new dispatch cancels the running one (latest wins) |
| `Drop` | exhaust | ignored while one is running — the user's *"exclusive"*, per-action |
| `Queue` | concat | serialised |

⚠ **The two axes are not the same question.** `Drop` is *"don't re-enter **me**"*; `Exclusive` is
*"nothing else runs until I finish"*. The user's phrasing merged them; keeping them apart is what lets
*Mark Target* be `Drop` without freezing the whole app.

🔒 **`Exclusive` carries a UX obligation** (the VS Code lesson): it must surface progress and a cancel
affordance, or the app simply looks hung. **Do not ship `Exclusive` without a status-bar presence.**

#### ⚠ Sized to the evidence — and one live bug it fixes

**There are exactly two async handlers in the Editor** — `EditorSubsystem.cs:1462` and `:1479`
(*Mark Target*, *Mark Area Targets*), both `async void`, both the same pick-then-apply kind. So:

| | |
|---|---|
| **Defaults `Concurrent` + `None`** | preserve today's behaviour exactly — additive, non-breaking |
| ⭐ **`Drop` on those two fixes a real defect** | today, invoking *Mark Target* twice starts **two** concurrent picks with no guard. `Drop` is the one policy actually needed now |
| `Restart` / `Queue` | no current consumer. **Implement anyway** — they are one `switch` arm each over the same machinery, and the set is closed and proven rather than speculative |

#### 🔗 And it renames a concept we already had

An action with `CancelsModalTool = false` is **transparent** in AutoCAD's exact sense — it runs without
disturbing the modal tool underneath. **Use that word**: it gives the team a 40-year-old reference point
instead of a bespoke term, and it makes the default (`false` = transparent) self-explanatory.

### 2. Registry — one per subsystem, carries the binding

```csharp
public sealed class EntityActionRegistry            // Hrot.Presentation
{
    public void Register(
        EntityActionDescriptor              descriptor,
        Func<EntityActionContext,bool>?     isVisible      = null,  // default: always
        Func<EntityActionContext,bool>?     isEnabled      = null,  // default: always
        Func<EntityActionContext,string>?   disabledReason = null,
        Func<EntityActionContext,string>?   dynamicLabel   = null,  // "Mark Target for {n} Units..."
        Action<EntityActionContext>?        execute        = null,  // default: publish GlobalActionRequestedEvent
        IReadOnlyList<EntityActionDescriptor>? children    = null);
}
```

`Func<>` predicates, not `bool` — ImGui is immediate-mode and `PopulateMenu` runs **every frame inside
`BeginPopup`**, so per-frame evaluation comes for free. `isVisible` is separate from `isEnabled`: hiding
an item you could explain is worse UX, and 10+ real items need hiding while `BTreeNodeContextMenuProvider`
needs greying (Q26-B).

### 3. Context — opaque to the generic layer

```csharp
public sealed class EntityActionContext
{
    public Entity                Entity             { get; }  // clicked entity, or the current fan-out target
    public IReadOnlyList<Entity> Selection          { get; }
    public ISimulationView       View               { get; }
    public string                CurrentPerspective { get; }  // already a Fdp.Presentation concept
}
```

⚠ **Selection is in the context, not faked by a closure over `_selectionState`** — the current workaround
in every multi-target item (Q26-B).

### 4. Multi-select semantics ([UXR-91](UX_Requirements.md#uxr-91))

| | Rule |
|---|---|
| **Visibility** | **AND over the selection** — shown only if `isVisible` holds for *every* selected entity. A mixed selection shows `Delete`, not `Edit Route` |
| **`PerEntity`** (default) | `execute` runs **once per selected entity** |
| **`Selection`** | `execute` runs **once**, reading `ctx.Selection` — *Mark Target*, *Mark Area Targets*, which a pure fan-out would get wrong |

⚠ **Explicitly ruled out:** show the item and apply it to the applicable subset. Recorded so it is not
"helpfully" relaxed later.

## Where it lives — the precedent already decides this

🔒 **Constraint 1 (no higher-level concept in a generic component) is satisfied by construction, and
`Fdp.Presentation` needs *zero* changes.**

| Layer | Gets | Why |
|---|---|---|
| `Fdp.Presentation` | **nothing new** | the adapter is registered through the existing `IEntityContextMenuHandler` — the panel's view is unchanged |
| **`Hrot.Presentation`** | descriptor · registry · context · the adapter | ⭐ **precedent:** `FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu` is *already* one shared action declaration, bound per subsystem at registration time, and **all five subsystems call it** (`IgSubsystem.cs:128` · `StrideMockSubsystem.cs:112` · `CgfSubsystem.cs:677` · `SimHostSubsystem.cs:235` · `EditorSubsystem.cs:3600`) |
| each subsystem | its `Register(...)` calls | the "profile" — which is the composition root, per Q26-B |

Two existing APIs need **no modification at all**, which is most of why this is `RW-M` and not `RW-H`:

- `IContextMenuBuilder` — `AddItem(label, callback, enabled)` is sufficient once predicates are evaluated
  at populate time. ⚠ *One gap:* it has **no tooltip channel**, so `disabledReason` renders appended to
  the label — deliberate and reversible; extending the builder is an optional follow-up, not a blocker.
- `EntityInspectorPanel` — it **already** routes to the multi-entity overload when `selCount > 1`
  (`:370-377`). The overload is a no-op only because no *handler* overrides it.

## Relation to `GlobalActionRegistry` — build **on**, per Q26-C1

`GlobalActionRegistry` is `int id → (ISimulationView, Entity)`, one registry **per subsystem** (Editor
`:1135` · SimHost `SimHostApp.cs:359` · ReplayBrowser `:204`; CGF and IG construct none).

| Binding style | When | Result |
|---|---|---|
| **Default** — register a `GlobalActionHandler`, leave `execute` null | `PerEntity` actions with a `GlobalActionIds` value | the descriptor publishes `GlobalActionRequestedEvent{Id, target}`; the existing dispatch runs unchanged. Fan-out publishes N events — correct by construction |
| **Closure** — supply `execute` | `Selection`-mode items, and anything needing `ctx` | the handler signature cannot carry a selection, so this escape hatch is required, not optional |

🔒 **No parallel dispatch, no new id space.** ⭐ A side effect worth naming: this is the first thing that
gives **CGF and IG a `GlobalActionRegistry`** — today they have none, which is part of
[UXI-23](UX_Issues.md#uxi-23).

## Migration — strangler, behaviour-identical

| Step | Change |
|--:|---|
| 1 | Add descriptor / registry / context / `EntityActionMenuHandler : IEntityContextMenuHandler`, plus unit tests. **Nothing calls it yet** |
| 2 | Declare the shared descriptors: `CenterOnEntity`, `Select`, `Delete`, `Rotate`, `EditOverlay`, `EditRoute`, `Rename`, `Inspect` — ids from `GlobalActionIds` |
| 3 | Convert **one** host — **CGF** (4 items, all direct calls, smallest surface). Prove the shape |
| 4 | Convert SimHost, then IG, then Editor (largest: 4 handlers, 11 registry entries, 2 `async void`) |
| 5 | Point `SharedContextMenuPopulator`'s single caller (`ContextMenuLogic.cs:121`) at the registry; **delete both copies** of the populator and of `IEntityActionController` |

**Gate at every step:** the menu is *visually identical* before and after — same labels, same order, same
separator. Screenshot Editor / SimHost / CGF.

## 🔒 Out of scope — named so it is not quietly widened

| | Why |
|---|---|
| Unifying the three `Delete` **handlers** | user ruling: the divergence is structural (networkless editor) |
| The map and ORBAT surfaces | that is [UXI-04](UX_Issues.md#uxi-04) / Stage 2 — UXI-03 ships the vocabulary and **one** adapter |
| IG's DDS-authored JSON menu | ruled a separate pipeline (Q26-A2) |
| Adding a confirm on `Delete` | real ([UXI-16](UX_Issues.md#uxi-16)) but a separate issue — **zero** existing items confirm |
| Icons, numeric priority, checked, style | measured speculative (Q26-B) |
| Multi-select *acquisition* | ⚠ **[corrected 2026-08-13](UX_Tasks_Detail.md#corrections)** — ctrl/shift additive click **does** exist, in the **inspector list** (`EntityInspectorPanel.cs:410-437`); it is the **map** that has none. [UXI-24](UX_Feature_Multi_Select.md) is still a prerequisite for exercising UXR-91, not for building this |

## Risks

| | |
|---|---|
| ⚠ **`disabledReason` in the label** | cosmetic compromise; revisit if it reads badly in-app. A Windows session decides |
| ⚠ **`Selection`-mode items are `async void`** today | *Mark Target* / *Mark Area Targets* await an interactive pick. Since the signature is being designed anyway, `execute` should return `Task` so the host can observe failure — closes [UXI-17](UX_Issues.md#uxi-17) for free |
| ⚠ **`Teleport` (`GlobalActionIds.Teleport = 14`) has no consumer found** | declared and possibly never wired. Verify before publishing a descriptor for it |
| ⚠ **`long entityId` ↔ `Entity`** | ExCon's path is network-id native. Its adapter converts at its own boundary — the shared context stays `Entity` |
