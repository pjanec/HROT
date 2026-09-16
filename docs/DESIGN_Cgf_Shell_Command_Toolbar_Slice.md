<!--STATUS
state: LIVE
build-state: BUILT — `2026-08-26`, ids `CE-037`..`CE-040`. ⭐ §9 AS BUILT carries three argued
  deviations and WINS over §3 where they disagree. Was: READY-TO-BUILD — A2 approved (user, 2026-08-26). Extract a shared TOOLBAR-only common-core
  helper both hosts call; adopt on CGF with a real icon provider; defer the UXI-05 menu to its own slice.
updated: 2026-08-26
current-answer: the whole file. Decision trail + options: Architect_Question_58 (A2 approved).
  Intent basis: UX/UX_Feature_Shell_Parity.md (UXI-35, ruling 58/59) + DESIGN_..._Slice2_Open_Asset.md §7.
known-conflict: shares CgfSubsystem.cs + EditorSubsystem.cs with the UI/CGF lane's in-flight work ⇒
  dispatch IN that lane, sequenced after it; rule-4 re-pull before the final commit.
-->
# DESIGN — **CGF shell-command + main-toolbar adoption** *(CE-016 §7, slice A2)*

> 🎯 Route CGF's main toolbar through the **shared** `ShellEditorCommands → ToolbarCommandAdapter (+IIconProvider)`
> pipeline the editor already runs, so CGF's toolbar carries real icon-bearing command buttons that are
> MCP-observable and assert **SAME** on both hosts — closing **seam-law instance 30** *(ruling 58: the
> shell registries have one writer, the editor)*. ⛔ **Menu (UXI-05) is OUT** — its own slice.

## 1. ⭐ INTENT BASIS *(cited — R-129: intent is in the design, not the code)*
| source | binds this slice |
|---|---|
| `Architect_Question_58_*` | the decision + the three options; **A2 approved** *(user, `2026-08-26`)* |
| `UX/UX_Feature_Shell_Parity.md` (UXI-35, rulings 58/59) | ⭐⭐⭐ **item set is DERIVED, not per-host** — *"One registration list… No per-host menu file, no `if (host==…)`."* Names an `ISubsystemShell` helper. Editor = richest reference; CGF = adopter |
| ruling **49** | a command a host cannot service is **OMITTED**, not greyed |
| ruling **13** | status bar reserved ⇒ time control on the toolbar *(CE-034, already done)* |
| `DESIGN_..._Slice2_Open_Asset.md` §7 | *"Every feature slice's acceptance must include 'its toolbar affordance is present and SAME on CGF.'"* |

## 2. ⭐⭐ INVENTORY — as-is *(codebase-memory graph @ 192k nodes + 3 read-only scans, `2026-08-26`)*
**The whole pipeline EXISTS and is shared; CGF under-adopts it.** *(full enumeration: `Architect_Question_58` §1.)*
- CGF's `WindowManager` already **owns** an (empty) `ShellEditorCommands` and a `MainToolbarManager`.
- CGF today: `MainToolbarTimeControlSection` (`CgfSubsystem.cs:1082`) + **two ad-hoc `ImGui.Button`** entries `"SaveAllAiDocuments"`/`"QuickReloadAiAsset"` (`:1657-1667`, no icons, not commands) + a dangling `ToolbarSep_TimeToPersp` (`:1087`). **No** `ShellEditorCommands` use, **no** `ToolbarCommandAdapter`, **no** `IIconProvider`.
- Editor reference wiring: `EditorSubsystem.cs:4464-4562` — `new SilkIconProvider(windowManager.Atlas)`; register Save/Open/New + AI-debug + compileReload/fullRebuild on `windowManager.ShellCommands`; `ToolbarCommandAdapter.Register(...)` per command; `PerspectiveToolbarSection`. **Sole writer** of the four registries *(ruling 58 / seam-law 30)*.
- ⚠ The **canvas** `/editor/commands` MCP bus is a DIFFERENT `IEditorCommands` instance and already answers on CGF *(MD-008)* — ⛔ not this slice.

## 3. ⭐⭐⭐ WHAT TO BUILD *(A2)*
| # | item | the one thing not to get wrong |
|---|---|---|
| **①** | ⭐ **A shared toolbar common-core helper** `CgfEditorShellToolbar` *(or `ISubsystemShell` toolbar half)* in **`Hrot.Editor.AiShared`** — `RegisterCommonCore(shell, toolbar, icons, hostServices)`: registers the common-core command descriptors on `shell` and `ToolbarCommandAdapter.Register`s each onto `toolbar`, at the editor's ids + sort orders | ⛔⛔ **ONE registration list** *(ruling 58)* — ⛔ NOT a CGF-private copy of the editor's inline list |
| **②** | ⭐⭐ **Extract the editor's inline toolbar wiring** *(`EditorSubsystem.cs:4464-4562`)* to call `CgfEditorShellToolbar.RegisterCommonCore` | ⛔⛔ **behaviour-preserving** — the editor's rendered toolbar entry list is **byte-identical** after; that is this item's gate |
| **③** | ⭐⭐ **Adopt on CGF** — `new SilkIconProvider(windowManager.Atlas)`, call `RegisterCommonCore(windowManager.ShellCommands, windowManager.MainToolbar, icons, cgfServices)`, **delete** the two ad-hoc `ImGui.Button` entries + the dangling separator | ⭐ debug-step handlers route through **CGF's cluster debug controller** *(CE-025..028)*, ⛔ not the editor's `AiDebugCommands` |
| **④** | ⭐ **Flip the conformance rail** — delete the `main-toolbar` known-divergence entry in `ClusterConformanceRails.cs` *(`:256-259`)*; the three-way diff then asserts the **shared subset** is SAME by id+sortOrder+visibility | ⛔ NOT full-array identity — the editor legitimately has more *(fullRebuild, scenario-menu)*; those are **declared absent** on CGF *(ruling 49)* |

**The common-core subset CGF registers** *(§58-B)*: Save · SaveAll · Open Asset · New Asset · QuickReload — CGF already holds the services *(save/reload CE-022; create/recipe MA-019..023)* — plus AI-debug step routed through CGF's controller. **OMIT** `fullRebuild` + scenario-menu on CGF *(declared absent)*.

## 4. ⭐⭐ CLASS DIAGRAM *(authoritative)*
```mermaid
classDiagram
    class IEditorCommands {
        <<interface · NodeEditor.Core>>
        +All() IReadOnlyList
        +Get(id) EditorCommandDescriptor
        +Invoke(id, ctx) EditorCommandResult
    }
    class ShellEditorCommands {
        <<Fdp.Presentation · WindowManager-owned · EXISTS>>
        +Register(descriptor, action)
    }
    class ToolbarCommandAdapter {
        <<static · Fdp.Presentation · EXISTS>>
        +Register(toolbar, commands, id, iconProvider, sortOrder)$
        +GetState(commands, id)$ ToolbarCommandState
    }
    class MainToolbarManager {
        <<Fdp.Presentation · EXISTS>>
        +RegisterEntry(id, sortOrder, height, render)
        +BuildViewModel() MainToolbarPanelViewModel
    }
    class IIconProvider {
        <<interface · NodeEditor.Core>>
        +TryGet(key, out handle) bool
    }
    class SilkIconProvider {
        <<Hrot.Editor.AiShared · EXISTS>>
        +SilkIconProvider(IconAtlas)
    }
    class CgfEditorShellToolbar {
        <<NEW · Hrot.Editor.AiShared · the ONE derived common-core list>>
        +RegisterCommonCore(shell, toolbar, icons, hostServices)$
    }
    class EditorSubsystem {
        <<Hrot.Editor · reference — calls the helper after extraction>>
    }
    class CgfSubsystem {
        <<Hrot.CGF · adopter — calls the helper>>
    }
    ShellEditorCommands ..|> IEditorCommands
    SilkIconProvider ..|> IIconProvider
    CgfEditorShellToolbar ..> ShellEditorCommands : registers common-core onto
    CgfEditorShellToolbar ..> ToolbarCommandAdapter : per command
    ToolbarCommandAdapter ..> MainToolbarManager : RegisterEntry
    ToolbarCommandAdapter ..> IIconProvider : TryGet(IconKey)
    EditorSubsystem ..> CgfEditorShellToolbar : calls (was inline)
    CgfSubsystem ..> CgfEditorShellToolbar : calls
    CgfSubsystem ..> SilkIconProvider : constructs from WM.Atlas
```

## 5. ⭐⭐ SEQUENCE DIAGRAM *(authoritative — the CGF adoption path)*
```mermaid
sequenceDiagram
    participant CGF as CgfSubsystem.RegisterWindows
    participant Helper as CgfEditorShellToolbar
    participant Shell as WM.ShellCommands
    participant Adapter as ToolbarCommandAdapter
    participant Bar as WM.MainToolbar
    participant Icons as SilkIconProvider

    CGF->>Icons: new SilkIconProvider(WM.Atlas)
    CGF->>Helper: RegisterCommonCore(Shell, Bar, Icons, cgfServices)
    Helper->>Shell: Register(save/open/new/reload/step descriptors + handlers)
    Note over Helper: debug-step handlers -> CGF's cluster debug controller (CE-025..028)
    loop each common-core command CGF can service
        Helper->>Adapter: Register(Bar, Shell, id, Icons, sortOrder)
        Adapter->>Shell: Get(id)
        Adapter->>Bar: RegisterEntry(id, sortOrder, render)
    end
    Note over Bar: BuildViewModel() now dumps the shared subset with icons -> main-toolbar PanelKind
    Note over CGF: fullRebuild + scenario-menu OMITTED (ruling 49: absent, not greyed)
```

## 6. ⭐ ACCEPTANCE / RAILS
- CGF's `main-toolbar` PanelKind dumps the shared-subset entries **with icons**, by id+sortOrder; the `main-toolbar` known-divergence entry is **deleted** ⇒ the three-way conformance rail asserts SAME on the shared subset.
- The editor's rendered toolbar entry list is **unchanged** after the extraction *(the byte-identical gate — a headless `MainToolbarManager.BuildViewModel` diff before/after)*.
- Each registered command **invokes** through `IEditorCommands.Invoke` *(headless `ToolbarCommandAdapter.GetState` rail)*; debug-step reaches CGF's cluster controller.
- Omitted commands *(fullRebuild, scenario-menu)* are **declared absent**, not greyed *(ruling 49)*.

## 7. ⭐ LANE & GATES
⭐ **Dispatch to the UI/CGF lane** *(owns `CgfSubsystem.cs` + `EditorSubsystem.cs` — so the extraction is IN-lane)*, sequenced **after** its current work. ⛔ Does not touch the diagnostics lane's files. ⭐ **rule-4 re-pull** before the final commit *(both shared files are hot)*.
Build the AFFECTED projects *(`Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF` · `Hrot.SystemTests`)* — ⛔ never the whole solution in the fix loop. Gates per the rule-8 contract; the conformance rail is **T3 — background it**. Obligation ⑤: fold any as-built deviation back into **this** doc before the batch closes.

## 8. ⛔ EXPLICITLY OUT
- **The menu (UXI-05 menu-follows-focus)** — its own slice; when built, its menu registration **extends this same helper** *(not a second list)*.
- The canvas `/editor/commands` bus *(MD-008 — already answers on CGF)*.
- `PerspectiveToolbarSection` on CGF is **optional here**: if the dangling `ToolbarSep_TimeToPersp` is removed, no perspective section is required for A2; adding it is a cheap round-out if CGF has a perspective switcher wired *(it does — `WindowManagerPerspectiveSwitcher`)* — implementer's call, reported either way.

---

# ⭐⭐⭐ 9. AS BUILT — `2026-08-26`, ids `CE-037`…`CE-040` *(obligation ⑤)*

> ⭐⭐ **All four items shipped.** ⛔ **Three deviations from §3, each measured and argued** — this section
> wins over §3 where they disagree.

## 9.1 ⭐ THE MECHANISM — **derived, not declared**

⭐⭐⭐ `CgfEditorShellToolbar.RegisterCommonCore` emits a toolbar entry **only for a command the host's
shell can service** *(`IEditorCommands.Get(id) != null`)*. ⇒ ⛔ there is no CGF list and no editor list:
the editor registers more commands, so it gets more buttons, **from the same table**.
📌 **Ruling 49 falls out by construction** — a command a host cannot service is ABSENT because nothing
registers an entry for it, ⛔ not greyed by a per-host branch.

⭐⭐ **A separator is emitted only when the group it brackets emitted something.** ⇒ the dangling-rule
defect this slice removes from CGF *(`ToolbarSep_TimeToPersp`)* cannot recur.
⚠ **A separator names the group whose presence JUSTIFIES it** — it may trail that group
*(`ToolbarSep_OpenAsset` after File)* or lead it *(`ToolbarSep_PerspToAiDebug` before AI-debug)*.
🔴 **The first cut gave `ToolbarSep_OpenAsset` a group of its own, so nothing ever made it live and it
vanished from the EDITOR** — caught by the byte-identical gate before anything shipped.

## 9.2 ⛔⛔ DEVIATION 1 — **NO Save-All BUTTON** *(§3's subset sentence)*

§3 lists the CGF subset as *"Save · SaveAll · Open Asset · New Asset · QuickReload"*.
📐 **Measured: the editor's toolbar has NO Save-All button** — only `shell.save` at `-9`.
⇒ ⛔ a `SaveAll` slot would emit it on the **EDITOR** too *(its shell registers `shell.saveAll`)*,
breaking item ②'s byte-identical gate and changing a UI nobody asked to change.
⭐ **The gate wins.** `SaveAllId` stays exposed as a constant, and the rail asserts the button's ABSENCE,
so adding it later is a deliberate two-host decision rather than a silent drift.

## 9.3 ⛔⛔⛔ DEVIATION 2 — **the AI-DEBUG GROUP IS OMITTED ON CGF**

§3 ③ says *"debug-step handlers route through **CGF's cluster debug controller** (CE-025..028)"*.
📐 **Measured: those are two different concepts.**

| | binds to | means |
|---|---|---|
| the editor's `debug.*` ids | ⭐ **`IDebugSessionRegistry.ActiveSession`** *(`AiDebugCommands`, every one of them)* | stepping an **AI GRAPH** debug session — breakpoints in a blueprint/BTree |
| `CgfClusterDebugTimeController` | `IEngineDebugTimeController` | **CLUSTER TIME** — `RequestPause` / `RequestResume` / `RequestStepOneTick` |

⛔ **CGF has no AI debug session at all** — this very file passes `session: null` to `QuickReloadService`
*(slice 1 §9.4)*. ⇒ ⭐⭐⭐ **binding the time controller to `debug.continue`/`debug.pause` would make ONE ID
MEAN TWO DIFFERENT THINGS across the two hosts — and the conformance rail asserts SAME *by id*.** That is
the opposite of what this slice exists to do.
⭐ **Cluster-time stepping already has its own affordance on CGF:** `MainToolbarTimeControlSection`.
⇒ if AI-debug parity is wanted later, it needs **CGF to host a debug session**, not an id re-binding.

## 9.4 ⚠ DEVIATION 3 — **Open / New omitted on CGF**

📐 CGF composes no `AssetPickerLauncher` and no `NewAssetLauncher` — the editor builds both from catalogs
and a router this host does not wire. ⇒ ruling 49: **absent, not greyed**.
⚠ **CGF can still CREATE assets over MCP** *(`MA-019`…`023`)*; what it lacks is an interactive picker.

## 9.5 ⭐⭐ WHAT CGF GAINED

⛔ **Deleted:** two ad-hoc `ImGui.Button` entries *(`"Save All"` / `"Reload AI"`, sortOrder 10/11 — no
icon, no command id, no enablement, no MCP identity)* and the dangling `ToolbarSep_TimeToPersp`.
⭐ **Registered instead:** `shell.save` *(via the shared `ShellSaveCommands.Register`)* and
`blueprint.compileReload`, at the editor's own ids and sort orders, with real icons.
⚠ CGF's Save-As is a **no-op with a logged reason** — it needs a modal browser this host does not compose;
⛔ the shared command is registered either way rather than crashing a headless node.

## 9.6 ⭐⭐⭐ THE GATE, AND WHAT IT CAUGHT

`TheToolbarLayoutIsOneListTests` *(5 tests, headless, ~30 ms)* — the pre-extraction entry list asserted by
**id AND sortOrder**, the derivation, the separator rule, the null-toolbar case, and the **id mirror**.

⚠⚠ **It caught two real defects in the helper before anything shipped:**
| 🔴 | |
|---|---|
| the separator group *(§9.1)* | `ToolbarSep_OpenAsset` disappeared from the editor |
| **guessed ids** | the first draft wrote `"ai.debug.continue"`; the real id is **`"debug.continue"`** |

⛔⛔ **The id drift is the dangerous one and it is SILENT:** the derived-subset rule means an id nothing
registers simply yields no button ⇒ **a typo deletes the whole AI-debug toolbar group and fails nothing.**
⭐ `The_mirrored_debug_ids_match_their_source_of_truth` is why that can happen only once — the mirror
exists because `AiShared` sits **below** `Hrot.Blueprints.Editor` and cannot reference `AiDebugCommands`.
