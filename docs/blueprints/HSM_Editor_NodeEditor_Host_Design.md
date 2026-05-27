# HSM Editor — NodeEditor Host Detailed Design

> **Status:** Detailed design, derived from `AI_Editor_Shared_Infrastructure.md` + `NodeEditor_Extension_NodeAttachments.md` + `NodeEditor_Extension_ContainerNodes.md` + `NodeEditor_Extension_CustomCanvasRenderer.md` + `BTree_Editor_NodeEditor_Host_Design.md` (for parallel patterns) + `FastHSM.txt` source.
> **Audience:** Implementation agent and human reviewer.
> **Drives:** The `Hrot.Hsm.Editor` assembly — the HSM-specific host code that plugs into NodeEditor and the shared AI editor infrastructure.
> **Doesn't cover:** Kernel internals (owned by FastHSM). NodeEditor primitives (owned by NodeEditor). Shared editor infrastructure (owned by `AI_Editor_Shared_Infrastructure.md`). Runtime debug protocol implementation against the HSM kernel (sketched in §13; kernel-side details elsewhere).
> **Companion code lives in:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/` — host services, projection, emitter, runtime debug session, five custom canvas renderers.

---

## Table of Contents

1. Scope and design goals
2. The shape, in one picture
3. Asset model and projection
4. The fluent C# emitter
5. NodeEditor host services
6. Containers — composite states and parallel composites
7. Transitions — pin-based or first-class?
8. Initial-state markers, history pseudo-states, final states
9. Events table and event scoping
10. Action / Guard pickers and OutputLaneMask inference
11. Inspectors and facet structs
12. Validation
13. Runtime debug session and overlay
14. Trace timeline lanes
15. Custom canvas renderers — full list
16. Quick reload pipeline
17. Slice plan
18. Test strategy
19. Open questions

---

## 1. Scope and design goals

### 1.1 What this assembly owns

`Hrot.Hsm.Editor` is the HSM-specific host code. Like its BTree sibling, most heavy lifting (canvas, selection, inspector, refactor, debug-session base, fluent emitter framework) lives in shared infrastructure or NodeEditor.

What this assembly owns:

- **`HsmAsset`** — the in-editor model of an HSM asset, projecting from `HsmDefinitionBlob` + `MachineMetadata` + the layout method.
- **`HsmGraphModel`** — the `IGraphModel` adapter exposing `HsmAsset` to NodeEditor's canvas; uses `IContainerNodeModel` (per `NodeEditor_Extension_ContainerNodes.md`) for composite states.
- **`HsmCommandSink`** — translates NodeEditor `GraphCommand` records into edits on `HsmAsset` plus deferred file regeneration.
- **`HsmNodeCatalog`** — the `INodeCatalog` providing palette content; static state/region/transition kinds plus dynamic action/guard entries from `HsmActionDispatcher`.
- **`HsmFluentEmitter`** — produces the deterministic C# (fluent builder method + `[HsmLayout]` method) from `HsmAsset` on save.
- **`HsmAssetContributor`** — implements `IAssetCatalogContributor`; reflects the loaded assembly for `[HsmDefinition]` methods.
- **`HsmRuntimeInspectorPane`** — the HSM-specific pane plugged into the shared Runtime Inspector window.
- **`HsmTraceLaneProvider`** — registers the HSM-specific trace lanes.
- **`HsmDebugSession`** — implements `IHsmDebugSession : IAiDebugSession`.
- **`HsmFacetMapper`** — produces the StructEdit facet structs (`StateFacet`, `TransitionFacet`, `RegionFacet`, etc.).
- **Five custom canvas renderers** (per the CustomCanvasRenderer extension §16.1): `hsm.transition_labels`, `hsm.initial_state_arrows`, `hsm.region_conflicts`, `hsm.history_glyphs`, `hsm.runtime_overlay`.

### 1.2 What this assembly does NOT own

- **`HsmDefinitionBlob`**, `InstanceHeader`, `StateDef`, `TransitionDef`, `RegionDef`, the kernel, the hot reload manager — all owned by `Fhsm.Kernel` / `Fhsm.Compiler` / `Fhsm.HotReload`.
- **The canvas** — NodeEditor.UI renders the canvas; this host provides the model behind it.
- **The Inspector, Asset Browser, Runtime Inspector, Trace Timeline, Find Results windows** — all shared.
- **The fluent builder API** (`HsmBuilder`, `StateBuilder`, `TransitionBuilder`) — defined in `Fhsm.Compiler`. Editor emits C# that calls it; editor doesn't extend it (but a small extension is needed — see §1.4 and §19 open questions).

### 1.3 Design goals

- **Author writes fluent C#; editor projects from compiled assembly.** Same model as BTree. The asset is its `.cs` file plus the layout sibling method.
- **Statechart-style nested rendering.** Composite states visually contain their children; orthogonal regions are dashed dividers; transitions connect state nodes with labels at midpoints. Uses NodeEditor's `IContainerNodeModel` extension and the `CustomCanvasRenderer` extension for labels and overlays.
- **Transitions are first-class but rendered as links.** Each transition is a NodeEditor `ILinkModel` plus a sidecar metadata record carrying the HSM-specific fields (event ID, guard function, action function, priority, kind).
- **OutputLaneMask is inferred, not authored.** Each `[HsmAction]` declares its target `CommandLane` (or none); the flattener unions lanes from a state's `OnEntry`, `OnExit`, `Activity` actions into the state's `OutputLaneMask`. Inspector shows the result read-only.
- **Global transitions live outside the canvas.** They appear in the Events table and a globals strip; clicking a global highlights its target state.
- **Quick Reload ≤ 100 ms.** Tier-classified Cosmetic / Soft / Hard per shared infra §17.
- **Live debug overlay correlates via `StableId`.** The runtime's active leaves (a bitset of `FlatIndex` values) symbolicate through `MachineMetadata` to state names; the editor matches by `StableId` from the layout method.
- **All three NodeEditor extensions are used.** NodeAttachments for state-flag badges (history pseudo-states are state-flag-driven; deferred-event indicators are pills); ContainerNodes for composite/parallel state rendering; CustomCanvasRenderer for transitions, initial-state arrows, conflicts, runtime overlay.

### 1.4 Required kernel-side additions

Two small additions to `Fhsm.Compiler` are needed for the editor to round-trip cleanly:

- **`stableId : Guid` parameter on `HsmBuilder.State(name, stableId)`** and on `StateBuilder.AddChild(name, stableId)`. Defaults to `Guid.NewGuid()` so existing handwritten code stays valid. Today `StateNode.StableId` exists in the editor-side graph but isn't surfaced through the builder API.
- **`visualId : Guid` parameter on `TransitionBuilder.GoTo(target, visualId)`** and on `HsmBuilder.GlobalTransition(...)`. Same defaulting. Today `TransitionNode` has no Guid.

Plus one attribute-property addition:

- **`Lane = CommandLane.X` property on `[HsmAction]`** — needed for OutputLaneMask inference (§10). Additive; defaults to a "no lane / inferred" sentinel.

All three are small builder API extensions; track as kernel-side tickets to land before Slice 1 codes against them.

---

## 2. The shape, in one picture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ File  Edit  View  Debug   [▶ Play] [⏸] [⏯ Step] [↻ Reset] [⏺ Record]          │
│                           Mode: ( ) Release  ( ) Debug  (●) Trace             │
├────────────────┬─────────────────────────────────────────┬───────────────────┤
│ ASSET BROWSER  │ HSM CANVAS — Combat/EnemyBrain.hsm      │ INSPECTOR         │
│ (shared)       │                                          │ (shared)          │
│                │  ┌──────── EnemyBrain ──────────────┐   │ Selected:         │
│ ▼ Combat       │  │ ⦿─→[Idle] ──OnSight──→[Alert]   │   │   Transition #14  │
│  • EnemyBrain  │  │                            │     │   │ ─────────────     │
│  • BossPhases  │  │   ┌── Alert (parallel) ────┴───┐ │   │ Event: OnLostS… ▾ │
│ ▼ Patrol       │  │   │ ╔ Locomotion ════════════╗ │ │   │ Guard: AmmoOk  ▾  │
│  • Patrol_BT   │  │   │ ║ ⦿→[Walk]──→[Sprint]    ║ │ │   │ Effect:Stash…  ▾  │
│ ▼ Blueprints   │  │   │ ╚════════════════════════╝ │ │   │ Priority:  [128]  │
│  • DoorActor   │  │   │ ╔ Combat ════════════════╗ │ │   │ Kind: ( )Ext      │
│                │  │   │ ║ [Aim 🕓OnHit]──Fire──→ ║ │ │   │       ( )Int      │
│ + New HSM      │  │   │ ║  H            ⚠2 lanes ║ │ │   │       (●)Local    │
├────────────────┤  │   │ ╚════════════════════════╝ │ │   │ ☐ Breakpoint      │
│ EVENTS         │  │   └── OnLostSight──→[Search]──┘ │   │                   │
│ ID  Name  ⚡🕓🔗│  │                                  │   │ LCA: Alert        │
│ 01  OnSight    │  └──────────────────────────────────┘   │ LCA cost: 4       │
│ 02  OnLostSig  │                                          │ ─────────────     │
│ 03  Fire       │  Globals: ⚡ Die from anywhere → [Dead]   │ OutputLanes:      │
│ 04  Reload  🕓 │                                          │  Animation (Stash)│
│ 05  Die  ⚡    │  [Quick Reload] [Save & Rebuild]         │  Navigation (Walk)│
│ + Add Event    │                                          │  (read-only)      │
└────────────────┴─────────────────────────────────────────┴───────────────────┘
┌──────────────────────────────────────────────────────────────────────────────┐
│ TRACE TIMELINE (shared)  ◄ tick 412 / 1024 ►  Phase: ░Entry ▓RTC ░Activity   │
│ States      │ ░░░░░░▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░▓▓▓▓░░░  Alert.Combat.Aim       │
│ Events      │ ┄┄┄┄┄┄┄┄│OnSight┄┄┄┄┄┄┄│OnLost┄┄┄┄┄┄┄│Fire ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄ │
│ Actions     │ ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄└Aim┄┄┄┄┄┄┄┄┄┄┄└Reload┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄ │
│ Guards      │ ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄└AmmoOk(t)┄┄┄┄┄┄┄└LowHP(f)┄┄┄┄┄┄┄┄┄┄┄┄┄┄ │
│ Conflicts   │                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

Six shared windows (Asset Browser, Inspector, Runtime Inspector, Trace Timeline, Find Results, Hot Reload Status) plus three HSM-specific (Canvas, Events table, optional Globals strip).

---

## 3. Asset model and projection

### 3.1 The two-sided model

Like BTree, an HSM asset has two synchronized representations:

- **Kernel side**: `HsmDefinitionBlob` (containing `HsmDefinitionHeader`, `StateDef[]`, `TransitionDef[]`, `RegionDef[]`, `GlobalTransitionDef[]`, `LinkerTable`) plus `MachineMetadata`. Loaded from the assembly. Read-only.
- **Editor side**: `HsmAsset`. Mutable. Tracks layout, breakpoints (per-user session), comments, the editor-side identity of each state / transition / region.

```csharp
namespace Hrot.Hsm.Editor.Model;

public sealed class HsmAsset : IEditableAsset
{
    public Guid AssetId { get; }
    public string Name { get; set; }
    public AssetKind Kind => AssetKind.Hsm;
    public string SourceFilePath { get; }
    public bool IsDirty { get; private set; }
    public bool IsEditorOwned { get; }

    public HsmDefinitionBlob Blob { get; }
    public MachineMetadata Metadata { get; }

    public StateNode RootState { get; }                              // synthetic root (always present)
    public IReadOnlyList<StateNode> AllStates { get; }
    public IReadOnlyList<TransitionNode> AllTransitions { get; }
    public IReadOnlyList<GlobalTransitionNode> AllGlobalTransitions { get; }
    public IReadOnlyList<RegionNode> AllRegions { get; }
    public IReadOnlyList<EventDefinition> AllEvents { get; }

    public Vector2 CanvasPanOffset { get; set; }
    public float CanvasZoomLevel { get; set; }

    public event Action? Changed;
}

public sealed class StateNode  // editor-side, augments the compiler-side StateNode
{
    public Guid StableId;                          // primary editor identity
    public ushort FlatIndex;                       // re-derived per reload; index into Blob.States
    public string Name;
    public StateNode? Parent;
    public List<StateNode> Children { get; } = new();
    public List<TransitionNode> OutgoingTransitions { get; } = new();
    public List<RegionNode> Regions { get; } = new();

    public bool IsInitial;
    public bool IsHistory;
    public bool IsDeepHistory;
    public bool IsParallel;
    public bool IsFinal;

    public string? OnEntryAction;
    public string? OnExitAction;
    public string? ActivityAction;
    public string? TimerAction;

    public byte OutputLaneMask;                    // inferred from action declarations; read-only in editor

    public List<ushort> DeferredEventIds { get; } = new();   // events deferred while in this state

    // Editor-only (persisted in layout method)
    public Vector2 Position;
    public Vector2? Size;
    public string? Comment;
    public bool IsCollapsed;
    public string? ColorOverride;

    // Editor-only ephemeral (not persisted in layout method)
    public bool IsBreakpoint;                      // session-local
}

public sealed class TransitionNode
{
    public Guid VisualId;                          // primary editor identity
    public ushort FlatIndex;                       // re-derived per reload; index into Blob.Transitions

    public StateNode Source;
    public StateNode Target;
    public ushort EventId;
    public string? EventName;                      // for display; symbolicated from MachineMetadata
    public string? GuardFunction;
    public string? ActionFunction;
    public byte Priority;
    public TransitionKind Kind;                    // External | Internal | Local
    public ushort SyncGroupId;

    public List<Vector2> Waypoints { get; } = new();   // editor-only; for routing
    public string? Comment;
    public bool IsBreakpoint;                      // session-local
}

public enum TransitionKind { External, Internal, Local }

public sealed class GlobalTransitionNode
{
    public Guid VisualId;
    public ushort FlatIndex;                       // index into Blob.GlobalTransitions
    public StateNode Target;
    public ushort EventId;
    public string? EventName;
    public string? GuardFunction;
    public string? ActionFunction;
    public byte Priority;
    public string? Comment;
    public bool IsBreakpoint;
}

public sealed class RegionNode
{
    public Guid StableId;                          // separate identity from parent state
    public byte RegionIndex;                       // 0..(RegionCount-1) within parent state
    public string Name;                            // editor-only; not in the kernel
    public byte Priority;
    public StateNode? InitialChild;                // the initial child of this region

    // Editor-only
    public string? Comment;
    public string? ColorOverride;
}

public sealed class EventDefinition
{
    public ushort EventId;
    public string Name;
    public int PayloadSize;                        // 0, 4, 12, or 16 bytes
    public bool IsIndirect;
    public bool IsDeferrable;                      // whether some state defers this
    public EventPriority Priority;
    public bool HasGlobalTransition;               // computed from GlobalTransitions
}
```

### 3.2 Projection from compiled assembly to editor model

On asset open:

1. The asset catalog (shared infra) provides the `HsmAsset` shell from reflection of the `[HsmDefinition]` method.
2. The contributor invokes the `Compile()` method on the assembly to get `HsmDefinitionBlob` + `MachineMetadata`.
3. The projection walker iterates `Blob.States[]` in order; builds an editor `StateNode` per kernel state, using `StateDef.ParentIndex` / `FirstChildIndex` / `ChildCount` to reconstruct the hierarchy.
4. `StableId` for each state is resolved by:
   - If the layout method is present, lookup by `FlatIndex → StableId` via a map provided in the layout entry (the layout method's `State(stableId, …)` entries are keyed by stable Guid; matching them to FlatIndex requires a `Name → FlatIndex` lookup since names are stable).
   - If absent, mint a fresh Guid and log a "first save will persist" diagnostic.
5. Similarly for transitions (`VisualId`) and regions (`StableId`).
6. Events are read from `MachineMetadata.EventNames` plus any `[HsmDefinition]` attribute-level event declarations.
7. Layout method is invoked via `LayoutDiscovery.TryGetLayout<HsmLayoutAttribute, HsmEditorLayout>`. Each `HsmStateLayoutEntry` / `HsmTransitionLayoutEntry` / `HsmRegionLayoutEntry` matches by Guid; populates positions, waypoints, sizes, comments.
8. `OutputLaneMask` per state is recomputed from action declarations (see §10.3); stored on `StateNode`.

### 3.3 Identity bridges

```csharp
public sealed class HsmAsset
{
    private Dictionary<Guid, StateNode> _stableIdToState;
    private Dictionary<Guid, TransitionNode> _visualIdToTransition;
    private Dictionary<Guid, RegionNode> _stableIdToRegion;
    private Dictionary<ushort, StateNode> _flatIndexToState;       // re-derived per reload
    private Dictionary<ushort, TransitionNode> _flatIndexToTransition;
    private Dictionary<ushort, EventDefinition> _eventIdToEvent;
}
```

Three Guid → editor-model lookups, plus two flatIndex → editor-model lookups for runtime correlation. All built once on projection; invalidated and rebuilt on reload.

### 3.4 Auto-layout — statechart layout

For newly authored states (no layout method entry), the editor runs a hierarchy-aware statechart layout:

1. Root states laid out left-to-right, vertically centered, with 200 px spacing.
2. Children of a composite state laid out inside the composite's interior using a simple grid (rows of 3 by default, 40 px spacing).
3. Parallel composites: each region laid out as a horizontal strip, children inside positioned grid-wise within the strip.
4. Container bounds (per `NodeEditor_Extension_ContainerNodes.md` §5) auto-resize to enclose children.

Auto-layout runs only when opening an asset with missing layout entries. A "Reset layout" toolbar action re-runs across the whole machine.

For typical HSMs (15–80 states, 3–5 levels of nesting), the result is readable but not optimal. Users will manually arrange to taste; the layout method preserves those arrangements.

---

## 4. The fluent C# emitter

### 4.1 Emit shape

`HsmFluentEmitter : IFluentCSharpEmitter<HsmAsset>` produces a deterministic `.cs` file mirroring the `TrafficLightExample` shape from FastHSM.txt:

```csharp
// HROT_EDITOR_GENERATED — manual edits to this file will be overwritten by the AI editor on next save.
// AssetId: a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29

using System;
using System.Numerics;

using Fhsm.Compiler;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Hrot.AI.Behaviors.Machines.Layout;
using Hrot.Game.Combat;

namespace Hrot.AI.Behaviors.Machines;

public static class EnemyBrain
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder("EnemyBrain");

        // Events (id-stable; order matches event registration in the editor)
        builder.Event("OnSight",     eventId: 1);
        builder.Event("OnLostSight", eventId: 2);
        builder.Event("Fire",        eventId: 3, payloadSize: 12);
        builder.Event("Reload",      eventId: 4, isDeferred: true);
        builder.Event("Die",         eventId: 5, payloadSize: 4);

        // Action and guard registrations (alphabetical FQN; only those used by this machine)
        builder.RegisterAction("Hrot.Game.Combat.CombatActions.EnterIdle");
        builder.RegisterAction("Hrot.Game.Combat.CombatActions.StashWeapon");
        builder.RegisterAction("Hrot.Game.Combat.CombatActions.WalkActivity");
        builder.RegisterGuard ("Hrot.Game.Combat.CombatGuards.AmmoOk");

        // States — emitted in stable order: depth-first, children in source-of-truth order
        builder.State("Idle", stableId: new Guid("b471-..."))
               .Initial()
               .OnEntry("Hrot.Game.Combat.CombatActions.EnterIdle")
               .On(1).GoTo("Alert", visualId: new Guid("c5e8-..."));

        builder.State("Alert", stableId: new Guid("d091-..."))
               .Parallel()
               .Region("Locomotion", priority: 1, stableId: new Guid("e2a3-..."))
                   .InitialChild("Walk")
                   .AddChild("Walk", stableId: new Guid("f7c0-..."))
                       .Activity("Hrot.Game.Combat.CombatActions.WalkActivity")
                   .AddChild("Sprint", stableId: new Guid("a4b5-..."))
               .EndRegion()
               .Region("Combat", priority: 2, stableId: new Guid("c6d7-..."))
                   .InitialChild("Aim")
                   .AddChild("Aim", stableId: new Guid("e8f9-..."))
                       .DeferEvent(4)  // defer Reload while in Aim
                       .On(3).GoTo("Reloading", guardName: "Hrot.Game.Combat.CombatGuards.AmmoOk",
                                                 visualId: new Guid("0a1b-..."))
                   .AddChild("Reloading", stableId: new Guid("2c3d-..."))
                       .OnEntry("Hrot.Game.Combat.CombatActions.StashWeapon")
               .EndRegion()
               .On(2).GoTo("Search", visualId: new Guid("4e5f-..."));

        builder.State("Search", stableId: new Guid("6071-...")) /* ... */ ;
        builder.State("Dead",   stableId: new Guid("8293-..."))
               .IsFinal();

        // Global transitions
        builder.GlobalTransition(eventId: 5, target: "Dead",
                                  priority: 255,
                                  visualId: new Guid("a4b5-..."));

        return builder;
    }

    [HsmDefinition("EnemyBrain", AssetId = "a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .Canvas(panOffset: new Vector2(0f, 0f), zoomLevel: 1.0f)
        .State("b471-...", position: new Vector2(100f, 120f))
        .State("d091-...", position: new Vector2(280f, 120f), size: new Vector2(420f, 240f))
        .State("e2a3-...", /* region, only StableId + comment */)
        .State("e8f9-...", position: new Vector2(310f, 200f), comment: "burst-fire entry point")
        .Transition("c5e8-...", waypoints: new[] { new Vector2(220f, 130f) })
        .Transition("4e5f-...", label: null)
        .Build();
}
```

Three methods: `CreateBuilder()` (the fluent), `Compile()` (the `[HsmDefinition]` thunk), `Layout()` (editor-only).

### 4.2 Deterministic emit rules (HSM-specific)

Shared emitter rules (`AI_Editor_Shared_Infrastructure.md` §6.2) plus HSM-specific:

1. **Events emit first**, in EventId ascending order.
2. **Action and guard registrations emit second**, alphabetical by FQN. Only those *referenced* by this machine are registered (the registry call in the fluent builder is informational; the kernel doesn't require it but the source generator may).
3. **States emit in depth-first order from `RootState`'s children.** Children of a composite emit in their model order (which is the order the user authored them and matches the order shown in the canvas children-list).
4. **State configuration emits in a stable subsection order per state**: `Initial()` → `IsFinal()` → `Parallel()` → `OnEntry()` → `OnExit()` → `Activity()` → `TimerAction()` → `DeferEvent()` calls → outgoing transitions → child states (if not parallel) or regions (if parallel).
5. **Region configuration emits**: `Region(name, priority, stableId)` → `InitialChild(name)` → `AddChild(name, stableId)` calls → `EndRegion()`.
6. **Transitions emit using `.On(eventId)` for normal, `.OnCompletion()` for completion-trigger transitions (eventId 0).** Optional fluent: `.WithGuard(...)`, `.WithAction(...)`, `.WithPriority(...)`, `.AsInternal()` / `.AsLocal()` (default external).
7. **Global transitions emit at the end**, alphabetical by EventId.
8. **Floats use `f` suffix**, EventIds use `ushort` literal context (no suffix needed; emit as `1`, `2`, etc.), priorities as `byte` literals (`128`, `255`).

### 4.3 Layout method emission

Layout entries are sorted by Guid lexicographic ordering (stable across runs). Each entry emits:

- `State(stableIdString, position?, size?, comment?, collapsed?, color?)` — omitted fields default.
- `Transition(visualIdString, waypoints?, label?, comment?)`.
- `Region(stableIdString, color?, comment?)` — regions usually have no position (they're laid out automatically inside their parent state); the entry exists primarily for color overrides and comments.

### 4.4 Round-trip property

Same as BTree: emit → write to in-memory string → Roslyn parse → reflect type → re-project → compare. Byte-identical C# string; structure-identical model. CI runs against 5–10 representative HSMs including parallel composites and history states.

---

## 5. NodeEditor host services

`Hrot.Hsm.Editor` provides an `IEditorHostServices` instance when the HSM canvas window is created:

```csharp
public sealed class HsmEditorHostServices : IEditorHostServices
{
    public INodeCatalog       NodeCatalog       => _nodeCatalog;
    public ITypeSystem        TypeSystem        => _typeSystem;
    public ILinkValidator     LinkValidator     => _linkValidator;
    public IGraphCommandSink  CommandSink       => _commandSink;
    public IPickerRegistry    Pickers           => _pickers;
    public IClipboard         Clipboard         => _sharedClipboard;
    public IIconProvider      Icons             => _sharedIcons;
    public IDiagnosticsSink?  Diagnostics       => _sharedDiagnostics;
    public IDebugSession?     Debug             => _debugSession;
    public IInputSource       Input             => _sharedInput;
    public IEditorTheme       Theme             => _sharedTheme;
    public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => _customRenderers;
}
```

### 5.1 `HsmNodeCatalog`

Two parts.

**Static node kinds**:

| `NodeKindKey` | Purpose |
|---|---|
| `hsm.state.simple` | A regular state (no children, no parallel) |
| `hsm.state.composite` | A composite state (will contain children) |
| `hsm.state.parallel` | A parallel composite (will contain regions) |
| `hsm.state.final` | A final state |
| `hsm.state.history` | Shallow history pseudo-state |
| `hsm.state.deepHistory` | Deep history pseudo-state |

Transitions and regions are not "nodes" in the catalog — transitions are links (created by dragging from one state to another), regions are part of a parallel state's structure (added via the state's right-click "Add Region" command).

**Dynamic action and guard entries** — populated from `HsmActionDispatcher` and the reference catalog:

```csharp
foreach (var registryEntry in _hsmActionDispatcher.AllActions)
{
    var fqn = _referenceCatalog.ResolveFqn(registryEntry.ShortName);
    yield return new NodeCatalogEntry(
        Kind: new NodeKindKey($"hsm.action.{fqn}"),
        Title: registryEntry.ShortName,
        Category: NodeCategory.Function,
        Description: $"HSM Action: {fqn}",
        IconKey: "hsm/action");
}
```

These don't appear as draggable nodes — they're used by the inspector's method picker. Including them in the catalog lets the catalog do the same fuzzy search across types that BTree does.

### 5.2 `HsmTypeSystem`

HSM has no pin-based typing. Transitions are first-class records (see §7); they don't use `ITypeSystem.GetPinColor` etc. The type system is effectively a stub:

```csharp
public sealed class HsmTypeSystem : ITypeSystem
{
    // All methods return defaults; HSM doesn't use the pin type system.
    public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info) { info = default; return false; }
    public Vector4 GetPinColor(TypeKey key) => Vector4.One;
    public PinShape GetPinShape(TypeKey key, ContainerKind c) => PinShape.Circle;
    public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;
    public bool AreCompatible(TypeKey from, TypeKey to) => true;   // any state can transition to any state
    public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
}
```

States have a single invisible "any" pin used as the link endpoint. The canvas doesn't render pin glyphs for HSM states.

### 5.3 `HsmLinkValidator`

The link validator enforces HSM-specific transition rules:

```csharp
public LinkValidationResult Validate(PinId from, PinId to)
{
    var sourceState = ResolveStateFromPin(from);
    var targetState = ResolveStateFromPin(to);

    if (sourceState == null || targetState == null)
        return new(LinkValidity.Invalid, "Endpoint not a state", false, null);

    // Final states cannot be the source of an outgoing transition
    if (sourceState.IsFinal)
        return new(LinkValidity.Invalid, "Final state cannot have outgoing transitions", false, null);

    // History pseudo-states cannot be the target of normal transitions
    // (they're entered automatically via history-restore on parent re-entry)
    if (targetState.IsHistory && !IsExplicitHistoryEntry(sourceState, targetState))
        return new(LinkValidity.Invalid, "History pseudo-state is not a normal transition target", false, null);

    // (Self-transitions are allowed — they're internal/local/external per inspector)
    return new(LinkValidity.Valid, null, false, null);
}
```

The validator is conservative — most state-to-state transitions are valid in HSM semantics. The actual validation (e.g., LCA cost, sync-group consistency) runs as part of the HSM validator (§12), not the per-link validator.

### 5.4 `HsmCommandSink`

Translates NodeEditor `GraphCommand` records:

```csharp
public GraphCommandResult Apply(GraphCommand command)
{
    switch (command)
    {
        case GraphCommand.MoveNodes mn: ApplyStateMoves(mn.Moves); break;
        case GraphCommand.AddNode an:   ApplyAddState(an); break;
        case GraphCommand.RemoveNodes rn: ApplyRemoveStates(rn.NodeIds); break;
        case GraphCommand.AddLink al:   ApplyAddTransition(al.From, al.To); break;
        case GraphCommand.RemoveLinks rl: ApplyRemoveTransitions(rl.LinkIds); break;
        case GraphCommand.SetNodeProperty sp: ApplySetStateProperty(sp); break;

        // Container extension commands (per NodeEditor_Extension_ContainerNodes.md)
        case GraphCommand.ChangeParent cp: ApplyStateReparent(cp); break;
        case GraphCommand.SetContainerCollapsed sc: ApplySetCollapsed(sc); break;
        case GraphCommand.AddRegion ar: ApplyAddRegion(ar); break;
        case GraphCommand.RemoveRegion rr: ApplyRemoveRegion(rr); break;
        case GraphCommand.ReorderRegions rr2: ApplyReorderRegions(rr2); break;
        case GraphCommand.SetRegionProperty srp: ApplySetRegionProperty(srp); break;

        // NodeAttachments commands (state-flag badges, deferred-event indicators)
        case GraphCommand.AddAttachment aa: ApplyAddFlagBadge(aa); break;
        case GraphCommand.RemoveAttachments ra: ApplyRemoveFlagBadges(ra.AttachmentIds); break;

        case GraphCommand.Batch b: foreach (var sub in b.Commands) Apply(sub); break;

        default: return GraphCommandResult.Failed($"Unsupported: {command.GetType().Name}");
    }

    _scheduler.ScheduleSave(_asset);
    return GraphCommandResult.Ok;
}
```

---

## 6. Containers — composite states and parallel composites

### 6.1 Composite states as containers

A state with `ChildCount > 0` (or `IsParallel = true`) is rendered as a NodeEditor container per the ContainerNodes extension. The editor's `StateNode` implements `IContainerNodeModel`:

```csharp
public sealed class StateNode : IContainerNodeModel
{
    // INodeModel members
    public NodeId Id => new NodeId(StableId);
    public NodeKindKey Kind => ResolveKindKey(this);
    public string Title => Name;
    public NodeCategory Category => GetCategoryByFlags(this);
    public Vector2 Position { get; set; }
    public NodeId? ParentContainerId =>
        Parent != null && Parent != _asset.RootState ? new NodeId(Parent.StableId) : null;
    // ... etc

    // IContainerNodeModel members
    public bool IsContainer => Children.Count > 0 || IsParallel;
    public IReadOnlyList<NodeId> ChildNodeIds => Children.Select(c => new NodeId(c.StableId)).ToList();
    public IReadOnlyList<RegionDescriptor> Regions => IsParallel
        ? _regions.Select(r => new RegionDescriptor(r.RegionIndex, r.Name, r.Priority, ColorOverride: r.ColorOverride.AsVector4())).ToList()
        : Array.Empty<RegionDescriptor>();
    public int GetRegionIndexForChild(NodeId childId) => /* lookup */;
    public ContainerPadding Padding => new(Top: 8, Right: 12, Bottom: 12, Left: 12);
    public Vector2 MinimumInteriorSize => IsParallel ? new(280, 120) : new(200, 80);
    public bool IsCollapsed { get; set; }
}
```

### 6.2 Parallel composites with regions

When `IsParallel = true`, the container exposes `Regions` (one `RegionDescriptor` per `RegionNode` in the editor model). The ContainerNodes extension handles rendering: dashed dividers between regions, region headers showing region name + priority, children grouped by region.

The editor-side `RegionNode.RegionIndex` matches the position of the child within its parent's children list — specifically, the `IContainerNodeModel.GetRegionIndexForChild(childId)` method walks the parent state's children list, partitioned by region, to find which region the child belongs to.

Adding a region:
1. User right-clicks a composite → "Make Parallel" (if not already) → "+ Region" (in the composite header).
2. Editor emits `GraphCommand.AddRegion(containerId: parent.StableId, insertAtIndex: ..., regionName: "Region 1", priority: 1)`.
3. Command sink updates the model.

Dragging a state across a region boundary:
1. NodeEditor's drop-target logic detects the cursor is in a different region than the dragged state's current.
2. On drop, emits `GraphCommand.ChangeParent(NewParentContainerId: container.StableId, NewRegionIndex: newRegionIdx, NewLocalPosition: ...)`.
3. Command sink updates the model.

### 6.3 Composite vs. simple

A simple state (no children) renders as a regular NodeEditor node. Adding a child to it (via drop or right-click "Add Child State") promotes it to a composite — the next render uses container rendering. The promotion is implicit: there's no "make composite" command; adding a first child does it.

Conversely, removing all children from a composite demotes it to a simple state. The container outline disappears; padding goes away.

### 6.4 Composite collapse

The ContainerNodes extension supports `IsCollapsed` (collapsed containers render as a tall pill, children hidden). HSM uses this for screen real-estate management: a composite the user isn't currently focused on can be collapsed.

When collapsed, transitions to/from children render as terminating at the collapsed container's edge (with a small dot indicator and hover tooltip showing the hidden endpoint). This is exactly how ContainerNodes §6.2 specifies the behavior.

---

## 7. Transitions — pin-based or first-class?

### 7.1 The question

NodeEditor's link primitive is pin-based: `ILinkModel.FromPin` and `ILinkModel.ToPin`. HSM transitions are state-to-state with no pin semantics. Two options:

**Option A: Give every state two invisible "any" pins.** One input pin, one output pin. All transitions connect output-to-input. Sidecar metadata carries HSM-specific properties.

**Option B: Author a custom link primitive that bypasses pins.** Would require NodeEditor changes.

**Decision: Option A.** Same pragmatism as BTree (which uses reversed pin direction to fit NodeEditor's existing fan-out rule). Each state has one hidden output pin and one hidden input pin. Transitions connect them. The canvas doesn't render pin glyphs for HSM. NodeEditor's wire-routing, hit-test, selection, and undo all work without modification.

### 7.2 The link-transition bridge

```csharp
public sealed class HsmGraphModel : IGraphModel
{
    // Each transition is exposed to NodeEditor as an ILinkModel
    public IReadOnlyCollection<ILinkModel> Links =>
        _asset.AllTransitions.Select(t => new HsmTransitionLink(t)).ToList();
}

internal sealed class HsmTransitionLink : ILinkModel
{
    private readonly TransitionNode _transition;

    public LinkId Id => new LinkId(_transition.VisualId);
    public PinId FromPin => new PinId(_transition.Source.HiddenOutputPinId);
    public PinId ToPin   => new PinId(_transition.Target.HiddenInputPinId);
    public LinkStyle Style => GetStyleForKind(_transition.Kind);
    public IReadOnlyList<Vector2> Waypoints => _transition.Waypoints;
}
```

The transition's `VisualId` is the `LinkId` value. Sidecar metadata lives on the `TransitionNode` editor object.

### 7.3 Transition label rendering

NodeEditor links don't carry labels. The HSM host registers a custom canvas renderer (`hsm.transition_labels`, `AfterWires` pass) that draws `Event[Guard]/Action` at the midpoint of each transition's rendered Bezier. See §15.

### 7.4 Transition kinds — external, internal, local

The HSM kernel distinguishes three transition kinds via `TransitionFlags.IsExternal` and `TransitionFlags.IsInternal`:

- **External** (default): exits the source state, traverses LCA, enters the target state.
- **Internal**: stays in the source state; no exit/entry. Dashed loop inside the source.
- **Local**: re-enters the source (or a sub-state); doesn't cross the LCA boundary.

Visual rendering differs per kind:

- **External**: standard Bezier arrow exiting source boundary, entering target.
- **Internal**: small dashed loop *inside* the source state (rendered by the custom renderer; NodeEditor's link doesn't natively support this shape, so the renderer overrides the link's path).
- **Local self**: arrow loops back to source crossing only inner sub-state boundaries.

Internal transitions are drawn entirely inside the source state; from NodeEditor's perspective the link is from `Source.HiddenOutputPin` to `Source.HiddenInputPin` (a self-link), and the custom renderer detects the self-link plus `IsInternal` flag and draws the dashed loop.

### 7.5 Self-transitions (non-internal)

A self-transition from `S` to `S` where `Kind != Internal` is an external self-transition — the kernel exits S, runs OnExit, then re-enters S, runs OnEntry. Visually rendered as an arrow that loops out and back to S.

For a state that's a composite, a "local self" transition re-enters the composite without exiting it fully; the LCA cost is reduced. The renderer draws the loop crossing only inner sub-state boundaries.

---

## 8. Initial-state markers, history pseudo-states, final states

### 8.1 Initial-state markers

The `⦿─→` marker on a composite state's initial child is **not a transition** — the kernel uses `StateDef.InitialChildIndex` to determine entry. The editor renders it as a custom visual via the `hsm.initial_state_arrows` renderer:

- A small filled circle ⦿ inside the composite's interior, in the top-left area.
- An arrow from the circle to the initial child's left edge.
- For parallel composites, each region has its own ⦿─→ pair pointing at its initial child.

The renderer reads each composite's `InitialChild` (computed from `IsInitial` flag on the child state) and draws the marker accordingly. The marker is informational; clicking it doesn't trigger selection. The corresponding inspector affordance to set "this child is initial" is on the child state's facet, not the marker.

### 8.2 History pseudo-states

The HSM kernel models history as a flag on a state (`StateFlags.IsHistory` / `StateFlags.IsDeepHistory`). The editor exposes history as either:

**Option A: A flag on an existing state.** Implementation simple; visual ambiguous (which states have history flags?).

**Option B: Distinct palette entries (Shallow History / Deep History) that produce small dedicated state nodes.** Each is a state with `IsHistory = true` plus `StateFlags.HasOnEntry/Exit = false`. The canvas renders these as small circled `H` or `H*` glyphs instead of as full rectangles.

**Decision: Option B.** History pseudo-states are visually distinct from regular states by UML convention; treating them as a separate palette entry preserves that. The kernel-side representation is still a state with the IsHistory flag (so the runtime works unchanged); the editor's rendering treats them as the small circled glyphs.

The custom renderer `hsm.history_glyphs` draws these:
- 20 px diameter circle, white outline 2 px, gray fill at 50% alpha.
- `H` (sans-serif, 12 px) centered for shallow history, `H*` for deep.
- Hit-testable: clicking selects the pseudo-state (the underlying `StateNode`).
- Inspector shows a HistoryFacet (`IsDeep: bool`, transitions to/from list).

### 8.3 Final states

`StateFlags.IsFinal` indicates a final state. Visual: a circle inside a circle (⊙), 24 px diameter outer, 14 px diameter inner filled. Rendered by the `hsm.history_glyphs` renderer for consistency (both are small glyph-like states).

A final state cannot have outgoing transitions (validated by `HsmLinkValidator` §5.3). Reaching a final state sets `InstanceFlags.Terminated`.

### 8.4 Combining flags

A state can be (IsHistory + IsDeepHistory + IsParallel + IsFinal) in theory but most combinations are nonsensical. The validator (§12) rejects:
- History + Parallel (UML doesn't define this).
- Final + Composite (final states have no children).
- History on root state (history is a child-tracking mechanism).

---

## 9. Events table and event scoping

### 9.1 Events table window

Registered as `hsm_events`. Shows all events declared in the current asset:

```
┌──────────────────────────────────────────────────────────────────┐
│ EVENTS — EnemyBrain                                              │
├────┬────────────────┬─────────┬─────────────┬────────┬──────────┤
│ ID │ Name           │ Payload │ Flags       │ Pri.   │ Global   │
├────┼────────────────┼─────────┼─────────────┼────────┼──────────┤
│ 01 │ OnSight        │ 0 B     │             │ Normal │          │
│ 02 │ OnLostSight    │ 0 B     │             │ Normal │          │
│ 03 │ Fire           │ 12 B    │             │ Normal │          │
│ 04 │ Reload         │ 0 B     │ 🕓 deferred │ Normal │          │
│ 05 │ Die            │ 4 B     │             │ Intrpt │ ⚡ Dead   │
├────┴────────────────┴─────────┴─────────────┴────────┴──────────┤
│ + Add Event                                                      │
└──────────────────────────────────────────────────────────────────┘
```

Each row is one `EventDefinition`. Columns:
- **ID**: ushort
- **Name**: string
- **Payload**: bytes (0, 4, 12, or 16)
- **Flags**: 🕓 if `IsDeferred` set on any state's `DeferredEventIds`; 🔗 if `IsIndirect`
- **Priority**: `EventPriority` enum value
- **Global**: ⚡ + target state name if a `GlobalTransitionNode` references this event

Clicking a row selects the event (`ActiveSubSelection = HsmEventSelection(eventId)`); inspector shows `EventFacet` for editing.

Right-click a row → "Find references" (lists all transitions using this event); "Rename" (refactor across the machine); "Delete" (removes; flags warning if any transition references it).

### 9.2 Event scoping (per §4.6 of shared infra)

Event names are machine-scoped: two different HSM assets can both have `OnSight` without colliding. The reference catalog (shared infra §4.3) uses `{MachineAssetId}::{EventName}` as the canonical key. Renaming `OnSight` in `EnemyBrain` doesn't touch `OnSight` in `SoldierAI`.

### 9.3 Global transitions strip

Below the canvas (or as a collapsible strip at top), the editor shows global transitions:

```
Globals: ⚡ Die → [Dead]     ⚡ Pause → [PausedMenu]     + Add Global
```

Each global is a chip; clicking highlights its target state and dims everything else. Right-click → edit / remove / change target.

The strip is rendered by a separate small ImGui panel embedded in the canvas window's chrome — NOT a custom canvas renderer (it's window chrome, not canvas content).

---

## 10. Action / Guard pickers and OutputLaneMask inference

### 10.1 Action picker

Same pattern as BTree's. The inspector's action fields use a `[BehaviorHashPicker]` (or new `[HsmActionPicker]`) attribute that surfaces a dropdown over `HsmActionDispatcher.AllActions`. Grouped by declaring type; fuzzy search.

Blueprint-hosted actions (declared with `hostings: ["HsmAction"]`) appear in the same picker, grouped under "Blueprint-hosted actions" (shared infra §4.7).

### 10.2 Guard picker

Same shape for guards. `HsmActionDispatcher.AllGuards` is the source. Blueprints with `hostings: ["HsmGuard"]` appear.

### 10.3 OutputLaneMask inference

The kernel's `StateDef.OutputLaneMask` (1 byte; bit set per `CommandLane` written to) is *not* user-authored in the editor. Instead:

1. Each `[HsmAction]` declares its target lane via an attribute property:
   ```csharp
   [HsmAction(Name = "StashWeapon", Lane = CommandLane.Animation)]
   public static void StashWeapon(void* instance, void* context, HsmCommandWriter* writer) { ... }
   ```

2. At asset open, the editor reflects each `[HsmAction]`'s `Lane` property.

3. For each state, the editor computes `OutputLaneMask` as the bitwise OR of the lanes of its `OnEntry`, `OnExit`, and `Activity` actions.

4. The inspector shows this as read-only with a tooltip explaining the source:
   ```
   Output lanes: Animation (StashWeapon), Navigation (WalkActivity)
   (Inferred from action declarations; not editable)
   ```

5. The fluent emitter does **not** emit `.WithOutputLaneMask(...)` — the kernel computes it at compile time. The editor's role is to expose it for inspection.

If two parallel-region states have overlapping `OutputLaneMask` bits (both write to `Animation`), the validator (§12) flags a conflict — see §12.1 and §15.3.

### 10.4 Required kernel-side addition

The `Lane = CommandLane.X` property on `[HsmAction]` is needed but doesn't currently exist (FastHSM.txt §14614 shows the attribute without it). This is a small additive change to `HsmActionAttribute` — same as the `stableId`/`visualId` additions in §1.4.

---

## 11. Inspectors and facet structs

### 11.1 Facet structs

```csharp
public struct StateFacet
{
    [EditDisplayName("Name")]
    public string Name;

    [EditDisplayName("On Entry action")]
    [HsmActionPicker]
    public string? OnEntryAction;

    [EditDisplayName("On Exit action")]
    [HsmActionPicker]
    public string? OnExitAction;

    [EditDisplayName("Activity (tick) action")]
    [HsmActionPicker]
    public string? ActivityAction;

    [EditDisplayName("Timer action")]
    [HsmActionPicker]
    public string? TimerAction;

    public StateFlags Flags;                    // [Flags] enum renders as checkboxes; IsInitial / IsFinal / IsParallel / etc.

    [EditDisplayName("Deferred events")]
    public List<ushort> DeferredEventIds;       // Multi-select via EventPicker

    [EditReadOnly]
    [EditDisplayName("Output lanes (inferred)")]
    public string OutputLanesSummary;           // Human-readable from OutputLaneMask

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string StableId;

    [EditReadOnly]
    public int IncomingTransitionCount;

    [EditReadOnly]
    public int OutgoingTransitionCount;
}

public struct TransitionFacet
{
    [EditDisplayName("Source state")]
    [EditReadOnly]
    public string SourceStateName;

    [EditDisplayName("Target state")]
    [HsmStateSelector]                          // dropdown of all states in the machine
    public string TargetStateName;

    [EditDisplayName("Event")]
    [HsmEventPicker]
    public ushort EventId;

    [EditDisplayName("Guard")]
    [HsmGuardPicker]
    public string? GuardFunction;

    [EditDisplayName("Effect action")]
    [HsmActionPicker]
    public string? ActionFunction;

    [EditDisplayName("Priority")]
    [EditRange(0, 255)]
    public byte Priority;

    public TransitionKind Kind;                 // enum → radio buttons

    [EditDisplayName("Sync group")]
    [HsmSyncGroupPicker]
    public ushort SyncGroupId;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    [EditDisplayName("LCA (least common ancestor)")]
    public string LcaStateName;

    [EditReadOnly]
    [EditDisplayName("LCA cost")]
    public ushort LcaCost;
}

public struct RegionFacet
{
    [EditDisplayName("Region name")]
    public string Name;

    [EditDisplayName("Priority")]
    [EditRange(0, 255)]
    public byte Priority;

    [EditDisplayName("Initial child")]
    [HsmStateSelector]                          // filtered to children of this region's parent in this region
    public string? InitialChildName;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Color override")]
    public string? ColorOverride;

    [EditReadOnly]
    public string StableId;
}

public struct EventFacet
{
    [EditDisplayName("Event name")]
    public string Name;

    [EditReadOnly]
    public ushort EventId;

    [EditDisplayName("Payload size (bytes)")]
    public int PayloadSize;                     // 0, 4, 12, or 16

    public bool IsIndirect;

    [EditDisplayName("Priority class")]
    public EventPriority Priority;

    [EditReadOnly]
    [EditDisplayName("Deferred by")]
    public string DeferredByStatesSummary;      // comma-separated state names deferring this event

    [EditReadOnly]
    [EditDisplayName("Used in transitions")]
    public int TransitionReferenceCount;

    [EditReadOnly]
    [EditDisplayName("Global transition")]
    public string? GlobalTransitionTarget;      // null if no global; else target state name
}

public struct GlobalTransitionFacet
{
    [EditDisplayName("Event")]
    [HsmEventPicker]
    public ushort EventId;

    [EditDisplayName("Target state")]
    [HsmStateSelector]
    public string TargetStateName;

    [EditDisplayName("Guard")]
    [HsmGuardPicker]
    public string? GuardFunction;

    [EditDisplayName("Effect action")]
    [HsmActionPicker]
    public string? ActionFunction;

    [EditDisplayName("Priority")]
    [EditRange(0, 255)]
    public byte Priority;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}
```

### 11.2 Inspector dispatch

The shared Inspector window (shared infra §10) dispatches on `ActiveSubSelection`:

```csharp
private IEditSession OpenSessionFor(IAssetSubSelection sub) => sub switch
{
    HsmStateSelection ss     => _editService.Open(_facetMapper.GetStateFacet(ss.StableId)),
    HsmTransitionSelection ts => _editService.Open(_facetMapper.GetTransitionFacet(ts.VisualId)),
    HsmRegionSelection rs    => _editService.Open(_facetMapper.GetRegionFacet(rs.StableId, rs.RegionIndex)),
    HsmEventSelection es     => _editService.Open(_facetMapper.GetEventFacet(es.EventId)),
    HsmGlobalTransitionSelection gs => _editService.Open(_facetMapper.GetGlobalTransitionFacet(gs.VisualId)),
    _ => /* default */
};
```

The HSM-specific subselection types are added to the shared `IAssetSubSelection` registry:

```csharp
public sealed record HsmStateSelection(Guid StableId) : IAssetSubSelection;
public sealed record HsmTransitionSelection(Guid VisualId) : IAssetSubSelection;
public sealed record HsmRegionSelection(Guid ParentStableId, int RegionIndex) : IAssetSubSelection;
public sealed record HsmEventSelection(ushort EventId) : IAssetSubSelection;
public sealed record HsmGlobalTransitionSelection(Guid VisualId) : IAssetSubSelection;
```

The first three are listed in the shared infrastructure doc (§4.1 already covers them); the latter two are HSM-specific additions.

### 11.3 LCA computation for transition facet

When a transition is selected, the inspector shows the LCA. The HSM kernel pre-computes `TransitionDef.Cost` (the LCA distance) but doesn't store the LCA state's identity. The editor computes the LCA at inspector-render time by walking ancestors:

```csharp
public StateNode FindLca(StateNode a, StateNode b)
{
    var aPath = AncestorPathFromRoot(a);
    var bPath = AncestorPathFromRoot(b);
    StateNode lca = _asset.RootState;
    for (int i = 0; i < Math.Min(aPath.Count, bPath.Count); i++)
    {
        if (aPath[i] == bPath[i]) lca = aPath[i];
        else break;
    }
    return lca;
}
```

This is also used by the canvas renderer (§15.2) to highlight the LCA composite when a transition is selected.

---

## 12. Validation

```csharp
public enum HsmDiagnosticCode
{
    CompositeWithoutInitialChild,
    MultipleInitialChildrenInSameParent,
    HistoryOutsideComposite,
    FinalStateWithChildren,
    FinalStateWithOutgoingTransition,
    UnboundAction,
    UnboundGuard,
    OutputLaneConflict,                  // two parallel regions write to same lane
    StateDepthExceeded,                  // depth > 16 (kernel limit)
    RegionCountExceedsTier,              // tier-1 = 2, tier-2 = 4, tier-3 = 8
    TransitionPriorityCycle,             // potential infinite RTC microstep
    EventReferenceDangling,              // event removed but still referenced
    ActionSignatureMismatch,
    DanglingReferenceAfterReload,
}
```

### 12.1 Rules

| Rule | Severity | Trigger |
|---|---|---|
| Composite state without exactly one initial child | Error | Composite (children > 0) and no child has IsInitial=true, OR multiple children have IsInitial=true |
| History pseudo-state's parent isn't a composite | Warning | History flag set on a leaf state |
| Final state has children | Error | IsFinal=true and ChildCount > 0 |
| Final state has outgoing transitions | Error | IsFinal=true and OutgoingTransitions.Count > 0 |
| Action/guard reference returns null from HsmActionDispatcher | Error | Method not in registry |
| Two parallel regions' descendants both write to the same CommandLane | Warning | OutputLaneMask of leaf states in different regions overlap |
| State depth > 16 | Error | StateNode.Depth > 16 (kernel byte limit) |
| Region count exceeds intended tier (declared by user, or auto-detected from instance size) | Warning | RegionCount > tier-allowed |
| Cycle of same-priority transitions reachable in one tick | Warning | Static analysis finds potential infinite microstep |
| Event referenced by a transition but no longer in EventDefinitions | Error | Removed event |
| Action method's `Lane` attribute changed | Info | "OutputLaneMask updated" |
| Reference catalog reports a missing referent after reload | Error | Same as BTree §11 |

### 12.2 OutputLaneMask conflict detection

The validator walks all parallel composite states. For each one:
1. For each pair of distinct regions (R1, R2):
   - Compute the union of `OutputLaneMask` across all leaf states in R1.
   - Same for R2.
   - If the AND of these two unions is nonzero, there's a conflict on at least one lane.
   - Surface a diagnostic per state that contributes to the conflict (so the user can see "which state's action causes this").

This is conservative — a real conflict only occurs when the regions are simultaneously active in a specific tick and the kernel's `Conflict` opcode fires. But static analysis correctly catches all potential conflicts.

### 12.3 Surfacing

Same as BTree §11.2 — affected states get colored outlines, inspector shows messages, a future Diagnostics window aggregates.

The `hsm.region_conflicts` custom canvas renderer (§15.3) draws yellow connector lines between conflicting states across regions, giving a visual signal beyond the per-state outline.

---

## 13. Runtime debug session and overlay

### 13.1 `IHsmDebugSession`

```csharp
namespace Hrot.Hsm.Editor.Debug;

public interface IHsmDebugSession : IAiDebugSession
{
    HsmInstanceSnapshot? GetCurrentStateSnapshot();
    IReadOnlyList<HsmTraceRecord> GetRecentTraceHistory(int max = 100);

    event Action<HsmBreakpointHit>? OnBreakpointHit;
    event Action<HsmStateEntered>? OnStateEntered;
    event Action<HsmStateExited>? OnStateExited;
    event Action<HsmTransitionFired>? OnTransitionFired;
    event Action<HsmEventQueued>? OnEventQueued;
    event Action<HsmRegionConflict>? OnRegionConflict;
    event Action<HsmGuardEvaluated>? OnGuardEvaluated;
    event Action<HsmTimerEvent>? OnTimerEvent;
}

public sealed record HsmInstanceSnapshot(
    Entity Self,
    Guid AssetId,
    IReadOnlyList<Guid> ActiveLeafStableIds,         // symbolicated from active leaf bitset
    IReadOnlyList<HsmEventQueueEntry> EventQueue,
    IReadOnlyList<HsmTimerSlot> TimerSlots,
    IReadOnlyList<HsmHistorySlot> HistorySlots,
    InstancePhase Phase,
    byte MicroStep,
    byte ConsecutiveClamps,
    InstanceFlags Flags,
    uint RngState,
    ushort Generation);

public sealed record HsmEventQueueEntry(
    ushort EventId,
    string EventName,             // symbolicated
    EventFlags Flags,             // IsDeferred, IsIndirect
    EventPriority Priority,
    int QueuePosition);

public sealed record HsmTimerSlot(
    int SlotIndex,
    Guid? OwningStateStableId,    // null if slot is empty
    float RemainingTicks);        // 0 if fired

public sealed record HsmHistorySlot(
    int SlotIndex,
    Guid? OwningCompositeStableId,
    Guid? RecordedChildStableId,
    bool IsDeepHistory);

public sealed record HsmTransitionFired(
    Entity Self,
    Guid AssetId,
    Guid TransitionVisualId,
    Guid SourceStateStableId,
    Guid TargetStateStableId,
    ushort EventId,
    bool GuardResult,
    ushort SyncGroupId,
    float SimulationTime);

// (Other event records similarly shaped.)
```

### 13.2 Step-control semantics

Per shared infra §12.3:

- **Continue**: clear pause flag; resume normal RTC processing.
- **Pause**: set pause flag (the kernel already has `InstanceFlags.DebugTrace`; pause needs an additional bit — see §19 open question).
- **Step Into**: process the next single event from the queue. Pause again before the next RTC microstep.
- **Step Over**: advance one microstep at the current state (run any transitions originating from current active states this tick, then pause).
- **Step Out**: run to RTC quiescence (the point where no more transitions are pending). Pause when the instance enters the Activity phase.

### 13.3 Breakpoints

Same per-user, session-local pattern as BTree. Breakpoints can be set on:
- A state (break on enter, or break on exit)
- A transition (break on fire)
- A region (break on any state enter within the region — convenience aggregate)
- An event (break when event handled — convenience cross-state)

UI: click the gutter to the left of a state's title bar to toggle. Transitions get a small dot affordance on their label (custom renderer support).

### 13.4 Live overlay renderer

`hsm.runtime_overlay`, `AfterNodes` pass:

```csharp
public void Render(ICanvasRenderContext ctx)
{
    var session = ctx.DebugSession as IHsmDebugSession;
    var snapshot = session?.GetCurrentStateSnapshot();
    if (snapshot is null) return;

    // 1. Active configuration glow on every active leaf and its ancestors
    foreach (var leafId in snapshot.ActiveLeafStableIds)
    {
        var leaf = _asset.FindState(leafId);
        if (leaf is null) continue;

        // Glow the leaf
        DrawActiveOutline(ctx, leaf, intensity: 1.0f);

        // Glow ancestors with diminishing intensity
        var ancestor = leaf.Parent;
        int depth = 1;
        while (ancestor != null && ancestor != _asset.RootState)
        {
            DrawActiveOutline(ctx, ancestor, intensity: 1.0f / (1 + depth * 0.5f));
            ancestor = ancestor.Parent;
            depth++;
        }
    }

    // 2. Last transition pulse (if any fired recently)
    var lastTransition = session.GetMostRecentTransition();
    if (lastTransition != null && IsRecent(lastTransition.SimulationTime))
    {
        PulseTransitionArrow(ctx, lastTransition.TransitionVisualId, frequency: 4.0f);
    }

    // 3. Breakpoint markers
    foreach (var bp in session.GetBreakpoints())
    {
        DrawBreakpointMarker(ctx, bp);
    }

    // 4. Deferred event glyphs
    foreach (var stateId in snapshot.ActiveLeafStableIds)
    {
        var state = _asset.FindState(stateId);
        if (state?.DeferredEventIds.Count > 0)
        {
            // A 🕓 badge already renders as an attachment on this state.
            // Highlight it brighter while the state is active.
        }
    }
}
```

### 13.5 Live runtime inspector pane

```csharp
public sealed class HsmRuntimeInspectorPane : IRuntimeInspectorPane
{
    public AssetKind TargetKind => AssetKind.Hsm;

    public void Draw(IRuntimeInspectorContext ctx)
    {
        var session = ctx.DebugSession as IHsmDebugSession;
        var snapshot = session?.GetCurrentStateSnapshot();
        if (snapshot is null)
        {
            ImGui.Text("No live HSM state");
            return;
        }

        DrawHeader(snapshot);              // Entity, AssetId, Phase, MicroStep, Flags
        DrawActiveConfigurationPanel(snapshot);   // Path-to-leaf for each active region
        DrawEventQueuePanel(snapshot);     // Queue position, deferred-vs-active, priority
        DrawTimerSlotsPanel(snapshot);     // Slot index, owner state, remaining ticks
        DrawHistorySlotsPanel(snapshot);   // Composite + recorded child, shallow/deep
        DrawRngAndGenerationPanel(snapshot);  // RngState, Generation, MachineId
    }
}
```

---

## 14. Trace timeline lanes

`HsmTraceLaneProvider` registers six lanes:

| Lane ID | Display | TraceLevel bits | Records |
|---|---|---|---|
| `hsm.states` | States | Lifecycle | StateEnter / StateExit; colored ribbons per state |
| `hsm.events` | Events | Decisions | EventHandled records; spike marks at queue events |
| `hsm.actions` | Actions | Decisions | ActionExecuted records; per-action durations |
| `hsm.guards` | Guards | Decisions | GuardEvaluated records; (t) for true, (f) for false |
| `hsm.timers` | Timers | Decisions | TimerSet / TimerFired |
| `hsm.conflicts` | Conflicts | Errors | Conflict opcodes; red marks on the timeline |

Symbolication via `MachineMetadata.GetStateName/GetEventName/GetActionName`.

The shared Trace Timeline consumes these per shared infra §15.

---

## 15. Custom canvas renderers — full list

The HSM host registers five renderers (per the CustomCanvasRenderer extension §16.1):

### 15.1 `hsm.transition_labels` (`AfterWires`)

Renders `Event[Guard]/Action` at each transition's midpoint:

```
       OnSight [AmmoOk] / StashWeapon
              ────────►
```

Implementation outline:

- For each visible link (= transition), compute the rendered Bezier's midpoint via `LinkBezier.GetPointAt(0.5)` (NodeEditor utility).
- Build the label string: `Event[Guard]/Action`, omitting parts that are null. Event name comes from `MachineMetadata.GetEventName(EventId)`; guard and action are short-form FQNs (last segment after the final dot).
- For sync-group transitions, append a small badge `[SG:N]` after the label.
- For non-default priority (≠ 128), append `(P:N)`.
- Render the label at the midpoint with a small rounded background (4 px corner radius, theme-default node-body color at 85% alpha) for readability against wires.
- Hit-testable via `ICustomCanvasHitTester`; clicking selects the transition (writes `HsmTransitionSelection` to the selection store).
- Hover renders the label with brighter background and shows a tooltip with the full FQNs.
- For internal transitions (the dashed loop inside the source state, per §7.4), the label is drawn next to the loop instead of at a midpoint — the renderer detects `Kind == Internal` and shifts the label placement.

### 15.2 `hsm.initial_state_arrows` (`AfterNodes`)

Renders the ⦿─→ initial-state markers. For each composite (and each region within parallel composites):
- Place a small filled circle (6 px radius) inside the composite's interior, in the top-left corner area near the region header (or below the composite header for non-parallel composites).
- Draw an arrow from the circle to the initial child's center-left edge.
- The arrow is shorter than a normal transition (~30 px) and uses a thinner stroke (1.5 px) so it reads as "marker" rather than "transition."
- Color: theme accent at 70% alpha (distinct enough to see, not loud enough to compete with active transitions).
- Not hit-testable (informational only).

When a transition is selected, this renderer is also responsible for the **LCA highlight**: it draws a thin gold outline (1 px, theme.SelectionAccent at 50% alpha) around the LCA composite. The LCA is computed via the helper in §11.3 (`FindLca(transition.Source, transition.Target)`). This is a debugging aid that makes "where does this transition cross?" instantly visible — useful for HSM authors reasoning about external-vs-local kind choices.

The LCA highlight is *only* rendered when the inspector currently shows a transition; when no transition is selected, this renderer does only the initial-state arrows.

### 15.3 `hsm.region_conflicts` (`AfterNodes`)

When the validator (§12.2) detects two states across parallel regions writing to the same `CommandLane`, the renderer draws:

- A thin yellow line (1.5 px, theme.Warning color, 80% alpha) between the centers of the conflicting states, routed around intervening nodes if possible (simple straight line for v1; smarter routing deferred).
- A small ⚠ warning glyph at the line's midpoint, with a one-line label like "Animation lane" identifying which CommandLane is in conflict.
- The conflicting states themselves are not re-outlined (the standard validation outline from §12.3 handles that).

Hit-test: clicking the ⚠ glyph or the connector line opens a small popup panel anchored at the click point. The panel shows:
- Which CommandLane(s) are in conflict.
- Which actions (OnEntry / OnExit / Activity) on each side contribute.
- A "Suppress this warning" button (per-asset, persisted in the layout method as an editor-only metadata entry).
- A "Mark as intentional" toggle that converts the diagnostic from Warning to Info on subsequent validations.

The renderer iterates the validator's diagnostic list once per frame (cheap; typically 0–3 conflicts in a real HSM).

### 15.4 `hsm.history_glyphs` (`AfterNodes`)

Renders the small circled `H` / `H*` glyphs for shallow and deep history pseudo-states (per §8.2), and the ⊙ glyph for final states (per §8.3). These states render in the underlying NodeEditor as normal nodes, but this renderer replaces their visual:

- A history state's regular rectangle is suppressed (the renderer hides it via a clip rect trick: it draws the small glyph centered at the state's bounds-center *after* `AfterNodes` runs, but the state's body has been rendered transparent because the host's `StateNode.Category` for history kinds maps to a "transparent" theme entry).
- Glyph: 20 px diameter circle, 2 px outline in theme.Text default, fill at 30% alpha. Letter `H` (12 px sans-serif) for shallow; `H*` for deep; ⊙ (an inner filled circle) for final.
- Hit-test: clicking the glyph selects the state (the underlying `StateNode`). The hit area is slightly larger than the visible glyph (24 px) per the NodeEditor hit-area-padding convention.
- Selection feedback: when selected, the outline thickens to 3 px and uses theme.SelectionAccent.
- The glyph respects the standard `NodeState` flags — Error / Warning / Disabled / Executing all apply via the standard color rules.

This is the most "rendering-bypass" of the five renderers — it intentionally takes over what would otherwise be the node body rendering. Other approaches (a separate "small-node" mode in NodeEditor itself, or a more general node-skin extension) would be cleaner but bigger scope. The bypass is contained and well-defined; the renderer is the only one that has to coordinate with the host's category-to-color mapping in this way.

### 15.5 `hsm.runtime_overlay` (`AfterNodes`)

Specified in §13.4 above. Recap of what it renders:
- Active-configuration glow on every active leaf and its ancestors (intensity diminishes with depth).
- Last-transition pulse (~4 Hz for ~300 ms after firing).
- Breakpoint markers (small filled circles at the state header gutter or transition label gutter).
- Deferred-event highlights when the owning state is currently active.

`IsActive` is gated on `ctx.DebugSession?.IsAttached == true` so the renderer is fully skipped when no debug session is attached — zero cost in normal authoring mode.

### 15.6 Renderer registration order

The five renderers are registered in this order (which controls within-pass z-order per `NodeEditor_Extension_CustomCanvasRenderer.md` §10.1):

1. `hsm.initial_state_arrows` (AfterNodes) — sit on top of nodes but below conflicts and runtime overlay.
2. `hsm.region_conflicts` (AfterNodes) — sit on top of initial-state arrows so the warning is unmistakable.
3. `hsm.history_glyphs` (AfterNodes) — render after conflicts; the glyph is its own visual surface and selection outline must be on top.
4. `hsm.runtime_overlay` (AfterNodes) — runtime overlay is the most ephemeral; renders last so it overlays everything else.
5. `hsm.transition_labels` (AfterWires) — different pass entirely, runs earlier in the frame.

The selection outlines (NodeEditor's own pass 11) render after all custom AfterNodes content, so a selected transition's label still gets the standard selection treatment via the renderer's `ICustomCanvasSelectable` implementation.

---

## 16. Quick reload pipeline

### 16.1 Triggers

Same flow as BTree (`BTree_Editor_NodeEditor_Host_Design.md` §14): user clicks Save, regeneration scheduler emits `.cs` file, file watcher triggers MSBuild.

### 16.2 Classification

After the file write, the editor pre-classifies the reload tier (per shared infra §17.2) by comparing the in-memory asset's structure and parameter hashes against the previously-loaded blob. The hashing primitive is `XxHash64` (FastHSM.txt §1614) so the classification can use the same algorithm the kernel uses internally for `MachineId`:

- **StructureHash** covers state count, transition count, region count, parent-child topology, region structure, state flags (`IsParallel`, `IsHistory`, `IsFinal`), depth values.
- **ParamHash** covers action IDs, guard IDs, event IDs, priorities, deferred-event lists, sync-group IDs, transition `Kind` flags, region priorities.

Decision:
- `StructureHash` differs → Hard reload pending. Live instances will reset; confirmation dialog if any are present.
- `ParamHash` differs only → Soft reload pending. Live instances keep their `InstanceHeader` and active configuration.
- Neither differs → Cosmetic. No kernel notification.

The classification surfaces in the status indicator immediately after save, before the actual rebuild completes.

### 16.3 Layout-only edits are Cosmetic

A user dragging states around the canvas, adding comments, or collapsing composites produces only layout-method changes (per shared infra §7.4 — Cosmetic means the `[HsmLayout]` method changes but the `[HsmDefinition]` method does not). These never reach the kernel: MSBuild rebuilds (because the file changed) but the new assembly's `Compile()` output is byte-identical to the previous one. The editor refreshes its in-memory layout cache from the rebuilt assembly's `[HsmLayout]` method; no live instances are perturbed.

### 16.4 Post-reload editor refresh

After the new assembly loads:
1. Asset catalog rebuilds via subsystem contributors.
2. Reference catalog rebuilds (shared infra §4.3).
3. The HSM asset's projection re-runs: new `HsmDefinitionBlob` + `MachineMetadata` + layout method are read.
4. Editor model reconciles against the new projection. Entries with matching `StableId` (states), `VisualId` (transitions), `StableId` (regions) keep their layout-method-derived properties. New entries get default positions; vanished entries drop out.
5. `IGraphModel.Changed` fires; NodeEditor re-renders.
6. Validation re-runs.
7. Active runtime debug session (if any) re-symbolicates against the new `MachineMetadata` — state names may have changed for the same `FlatIndex`, but `StableId` resolves the lookup.

Author-perceived latency target: ≤ 100 ms.

---

## 17. Slice plan

### Slice 1: Authoring without debug
- `HsmAsset` projection from compiled assembly + layout method (depends on the three kernel-side prerequisites in §1.4)
- `HsmGraphModel`, `HsmCommandSink`, `HsmNodeCatalog`, `HsmLinkValidator`, `HsmTypeSystem`
- Composite states as containers (uses ContainerNodes extension)
- Parallel composites with regions
- Transitions via pin-bridge (`HsmTransitionLink`)
- `hsm.transition_labels` renderer (the must-have visual)
- `hsm.initial_state_arrows` renderer (no LCA highlight yet)
- Events table window + global transitions strip
- Action / Guard pickers wired to `HsmActionDispatcher`
- StateFacet / TransitionFacet / RegionFacet / EventFacet / GlobalTransitionFacet inspectors
- OutputLaneMask inference (requires Lane attribute kernel addition)
- `HsmFluentEmitter` with deterministic round-trip property test
- Validation diagnostics (§12.1 rules)
- Quick Reload with Cosmetic / Soft / Hard tiering

### Slice 2: Runtime inspection (read-only)
- `IHsmDebugSession` with `GetCurrentStateSnapshot()` and observer-mode lifecycle
- `hsm.runtime_overlay` renderer (active configuration glow, ancestor diminishing)
- `HsmRuntimeInspectorPane` (instance header, active configuration, event queue, timer slots, history slots)
- `HsmTraceLaneProvider` for the six trace timeline lanes
- LCA highlight in `hsm.initial_state_arrows` when a transition is selected

### Slice 3: Stepping and breakpoints
- Breakpoints on states, transitions, regions, events (session-local)
- Pause / Step Into / Step Over / Step Out controls wired through the debug session
- `hsm.region_conflicts` renderer with click-to-popup
- History pseudo-states + final states via `hsm.history_glyphs`

### Slice 4: Multi-instance and refactor
- Aggregate counters for state-entry frequency across instances (heatmap on states)
- Asset Browser live-instance count integration
- Find References on actions / guards / events surfacing in the shared Find Results window
- Rename refactor for events (machine-scoped) and actions (asset-wide, FQN-keyed) via shared refactor service

### Slice 5: Polish
- Reset-layout toolbar action
- Region color overrides via inspector
- Composite-collapse polish (transition-to-collapsed-container indicators)
- Comments on transitions and regions
- Drag-to-create-transition gesture refinement (snap-to-state)
- Diagnostics aggregation window (cross-asset, shared)

---

## 18. Test strategy

### 18.1 Unit tests (`Hrot.Hsm.Editor.Tests`)

- **`HsmAssetProjectionTests`** — given a fixture of `HsmDefinitionBlob` + `MachineMetadata` + layout method, verify editor model reconstruction. Includes parallel composites with regions.
- **`HsmFluentEmitterDeterminismTests`** — same model → same byte output across runs. Event registration order, action/guard alphabetical ordering, state depth-first emission order.
- **`HsmCommandSinkTests`** — each `GraphCommand` produces correct model edit. Container commands (ChangeParent, AddRegion, RemoveRegion) and attachment commands handled.
- **`HsmLinkValidatorTests`** — final states reject outgoing; history pseudo-states reject normal targets.
- **`HsmValidationTests`** — each diagnostic code triggers under expected conditions. OutputLaneMask conflict detection across regions.
- **`OutputLaneMaskInferenceTests`** — given fixtures of `[HsmAction]`-decorated methods with various `Lane` values, verify per-state mask is the correct union of OnEntry/OnExit/Activity lanes.
- **`LcaComputationTests`** — `FindLca` returns correct ancestor for various transition source/target pairs.
- **`HsmFacetMapperTests`** — facet structs round-trip through StructEdit correctly.

### 18.2 Integration tests (`Hrot.Hsm.Editor.IntegrationTests`)

- **Project-open-and-save round-trip** — open the TrafficLight example, modify one state's OnEntry, save, reload, verify the model matches.
- **Parallel composite with two regions** — fixture with 3 states in region 0 and 2 states in region 1. Verify all states project correctly with correct region indices.
- **Hot reload classification** — three fixtures (layout-only, transition-priority change, new state added); verify Cosmetic / Soft / Hard.
- **Region conflict** — fixture where two parallel regions both write to `CommandLane.Animation`; verify the validator surfaces the conflict and the `hsm.region_conflicts` renderer would draw a line between the right states.
- **Cross-asset refactor: rename an event** — verify the rename is machine-scoped (only the owning machine's transitions are updated; sibling HSMs with same-name events are untouched).
- **Debug session attach with transition breakpoint** — attach to a running entity, set breakpoint on a transition, advance ticks, verify the breakpoint hit fires with correct source/target/event payload.

### 18.3 Visual / manual tests

A "HSM" scenario in the shared editor's test harness:
- The TrafficLight example (3 simple states, completion transitions).
- A 6-state machine with one parallel composite (2 regions × 2 states each).
- A machine with a history pseudo-state and a final state.
- A machine with a global transition.
- Live debug overlay with a fake entity producing tick events.

Manual checklist:
- Composite states render with auto-resize; dragging child resizes container.
- Parallel composite shows two regions with dashed dividers and region headers.
- Transition labels visible and readable; clicking selects transition; inspector shows correct facet.
- Internal transition renders as dashed loop; external self-transition as out-and-back arrow.
- ⦿─→ initial markers visible in each composite and each region.
- H / H* / ⊙ glyphs render correctly for history and final states.
- Selecting a transition shows LCA highlight on the correct ancestor composite.
- Region conflict line + ⚠ glyph appears when fixture has overlapping lanes; popup explains.
- Runtime overlay glows the correct active leaf and ancestors during simulated ticks.

---

## 19. Open questions

1. **`InstanceFlags` already has `DebugTrace`; needs `Paused` too.** The kernel today has `InstanceFlags.DebugTrace` but no pause flag. Adding `Paused` to the enum is straightforward (one bit; lots of room — bits 6 and 7 are already reserved). Track as a kernel-side ticket alongside the three additions from §1.4. Without it, step controls (§13.2) can't function.

2. **Should the inferred `OutputLaneMask` emit `.WithOutputLaneMask(...)` for forward-compatibility?** Today the editor doesn't emit it — the kernel re-computes it at compile time from action declarations. The argument *for* emitting: if a user inspects the generated `.cs` they can see the mask explicitly, and CI can diff masks across changes. The argument *against*: an extra source of truth that could drift from action declarations. Decision deferred; lean toward "no, keep it inferred" unless the diff-visibility argument becomes important.

3. **Internal transition rendering complexity.** §7.4 specifies internal transitions as dashed loops drawn entirely inside the source state, with the `hsm.transition_labels` renderer detecting the internal kind and offsetting the label. The hidden-pin trick makes the underlying link a self-link, but ImGui's wire-routing for self-links draws an arc *outside* the node, which doesn't suit internal transitions. The custom renderer needs to *override* the link's bezier path for internal-kind links — drawing nothing for the link itself (or a tiny stub indicator) and drawing the dashed loop inside the source state. This is a renderer responsibility that interacts with NodeEditor's wire-drawing in a non-obvious way. Sanity-check this with the NodeEditor implementer before Slice 1 codes it.

4. **History pseudo-states as separate palette entries vs. flag on existing state.** §8.2 chose Option B (separate palette entries with the rendering bypass in §15.4). The alternative (Option A: flag-on-state with a category swap) is simpler in code but ambiguous in UX. Worth one final UX review against an artist or design-oriented teammate before Slice 3 codes the dedicated rendering. If consensus says "flag is fine," the design changes are small and contained to §8.2 and §15.4.

5. **Hidden-pin convention parallels BTree's reversed-pin trick.** Both editor hosts work around NodeEditor's pin model rather than extending it. This is pragmatic for v1, but if a third subsystem editor lands (Blueprint composite-nodes? a future visual scripting tool?) and *also* wants pin-bypass semantics, a NodeEditor extension to formalize "link kinds that don't require pins" might be worth designing. Defer until a third use case actually emerges; two is "two coincidences," three would be a pattern.

6. **Cross-asset transition references?** Some HSM designs allow a transition's target to be a state in a different machine (delegation). The kernel doesn't support this today, and the editor design assumes single-asset machines. If cross-asset transitions land, the canvas needs subtree-like black-box rendering for "transition to external state" and the refactor service needs to track these as references. Not in scope for v1; document the want.

---
