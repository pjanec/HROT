<!--STATUS
state: LIVE
build-state: PARTIAL
verified: 2026-08-28 (coordinator source scan)
current-answer: PARTIAL. Mechanism built + railed (GlobalMenuRegistry.ResolveBinding, WindowManager.RenderGlobalMenu, CE-041..045). MISSING: no production menu item is perspective-scoped (all register global), and 4 hosts' BeginMainMenuBar blocks stay unguarded so --mode all still stacks them.
-->
# Feature design — the main menu follows focus

> **Design for [UXI-05](UX_Issues.md#uxi-05) · drafted 2026-08-10.**
> **Status: 🟡 PARTIAL — mechanism built + railed (`GlobalMenuRegistry.ResolveBinding`, `WindowManager.RenderGlobalMenu`, CE-041..045); no production menu item is perspective-scoped yet and 4 hosts' `BeginMainMenuBar` blocks stay unguarded.**
>
> Implements [UXR-86](UX_Requirements.md#uxr-86). Vocabulary:
> [Glossary — focus follows perspective](UX_Glossary_Host_Mode_Subsystem.md#-co-running-subsystems-independent-and-focus-follows-perspective).

## 0. Prior art — ✅ checked before designing ([rule 6](UX_Issues.md#rules))

| Exists? | What | Bearing |
|:--:|---|---|
| ⭐ | **`MainToolbarManager`'s perspective filter** — `ToolbarItem.Perspective` (`string?`), set at registration, compared at draw: `item.Perspective == null \|\| item.Perspective == currentPerspective` (`:278-288`) | **This is the model.** Simpler than `WindowScope` — `null` *is* "global", no enum needed |
| ✅ | `ManagedWindow` + `WindowScope` — three-way OR: `Global \|\| _isPinned \|\| OwningPerspective == current` (`:154-168`) | the same idea one layer over; ⚠ its `_isPinned` escape hatch is **not** wanted here (below) |
| ✅ | **`DebugGizmoLayer.DrawMainMenu()`** — the de-duplicated `BeginMainMenuBar`/`DrawMenus`/`End` wrapper (`GizmoMap…/DebugGizmoLayer.cs:379-386`) | 🔴 **exists and not one of the four subsystems calls it** — only `GizmoViewerFrontend.cs:65`, a standalone viewer. [The seam law](UX_Seam_Inventory.md) again |
| ✅ | `MenuItemNode.GetEnabled` (`Func<bool>`) and `DynamicLabel` (`Func<string>`) | the per-frame idiom is **already** in this file — visibility is a sibling, not a new concept |
| ✅ | `WindowManager.CurrentPerspective` is in scope inside `RenderGlobalMenu` | ⇒ **zero new plumbing on the draw side.** It is already used 13 lines later (`:495`) |
| ❌ | Any owner/perspective/scope field on `MenuItemNode`, or any `perspective` parameter on the three `Register*` methods | **the entire gap** |

## ⭐ The union does not come from where the issue says

**`GlobalMenuRegistry` has exactly one production writer — the Editor**, with 10 items, all under
`File/*` (`EditorSubsystem.cs:3519-3538` + `ScenarioMenuCommands.cs:180`). Every other subsystem
registers **zero**. And the Editor **cannot co-run** (validated standalone).

⇒ **The registry is not producing a union today.** The union is produced by something else:

### 🔴 Four copy-pasted `BeginMainMenuBar` blocks, none of which checks focus

| Subsystem | Site |
|---|---|
| Editor | `EditorSubsystem.cs:1918` (block `1914-1926`) |
| SimHost | `SimHostVisualization.cs:376` (block `373-388`) |
| ReplayBrowser | `ReplayBrowserSubsystem.cs:425` (block `421-433`) |
| IG | `IgApplication.cs:1267` (block `1263-1279`) |

Each does `_gizmoLayer?.ConsumeMainMenu()` → and if non-empty, opens its **own** main menu bar and
appends its items. Each fires from its own subsystem update path, gated only on headless flags —
**never on `CurrentPerspective`**. In `--mode all` every composed subsystem appends to the same bar.

> ### ⇒ [UXI-05](UX_Issues.md#uxi-05) and [UXI-13](UX_Issues.md#uxi-13) are one defect seen from two sides
>
> UXI-13 files this as *"copy-pasted ×4, bypassing the overload built for it"*; UXI-05 files it as
> *"the menu is a flat union"*. **The four copies *are* the union.** One fix closes both.

⚠ A fifth direct drawer exists — `ExConMock.cs:196-201` — but it is guarded by `!_panelsWindowManaged`,
i.e. standalone-mock only. Out of scope; note it so it is not "discovered" later.

## The registry half — path is identity, perspective selects the binding

The trie is keyed by path and `RegisterItem` is **last-write-wins** (`TraversePath` returns the existing
node; `:79-87`). Note the sibling registry takes the opposite line:

| | On duplicate |
|---|---|
| `GlobalActionRegistry.Register` | **throws** — *"catches accidental duplicates early"* |
| `GlobalMenuRegistry.RegisterItem` | **silently overwrites** |

⚠ **Latent, not active** — one writer today, so nothing collides yet. But it becomes real the moment this
feature succeeds and a second subsystem contributes. **A visibility flag alone would not fix it: the
losing item never survives registration to be filtered.**

> ### ⭐ The resolution turns the collision into the feature
>
> **Path is the user-facing identity; perspective selects which binding applies.**
>
> Two subsystems both registering `File/Save` is **not** a collision to reject — it is the correct
> outcome. `File/Save` should sit at `File/Save` and do whatever the *focused* subsystem means by it.
>
> ⇒ a leaf holds **bindings, not one action**:
>
> ```csharp
> // MenuItemNode — additive
> public List<MenuBinding> Bindings { get; } = new();   // replaces the single OnClick at leaves
> public sealed record MenuBinding(string? Perspective, Action? OnClick,
>                                  Func<bool>? GetChecked, Action<bool>? OnCheckedChanged);
> ```
>
> **Resolution at draw time, in order:** binding whose `Perspective == CurrentPerspective` → else the
> `null` (global) binding → else **the leaf is not drawn**, and an empty submenu is not drawn either.

### Registration API — one optional parameter, mirroring the toolbar

```csharp
void RegisterItem         (string path, Action onClick,                              string? perspective = null);
void RegisterCheckableItem(string path, Func<bool> get, Action<bool> set,            string? perspective = null);
void RegisterSeparator    (string path,                                              string? perspective = null);
```

`null` = global, exactly as `ToolbarItem.Perspective` already means. **Every existing call site keeps
compiling and keeps its current behaviour** — the Editor's 10 items become global, which is correct for a
subsystem that cannot co-run.

🔒 **No `_isPinned` equivalent.** Windows have it because a user pins a window deliberately; a *menu item*
that ignores focus is the bug being fixed. Do not port the third OR-term.

## Migration

| Step | Change | Gate |
|--:|---|---|
| 1 | `MenuBinding` + `perspective` parameter + draw-time resolution in `RenderGlobalMenu` | **Editor menu byte-identical** — all 10 items are global |
| 2 | Add the focus guard to the four gizmo blocks: skip unless that subsystem's perspective is current | single-subsystem modes unchanged; `--mode all` stops stacking |
| 3 | *(hand to [UXI-13](UX_Issues.md#uxi-13))* replace the four blocks with the existing `DebugGizmoLayer.DrawMainMenu()` | pure de-duplication, no behaviour change |

**Acceptance:** run `--mode all`, switch perspective, and the menu bar's subsystem-dependent entries
change with it while File/Settings/Help/Window stay put — the two-tier rule from the
[Glossary](UX_Glossary_Host_Mode_Subsystem.md#-co-running-subsystems-independent-and-focus-follows-perspective).

⚠ **This is the first requirement in the programme that cannot be verified in a single-subsystem mode.**
`--mode all` (or any comma-list) is mandatory for the walk, and the Editor cannot participate.

## 🔒 Out of scope

| | Why |
|---|---|
| Consolidating the four blocks | [UXI-13](UX_Issues.md#uxi-13) — step 2 makes them correct, step 3 makes them one |
| `ExConMock`'s standalone bar | mock-only, `!_panelsWindowManaged` |
| `RenderPerspectiveMenu()` and the host-menu DTOs | they are *how you switch* perspective — filtering them would strand the user |
| Entity/context menus | [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)/[UXI-04](UX_Feature_Cross_Surface_Actions.md); those already follow focus |

## Risks

| | |
|---|---|
| ⚠ **Perspective names are magic strings** on both sides (`ToolbarItem.Perspective` already is) | a typo silently hides an item **forever**. Register from the same constant the subsystem uses for its windows, and assert it in a test |
| ⚠ **Empty parents** — a `Tools` menu whose children are all filtered out | resolution must skip an intermediate node with no visible descendants, or the bar grows dead headers |
| ⚠ [UXI-06](UX_Issues.md#uxi-06) is adjacent | perspective *restore* validates against subsystem names and drops the Editor's internal ones. If a menu item is bound to `BTree`, a bad restore hides it. Fix UXI-06 first or accept the coupling |
| ⚠ Behaviour change is invisible in every mode we normally test | see the acceptance note above — this needs a deliberate `--mode all` walk on Windows |
