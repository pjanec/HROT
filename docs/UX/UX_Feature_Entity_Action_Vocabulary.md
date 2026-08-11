# Feature design — the shared entity-action vocabulary

> **Design for [UXI-03](UX_Issues.md#uxi-03) · drafted 2026-08-10.**
> **Status: ✅ designed — ready to break into `UXT` tasks.**
>
> Implements [UXR-89](UX_Requirements.md#uxr-89) and the descriptor/binding split ruled in
> [Q26](Architect_Question_26_Entity_Action_Model.md) (A2 · B2 · C1 · D). This is
> [Stage 1](UX_Cleanup_Path.md#stage-1--name-the-vocabulary-no-surface-changes-yet).

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
    EntityActionExecution Execution = EntityActionExecution.PerEntity);
```

**`Group` is evidence, not taste.** The sequence *view → edit → destructive, separator before Delete* is
already written identically in `SharedContextMenuPopulator:37-51` **and** `EditorSubsystem.cs:1427-1450`
**and** three other hosts. Nothing enforces it today; it survives only because each host writes its whole
menu in one closure, and it breaks the moment two providers contribute. The enum encodes what is already
true. ❌ **No numeric priority** — `ContextMenuItemDto.Priority` is documented inert (Q26-B).

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
| Multi-select *acquisition* | no ctrl/shift additive click exists anywhere — [UXI-24](UX_Issues.md#uxi-24) is a prerequisite for exercising UXR-91, not for building this |

## Risks

| | |
|---|---|
| ⚠ **`disabledReason` in the label** | cosmetic compromise; revisit if it reads badly in-app. A Windows session decides |
| ⚠ **`Selection`-mode items are `async void`** today | *Mark Target* / *Mark Area Targets* await an interactive pick. Since the signature is being designed anyway, `execute` should return `Task` so the host can observe failure — closes [UXI-17](UX_Issues.md#uxi-17) for free |
| ⚠ **`Teleport` (`GlobalActionIds.Teleport = 14`) has no consumer found** | declared and possibly never wired. Verify before publishing a descriptor for it |
| ⚠ **`long entityId` ↔ `Entity`** | ExCon's path is network-id native. Its adapter converts at its own boundary — the shared context stays `Entity` |
