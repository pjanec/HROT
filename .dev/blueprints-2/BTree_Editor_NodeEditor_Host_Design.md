# BTree Editor — NodeEditor Host Detailed Design

> **Status:** Detailed design, derived from `AI_Editor_Shared_Infrastructure.md` + `NodeEditor_Extension_NodeAttachments.md` + `NodeEditor_Extension_ContainerNodes.md` + `NodeEditor_Extension_CustomCanvasRenderer.md` + `FastBTree.txt` source.
> **Audience:** Implementation agent and human reviewer.
> **Drives:** The `Hrot.BTree.Editor` assembly — the BTree-specific host code that plugs into NodeEditor and the shared AI editor infrastructure.
> **Doesn't cover:** Kernel internals (owned by FastBTree). NodeEditor primitives (owned by NodeEditor). Shared editor infrastructure (owned by `AI_Editor_Shared_Infrastructure.md`). Runtime debugging protocol (the BTree-specific extension of `IAiDebugSession` is sketched here in §11, but its detailed implementation against the BTree kernel is a separate kernel-side concern).
> **Companion code lives in:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/` — host services, projection, emitter, runtime debug session.

---

## Table of Contents

1. Scope and design goals
2. The shape, in one picture
3. Asset model and projection
4. The fluent C# emitter
5. NodeEditor host services
6. Decorator pills — collapse and round-trip
7. Observer Selector — distinct visual, guard badges
8. Subtrees as black boxes
9. Blackboard reflection
10. Action / Condition / Wait inspectors
11. Validation
12. Runtime debug session and overlay
13. Trace timeline lanes
14. Quick reload pipeline
15. Slice plan
16. Test strategy
17. Open questions

---

## 1. Scope and design goals

### 1.1 What this assembly owns

`Hrot.BTree.Editor` is the BTree-specific host code in the unified AI editor. It is the smallest of the three subsystem editors because the heavy lifting (canvas, selection, inspector, refactor, debug-session base, fluent emitter framework) lives in shared infrastructure or NodeEditor.

Concretely it owns:

- **`BehaviorTreeAsset`** — the in-editor model of a BTree asset, projecting from `BehaviorTreeBlob` + `NodeDebugMetadata[]` + the layout method.
- **`BTreeGraphModel`** — the `IGraphModel` adapter exposing `BehaviorTreeAsset` to NodeEditor's canvas.
- **`BTreeCommandSink`** — translates NodeEditor `GraphCommand` records into edits on `BehaviorTreeAsset` plus deferred file regeneration.
- **`BTreeNodeCatalog`** — the `INodeCatalog` providing palette content; static composite/decorator/leaf node kinds plus dynamic action/condition entries from `BehaviorRegistry`.
- **`BTreeFluentEmitter`** — produces the deterministic C# (fluent builder method + `[BTreeLayout]` method) from `BehaviorTreeAsset` on save.
- **`BTreeAssetContributor`** — implements `IAssetCatalogContributor` (shared infra §4.3); reflects the loaded assembly for `[BTreeDefinition]` methods.
- **`BTreeRuntimeInspectorPane`** — the BTree-specific pane plugged into the shared Runtime Inspector window (shared infra §14).
- **`BTreeTraceLaneProvider`** — registers the BTree-specific trace lanes (shared infra §15.2).
- **`BTreeDebugSession`** — implements `IBTreeDebugSession : IAiDebugSession`.
- **`BTreeFacetMapper`** — produces the StructEdit facet structs (`RepeaterFacet`, `WaitFacet`, `ActionFacet`, etc.) consumed by the shared Inspector window.
- **Three custom canvas renderers** — `btree.observer_guard_badges`, `btree.subtree_boundaries`, `btree.runtime_overlay`, `btree.heatmap_overlay` (per the CustomCanvasRenderer extension §17.1).

### 1.2 What this assembly does NOT own

- **`BehaviorTreeBlob`**, `BehaviorTreeState`, `NodeDefinition`, the interpreter, the hot reload kernel — all owned by `Fbt.Kernel` / `Fbt.Compiler` / `Fbt.HotReload`.
- **The canvas** — NodeEditor.UI renders the canvas; this host provides the model behind it.
- **The Inspector window** — shared infrastructure provides the window; this host provides the facet structs that the window dispatches to.
- **The Asset Browser, Runtime Inspector window, Trace Timeline, Find Results window** — all in shared infrastructure.
- **The fluent builder API** (`BTreeBuilder<TBB,TCtx>`) — defined in `Fbt.Compiler`. The editor emits C# code that calls it; the editor doesn't extend it.

### 1.3 Design goals

- **Author writes fluent C#; editor projects from compiled assembly.** No JSON in v1. The asset is its `.cs` file plus the layout sibling method.
- **Decorators render as pills (Unreal-style); store as nested builder calls.** Round-trip works because pill ↔ wrapper-node mapping is deterministic and the wrapper's `visualId` is preserved.
- **Observer Selectors are a distinct palette item with distinct visual.** Eye glyph, guard badges on observer-child connections, no flag-on-selector confusion.
- **Subtrees are black boxes.** No inline preview. Double-click to navigate into the referenced asset.
- **C# struct is the blackboard schema source of truth.** The editor reflects user-defined blackboard structs; it does not invent its own schema authoring.
- **Quick Reload ≤ 100 ms.** Matches the Blueprint editor's target. Tier-classified Cosmetic / Soft / Hard per shared infrastructure §17.
- **Live debug overlay correlates via `NodeDebugMetadata.VisualId`.** The runtime's `RunningNodeIndex` is symbolicated through the metadata array; editor nodes match by VisualId.
- **All three NodeEditor extensions are used here.** NodeAttachments (decorator pills); CustomCanvasRenderer (observer-guard badges, subtree-boundary indicators, runtime overlay, heatmap); ContainerNodes is *not* used (BTree is a tree, not a nested-state diagram).

---

## 2. The shape, in one picture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ File  Edit  View  Debug   [▶ Play] [⏸] [⏯ Step] [↻ Reset] [⏺ Record]          │
│                           Mode: ( ) Release  ( ) Debug  (●) Trace             │
├────────────────┬─────────────────────────────────────────┬───────────────────┤
│ ASSET BROWSER  │ TREE CANVAS — Combat/OrcGuard.bt        │ INSPECTOR         │
│ (shared)       │                                          │ (shared)          │
│                │              ┌─────────┐                 │ Selected:         │
│ ▼ Combat       │              │ Root    │                 │   Repeater (pill) │
│  • OrcGuard    │              └────┬────┘                 │ ─────────────     │
│  • OrcAmbush   │              ┌───┴─────────┐ 👁           │ Count:    [3]     │
│ ▼ Patrol       │              │ObserverSel  │              │ Comment:  [    ]  │
│  • Simple      │              └───┬──────┬──┘              │ ☐ Breakpoint      │
│ ▼ Subtrees     │  👁 OBSERVES  ┌──┘      └──┐              │                   │
│  • Search      │   ┌────┬──────┴────┐  ┌────┴───┐          │ Visual ID:        │
│                │   │ ↺×3│ Sequence  │  │Action  │          │ a3f2-…-9c01      │
│ + New Tree     │   │ ⏲2s│ ⊡ (pills) │  │Attack  │          │                   │
├────────────────┤   └────┴─┬────┬────┘  │  ⊡     │ ← sel    │ Last result: ✓    │
│ BLACKBOARD     │     ┌────┴┐ ┌─┴──┐    └────────┘          │ Tick count:  47   │
│ CombatBB       │     │Cond │ │Wait│                        │                   │
│ • int  Ammo    │     │HasT │ │1.5s│  ┌──────────┐          │                   │
│ • bool TVis    │     └─────┘ └────┘  │ Subtree  │          │                   │
│ • Vec3 Pos     │                     │ Search 📦│          │                   │
│ (reflected)    │                     └──────────┘          │                   │
└────────────────┴─────────────────────────────────────────┴───────────────────┘
┌──────────────────────────────────────────────────────────────────────────────┐
│ TRACE TIMELINE (shared)  ◄ tick 412 / 1024 ►  [Pause on Failure ▾]           │
│ NodeStatus  │ ░░▒▒▓▓██ Sequence→Cond(S)→Wait(R)→…→Repeater iter 1/3          │
│ Stack       │ ┄┄┄┄┄┄┄┄│push subtree┄┄┄┄┄┄┄┄│pop subtree┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄ │
│ Async       │ ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄│issued #42┄┄┄┄┄│resolved #42┄┄┄┄┄┄┄┄┄┄┄┄┄ │
└──────────────────────────────────────────────────────────────────────────────┘
```

The seven shared windows (Asset Browser, Inspector, Runtime Inspector, Trace Timeline, Find Results) plus the BTree-specific Tree Canvas and Blackboard panel. Same chrome as the HSM and Blueprint editors; the canvas content is what differs.

---

## 3. Asset model and projection

### 3.1 The two-sided model

A BTree asset has two synchronized representations:

- **Kernel side (`BehaviorTreeBlob` + `NodeDebugMetadata[]`)** — the compiled, runtime-shaped data. Loaded from the assembly. Read-only as far as the editor is concerned.
- **Editor side (`BehaviorTreeAsset`)** — the editor's model. Mutable. Tracks layout, breakpoints (per-user session), comments, expression-target fields, and the editor-side identity of each node.

```csharp
namespace Hrot.BTree.Editor.Model;

public sealed class BehaviorTreeAsset : IEditableAsset
{
    public Guid AssetId { get; }
    public string Name { get; set; }
    public AssetKind Kind => AssetKind.BTree;
    public string SourceFilePath { get; }
    public bool IsDirty { get; private set; }
    public bool IsEditorOwned { get; }

    public string BlackboardTypeName { get; }    // FQN, reflected from generic argument
    public string ContextTypeName    { get; }    // FQN

    public BehaviorTreeBlob Blob { get; }                 // current compiled blob from the loaded assembly
    public NodeDebugMetadata[] DebugMetadata { get; }     // parallel to Blob.Nodes

    public IReadOnlyList<BTreeEditorNode> Nodes { get; }  // editor-side node list
    public IReadOnlyList<BTreeEditorPill> Pills { get; }  // decorator pills (attachments in NodeEditor terms)

    public Vector2 CanvasPanOffset { get; set; }
    public float CanvasZoomLevel { get; set; }

    public event Action? Changed;
}

public sealed class BTreeEditorNode
{
    public Guid VisualId;              // primary editor identity; matches NodeDebugMetadata.VisualId
    public NodeType KernelType;        // Root, Sequence, Selector, Action, Subtree, etc.
    public int KernelBlobIndex;        // index into Blob.Nodes (re-derived per reload)
    public Vector2 Position;           // canvas coords
    public string DisplayLabel;        // human-readable; from DebugMetadata.Label
    public string? Comment;            // from DebugMetadata.CustomComment
    public List<Guid> ChildVisualIds;  // ordered (BTree composites are order-sensitive)

    // Per-node-type payload (mutually exclusive; only one populated)
    public BTreeActionPayload? Action;
    public BTreeConditionPayload? Condition;
    public BTreeWaitPayload? Wait;
    public BTreeSubtreePayload? Subtree;

    // Per-node ephemeral state (not persisted in layout method)
    public bool IsBreakpoint;          // session-local, per-user
}

public sealed class BTreeEditorPill
{
    public Guid VisualId;              // pill identity (its own; distinct from host's)
    public Guid HostNodeVisualId;      // the host BTree node the pill belongs to (the wrapper's child)
    public NodeType DecoratorType;     // Inverter, Repeater, Cooldown, ForceSuccess, ForceFailure, UntilSuccess, UntilFailure
    public int? IntParam;              // Repeater's count (from IntParams[PayloadIndex])
    public float? FloatParam;          // Cooldown's duration (from FloatParams[PayloadIndex])
    public string? Comment;
    public int StackIndex;             // ordering within host's pill stack
}

public sealed class BTreeActionPayload
{
    public string MethodFqn;           // "Hrot.Game.Combat.CombatActions.AimAndFire"
    public string? ExpressionTargetField;  // "AmmoCount" for `dto => dto.AmmoCount`
    public BTreeActionDelegateShape DelegateShape;  // ThreeParamReusable | FourParamFull
}

public sealed class BTreeConditionPayload
{
    public string MethodFqn;
    public string? ExpressionTargetField;
    public BTreeActionDelegateShape DelegateShape;
}

public sealed class BTreeWaitPayload
{
    public float Duration;             // seconds; from FloatParams[PayloadIndex]
}

public sealed class BTreeSubtreePayload
{
    public Guid SubtreeAssetId;        // resolved at open time from SubtreeAssetIds[PayloadIndex]
    public string SubtreeName;         // human-readable, e.g. "Search_BT"
    public bool IsResolved;            // false if the referenced asset isn't in the catalog
}

public enum BTreeActionDelegateShape
{
    ThreeParamReusable,   // ReusableActionDelegate<TValue, TContext> with expression target
    FourParamFull         // NodeLogicDelegate<TBB, TContext> with full blackboard access
}
```

### 3.2 Projection from compiled assembly to editor model

On asset open:

1. The asset catalog (shared infra §3.6) provides the `BehaviorTreeAsset` shell, populated from reflection of the `[BTreeDefinition]` method.
2. The contributor invokes the `Build()` method on the loaded assembly to get the `BehaviorTreeBlob`. It also retrieves `DebugMetadata[]` (which the source generator emits alongside the blob).
3. The projection walker scans `Blob.Nodes[]` in depth-first order, producing one `BTreeEditorNode` per kernel node, OR collapsing decorator-wrapper kernel-nodes into pills (see §6).
4. Each editor node's `VisualId` is taken from `DebugMetadata[i].VisualId`. If `DebugMetadata` is absent (handwritten asset without `visualId:` arguments), the editor mints fresh Guids and logs a one-shot diagnostic: "Minted VisualIds for OrcGuard_BT; will persist on first save."
5. The layout method (if any) is invoked via `LayoutDiscovery.TryGetLayout<BTreeLayoutAttribute, BTreeEditorLayout>` (shared infra §7.3). Each `BTreeNodeLayoutEntry` matches an editor node by VisualId; the entry's `Position`, `Comment`, `ExpressionTargetField` populate the editor node.
6. If no layout entry exists for a node (newly authored, or layout method absent entirely), auto-layout assigns a position via the tidy-tree algorithm (§3.4).
7. Subtree payloads resolve their `SubtreeAssetId` against the asset catalog; unresolved references are flagged with a warning diagnostic but the canvas still renders the subtree as a red-outlined black box.

### 3.3 The kernel-blob-index ↔ visualId mapping

The runtime knows `Blob.Nodes[i]`. The editor knows `BTreeEditorNode.VisualId`. The bridge is `NodeDebugMetadata[i].VisualId` parsed back to Guid.

A small lookup table on `BehaviorTreeAsset` makes this O(1):

```csharp
public sealed class BehaviorTreeAsset
{
    private Dictionary<Guid, int> _visualIdToBlobIndex;
    private Dictionary<Guid, BTreeEditorNode> _visualIdToNode;
    private Dictionary<Guid, BTreeEditorPill> _visualIdToPill;

    public BTreeEditorNode? FindNode(Guid visualId) =>
        _visualIdToNode.TryGetValue(visualId, out var n) ? n : null;

    public int FindBlobIndex(Guid visualId) =>
        _visualIdToBlobIndex.TryGetValue(visualId, out var i) ? i : -1;
}
```

The lookup rebuilds whenever the asset re-projects (after hot reload).

### 3.4 Auto-layout — tidy-tree

For newly authored nodes (no layout method entry), the editor runs a tidy-tree algorithm (Reingold-Tilford) at the first canvas render and writes the computed positions to the editor model. Layout method then captures them on next save.

Parameters:
- Horizontal spacing between siblings: 40 px
- Vertical spacing between parent and child: 80 px
- Root is centered at canvas origin (0, 0)
- Pill rows above a host node count toward vertical spacing (each pill row is 23 px tall, host header is 24 px tall — so an N-row-pill host needs `80 + 23*N` clearance instead of 80)

Auto-layout runs only on opening an asset with missing layout entries. Once positions are persisted, they're authoritative.

A "Reset layout" toolbar action re-runs tidy-tree across the whole tree, ignoring stored positions. Used when an asset has drifted into mess via hand-edits or merge conflicts.

---

## 4. The fluent C# emitter

### 4.1 Emit shape

`BTreeFluentEmitter : IFluentCSharpEmitter<BehaviorTreeAsset>` produces a deterministic `.cs` file with this structure:

```csharp
// HROT_EDITOR_GENERATED — manual edits to this file will be overwritten by the AI editor on next save.
// AssetId: f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21

using System;
using System.Numerics;

using Fbt;
using Fbt.Compiler;
using Hrot.AI.Behaviors.Trees.Layout;
using Hrot.Game.Combat;

namespace Hrot.AI.Behaviors.Trees;

public static class OrcGuard
{
    public static BTreeBuilder<CombatBlackboard, CombatContext> CreateBuilder() =>
        new BTreeBuilder<CombatBlackboard, CombatContext>()
            .ObserverSelector(s => s
                .Sequence(seq => seq
                    .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat,
                               visualId: new Guid("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29"))
                    .Repeater(3, r => r
                        .Cooldown(2.0f, c => c
                            .Action(dto => dto.AmmoCount, CombatActions.AimAndFire,
                                    visualId: new Guid("c5e8b471-7a44-4d6e-9b1c-8f7a6e5d4c3b")),
                            visualId: new Guid("c0d1aa11-3f55-4e7a-8b2c-1d0e9f8a7b6c")),
                        visualId: new Guid("b4711d22-1d22-4c5e-9a3b-2c4d5e6f7a8b")),
                    visualId: new Guid("d0913f55-3f55-4e6a-8b7c-2d1e0f9a8b7c"))
                .Action(CombatActions.HoldPosition,
                        visualId: new Guid("e2a3bb66-bb66-4c7d-9e8f-3a2b1c0d9e8f")),
                visualId: new Guid("f7c01188-1188-4c5d-9e3a-7b6c5d4e3f21"));

    [BTreeDefinition("OrcGuard_BT", AssetId = "f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21")]
    public static BehaviorTreeBlob Build() => CreateBuilder().Compile("OrcGuard_BT");

    [BTreeLayout("f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21")]
    public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
        .Canvas(panOffset: new Vector2(12f, -34f), zoomLevel: 1.0f)
        .Node("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29",
              position: new Vector2(120f, 340f),
              expressionTarget: "ThreatVisible")
        .Node("b4711d22-1d22-4c5e-9a3b-2c4d5e6f7a8b",
              position: new Vector2(280f, 200f))                              // Repeater (pill)
        .Node("c0d1aa11-3f55-4e7a-8b2c-1d0e9f8a7b6c",
              position: new Vector2(280f, 200f))                              // Cooldown (pill)
        .Node("c5e8b471-7a44-4d6e-9b1c-8f7a6e5d4c3b",
              position: new Vector2(280f, 480f),
              expressionTarget: "AmmoCount",
              comment: "burst fire pattern")
        .Node("d0913f55-3f55-4e6a-8b7c-2d1e0f9a8b7c",
              position: new Vector2(220f, 200f))
        .Node("e2a3bb66-bb66-4c7d-9e8f-3a2b1c0d9e8f",
              position: new Vector2(580f, 200f),
              comment: "fallback when no threats")
        .Node("f7c01188-1188-4c5d-9e3a-7b6c5d4e3f21",
              position: new Vector2(400f, 60f))
        .Build();
}
```

Three methods in stable order: `CreateBuilder()` (the fluent), `Build()` (the `[BTreeDefinition]` thunk), `Layout()` (the editor-only).

### 4.2 Deterministic emit rules (BTree-specific)

The shared emitter rules (`AI_Editor_Shared_Infrastructure.md` §6.2) apply. Additional BTree-specific rules:

1. **Fluent call indentation.** Each builder call's lambda body is indented one level deeper than its parent. Always 4 spaces.
2. **`visualId:` argument position.** Always the last argument of each builder call, on its own line. The leading argument(s) (action method ref, expression target, repeater count, etc.) precede it.
3. **Children of composites: explicit lambda body.** `seq => seq.Child(...)` not `seq.Child(...)`. Reads cleanly even with one child.
4. **Action method references: fully qualified or short, per `using` set.** If `using Hrot.Game.Combat;` is in the file, emit `CombatActions.AimAndFire`. Otherwise emit `Hrot.Game.Combat.CombatActions.AimAndFire`. The `using` set is computed per-emission and sorted (shared infra §6.4).
5. **Pill emission order.** When the host kernel node has decorators wrapped around it (per §6), the wrappers are emitted *outside-in* — the outermost decorator is the outermost fluent call. This matches the kernel's "outermost evaluates result-bubbling last" semantics.
6. **Empty composites are valid.** A `.Sequence(seq => { })` emits with a single-line empty lambda. The validator (§11) flags it as a warning.
7. **Wait nodes always emit float duration with `f` suffix.** `.Wait(1.5f, ...)` not `.Wait(1.5, ...)`.
8. **Repeater counts emit as `int` literals.** `.Repeater(3, ...)` not `.Repeater(3.0, ...)`.

### 4.3 Layout method emission

The `[BTreeLayout]` method follows the shared rules:
- Layout entries are sorted by `Guid` lexicographic ordering (stable across runs).
- Pills get entries with positions but `position` defaults to `default(Vector2)` since pills don't have their own visual position (they're stacked above the host).
- Optional fields (`comment`, `expressionTarget`) are emitted only when non-null. Empty `Node()` calls (`Node("guid")`) are valid for nodes with default position and no metadata.

### 4.4 Round-trip property

The emitter has a self-test: emit → write to in-memory string → invoke Roslyn → reflect type → re-project → compare with original `BehaviorTreeAsset`. Result MUST be byte-identical in the C# string and structure-identical in the model. CI runs this against a fixture of 5–10 representative trees.

Failure modes the round-trip catches:
- Reordered children that the emitter forgot to preserve.
- A visualId on a wrapper-decorator that the emitter dropped.
- Expression target lambda regenerated as wrong field.
- Float/int formatting changes (locale-specific commas, etc.).

---

## 5. NodeEditor host services

`Hrot.BTree.Editor` provides an `IEditorHostServices` instance to NodeEditor when the BTree canvas window is created. Composition:

```csharp
public sealed class BTreeEditorHostServices : IEditorHostServices
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

Shared (from `Hrot.Editor.AiShared`): `Clipboard`, `Icons`, `Diagnostics`, `Input`, `Theme`.
BTree-specific: `NodeCatalog`, `TypeSystem`, `LinkValidator`, `CommandSink`, `Pickers`, `Debug`, `CustomCanvasRenderers`.

### 5.1 `BTreeNodeCatalog`

Provides palette content. Two parts:

**Static node kinds** (always present):

| `NodeKindKey` | Category | Display |
|---|---|---|
| `bt.composite.sequence` | FlowControl | Sequence |
| `bt.composite.selector` | FlowControl | Selector |
| `bt.composite.observerSelector` | FlowControl | Observer Selector (👁) |
| `bt.composite.parallel` | FlowControl | Parallel |
| `bt.leaf.action` | Function | Action |
| `bt.leaf.condition` | Pure | Condition |
| `bt.leaf.wait` | Function | Wait |
| `bt.leaf.subtree` | Macro | Subtree |
| `bt.decorator.inverter` | Custom | Inverter (pill) |
| `bt.decorator.repeater` | Custom | Repeater (pill) |
| `bt.decorator.cooldown` | Custom | Cooldown (pill) |
| `bt.decorator.forceSuccess` | Custom | ForceSuccess (pill) |
| `bt.decorator.forceFailure` | Custom | ForceFailure (pill) |
| `bt.decorator.untilSuccess` | Custom | UntilSuccess (pill) |
| `bt.decorator.untilFailure` | Custom | UntilFailure (pill) |

Decorator entries are special: selecting them in the palette creates a pill on the currently-selected node, not a new node on the canvas. The catalog tags these with a metadata flag `PaletteAction = AttachToSelected`.

**Dynamic action and condition entries** — populated from `BehaviorRegistry` plus the editor's reference catalog (shared infra §4.3):

```csharp
foreach (var registryEntry in _behaviorRegistry.AllActions)
{
    var fqn = _referenceCatalog.ResolveFqn(registryEntry.ShortName);
    yield return new NodeCatalogEntry(
        Kind: new NodeKindKey($"bt.leaf.action.{fqn}"),
        Title: registryEntry.ShortName,
        Category: NodeCategory.Function,
        Keywords: registryEntry.Tags ?? Array.Empty<string>(),
        Description: $"Action: {fqn}",
        IconKey: "bt/action");
}
```

Plus an additional layer for Blueprint-hosted AiPrimitives (those declared with `hostings: ["BTreeAction"]`) — they appear in the same catalog, grouped under "Blueprint-hosted actions" (shared infra §4.7). The catalog re-queries on `IAssetCatalog.Changed` (hot reload completed) so newly-added Blueprints show in the picker on the next open.

### 5.2 `BTreeTypeSystem`

BTree has effectively no link-level type system — children of a composite are an ordered list of structural sub-nodes, not pin-routed data. The type system implementation is minimal:

```csharp
public sealed class BTreeTypeSystem : ITypeSystem
{
    private static readonly TypeKey ExecKey = new TypeKey("bt.exec");

    public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
    {
        if (key == ExecKey)
        {
            info = new TypeDisplayInfo("execution", "Tree edge", Vector4.One);
            return true;
        }
        info = default;
        return false;
    }

    public Vector4 GetPinColor(TypeKey key) => Vector4.One;  // white for the implicit exec edge
    public PinShape GetPinShape(TypeKey key, ContainerKind c) => PinShape.Triangle;
    public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;
    public bool AreCompatible(TypeKey from, TypeKey to) => from == to;
    public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
}
```

We model BTree edges as a single implicit exec type. Every node has at most one input "parent" pin and at most one output "child" pin (the actual children are an ordered list represented as one pin connected to multiple children — NodeEditor's exec-fan-out rule per NodeEdit §10).

Wait — the existing connection rules forbid "one exec output → many exec inputs." We need to bend that for BTree, since a composite's child list is conceptually one-to-many. Two options:

**Option A: Use the existing rule "many exec outputs → one exec input."** Reverse the direction in the model — each child has *one* "output" going to *one* parent. The parent has *one* "input" receiving from many children. This is upside-down semantically (parents emit, children receive in BTree execution flow) but matches NodeEditor's existing rule.

**Option B: Add a per-asset-type rule override.** Allow the BTree host to relax the fan-out rule for its exec edges. Requires a NodeEditor change (a per-`TypeKey` rule override on `ITypeSystem` or `ILinkValidator`).

Option A is cheaper but confusing in source. Option B is cleaner but requires NodeEditor work.

**Decision: Option A.** BTree pin direction is reversed: children have output-pins pointing at parents' input-pins. Visually the wires still flow top-to-bottom (parent above, children below) because NodeEditor's wire routing is based on pin positions, not pin directions. The user sees the right picture; the model uses the legal NodeEditor connection rule. We document this clearly in the BTree host's developer notes so future maintainers don't get confused.

### 5.3 `BTreeLinkValidator`

Cycle prevention (a BTree is by definition acyclic), plus structural rules:

- A node can have at most one parent. Adding a second incoming edge replaces the existing edge.
- The Root node has no parent (its parent pin is hidden).
- Decorator pills are attachments, not nodes — they don't participate in linking.
- Subtree nodes are leaves — they don't accept outgoing edges.
- Leaves (Action, Condition, Wait) don't accept outgoing edges either.

```csharp
public LinkValidationResult Validate(PinId from, PinId to)
{
    var fromNode = _graph.FindNode(_graph.FindPin(from)!.OwnerNodeId)!;
    var toNode = _graph.FindNode(_graph.FindPin(to)!.OwnerNodeId)!;

    // toNode is the "parent" (per the reversed convention from §5.2)
    if (IsLeaf(toNode.Kind))
        return new(LinkValidity.Invalid, "Leaf nodes cannot have children", false, null);

    if (WouldCreateCycle(fromNode, toNode))
        return new(LinkValidity.Invalid, "Would create a cycle", false, null);

    return new(LinkValidity.Valid, null, false, null);
}
```

Cycle detection runs locally: walk up the ancestor chain of `toNode` looking for `fromNode`. Bounded by tree depth, fast in practice.

### 5.4 `BTreeCommandSink`

Translates NodeEditor `GraphCommand` records into edits on `BehaviorTreeAsset`:

```csharp
public GraphCommandResult Apply(GraphCommand command)
{
    switch (command)
    {
        case GraphCommand.MoveNodes moveNodes:
            ApplyNodeMoves(moveNodes.Moves);
            break;

        case GraphCommand.AddNode add:
            ApplyAddNode(add);
            break;

        case GraphCommand.RemoveNodes rem:
            ApplyRemoveNodes(rem.NodeIds);
            break;

        case GraphCommand.AddLink link:
            ApplyAddLink(link.From, link.To);
            break;

        case GraphCommand.RemoveLinks unlink:
            ApplyRemoveLinks(unlink.LinkIds);
            break;

        case GraphCommand.SetNodeProperty setProp:
            ApplySetNodeProperty(setProp.NodeId, setProp.Key, setProp.Value);
            break;

        // From the NodeAttachments extension:
        case GraphCommand.AddAttachment att:
            ApplyAddPill(att);
            break;

        case GraphCommand.RemoveAttachments remAtt:
            ApplyRemovePills(remAtt.AttachmentIds);
            break;

        case GraphCommand.SetAttachmentProperty setAttProp:
            ApplySetPillProperty(setAttProp.Id, setAttProp.Key, setAttProp.Value);
            break;

        case GraphCommand.ReorderAttachments reorder:
            ApplyReorderPills(reorder.HostNodeId, reorder.NewOrder);
            break;

        case GraphCommand.Batch batch:
            foreach (var sub in batch.Commands) Apply(sub);
            break;

        default:
            return GraphCommandResult.Failed($"Unsupported command: {command.GetType().Name}");
    }

    _scheduler.ScheduleSave(_asset);  // shared infra: debounced save
    return GraphCommandResult.Ok;
}
```

Each `Apply*` method mutates `BehaviorTreeAsset` in-place. The `Changed` event fires from the asset on completion, which propagates to NodeEditor as a `GraphChangeNotification` for re-render.

The pill-handling commands map to `BTreeEditorPill` operations on `BTreeEditorAsset.Pills`. See §6 for the pill model.

---

## 6. Decorator pills — collapse and round-trip

This is the BTree host's signature feature. The decision (from earlier brainstorm rounds): **render as pills, store as nested builder calls**.

### 6.1 The mapping

For each `NodeType` value, classify it:

- **Composite** (Sequence, Selector, Observer Selector, Parallel, Root): regular node. Has children.
- **Leaf** (Action, Condition, Wait, Subtree): regular node. No children.
- **Decorator** (Inverter, Repeater, Cooldown, ForceSuccess, ForceFailure, UntilSuccess, UntilFailure): collapsed into a pill on its single child.

The kernel always represents decorators as parent nodes with one child. The editor projects this to:
- If the decorator's child is itself a decorator, peel both into pills on the *innermost* non-decorator node, in source order (outermost decorator becomes the topmost pill).
- If the decorator's child is a composite or leaf, the decorator becomes a pill on that node.

### 6.2 Projection example

The kernel sees:

```
Root
└─ Cooldown(2.0s)
    └─ Repeater(3)
        └─ Sequence
            ├─ Condition(HasThreat)
            └─ Action(AimAndFire)
```

The editor projects to:

```
Root
└─ Sequence  [pills: ↺×3, ⏲2s]
    ├─ Condition(HasThreat)
    └─ Action(AimAndFire)
```

Two pills on the Sequence; the wrapper kernel nodes (Cooldown, Repeater) collapse. The pill's `StackIndex`:

- Inverter, Repeater, Cooldown, etc. are stacked **left-to-right** in source order = innermost-to-outermost.
- So Repeater (innermost wrapper) has `StackIndex = 0` (leftmost); Cooldown (outermost) has `StackIndex = 1` (rightmost).
- Reading visually left-to-right = innermost-to-outermost = the order children's results bubble up.

This is opposite to my earlier brainstorm note ("outermost = topmost = evaluates last"). On reflection, **innermost-to-outermost from left-to-right** reads more naturally because that's the order results bubble: the inner Repeater iterates first, then the Cooldown gates each iteration. Updating to this convention now; it's the right call.

### 6.3 Pill emission on save

On save, the emitter walks the editor tree. For each editor node with pills:

```csharp
private void EmitNodeWithPills(StringBuilder sb, BTreeEditorNode node, int indent)
{
    // Walk pills from StackIndex high to low (outermost first, which is the rightmost pill).
    // Emit each as a fluent wrapper around the next inner expression.

    var pillsByStack = node.Pills().OrderByDescending(p => p.StackIndex).ToList();

    if (pillsByStack.Count == 0)
    {
        EmitPlainNode(sb, node, indent);
        return;
    }

    foreach (var pill in pillsByStack)
        EmitPillOpen(sb, pill, indent++);

    EmitPlainNode(sb, node, indent);

    foreach (var pill in pillsByStack)
        EmitPillClose(sb, pill, --indent);
}
```

Round-trip property holds: the emit produces nested decorator calls; the next projection collapses them back into the same pill stack.

### 6.4 Pill commands

The NodeAttachments commands (`AddAttachment`, `RemoveAttachments`, etc.) map naturally:

- **AddAttachment** with `Category: Decorator` and `HostProperties` carrying decorator kind, count or duration:
  ```csharp
  new GraphCommand.AddAttachment(
      NewId: ...,
      HostNodeId: ...,
      Category: AttachmentCategory.Decorator,
      Glyph: "↺",
      Label: "×3",
      Tooltip: "Repeater: 3 iterations",
      StackIndex: ...,
      HostProperties: new Dictionary<string, object?>
      {
          ["decoratorType"] = NodeType.Repeater,
          ["intParam"] = 3,
      });
  ```
- **RemoveAttachments** drops the pill; the editor's command sink updates the kernel node tree accordingly.
- **SetAttachmentProperty** lets the inspector update Repeater's count or Cooldown's duration without removing/re-adding.

### 6.5 The Add Decorator UX

The user adds a decorator via:

- **Palette: drop a decorator entry** — the palette's drop-action mode is `AttachToSelected`; dropping on a node creates a pill, not a new node.
- **Right-click a node → "Add Decorator →"** submenu lists decorator kinds. Selection emits `AddAttachment`.
- **Drag a decorator pill onto a node** — drop replaces the existing decorator if one of the same kind already exists; otherwise adds.

Removing a pill:
- Click pill, press Delete.
- Right-click pill → "Remove decorator."

Reordering pills:
- Drag a pill horizontally within its host's pill stack. The NodeAttachments extension supports this via `ReorderAttachments`.

### 6.6 What about Service nodes?

`NodeType.Service` (kernel value 30) is currently a planned-but-not-implemented decorator. When implemented, it would attach to a composite and run periodically while that composite is active. **Decision: also a pill, but with a distinct glyph.** Services don't fit the "decorator wrapper" pattern as cleanly (they're more like attached behaviors than transforming wrappers), but rendering them as pills with a distinct color/glyph keeps the visual vocabulary consistent. Deferred to when Services land in the kernel.

`NodeType.Observer` (value 31) is the standalone Observer (a leaf-like guard placed inside an Observer Selector's children). It is *not* a pill — it's a leaf node with its own visual entry. See §7.

---

## 7. Observer Selector — distinct visual, guard badges

### 7.1 Palette identity

Observer Selector is a separate palette entry (`bt.composite.observerSelector`) distinct from Selector (`bt.composite.selector`). The fluent builder has two methods:

```csharp
// Standard
builder.Selector(s => s.Action(...).Action(...));

// With abort behavior (re-evaluates earlier guards each tick)
builder.ObserverSelector(s => s.Condition(...).Action(...));
```

The kernel distinguishes via `NodeType.Selector` vs. `NodeType.Observer` (used as the composite's type, not as a child). Editor's projection maps each to its appropriate palette identity.

### 7.2 Visual treatment

The standard Selector renders with normal header coloring (`NodeCategory.FlowControl` orange). The Observer Selector adds:

- **Eye glyph (👁) in the header** — rendered to the left of the title text, ~14 px square.
- **Slightly darker header tint** — Observer Selectors use a 10% darker orange to visually distinguish from Selectors at a glance. (Optional polish; the eye alone is the primary signal.)

### 7.3 Guard badges on observer-child connections

The host registers a custom canvas renderer at the `AfterWires` pass (`btree.observer_guard_badges`):

```csharp
public sealed class ObserverGuardBadgeRenderer : ICustomCanvasRenderer
{
    public string Id => "btree.observer_guard_badges";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterWires;
    public bool IsActive => true;

    public void Render(ICanvasRenderContext ctx)
    {
        foreach (var linkId in ctx.VisibleLinks)
        {
            var link = ctx.Graph.FindLink(linkId);
            if (link is null) continue;

            var parent = GetParentNodeForLink(ctx.Graph, link);
            var child = GetChildNodeForLink(ctx.Graph, link);

            if (parent.Kind != BTreeNodeKinds.ObserverSelector) continue;
            if (!IsGuardKind(child.Kind)) continue;

            // Compute a point ~30% along the link from parent to child
            var midpoint = ComputeLinkPointAt(link, 0.3f);
            var screenPos = ctx.CanvasToScreen(midpoint);

            // Draw a small pill: eye glyph + "OBSERVES" label
            DrawGuardBadge(ctx, screenPos);
        }
    }

    private bool IsGuardKind(NodeKindKey kind) =>
        kind == BTreeNodeKinds.Condition || kind == BTreeNodeKinds.Observer;
}
```

This mirrors the kernel's `IsGuardNode` check (FastBTree.txt §1985): a child is a guard if its `NodeType` is `Condition` or `Observer`. The badge sits above the link's near-parent third, drawn as a small rounded pill with `👁 OBSERVES`.

Clicking the badge does nothing in v1 — it's informational. A tooltip on hover explains: "This child is a guard. The Observer Selector re-evaluates it every tick; failure preempts the running sibling and bumps TreeVersion."

The renderer participates in hit-test only for the tooltip; selection routes to the link, not the badge.

### 7.4 What about plain Selectors with Condition children?

A plain Selector with Condition children does NOT preempt; the Condition only runs when the Selector reaches that child in priority order. No `👁 OBSERVES` badge appears.

This is the visual difference between Selector and ObserverSelector: the eye on the header + badges on guard children. Authors can see at a glance which selectors have preemption.

---

## 8. Subtrees as black boxes

### 8.1 Visual

A Subtree node renders as a regular node with:
- Category color: Macro purple (the existing `NodeCategory.Macro`)
- Title: the referenced asset's name (e.g., "Search_BT")
- A 📦 glyph in the header
- A small text strip below the header: "subtree" in muted gray
- No expanded children inside the node body — it stays a single rounded rect

### 8.2 Resolution

The Subtree node's `BTreeSubtreePayload.SubtreeName` is the string stored in `BehaviorTreeBlob.SubtreeAssetIds[PayloadIndex]`. On asset open, the projection layer resolves it:

1. Look up the name in the asset catalog.
2. If found: `SubtreeAssetId` is set, `IsResolved = true`.
3. If not found: `SubtreeAssetId = Guid.Empty`, `IsResolved = false`. The node renders with a red outline; a tooltip says "Subtree 'X' not found in the project."

### 8.3 Navigation

Double-click a Subtree node:
1. Editor checks `IsResolved`.
2. If resolved, calls `EditorSelectionStore.ActiveAsset = referencedAsset`. The canvas window's content switches to the referenced asset.
3. A breadcrumb at the top of the canvas shows: `OrcGuard_BT › Subtree(Search_BT)`. Clicking the breadcrumb returns to the caller (sets `ActiveAsset` back to the parent).
4. If unresolved, no navigation. A status message at the bottom shows "Cannot open: asset not found."

The breadcrumb is a thin strip rendered as a custom canvas renderer at the `TopMost` pass when the canvas is showing a "navigated-from" asset. It's not part of NodeEditor; the BTree host owns it.

### 8.4 Inspector

Selecting a Subtree node shows in the Inspector:
- The referenced asset name (combo box to change to another asset in the catalog)
- The referenced asset's AssetId (read-only display)
- A "Open referenced asset" button

No inline preview of the subtree's contents. Honest to the runtime: the runtime treats the subtree as an opaque blob until execution enters it.

---

## 9. Blackboard reflection

### 9.1 The reflection model

The blackboard struct is defined in user code:

```csharp
public struct CombatBlackboard
{
    public int AmmoCount;
    public bool ThreatVisible;
    public Vector3 LastKnownPosition;
    public float EngagementRange;
}
```

The editor reflects this struct (the generic argument of `BTreeBuilder<TBlackboard, TContext>`) and exposes the field list:

```csharp
public sealed class BlackboardSchema
{
    public Type StructType { get; }
    public IReadOnlyList<BlackboardField> Fields { get; }
}

public sealed record BlackboardField(
    string Name,
    Type FieldType,
    BlackboardFieldKind Kind);   // Bool | Numeric | Vector | Enum | Struct | Other

public enum BlackboardFieldKind
{
    Bool,
    Numeric,    // int, float, double, long, short, byte
    Vector,     // Vector2, Vector3, Vector4
    Enum,
    Struct,     // user-defined struct
    Other,      // anything else (rare; surfaced as warning in the panel)
}
```

`BlackboardSchema` is computed once when the asset opens and re-computed on hot reload (the blackboard struct might gain or lose fields).

### 9.2 The Blackboard panel

A dedicated docked window, registered as `bt_blackboard_panel`. Shows the schema as a read-only list:

```
┌──────────────────────────────────────┐
│ BLACKBOARD: CombatBlackboard          │
├──────────────────────────────────────┤
│ int       AmmoCount                   │
│ bool      ThreatVisible               │
│ Vector3   LastKnownPosition           │
│ float     EngagementRange             │
└──────────────────────────────────────┘
```

In edit mode the panel is read-only. The user does not author blackboard fields in the editor; they edit the C# struct in their IDE. The editor reflects the result.

In debug mode (when an entity is selected and the runtime inspector is attached), the panel switches to live-values mode: each field shows its current value on the attached entity, refreshed every frame via the debug session. A "Make Editable" toggle promotes the panel to writable, allowing the user to mutate live blackboard state on the entity (useful for forcing test scenarios). Mutation is gated behind a confirmation banner and operates via the debug session's snapshot-and-write API.

### 9.3 Field changes between sessions

If the user removes a field from the blackboard struct and that field was referenced by an Action's expression target (`dto => dto.RemovedField`), the next reload fails to compile the user's tree code. The compile failure surfaces in the editor's diagnostics panel; the user fixes by either re-adding the field or updating the Action references via the refactor service.

If the user renames a field, references break the same way. The refactor service can rename across references if invoked from the field's right-click → Rename in the panel (the panel exposes the underlying field as an `IAssetSubElement` of kind `BlackboardField`).

---

## 10. Action / Condition / Wait inspectors

### 10.1 Facet structs

The Inspector (shared infra §10) dispatches on the selected sub-element's type and renders the appropriate facet via StructEdit. BTree provides:

```csharp
public struct BTreeActionFacet
{
    [EditDisplayName("Method")]
    [BehaviorHashPicker]                              // existing engine attribute
    public string MethodFqn;

    [EditDisplayName("Expression target (blackboard field)")]
    [BlackboardFieldPicker]                           // new BTree-specific picker attribute
    public string? ExpressionTargetField;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public string LastResult;     // "Success", "Failure", "Running", "—"

    [EditReadOnly]
    public int TickCount;
}

public struct BTreeConditionFacet
{
    // Same shape as BTreeActionFacet (Conditions and Actions differ only in NodeStatus semantics)
}

public struct BTreeWaitFacet
{
    [EditUnit("seconds")]
    [EditRange(0.0, 600.0)]
    public float Duration;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;
}

public struct BTreeSequenceFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public int ChildCount;
}

// Similar for SelectorFacet, ObserverSelectorFacet, ParallelFacet, SubtreeFacet, RootFacet

// Pill facets (decorators rendered as attachments)
public struct BTreeRepeaterFacet
{
    [EditRange(1, 9999)]
    public int Count;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

public struct BTreeCooldownFacet
{
    [EditUnit("seconds")]
    [EditRange(0.0, 600.0)]
    public float Duration;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

public struct BTreeInverterFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

// Similar for ForceSuccess, ForceFailure, UntilSuccess, UntilFailure
```

### 10.2 Method picker (`BehaviorHashPicker`)

The existing `[BehaviorHashPicker]` StructEdit attribute (from `StructEdit.md` §91) renders as a dropdown populated from a registered `BehaviorRegistry` instance — exactly what we need. The editor wires its `BehaviorRegistry` instance to the picker context at editor startup. The picker groups entries by declaring type and supports fuzzy search.

The picker filters by `<TBlackboard, TContext>` compatibility: only methods whose signature accepts the asset's blackboard and context types appear. This filtering is the editor's responsibility (the registry holds methods of multiple signatures); the picker delegates to a host-supplied filter function.

### 10.3 Expression target picker (`BlackboardFieldPicker`)

A new StructEdit attribute, BTree-specific, lives in `Hrot.BTree.Editor`. Renders as a dropdown over the asset's blackboard fields. Picks are constrained to the action method's expected target type (a `ReusableActionDelegate<TValue, TCtx>` with `TValue = int` only accepts integer-typed blackboard fields).

When the action method is changed via `BehaviorHashPicker`, the expression target picker updates its filter accordingly. If the previous expression target field is no longer valid (wrong type for the new method), it's cleared and a warning chip appears in the facet.

### 10.4 Live state in the facet

Three read-only fields (`LastResult`, `TickCount`, plus a similar `LastDuration` for Wait) are populated by the runtime inspector when an entity is attached. In edit mode, they show "—". The facet's `IEditSession` source provides these values by querying the debug session's `GetCurrentStateSnapshot()` each frame.

---

## 11. Validation

Validation runs after each model mutation (debounced ~200 ms) and produces a list of `BTreeDiagnostic` records:

```csharp
public sealed record BTreeDiagnostic(
    Guid VisualId,         // which element has the issue (or Guid.Empty for tree-level)
    BTreeDiagnosticSeverity Severity,
    BTreeDiagnosticCode Code,
    string Message);

public enum BTreeDiagnosticSeverity { Info, Warning, Error }

public enum BTreeDiagnosticCode
{
    EmptyComposite,
    UnboundActionMethod,
    UnboundConditionMethod,
    RepeaterCountInvalid,
    WaitDurationInvalid,
    UnresolvedSubtree,
    StackDepthExceeded,
    BlackboardFieldMissing,
    MethodSignatureMismatch,
    DanglingReferenceAfterReload,
    CycleDetected,
    OrphanedNode,
}
```

### 11.1 Rules

| Rule | Severity | Trigger |
|---|---|---|
| Sequence or Selector or ObserverSelector with zero children | Warning | `ChildCount == 0` on composite |
| Action with `MethodFqn == ""` | Error | unbound |
| Condition with `MethodFqn == ""` | Error | unbound |
| Repeater with `Count <= 0` | Warning | |
| Wait with `Duration <= 0` | Warning | |
| Subtree with `IsResolved == false` | Error | referenced asset not in catalog |
| Static nesting depth `> 8` levels (counting subtree nesting) | Warning | `BehaviorTreeState.NodeIndexStack` is 8 deep; exceeding risks runtime overflow |
| Blackboard field referenced by an action's expression target no longer exists | Error | reflected blackboard struct changed |
| Action method signature incompatible with `<TBlackboard, TContext>` | Error | rare; surfaces on hot reload |
| Reference catalog reports a missing referent after reload | Error | `IReferenceCatalog` (shared infra §4.3) returned no candidate |
| Cycle detected | Error | should never happen due to link validator (§5.3); defensive |
| Orphaned node (not reachable from Root) | Warning | can happen mid-edit when links are being rewired |

### 11.2 Surfacing diagnostics

- **In the canvas**: each affected node's `NodeState` is updated (`Error` or `Warning` flag), which NodeEditor renders as a colored outline plus the standard ⚠ glyph in the header.
- **In the inspector**: the facet's StructEdit display shows a banner with the diagnostic message for the selected node.
- **In a Diagnostics window** (optional, shared infra; defer to Slice 2): a separate panel listing all diagnostics across the asset.

### 11.3 Validation lifecycle

Validation runs:
- On asset open (full pass).
- After each `GraphCommand` apply (incremental — only affected nodes re-validated).
- After hot reload completion (full pass).
- On demand via a toolbar "Validate" button.

The diagnostic set is held on `BehaviorTreeAsset`; the Asset Browser and Find Results window can surface an asset's error/warning count via badges next to its row.

---

## 12. Runtime debug session and overlay

### 12.1 `IBTreeDebugSession`

Implements `IAiDebugSession` (shared infra §12) with BTree-specific extensions (per shared infra §12.2):

```csharp
namespace Hrot.BTree.Editor.Debug;

public interface IBTreeDebugSession : IAiDebugSession
{
    BehaviorTreeStateSnapshot? GetCurrentStateSnapshot();
    IReadOnlyList<BTreeNodeExecuted> GetRecentNodeHistory(int max = 100);
    IReadOnlyList<BTreeAsyncEvent> GetRecentAsyncHistory(int max = 100);

    event Action<BTreeBreakpointHit>? OnBreakpointHit;
    event Action<BTreeNodeExecuted>? OnNodeExecuted;
    event Action<BTreeAsyncEvent>? OnAsyncIssued;
    event Action<BTreeAsyncEvent>? OnAsyncResolved;
    event Action<BTreeAsyncEvent>? OnAsyncAborted;   // tree-version-mismatch
}

public sealed record BTreeNodeExecuted(
    Entity Self,
    Guid AssetId,
    Guid NodeVisualId,
    NodeStatus Status,
    float SimulationTime,
    uint Tick);

public sealed record BTreeAsyncEvent(
    Entity Self,
    Guid AssetId,
    Guid NodeVisualId,
    int RequestId,
    uint TreeVersion,
    BTreeAsyncPhase Phase,    // Issued | Resolved | Aborted
    float SimulationTime);

public enum BTreeAsyncPhase { Issued, Resolved, Aborted }

public sealed record BTreeBreakpointHit(
    Breakpoint Breakpoint,
    Entity Self,
    NodeStatus? StatusAtHit,    // null if break-on-enter; populated if break-on-result
    float SimulationTime);
```

### 12.2 Step-control semantics

Per shared infra §12.3, BTree step controls map to:

- **Continue**: resume normal ticking on the paused entity (`InstanceFlags.Paused` cleared).
- **Pause**: set `InstanceFlags.Paused` (a new flag we need on the BTree entity's `DebugState`; today only HSM has equivalent — see §17 open question).
- **Step Into**: advance one tick where execution descends into a child of the currently-running composite. If the running node is a leaf, behaves as Step Over.
- **Step Over**: advance one tick; pause again at the next node entry at or above the current stack depth.
- **Step Out**: advance ticks until the running node's stack depth decreases (i.e., the current subtree returns).

Implementation lives in the kernel; the editor invokes via the debug session.

### 12.3 Breakpoints

- **Per-user, session-local**: not persisted in the `.cs` file (per earlier brainstorm decision). Held in `BTreeDebugSession`'s in-memory dictionary.
- **Per-VisualId**: a breakpoint is `(AssetId, ElementId == VisualId)`. The kernel checks against the loaded `BehaviorTreeBlob`'s `DebugMetadata[]` when running.
- **Break-on-enter** (default) and **break-on-result** (kernel side: pause when this node's evaluation completes; surfaces the `NodeStatus`).

UI: click the gutter to the left of a node to toggle breakpoint. Pill nodes have a smaller breakpoint dot rendered to the right of the pill body.

### 12.4 Runtime overlay renderer

Per the CustomCanvasRenderer extension §17.1, the BTree host registers `btree.runtime_overlay`:

```csharp
public sealed class BTreeRuntimeOverlayRenderer : ICustomCanvasRenderer
{
    public string Id => "btree.runtime_overlay";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;
    public bool IsActive => _ctx.DebugSession?.IsAttached == true;

    public void Render(ICanvasRenderContext ctx)
    {
        var session = ctx.DebugSession as IBTreeDebugSession;
        var snapshot = session?.GetCurrentStateSnapshot();
        if (snapshot is null) return;

        // 1. Pulsing yellow border on the running node
        var runningNode = _asset.FindNode(snapshot.RunningElementId);
        if (runningNode != null)
        {
            var bounds = ctx.GraphView.NodeCanvasBounds(runningNode.NodeId);
            DrawPulsingOutline(ctx, bounds, ctx.Theme.SelectionAccent, frequency: 2.0f);
        }

        // 2. Gold dim outlines on stack ancestry
        for (int i = 0; i < snapshot.StackPointer; i++)
        {
            var ancestorId = snapshot.StackElementIds[i];
            if (ancestorId is null) continue;
            var ancestorNode = _asset.FindNode(ancestorId.Value);
            if (ancestorNode != null)
            {
                var bounds = ctx.GraphView.NodeCanvasBounds(ancestorNode.NodeId);
                float dimFactor = 0.4f + (0.6f * (i + 1) / snapshot.StackPointer);  // brighter further down
                DrawDimOutline(ctx, bounds, ctx.Theme.SelectionAccent, dimFactor);
            }
        }

        // 3. Status glyphs from recent node history
        foreach (var executed in session.GetRecentNodeHistory(50))
        {
            var node = _asset.FindNode(executed.NodeVisualId);
            if (node is null) continue;
            DrawStatusGlyph(ctx, node, executed.Status);
        }

        // 4. Async-pending clock icons
        foreach (var asyncEvent in session.GetRecentAsyncHistory(20))
        {
            if (asyncEvent.Phase != BTreeAsyncPhase.Issued) continue;
            var node = _asset.FindNode(asyncEvent.NodeVisualId);
            if (node is null) continue;

            bool isZombie = asyncEvent.TreeVersion != snapshot.TreeVersion;
            DrawAsyncBadge(ctx, node, asyncEvent.RequestId, isZombie);
        }
    }
}
```

This is the most visually rich of the host's renderers. All overlays render at the `AfterNodes` pass, so they sit on top of nodes but below selection outlines.

### 12.5 Subtree-boundary indicator

A second custom renderer at `BeforeContent`:

```csharp
public sealed class SubtreeBoundaryRenderer : ICustomCanvasRenderer
{
    public string Id => "btree.subtree_boundaries";
    public CanvasRenderPass Pass => CanvasRenderPass.BeforeContent;
    public bool IsActive => _session?.IsAttached == true && CurrentStackPointer > 0;

    public void Render(ICanvasRenderContext ctx)
    {
        // When the kernel is inside a subtree (StackPointer > 0), the subtree's
        // root node is the entry point. Compute the AABB enclosing all nodes
        // reachable from that root, draw a faint blue dashed rectangle around it.
        var snapshot = _session.GetCurrentStateSnapshot();
        if (snapshot.StackPointer == 0) return;

        var subtreeRoot = _asset.FindNode(snapshot.StackElementIds[0]);
        if (subtreeRoot is null) return;

        var aabb = ComputeSubtreeAabb(subtreeRoot);
        var screenAabb = ctx.CanvasToScreen(aabb);

        DrawDashedRect(ctx, screenAabb,
                       color: new Vector4(0.3f, 0.5f, 1.0f, 0.3f),  // faint blue
                       dashLength: 8f, gapLength: 6f, lineWidth: 1.5f);
    }
}
```

Sits behind the actual nodes (drawn at `BeforeContent`) so it shows as a soft background tint indicating the bounded region.

### 12.6 Heatmap renderer

Per the multi-instance debug mode (shared infra §14.3), the heatmap renderer colors nodes by aggregate activity across all entities running the asset:

```csharp
public sealed class HeatmapOverlayRenderer : ICustomCanvasRenderer
{
    public string Id => "btree.heatmap_overlay";
    public CanvasRenderPass Pass => CanvasRenderPass.BeforeContent;
    public bool IsActive => _runtimeState.HeatmapModeActive;

    public void Render(ICanvasRenderContext ctx)
    {
        // For each visible node, fetch its aggregate entry count from the debug session.
        // Map count → color (cold = blue, mid = green/yellow, hot = red).
        // Draw a colored fill behind the node.
        var session = ctx.DebugSession as IBTreeDebugSession;
        var aggregates = session?.GetAggregateCounters(_asset.AssetId);
        if (aggregates is null) return;

        var maxCount = aggregates.Values.DefaultIfEmpty(0).Max();

        foreach (var nodeId in ctx.VisibleNodes)
        {
            var node = ctx.Graph.FindNode(nodeId);
            if (node is null) continue;

            var visualId = _asset.LookupVisualId(nodeId);
            if (!aggregates.TryGetValue(visualId, out var count)) continue;

            var heat = (float)count / maxCount;
            var fillColor = HeatToColor(heat);  // blue → green → yellow → red gradient

            var bounds = ctx.CanvasToScreen(ctx.GraphView.NodeCanvasBounds(nodeId));
            ctx.DrawList.AddRectFilled(bounds.Min, bounds.Max,
                                        ImGui.GetColorU32(fillColor));
        }
    }
}
```

Aggregate counters are maintained by the debug session when heatmap mode is on. Cost: a single increment per node-enter event per entity; cheap.

### 12.7 Live runtime inspector pane

`BTreeRuntimeInspectorPane` plugs into the shared Runtime Inspector window (shared infra §14.1):

```csharp
public sealed class BTreeRuntimeInspectorPane : IRuntimeInspectorPane
{
    public AssetKind TargetKind => AssetKind.BTree;

    public void Draw(IRuntimeInspectorContext ctx)
    {
        var session = ctx.DebugSession as IBTreeDebugSession;
        var snapshot = session?.GetCurrentStateSnapshot();
        if (snapshot is null)
        {
            ImGui.Text("No live BTree state");
            return;
        }

        // BTree-specific state panels:
        DrawHeader(snapshot);            // RunningNode, StackDepth, TreeVersion
        DrawStackPanel(snapshot);        // NodeIndexStack[0..StackPointer] symbolicated
        DrawLocalRegistersPanel(snapshot); // 4 ints
        DrawAsyncHandlesPanel(snapshot); // 3 ulongs (version, requestId), zombie detection
    }
}
```

Panel layouts match the brainstorm summary (the recap I gave in the earlier "BTree summary" message). All pulled from `BehaviorTreeStateSnapshot`.

---

## 13. Trace timeline lanes

`BTreeTraceLaneProvider` registers four lanes for BTree assets:

| Lane ID | Display | TraceLevel bits | Records shown |
|---|---|---|---|
| `bt.nodes` | NodeStatus | Lifecycle, Decisions | `BTreeNodeExecuted` records with status colored bars (green=Success, red=Failure, yellow=Running) |
| `bt.stack` | Stack | Lifecycle | Subtree push/pop events as bracketed ranges |
| `bt.async` | Async | Async | `BTreeAsyncEvent` records with phase color (issued=blue, resolved=green, aborted=red) |
| `bt.errors` | Errors | Errors | Tracer overflow, missing-method errors |

The shared Trace Timeline (shared infra §15) consumes these and renders the horizontal swim lanes per the ASCII sketch in §2 above.

Clicking a record in any lane (in Replay mode) jumps the canvas overlay's cursor to that tick.

---

## 14. Quick reload pipeline

### 14.1 Triggers

The user clicks Save (or autosave fires); the regeneration scheduler (shared infra §5.5) emits the `.cs` file. The engine's existing file watcher picks up the change and triggers MSBuild.

### 14.2 Classification

After the file write, the editor pre-classifies the reload tier (per shared infra §17.2) by comparing the in-memory asset's structure and parameter hashes against the previously-loaded asset:

- `StructureHash` differs → Hard reload pending.
- `ParamHash` differs only → Soft reload pending.
- Neither differs → Cosmetic.

The classification shows in the status indicator immediately after save, before the actual rebuild completes. Hard reloads trigger a confirmation dialog if live instances are present.

### 14.3 The rebuild itself

MSBuild compiles the user's project. The resulting assembly is loaded into the ALC. The kernel's `BTreeHotReloadManager.TryReload<TState>` swaps the blob:

- Cosmetic: no kernel action; only the editor reloads its layout cache.
- Soft: kernel patches `MethodNames[]` / `FloatParams[]` / `IntParams[]` / `SubtreeAssetIds[]` in-place; live entities keep their `BehaviorTreeState`.
- Hard: kernel bumps `TreeVersion` per entity; `Reset()` clears state.

### 14.4 Post-reload editor refresh

After the new assembly is loaded:
1. Asset catalog rebuilds (subsystem-provided contributors).
2. Reference catalog rebuilds (shared infra §4.3).
3. The BTree asset's projection re-runs: new `BehaviorTreeBlob` + `NodeDebugMetadata[]` + layout method are read.
4. The editor model is reconciled against the new projection: entries with matching VisualIds keep their layout-method-derived properties (positions, comments, breakpoints); new entries get default positions.
5. `IGraphModel.Changed` fires; NodeEditor re-renders.

Author-perceived latency target: ≤ 100 ms.

---

## 15. Slice plan

Implementation order, matching the overall five-slice plan from shared infra:

### Slice 1: Authoring without debug
- `BehaviorTreeAsset` projection from compiled assembly + layout method
- `BTreeGraphModel`, `BTreeCommandSink`, `BTreeNodeCatalog`, `BTreeLinkValidator`, `BTreeTypeSystem`
- Decorator pill collapse / round-trip
- Observer Selector palette entry + custom canvas renderer for guard badges
- Subtree black-box rendering with navigation
- Blackboard reflection + Blackboard panel (read-only)
- Action / Condition / Wait inspectors via StructEdit facets
- `BTreeFluentEmitter` with deterministic round-trip property test
- Validation diagnostics surfaced in canvas + inspector
- Quick Reload with Cosmetic / Soft / Hard tiering

### Slice 2: Runtime inspection (read-only)
- `IBTreeDebugSession` with `GetCurrentStateSnapshot()` and observer-mode lifecycle
- `BTreeRuntimeOverlayRenderer` for running-node pulse and stack ancestry
- Live blackboard panel (read-only mode)
- `BTreeRuntimeInspectorPane` showing kernel state fields
- `BTreeTraceLaneProvider` for the trace timeline

### Slice 3: Stepping and breakpoints
- Breakpoints (per-user session-local)
- Pause / Step Into / Step Over / Step Out controls wired through the debug session
- Live mutation of blackboard fields ("Make Editable" toggle)
- Subtree-boundary renderer
- Async-event lane in trace timeline

### Slice 4: Multi-instance
- Aggregate counter collection in the debug session
- Heatmap renderer
- Asset Browser live-instance count

### Slice 5: Polish
- Reset-layout toolbar action
- Find References on action FQNs (uses shared refactor service)
- Refactor: rename action surfaces in BTree (handled by shared refactor; BTree just provides reference data)
- Comments on individual nodes
- Decorator pill visual polish (animation when added/removed, hover details)

---

## 16. Test strategy

### 16.1 Unit tests (`Hrot.BTree.Editor.Tests`)

- **`BehaviorTreeAssetProjectionTests`** — given a fixture of `BehaviorTreeBlob` + `NodeDebugMetadata[]` + layout method, verify the editor model reconstructs correctly. Includes round-trip: project → mutate → emit → re-project, compare.
- **`DecoratorPillCollapseTests`** — kernel tree with a stack of decorator wrappers projects to pills on the innermost non-decorator. Outermost wrapper = rightmost pill. Emit-and-re-project round-trips.
- **`BTreeFluentEmitterDeterminismTests`** — same model → same byte output across runs. `using` ordering correct. Float literals use `f` suffix.
- **`BTreeCommandSinkTests`** — each `GraphCommand` produces the right model edit; ScheduleSave is called once per command.
- **`BTreeLinkValidatorTests`** — leaves reject outgoing edges; Subtree rejects outgoing edges; cycle detection fires.
- **`BTreeValidationTests`** — each diagnostic code triggers under the right conditions; severity is correct.
- **`BTreeNodeCatalogTests`** — palette includes static entries + dynamic entries from `BehaviorRegistry`; updates on registry change.
- **`BlackboardSchemaReflectionTests`** — reflects test blackboard structs; classifies field kinds; updates on hot reload.

### 16.2 Integration tests (`Hrot.BTree.Editor.IntegrationTests`)

- **Project-open-and-save round-trip** — load an asset from a representative project, mutate one node, save, reload, verify the model matches.
- **Hot reload classification** — three fixtures (layout-only change, parameter-only change, structure change); verify Cosmetic / Soft / Hard.
- **Subtree navigation** — double-click a Subtree, verify `ActiveAsset` switches; click breadcrumb, verify return.
- **Refactor: rename action via shared refactor service** — verify all referencing BTree assets are updated; one reload after batch.
- **Debug session attach with breakpoint hit** — attach to a running entity, set breakpoint, advance ticks, verify hit fires with correct payload.

### 16.3 Visual / manual tests

The shared editor's demo or test harness gains a "BTree" scenario:
- A 30-node tree with decorators, an Observer Selector, a Subtree reference, multiple action types.
- Live debug overlay with a fake entity producing tick events.
- Heatmap mode with synthetic multi-instance counters.

Manual checklist:
- Pills render in correct order; click selects pill; Inspector shows correct facet.
- Observer Selector eye glyph visible; guard badges on Condition children only.
- Subtree node renders correctly; double-click navigates; unresolved subtree shows red outline.
- Running-node pulse animates; stack ancestry dims gradient correctly.
- Pause / Step / Continue advance kernel state visibly.
- Heatmap colors map to counters correctly.

---

## 17. Open questions

1. **`InstanceFlags.Paused` for BTree.** Today the BTree kernel doesn't have a pause flag (only HSM has equivalent). Adding it is a kernel change — small but real. Should land in Slice 2 so step controls work. Track as a kernel-side ticket separate from this editor work.

2. **Parallel composite UX.** When the `Parallel` node type is implemented (currently planned-but-not-implemented), the editor needs to display its policy parameter. The fluent builder already accepts a `policy: int` argument. The inspector renders this; the picker offers known policy values once they're defined. Deferred until Parallel is implemented in the kernel.

3. **Service nodes.** Same as Parallel — implement when the kernel does. Render as pills with a distinct glyph (per §6.6).

4. **Standalone Observer leaves.** `NodeType.Observer` (value 31) is used as a leaf inside an Observer Selector's children (it's a guard that re-evaluates each tick). The palette gets a dedicated entry for it; the editor treats it as a Condition-equivalent for guard-badge purposes. Confirm with kernel team that Observer-as-leaf is the intended model (vs. Observer-as-decorator).

5. **Drag-from-canvas to inspector.** When a node is selected, drag-from-its-property-row in the Inspector to the canvas could create a "Find references" search popup. Out of v1 scope; track as Slice 5+ polish.

6. **Multiple-entity debug-overlay slot.** When two entities run the same BTree and both are inspected (one focused via `SelectedEntity`, one pinned via `ChainToMap=off` on a duplicate inspector window), the canvas should distinguish the two. The runtime overlay renderer currently shows only the `SelectedEntity`'s state. Deferred; document as a v2 enhancement.

---
