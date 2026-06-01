---
exported: 2026-06-01T17:16:41.760Z
source: NotebookLM
---

# NotebookLM Conversation

Exported: 6/1/2026, 7:16:41 PM

---

## User

## FDP 244 - blueprint edit wiring

21 zdrojů·1\. 6. 2026

These sources provide the technical framework for the **Behavior Tree (BTree) Editor** within the HROT AI subsystem. The system uses a **graph-based interface** to manage complex AI logic, featuring a **command sink** for translating visual edits into data mutations and a **fluent emitter** for generating deterministic C# code. Robust **debugging tools** are integrated, including a **live blackboard panel** for real-time memory inspection, **breakpoint management** for execution control, and **automated sanitizers** for comparing logic changes. The architecture relies on **reflection and metadata** to symbolicate runtime data, ensuring that visual nodes correspond accurately to the underlying **behavior blobs** and asynchronous tokens. Additionally, the codebase facilitates **hot reloading** and **asset cataloging**, allowing developers to iteratively refine AI behaviors without restarting the simulation.

Dnes • 11:53

What is the sttaus of the blueprint editing in the codebase version 244?

In codebase version 244, Blueprint editing is currently in the **Slice 1 implementation phase**, which provides a functional but intentionally minimal editing experience. A fully-featured visual node-graph canvas editor is explicitly deferred to **Slice 2 or later**.

**Current Capabilities and UI (Slice 1):**

-   **StructEdit-Driven Authoring:** For Slice 1, Blueprint assets are primarily edited using `StructEdit` forms over JSON rather than a full visual canvas.-   **Editor Windows:** The `Hrot.Blueprints.Editor` assembly implements a minimal ImGui editor that includes an Asset Browser, an Inspector window (powered by StructEdit), a Debug Panel, a Watch Panel, a Callstack, and a Hot Reload Log.-   **Graph Editor Canvas:** There is a `GraphEditorWindow` present in the codebase, but its canvas rendering is currently minimal and contains placeholder UI elements. For Slice 1, the graph view uses `ImDrawList` directly for basic custom rendering (drawing nodes as rectangles and links as bezier curves), and explicitly excludes advanced features like multi-select, box-select, group/comment nodes, waypoints, minimaps, and search boxes.-   **Host Integration:** The Blueprint editor acts as a host on top of the shared AI Editor infrastructure (`Hrot.Editor.AiShared`), sharing the same asset browser, selection bus, fluent C# emitter, and debug session interfaces as the BTree and HSM editors.

**Authoring and Compilation Features:**

-   **Dispatch Kinds:** Designers can author Blueprints for three dispatch kinds: Library (stateless utility functions), AiPrimitive (single-method graphs hosted by BTree or HSM), and Instance (entity-bound scripts with state and events).-   **Catalogs:** The editor utilizes hand-curated catalogs for Engine Events, Channel Commands, and Wait Primitives to dictate what authors can wire into their graphs (this is planned to transition to an attribute-driven discovery system in Slice 2).-   **Hot Reload:** The editor features a **Quick Reload** pipeline that provides sub-100ms in-memory compilation and assembly loading, allowing designers to test changes without interrupting the live game. It also includes a **Full Rebuild** option that saves the `.bp.json` asset to disk and invokes MSBuild for permanent commits.

**Deferred to Slice 2+:** The "real" visual node canvas editor remains one of the highest priorities for Slice 2. Other capabilities explicitly deferred to Slice 2 or later include in-UI refactoring operations (like promoting to variables or collapsing to functions), live multi-author collaboration, map/set containers, and cross-entity dispatcher calls.

ok, the NodeEdit library for Slice2 is already available in the codebase (specs here: NodeEdit-docs.txt) but before integrating it i would like to make sure the Slice 1 state is working at all, what do you think?

Verify the completion of Slice 1 before beginning the NodeEditor integration for the Blueprint subsystem. The architecture explicitly defers the visual node-graph canvas editor to Slice 2 or later, relying entirely on StructEdit forms over JSON for Slice 1 authoring,.

To confirm Slice 1 is complete, you must satisfy its exact Definition of Done. This requires passing the full automated xUnit test suite and successfully executing the five required demo scenarios end-to-end, including the headline MoveToAndFire demo,. Additionally, the minimal ImGui editor must be fully functional, allowing you to list assets, edit them via StructEdit, and correctly trigger the compile-and-reload pipelines.

While the BTree and HSM editors already implement the NodeEditor host interfaces in Slice 1,, integrating this library into the Blueprint editor represents a significant expansion in scope. Establish that the foundational runtime, compiler, and hot-reload pipelines are perfectly stable under Slice 1 constraints before introducing the visual canvas,.

How is Slice 1 state integrated now? I am using Hrot.clusterrunner with editor subsystem, which allows me to author simple scenarios. But i see no way how to work with blueprints!

The Blueprint editor is integrated into the `Hrot.ClusterRunner`'s editor mode via the `EditorSubsystem`, which initializes the `BlueprintWindowRegistrar` and registers the Blueprint-specific UI panels directly into the engine's `WindowManager`. When you launch the cluster runner using the `--mode editor` flag, these windows become available in the editor's perspective.

To work with Blueprints in the Slice 1 implementation, you must use the following workflow:

-   **Asset Browser**: This window serves as the entry point and automatically discovers `.bp.json` assets located in the `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/` directory. You can double-click existing assets here to load them or right-click to invoke the "New Asset" wizard.-   **Minimal Graph Editor**: Because the fully featured NodeEdit visual canvas is deferred to Slice 2, the current Graph Editor window provides only a functional minimum using `ImDrawList` to draw basic node rectangles and bezier curves.-   **StructEdit Inspector**: Authoring is not done through drag-and-drop node creation on the canvas. Instead, it is driven by `StructEdit` forms. When you click a node or empty space in the Graph Editor, the shared Inspector window dispatches to type-specific UI drawers to expose the editable properties, parameters, and local variables for that selection.-   **Compilation and Hot Reload**: Once edits are made in the Inspector, they must be applied to the running simulation. The Graph Editor toolbar provides a "Quick Reload" button that compiles the changes in-memory and injects them into the live game in under 100ms, as well as a "Save & Rebuild" button that writes the `.bp.json` to disk and invokes an MSBuild full rebuild.

ok i found in the main menu the "blueprints" option; is that a standalone subsystem? How comes it is integrated as a perspective switcher? I thought perspective switches are reserved fopr subsystems only... I thought there is just a single 'editor' subsystem..

The Blueprint editor is not a standalone subsystem; it is a standard engine module embedded entirely within the single `EditorSubsystem`. It has no separate ECS world, network participant, or cluster lifecycle of its own.

The "Blueprints" option you found in the main menu is not a perspective switcher. It is simply a menu category populated by the `IWindowRegistrar` during editor startup. When you click items under this category, it invokes delegates that construct or toggle the visibility of specific Blueprint editor windows, such as the Asset Browser or Graph Editor.

You are correct that true perspective switches are handled at the subsystem level. The engine's `PerspectiveCoordinatorSystem` manages transitions between top-level cluster roles (like `SimHost`, `IG`, `ExCon`, and `Editor`), synchronizing map camera ownership and global UI state across them.

The confusion likely stems from how the engine's `WindowManager` filters UI elements. Windows managed by the Blueprint editor declare their `WindowScope` as `PerspectiveBound` and set their owning perspective to `"Blueprints"` (for Blueprint-specific canvas windows) or `"Authoring"` (for shared AI windows). This tells the `WindowManager` to only render these ImGui panels when the user is actively working in those authoring views. However, this is strictly a UI visibility filter within the `EditorSubsystem` process, not a structural subsystem transition.

added HROT.Blueprint.Tests\_244.txt with some example blueprint json files.

i see the UI panels from the Blueprints menu. If I click Refresh on the Asset Browser, i see a table with assets like CoverAwarePatrol.bp, MoveAndFireCombo.bp and similar. But how can I load them for editing?

To load an asset for editing, double-click its row in the Asset Browser.

This action sets the asset as the `ActiveAsset` in the shared `EditorSelectionStore`. Because the editor windows are decoupled and communicate implicitly through this shared selection bus, double-clicking the row automatically causes the Graph Editor to load the asset's visual representation. Concurrently, the Inspector window will bind to the active asset, populating its `StructEdit` forms with the asset's root properties, variables, and parameters.

Keep in mind that under the current Slice 1 constraints, the Graph Editor provides only a minimal read-only visual layout; your actual authoring and modification of the blueprint's nodes and properties must be done through the forms in the Inspector window.

doubleclicking the line does nothing :-(

The issue is caused by an incomplete implementation in the double-click handler of `Hrot.Blueprints.Editor/AssetBrowserWindow.cs`.

When a row is double-clicked, the UI code attempts to retrieve the asset from memory: `var asset = _editorState.GetInMemoryAsset(entry.AssetId);`. If this lookup returns `null`, the code skips the `_selectionStore.SelectAsset(asset)` call and silently does nothing.

The `FileSystemAssetCatalog` currently only parses the JSON headers to extract the `AssetId` and file path in order to populate the browser list, meaning the full asset data is not automatically loaded into memory. While the editor design specifies that "full graph data is loaded only when the user selects an asset for editing", the `AssetBrowserWindow` implementation is missing the fallback step to actually read the file from `entry.Path`, deserialize the `.bp.json` payload, and place it into `_editorState` when the memory lookup fails.

Consequently, double-clicking any asset that was discovered on disk but is not yet in the `EditorState.InMemoryAssets` dictionary will fail to open.

is the AssetBrowserWindow behavior regarding opening the assets specified in the design documents? What are they saying about it?

Yes, the behavior for opening assets is specified in the design documents.

The _AI Editor — Shared Infrastructure Detailed Design_ dictates that double-clicking a row in the Asset Browser should open the asset in the appropriate subsystem editor's canvas window.

The _Blueprint Subsystem — Editor Detailed Design_ further defines the memory and loading model for this interaction. It specifies that the asset catalog initially uses a lightweight `BlueprintJsonServices.DeserializeHeader` method to extract only the header section for fast enumeration when the editor opens or refreshes. The full graph data is explicitly designed to be loaded into memory only when the user selects an asset for editing.

The current issue you are experiencing happens because the `AssetBrowserWindow` implementation strictly queries `_editorState.GetInMemoryAsset` on double-click, failing to implement the specified transition from the lightweight header state to the full disk load required for editing.

og pls tell me how to fix it according to the specs

To fix the double-click behavior in accordance with the editor architecture, you must update the `AssetBrowserWindow.cs` implementation to fall back to a disk read and full deserialization when the asset is not found in memory.

Locate the `DrawUI` method inside `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/AssetBrowserWindow.cs`. Find the double-click event handler block for the table row.

Replace the current incomplete logic:

```
if (ImGui.IsMouseDoubleClicked(0))
{
    var asset = _editorState.GetInMemoryAsset(entry.AssetId);
    if (asset != null) _selectionStore.SelectAsset(asset);
}
```

With the following implementation that satisfies the design requirement to load full graph data only upon editing selection:

```
if (ImGui.IsMouseDoubleClicked(0))
{
    var asset = _editorState.GetInMemoryAsset(entry.AssetId);
    if (asset == null)
    {
        try 
        {
            string json = File.ReadAllText(entry.Path);
            asset = Hrot.Blueprints.Core.BlueprintJsonServices.Deserialize(json);
            
            if (asset != null)
            {
                // Cache it so the Inspector and GraphEditor share the same mutable model
                _editorState.SetInMemoryAsset(asset);
            }
        }
        catch (Exception ex)
        {
            // Log or handle deserialization failure
            Console.WriteLine($"Failed to load asset from {entry.Path}: {ex.Message}");
        }
    }
    
    if (asset != null) 
    {
        _selectionStore.SelectAsset(asset);
    }
}
```

This change utilizes the `BlueprintJsonServices` for deserialization and correctly populates the `EditorState`'s memory cache before notifying the `EditorSelectionStore`. Once `_selectionStore.SelectAsset` fires, both the minimal Graph Editor and the StructEdit Inspector will automatically bind to the newly instantiated `BlueprintAsset` model.

ok, i double clicked now on the first row, i.e. CoverAwarePatrol.bp. in the Inspector UI panel I see on "Graph" tab the following: Graphs: 1 \* Tick

In the Graph Editor i see just 3 buttons Save, Quick Reload, Full Rebuild, and a gray text "Graph: CoverAwarePatrol"

Should't I see something more somewhere? No Struct Edit windows?

The state you are observing exactly matches the current implementation in codebase version 244. You will not see StructEdit forms for nodes because you cannot select any nodes.

The `GraphEditorWindow` currently implements a canvas placeholder rather than the actual canvas. The minimal `ImDrawList` node and link rendering specified in the design is missing, explicitly marked with a `TODO(D-BP-04)` comment. It only renders the disabled text "Graph: CoverAwarePatrol" inside an empty ImGui child window.

Because the canvas renders no visual nodes, you cannot click on anything to establish an `ActiveSubSelection`. The Inspector window relies on this sub-selection to dispatch to the appropriate StructEdit drawer for a specific node kind. Without a selected node, the Inspector's "Node" tab defaults to the empty state message: "Node inspector -- select a node in the graph editor.".

Additionally, if you are looking to edit asset-level variables, parameters, or working state, those are not handled by the main Inspector window. They are managed by a dedicated `BlueprintVariablesWindow`.

The Slice 1 blueprint editor integration is incomplete. You must implement the minimal graph canvas rendering and selection hit-testing in `GraphEditorWindow` before the node-level StructEdit forms can be accessed.

Well, this is where The NodeEdit might come in handy, right? Do you see the NodeEdit's demo examples (S01..S36) in the codebase?

The NodeEdit demo examples (S01 through S36) are present in the codebase version 244. They are located within the `NodeEditor.Demo` project under the `src/NodeEditor.Demo/Scenarios/` directory.

These scenarios serve as a standalone visual test harness to prove the editor library works end-to-end without requiring integration with a real engine subsystem. The codebase includes the complete implementations for all 36 scenarios, covering a wide range of features. Examples include basic operations in `S01_HelloCanvas`, contextual interactions in `S08_WireDropPicker`, advanced refactoring in `S22_CollapseToFunction`, performance validation in `S33_BigGraph`, and NodeEditor extensions like `S34_NodeAttachments`, `S35_ContainerNodes`, and `S36_CustomRenderers`.

The demo application utilizes a `DemoShell` orchestrator, a `FakeGraphModel`, and fake host services to render the full editor UI and allow switching between these scenarios.

However, while the `NodeEditor` library and its comprehensive test harness are fully implemented and available in the repository, the Blueprint subsystem's `GraphEditorWindow` has not yet been wired to use it. The Blueprint editor currently remains strictly on the Slice 1 implementation path, which explicitly defers the visual node-graph canvas editor and relies on a minimal `ImDrawList` canvas.

OK. You mentioned the editors from btrees and HSM are already implemented using the NodeEdit library. Can you check in the codebase?

The codebase version 244 confirms that the BTree and HSM editors fully implement the NodeEditor host interfaces.

The BTree editor subsystem provides `Hrot.BTree.Editor.Host.BTreeEditorHostServices`, which implements `IEditorHostServices`. This host service bundles BTree-specific implementations for the NodeEditor canvas, including `BTreeCommandSink` (implementing `IGraphCommandSink`), `BTreeLinkValidator` (implementing `ILinkValidator`), `BTreeNodeCatalog` (implementing `INodeCatalog`), and `BTreeTypeSystem` (implementing `ITypeSystem`).

The HSM editor subsystem provides an equivalent `Hrot.Hsm.Editor.Host.HsmEditorHostServices` class implementing `IEditorHostServices`. It registers its own host implementations, such as `HsmCommandSink`, `HsmLinkValidator`, `HsmNodeCatalog`, and `HsmTypeSystem`. Additionally, it uses `HsmGraphModel` as an adapter to expose the `HsmAsset` to the canvas via `IGraphModel`.

How to integrate these into the clusterrunner editor?

To integrate the `NodeEdit`\-backed BTree and HSM editors into the `ClusterRunner`'s `EditorSubsystem`, you must bridge their specific host implementations into the shared AI editor infrastructure and the engine's window manager.

In codebase version 244, `EditorSubsystem` relies on manual instantiation and wiring during initialization. You must execute the following integration steps:

**1\. Project Dependencies** Update `Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj` to include project references to `Hrot.BTree.Editor.csproj` and `Hrot.Hsm.Editor.csproj`.

**2\. Asset Catalog Wiring** The shared Asset Browser relies on an `IAssetCatalog`. You must instantiate `BTreeAssetContributor` and `HsmAssetContributor` and register them with the catalog. This ensures the Asset Browser discovers BTree and HSM assets alongside Blueprints and handles the transition from header-only disk reads to full in-memory models.

**3\. Host Services Initialization** Inside `EditorSubsystem.Initialize()`, instantiate `BTreeEditorHostServices` and `HsmEditorHostServices`. You will need to inject the shared infrastructure adapters for `IClipboard`, `IIconProvider`, `IInputSource` (an adapter mapping ImGui/Raylib inputs to `NodeEdit`'s expectations), and `IEditorTheme`.

**4\. Window Registration** Construct the specific `ManagedWindow` wrappers for the BTree and HSM canvases (which will host the `NodeEditor`'s `GraphView`). In `EditorSubsystem.RegisterWindows()`, pass these to the `WindowManager.RegisterWindow()` method. Ensure their `WindowScope` is set to `PerspectiveBound` and their owning perspective is mapped correctly (e.g., `"Authoring"`) so they share visibility states with the Asset Browser and Inspector.

**5\. Inspector Facet Dispatch** To allow `StructEdit` to render properties when a user clicks a node on the canvas, you must register `BTreeFacetMapper` and `HsmFacetMapper` with the shared `InspectorWindow`'s drawer registry. The shared Inspector relies on the single `EditorSelectionStore` to detect the `ActiveSubSelection` and route to these facet structs.

**6\. Debug Session Registry** Instantiate `BTreeDebugSession` and `HsmDebugSession` against the kernel and register them into the shared `IDebugSessionRegistry`. This wires them to the shared `RuntimeInspectorWindow` and `TraceTimelineWindow`, enabling step controls, breakpoints, and live state overlays.

can we go through these steps in details, one at a time? Pls detail #1

Modify `Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj` to include project references to the BTree and HSM editor assemblies. This is required because the `EditorSubsystem` acts as the single composition root and must manually instantiate the host services and window wrappers for each AI subsystem during initialization.

Insert the following `<ProjectReference>` entries into an `<ItemGroup>` within `Hrot.Editor.csproj`:

```
<ProjectReference Include="..\AI\Hrot.BTree.Editor\Hrot.BTree.Editor.csproj" />
<ProjectReference Include="..\AI\Hrot.Hsm.Editor\Hrot.Hsm.Editor.csproj" />
```

Both `Hrot.BTree.Editor.csproj` and `Hrot.Hsm.Editor.csproj` already correctly reference the shared infrastructure (`Hrot.Editor.AiShared`) and the `NodeEditor.Core` library. You do not need to add transitive references for the `NodeEdit` library directly to `Hrot.Editor`, as MSBuild will resolve them through these new subsystem dependencies.

ok now please 2. Asset Catalog Wiring

To wire the asset catalog for BTree and HSM assets, you must integrate their specific contributors into the shared `IAssetCatalog` during `EditorSubsystem` initialization. This ensures the shared Asset Browser can discover and list them alongside Blueprints.

**1\. Instantiate the Contributors** Both subsystems provide an implementation of `IAssetCatalogContributor`. You must instantiate them in your composition root:

-   Instantiate `Hrot.BTree.Editor.Catalog.BTreeAssetContributor`. Its constructor accepts an optional `BTreeDebugSession`, which you can supply now or wire later when setting up debug sessions.-   Instantiate `Hrot.Hsm.Editor.Catalog.HsmAssetContributor`.

**2\. Register with the Shared Catalog** The shared AI editor infrastructure provides `Hrot.Editor.AiShared.Catalog.AssetCatalog`, which merges multiple contributors. Pass your newly created contributors to it:

```
var assetCatalog = new AssetCatalog();
assetCatalog.AddContributor(bTreeContributor);
assetCatalog.AddContributor(hsmContributor);
// Note: You will also need to add the Blueprint file-system contributor here.
```

**3\. Invoke Assembly Reflection** Unlike the Blueprint catalog which reads `.bp.json` files from disk, the BTree and HSM contributors generate their in-editor models (`BehaviorTreeAsset` and `HsmAsset`) by reflecting over the compiled `Hrot.AI.Behaviors.dll`.

You must call `LoadFrom(assembly)` on both the `BTreeAssetContributor` and `HsmAssetContributor` immediately after the behavior assembly is loaded, as well as after every hot-reload completes. This scans for methods annotated with `[BTreeDefinition]` and `[HsmDefinition]` and projects them into the editor models. Once `LoadFrom` completes, the contributors fire their `ContributorChanged` events, which prompts the `AssetCatalog` to rebuild its merged cache and notify the Asset Browser to re-render.

pls detail 1. Instantiate the Contributors

To instantiate the contributors, you must create instances of the specific `IAssetCatalogContributor` implementations provided by the BTree and HSM editor assemblies.

For the BTree subsystem, instantiate `Hrot.BTree.Editor.Catalog.BTreeAssetContributor`. Its constructor accepts an optional `BTreeDebugSession` parameter (`public BTreeAssetContributor(BTreeDebugSession? debugSession = null)`). Supplying the debug session here allows the contributor to wire debug metadata into the session whenever an asset is registered, which is required for node-index symbolication to work correctly when `Update()` is called at runtime. If your debug sessions are not yet initialized in the composition root, you can pass `null`.

For the HSM subsystem, instantiate `Hrot.Hsm.Editor.Catalog.HsmAssetContributor`. This class has no dependencies and uses a parameterless constructor, so you can instantiate it directly.

pls tel;l exactly how

To instantiate the contributors and wire them into the catalog within `EditorSubsystem.cs`, you must manually construct the objects and replace the existing singleton catalog.

Add the necessary using directives:

```
using Hrot.BTree.Editor.Catalog;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Editor.AiShared.Catalog;
```

Inside `EditorSubsystem.Initialize()`, locate where the `IAssetCatalog` is currently created (likely `FileSystemAssetCatalog` for Blueprints) and implement the following setup:

```
// 1. Instantiate the specific contributors.
// Pass your BTreeDebugSession instance here if it is already initialized in your composition root;
// this is required to wire NodeDebugMetadata for runtime node-index symbolication.
var bTreeContributor = new BTreeAssetContributor(debugSession: null); 
var hsmContributor = new HsmAssetContributor();

// 2. Create the shared catalog that merges all contributors.
var assetCatalog = new AssetCatalog();
assetCatalog.AddContributor(bTreeContributor);
assetCatalog.AddContributor(hsmContributor);

// Note: The existing Blueprint FileSystemAssetCatalog implements IAssetCatalog directly.
// You must refactor it to implement IAssetCatalogContributor instead, and add it here:
// assetCatalog.AddContributor(blueprintContributor);

// 3. Trigger assembly reflection to populate the in-memory models.
// Replace 'aiAssembly' with your actual reference to the loaded Hrot.AI.Behaviors.dll.
bTreeContributor.LoadFrom(aiAssembly);
hsmContributor.LoadFrom(aiAssembly);
```

Once this is wired, you pass the unified `assetCatalog` to the `BlueprintWindowRegistrar`, `AssetBrowserWindow`, and any other shared infrastructure dependencies expecting an `IAssetCatalog`. The `AssetCatalog` will automatically listen to the `ContributorChanged` events fired by `LoadFrom` and rebuild the shared cache for the Asset Browser.

pls tell exactly how to do 2. Register with the Shared Catalog

To wire the shared catalog, you must refactor the existing Blueprint file discovery to act as a contributor, instantiate the unified catalog, and register all three subsystem contributors into it during editor initialization.

**1\. Refactor** **FileSystemAssetCatalog** Currently, `FileSystemAssetCatalog` directly implements the legacy Blueprint `IAssetCatalog`. You must update it to implement the unified `IAssetCatalogContributor` from `Hrot.Editor.AiShared.Catalog`.

Modify `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/FileSystemAssetCatalog.cs`:

```
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Blueprints.Editor;

public sealed class FileSystemAssetCatalog : IAssetCatalogContributor
{
    private readonly string _rootDirectory;

    public AssetKind Kind => AssetKind.Blueprint;
    public event Action? ContributorChanged;

    public FileSystemAssetCatalog(string rootDirectory)
    {
        _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
    }

    public IReadOnlyList<IEditableAsset> Enumerate()
    {
        var assets = new List<IEditableAsset>();
        if (!Directory.Exists(_rootDirectory))
            return assets;

        foreach (var filePath in Directory.EnumerateFiles(_rootDirectory, "*.bp.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("AssetId", out var idEl) || !idEl.TryGetGuid(out var assetId))
                    continue;

                // Provide a lightweight IEditableAsset wrapper for the browser
                assets.Add(new BlueprintEditableAssetAdapter(new Core.Assets.BlueprintAsset 
                { 
                    AssetId = assetId, 
                    Name = Path.GetFileNameWithoutExtension(filePath).Replace(".bp", ""),
                    // Store SourceFilePath for disk-read fallback on double-click
                }));
            }
            catch { continue; }
        }
        return assets;
    }
}
```

**2\. Update** **EditorSubsystem** **Initialization** In `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`, locate the `CreateBlueprintWindowRegistrar()` method. Replace the singular blueprint catalog instantiation with the shared `AssetCatalog` and add all three contributors.

```
using Hrot.Editor.AiShared.Catalog;
using Hrot.BTree.Editor.Catalog;
using Hrot.Hsm.Editor.Catalog;

// ...

private Fdp.Toolkit.Runner.IWindowRegistrar CreateBlueprintWindowRegistrar()
{
    var bpRootDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blueprints");

    // 1. Instantiate the contributors
    var blueprintContributor = new Hrot.Blueprints.Editor.FileSystemAssetCatalog(bpRootDir);
    var bTreeContributor = new BTreeAssetContributor(debugSession: null); 
    var hsmContributor = new HsmAssetContributor();

    // 2. Create the unified shared catalog
    var sharedCatalog = new AssetCatalog();

    // 3. Register contributors with the shared catalog
    sharedCatalog.AddContributor(blueprintContributor);
    sharedCatalog.AddContributor(bTreeContributor);
    sharedCatalog.AddContributor(hsmContributor);

    // 4. Trigger initial assembly reflection for BTree and HSM
    // Assuming _aiAssembly is your loaded Hrot.AI.Behaviors.dll
    // bTreeContributor.LoadFrom(_aiAssembly);
    // hsmContributor.LoadFrom(_aiAssembly);

    var store = new Hrot.Blueprints.Editor.EditorSelectionStore();
    var dirty = new Hrot.Blueprints.Editor.DirtyTracker();
    var state = new Hrot.Blueprints.Editor.EditorState();
    var coordinator = new Hrot.Blueprints.Editor.NullBlueprintEditorCoordinator();
    var console = new Hrot.Blueprints.Editor.SystemConsoleOutputConsole();
    
    var bpFdpCoord = new Fdp.Toolkit.Behavior.AiHotReloadCoordinator(
        new BehaviorRegistry(), _blueprintRegistry, new Fdp.Toolkit.Behavior.AiHotReloadCoordinatorOptions());
    
    // Pass the unified `sharedCatalog` into the services that require an IAssetCatalog
    var qrs = new Hrot.Blueprints.Editor.Reload.QuickReloadService(
        sharedCatalog, state, console, new Hrot.Blueprints.Core.Compiler.BlueprintCompiler(), bpFdpCoord, _blueprintDebugSession);
    var frs = new Hrot.Blueprints.Editor.Reload.FullRebuildService(console);
    var drawers = new Hrot.Blueprints.Editor.Inspector.DrawerRegistry();

    return new Hrot.Blueprints.Editor.BlueprintWindowRegistrar(
        sharedCatalog, store, dirty, state, _blueprintDebugSession!, coordinator, qrs, frs, drawers);
}
```

Ensure that `QuickReloadService`, `BlueprintWindowRegistrar`, and `AssetBrowserWindow` are fully transitioned to expect `Hrot.Editor.AiShared.Catalog.IAssetCatalog` rather than the legacy blueprint-specific interface.

now pls detail 3. Host Services Initialization

To initialize the host services, you must construct the shared infrastructure adapters that bridge the `NodeEdit` library to the engine, alongside the static subsystem-specific components.

Because `BTreeCommandSink`, `HsmCommandSink`, `BTreeLinkValidator`, and `HsmLinkValidator` directly mutate and validate against specific in-memory asset models (`BehaviorTreeAsset` and `HsmAsset`), you cannot instantiate `BTreeEditorHostServices` and `HsmEditorHostServices` as global singletons. Instead, you establish the shared dependencies in `EditorSubsystem.Initialize()` so they can be injected into the window factories (which will be detailed in Step 4) that construct the full host bundle per-asset.

Inside `EditorSubsystem.Initialize()`, add the following wiring:

**1\. Instantiate Shared NodeEditor Infrastructure** Create the adapters that implement `NodeEdit`'s cross-cutting concerns using the shared AI editor infrastructure:

```
// Use the shared implementations from Hrot.Editor.AiShared
IClipboard clipboard = new EngineClipboardAdapter(); 
IIconProvider icons = new SharedIconProvider(); // Loads from Hrot.Editor.AiShared/Icons/
IEditorTheme theme = new SharedEditorTheme(); 
IInputSource input = new ImGuiInputSourceAdapter(); // Maps engine ImGui inputs
IDiagnosticsSink diagnostics = new EngineDiagnosticsSink();

// Initialize the shared PickerRegistry provided by NodeEditor.UI
var pickers = new NodeEditor.UI.Picker.PickerRegistry();
pickers.SetServices(icons, theme);
```

**2\. Instantiate Static Subsystem Services** The BTree and HSM node catalogs and type systems are stateless and static, meaning they share an application lifetime. Initialize them once:

```
var bTreeCatalog = new BTreeNodeCatalog();
var bTreeTypeSystem = new BTreeTypeSystem();

var hsmCatalog = new HsmNodeCatalog();
var hsmTypeSystem = new HsmTypeSystem();
```

**3\. Prepare the Factory Handoff** You will pass these shared instances (`clipboard`, `icons`, `theme`, `input`, `pickers`, `diagnostics`) and the static catalogs into your Canvas Window wrappers.

When the user later double-clicks an asset to open a window, that window's factory will:

-   Receive the parsed `BehaviorTreeAsset` or `HsmAsset`.-   Construct the asset-bound `BTreeCommandSink` / `HsmCommandSink` and `BTreeLinkValidator` / `HsmLinkValidator`.-   Construct the required `ICustomCanvasRenderer` instances (e.g., `BTreeBreakpointGutterRenderer` or `HsmRuntimeOverlayRenderer`) which also require the active asset.-   Bundle them all into a new instance of `BTreeEditorHostServices` or `HsmEditorHostServices` to supply to the `NodeEdit.GraphView`.

in what source files do I need to do that?

You must implement the host services initialization entirely within `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`.

Locate the `Initialize(SubsystemConfig config)` method of the `EditorSubsystem` class. Instantiate the shared `NodeEdit` infrastructure adapters (such as `IClipboard`, `IIconProvider`, `IEditorTheme`, `IInputSource`, and `PickerRegistry`) and the static subsystem catalogs (`BTreeNodeCatalog`, `HsmNodeCatalog`) directly inside this method.

Store these initialized objects as private fields on the `EditorSubsystem` class. This ensures they are retained and available to be injected into the specific BTree and HSM canvas window factories later when the `RegisterWindows(WindowManager windowManager)` method is invoked.

pls detail 4. Window Registration

To execute the window registration, you must integrate the `ManagedWindow` wrappers for the BTree and HSM canvases into the engine's window manager. The subsystem editor canvases must share the `"Authoring"` perspective with the shared AI windows, such as the Asset Browser and Inspector, allowing the user to seamlessly switch between assets while preserving window visibility.

In `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`, locate the `RegisterWindows(WindowManager windowManager)` method. You must instantiate the BTree and HSM canvas windows and pass them to the window manager.

Implement the following wiring:

```
public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
{
    // Existing blueprint registrar execution
    _blueprintWindowRegistrar?.RegisterWindows(windowManager);

    if (_editorLogic == null) return;

    // 1. Register the BTree Canvas Window
    // The window ID must be "bt_graph_canvas" as specified by the browser double-click routing.
    var bTreeWindow = new Hrot.BTree.Editor.BTreeCanvasWindow(
        id: "bt_graph_canvas", 
        title: "BTree Canvas", 
        owningPerspective: "Authoring", 
        scope: WindowScope.PerspectiveBound,
        selectionStore: _aiEditorSelectionStore, // The shared bus from step 1
        hostServices: _bTreeHostServices         // The static/shared services from step 3
    );
    windowManager.RegisterWindow(bTreeWindow);

    // 2. Register the HSM Canvas Window
    var hsmWindow = new Hrot.Hsm.Editor.HsmCanvasWindow(
        id: "hsm_graph_canvas", 
        title: "HSM Canvas", 
        owningPerspective: "Authoring", 
        scope: WindowScope.PerspectiveBound,
        selectionStore: _aiEditorSelectionStore,
        hostServices: _hsmHostServices
    );
    windowManager.RegisterWindow(hsmWindow);

    // ... existing window registrations for other Editor panels ...
}
```

**Key architectural constraints for these windows:**

-   **WindowScope and Perspective:** Both windows must be constructed with `WindowScope.PerspectiveBound` and the `owningPerspective` parameter strictly set to `"Authoring"`. This ensures the engine's `WindowManager` filters their visibility correctly when the user transitions between engine modes.-   **Window IDs:** The string IDs (`"bt_graph_canvas"`) must exactly match the targets hardcoded in the shared `AssetBrowserWindow`'s double-click handlers, otherwise double-clicking an asset row will fail to focus the correct canvas.-   **Selection Binding:** Inside their respective `DrawClientArea()` overrides, these canvas windows must listen to the `_aiEditorSelectionStore.OnSelectionChanged` event. When `_aiEditorSelectionStore.ActiveAsset` changes to a `BehaviorTreeAsset` or `HsmAsset`, the window's factory logic must construct the asset-bound `CommandSink`, `LinkValidator`, and `GraphModel`, bundle them with the shared services injected here, and pass the resulting context to NodeEditor's `CanvasRenderer.Render()`.

and now please 5. Inspector Facet Dispatch

To implement the Inspector Facet Dispatch, you must configure the shared `InspectorWindow` to translate subsystem-specific canvas selections into `StructEdit` facet structs, and register the custom field pickers those structs require.

**1\. Per-Asset Facet Mapper Instantiation** The facet mappers depend on the active in-memory asset to resolve identifiers.

-   When the `EditorSelectionStore`'s `ActiveAsset` changes to a `BehaviorTreeAsset`, instantiate a `BTreeFacetMapper` bound to that asset.-   When the `ActiveAsset` changes to an `HsmAsset`, instantiate an `HsmFacetMapper`. Hold this mapper instance alive as long as the asset remains the active document.

**2\. Sub-Selection Dispatch Routing** Inside the shared `InspectorWindow`, intercept the `ActiveSubSelection` from the `EditorSelectionStore` and route it through the active mapper to generate the `IEditSession` target object:

-   **BTree:** Dispatch `BTreeNodeSelection` to the `BTreeFacetMapper` to yield specific structs like `BTreeActionFacet`, `BTreeSequenceFacet`, `BTreeRepeaterFacet`, or `BTreeSubtreeFacet`.-   **HSM:** Dispatch HSM sub-selections (`HsmStateSelection`, `HsmEventSelection`, `HsmGlobalTransitionSelection`, etc.) to the `HsmFacetMapper` to yield `StateFacet`, `EventFacet`, `RegionFacet`, or `TransitionFacet`.-   If `ActiveSubSelection` is null but an `ActiveAsset` exists, fall back to asset-level properties (e.g., name, variables).

**3\. Custom StructEdit Pickers Registration** The facet structs use marker attributes to render subsystem-specific dropdowns instead of raw text fields. You must register custom `IImGuiFieldDrawer` implementations with the `ComponentEditServiceBuilder` that constructs the Inspector's `StructEdit` service:

-   **BTree Pickers:** Wire `[BehaviorHashPicker]` to query the `BehaviorRegistry` for actions/conditions, and `[BlackboardFieldPicker]` to query the asset's blackboard schema.-   **HSM Pickers:** Wire `[HsmActionPicker]` and `[HsmGuardPicker]` to the `HsmActionDispatcher` catalogs. Wire `[HsmStateSelector]`, `[HsmEventPicker]`, and `[HsmSyncGroupPicker]` to query the active `HsmAsset`'s internal lists.

**4\. Commit Flow Wiring** The `InspectorWindow` creates an `IEditSession` over the returned facet struct. You must poll `session.IsDirty` at the end of each frame. When `IsDirty` transitions from true to false (indicating a commit), invoke the corresponding setter on the `BTreeFacetMapper` or `HsmFacetMapper`. The mapper will translate the struct modifications back into the `BehaviorTreeAsset` or `HsmAsset` model, which in turn schedules a `GraphCommand` to apply the edits and trigger deterministic C# regeneration.

pls detail 1. Per-Asset Facet Mapper Instantiation , be specific to what source file

Implement the per-asset facet mapper instantiation within the composition root at `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`. Because the shared infrastructure layer cannot depend directly on subsystem-specific classes, you must bridge them where all assemblies are visible.

Inside the `Initialize` method, you must subscribe to the `OnSelectionChanged` event on your shared `EditorSelectionStore` instance to detect when the user focuses a different asset. In the event handler, evaluate the type of the `ActiveAsset` property.

When the active asset is a `BehaviorTreeAsset`, instantiate a new `Hrot.BTree.Editor.Inspector.BTreeFacetMapper` bound to that specific asset. When the active asset is an `HsmAsset`, instantiate a new `Hrot.Hsm.Editor.Inspector.HsmFacetMapper` passing the active HSM asset.

You must hold this mapper instance alive and supply it to the shared `InspectorWindow` state, disposing the previous session. This enables the shared Inspector to route the `ActiveSubSelection` through the active mapper to yield the correct subsystem-specific `StructEdit` facet structs, such as `BTreeActionFacet` or `StateFacet`, while adhering to the strict dependency direction.

pls show me what to change in what file

You must modify `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` to handle the instantiation of the per-asset facet mappers and the subsequent dispatch routing.

**1\. Add the necessary using directives** At the top of the file, include the inspector and selection namespaces for both subsystems:

```
using Hrot.BTree.Editor.Inspector;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Editor.AiShared.Selection;
```

**2\. Declare the Facet Mapper fields** Inside the `EditorSubsystem` class, add private fields to hold the mappers so they remain alive while the asset is active:

```
private BTreeFacetMapper? _bTreeFacetMapper;
private HsmFacetMapper? _hsmFacetMapper;
```

**3\. Subscribe to the Selection Store** Inside the `Initialize(SubsystemConfig config)` method, subscribe to the shared selection bus:

```
_aiEditorSelectionStore.OnSelectionChanged += DispatchSelectionToInspector;
```

**4\. Implement the Dispatch Handler** Add the following method to `EditorSubsystem`. This evaluates the active asset, instantiates the bound mapper, and routes the sub-selection to yield the correct StructEdit facet:

```
private void DispatchSelectionToInspector()
{
    var activeAsset = _aiEditorSelectionStore.ActiveAsset;
    var subSelection = _aiEditorSelectionStore.ActiveSubSelection;
    object? facetTarget = null;

    if (activeAsset is Hrot.BTree.Editor.Model.BehaviorTreeAsset bTreeAsset)
    {
        // 1. Per-Asset Facet Mapper Instantiation
        _bTreeFacetMapper = new BTreeFacetMapper(bTreeAsset);

        // 2. Sub-Selection Dispatch Routing
        if (subSelection is BTreeNodeSelection nodeSelection)
        {
            // Dispatches to yield BTreeActionFacet, BTreeRepeaterFacet, etc.
            facetTarget = _bTreeFacetMapper.GetNodeFacet(nodeSelection.VisualId);
        }
    }
    else if (activeAsset is Hrot.Hsm.Editor.Model.HsmAsset hsmAsset)
    {
        // 1. Per-Asset Facet Mapper Instantiation
        _hsmFacetMapper = new HsmFacetMapper(hsmAsset);

        // 2. Sub-Selection Dispatch Routing
        if (subSelection is HsmStateSelection stateSelection)
        {
            facetTarget = _hsmFacetMapper.GetStateFacet(stateSelection.StableId);
        }
        else if (subSelection is HsmTransitionSelection transitionSelection)
        {
            facetTarget = _hsmFacetMapper.GetTransitionFacet(transitionSelection.VisualId);
        }
        else if (subSelection is HsmRegionSelection regionSelection)
        {
            facetTarget = _hsmFacetMapper.GetRegionFacet(regionSelection.StableId, regionSelection.RegionIndex);
        }
        else if (subSelection is HsmEventSelection eventSelection)
        {
            facetTarget = _hsmFacetMapper.GetEventFacet(eventSelection.EventId);
        }
    }
    else
    {
        _bTreeFacetMapper = null;
        _hsmFacetMapper = null;
    }

    // Fall back to asset-level properties if no specific sub-element is selected
    if (facetTarget == null && activeAsset != null)
    {
        facetTarget = activeAsset;
    }

    // Feed the resolved facet object to the shared InspectorWindow's StructEdit session.
    // (Assuming _inspectorWindow is your instantiated InspectorWindow reference)
    if (_inspectorWindow != null && facetTarget != null)
    {
        _inspectorWindow.SetEditTarget(facetTarget);
    }
}
```

did we detailed 2. Sub-Selection Dispatch Routing? if not, pls do so

The high-level routing block in `EditorSubsystem.cs` was provided in the previous step, which intercepts the shared `ActiveSubSelection` and calls `GetNodeFacet`, `GetStateFacet`, etc.. To complete the dispatch routing, you must implement the internal translation logic within `BTreeFacetMapper.cs` and `HsmFacetMapper.cs` to project the subsystem-specific editor models into `StructEdit` facet structs.

**1\. BTree Facet Mapper** Modify `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreeFacetMapper.cs`. The mapper must read the node from the `BehaviorTreeAsset` and dispatch based on its `KernelType`.

```
using System;
using Hrot.BTree.Editor.Model;

namespace Hrot.BTree.Editor.Inspector;

public class BTreeFacetMapper
{
    private readonly BehaviorTreeAsset _asset;

    public BTreeFacetMapper(BehaviorTreeAsset asset)
    {
        _asset = asset;
    }

    public object? GetNodeFacet(Guid visualId)
    {
        var node = _asset.FindNode(visualId);
        if (node == null) return null;

        // Dispatch based on the KernelType to yield the specific facet struct.
        return node.KernelType switch
        {
            Fbt.NodeType.Action => new BTreeActionFacet { VisualId = visualId.ToString(), MethodFqn = node.Action?.MethodFqn ?? string.Empty, Comment = node.Comment },
            Fbt.NodeType.Condition => new BTreeConditionFacet { VisualId = visualId.ToString(), MethodFqn = node.Condition?.MethodFqn ?? string.Empty, Comment = node.Comment },
            Fbt.NodeType.Wait => new BTreeWaitFacet { VisualId = visualId.ToString(), Duration = node.Wait?.Duration ?? 0f, Comment = node.Comment },
            Fbt.NodeType.Sequence => new BTreeSequenceFacet { VisualId = visualId.ToString(), ChildCount = node.ChildVisualIds.Count, Comment = node.Comment },
            Fbt.NodeType.Selector => new BTreeSelectorFacet { VisualId = visualId.ToString(), ChildCount = node.ChildVisualIds.Count, Comment = node.Comment },
            Fbt.NodeType.Parallel => new BTreeParallelFacet { VisualId = visualId.ToString(), ChildCount = node.ChildVisualIds.Count, Comment = node.Comment },
            Fbt.NodeType.Subtree => new BTreeSubtreeFacet { VisualId = visualId.ToString(), SubtreeName = node.Subtree?.SubtreeName ?? string.Empty, Comment = node.Comment },
            Fbt.NodeType.Root => new BTreeRootFacet { VisualId = visualId.ToString(), Comment = node.Comment },
            _ => null
        };
    }
}
```

This ensures the shared Inspector window receives concrete structs like `BTreeActionFacet` or `BTreeWaitFacet` when a canvas selection occurs.

**2\. HSM Facet Mapper** Modify `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmFacetMapper.cs`. It provides dedicated methods to retrieve state, transition, region, and event facets by locating them inside the active `HsmAsset`.

```
using System;
using System.Collections.Generic;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Inspector;

public sealed class HsmFacetMapper
{
    private readonly HsmAsset _asset;

    public HsmFacetMapper(HsmAsset asset)
    {
        _asset = asset;
    }

    public StateFacet GetStateFacet(Guid stableId)
    {
        var s = _asset.FindStateByStableId(stableId) 
            ?? throw new KeyNotFoundException($"State {stableId} not found");
        
        return new StateFacet
        {
            Name = s.Name,
            OnEntryAction = s.OnEntryAction,
            OnExitAction = s.OnExitAction,
            ActivityAction = s.ActivityAction,
            OutputLaneMask = s.OutputLaneMask,
            Comment = s.Comment,
            StableId = s.StableId.ToString()
        };
    }

    public RegionFacet GetRegionFacet(Guid stableId, int regionIndex)
    {
        var r = _asset.FindRegionByStableId(stableId)
            ?? throw new KeyNotFoundException($"Region {stableId} not found");

        return new RegionFacet
        {
            Name = r.Name,
            Priority = r.Priority,
            Comment = r.Comment,
            ColorOverride = r.ColorOverride,
            StableId = r.StableId.ToString()
        };
    }

    public EventFacet GetEventFacet(ushort eventId)
    {
        var e = _asset.FindEventById(eventId)
            ?? throw new KeyNotFoundException($"Event {eventId} not found");

        return new EventFacet
        {
            Name = e.Name,
            EventId = e.EventId,
            PayloadSize = e.PayloadSize,
            IsIndirect = e.IsIndirect,
            Priority = e.Priority
        };
    }
    
    // Transition implementations similarly look up the TransitionNode and map properties to TransitionFacet...
}
```

These mappers abstract away the underlying complex domain objects (like `BehaviorTreeAsset` and `HsmAsset`) so the shared `InspectorWindow` operates strictly on plain structs rendered by `StructEdit`.

now pls detail 3. Custom StructEdit Pickers Registration

To implement the custom StructEdit pickers for the BTree and HSM facets, you must create `IImGuiFieldDrawer` implementations that intercept the specific marker attributes and render ImGui dropdowns. You then register these drawers with the shared infrastructure.

1\. BTree Pickers Implementation

Create `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreeFieldDrawers.cs`. You must implement drawers for `typeof(string)` that check for `[BehaviorHashPicker]` and `[BlackboardFieldPicker]`.

```
using System;
using System.Linq;
using Fdp.Toolkit.Behavior;
using Hrot.Editor.AiShared.Selection;
using Hrot.Presentation.Editing; // IImGuiFieldDrawer
using StructEdit.Core;
using ImGuiNET;

namespace Hrot.BTree.Editor.Inspector;

public sealed class BTreeMethodPickerDrawer : IImGuiFieldDrawer
{
    private readonly BehaviorRegistry _registry;

    public BTreeMethodPickerDrawer(BehaviorRegistry registry) => _registry = registry;
    public Type TargetType => typeof(string);

    public bool DrawInput(ref object value, EditNode node)
    {
        if (!node.Metadata.CustomAttributes.Any(a => a is BehaviorHashPickerAttribute))
            return false; // Fallthrough to standard string input

        string current = value as string ?? string.Empty;
        bool changed = false;

        if (ImGui.BeginCombo("##MethodFqn", current))
        {
            // Note: In a complete implementation, filter by TBlackboard/TContext compatibility here.
            foreach (var name in _registry.GetRegisteredNames())
            {
                if (ImGui.Selectable(name, current == name))
                {
                    value = name;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }
        return changed;
    }
}

public sealed class BTreeBlackboardFieldPickerDrawer : IImGuiFieldDrawer
{
    private readonly EditorSelectionStore _store;

    public BTreeBlackboardFieldPickerDrawer(EditorSelectionStore store) => _store = store;
    public Type TargetType => typeof(string);

    public bool DrawInput(ref object value, EditNode node)
    {
        if (!node.Metadata.CustomAttributes.Any(a => a is BlackboardFieldPickerAttribute))
            return false;

        string current = value as string ?? string.Empty;
        bool changed = false;

        if (ImGui.BeginCombo("##BbField", current))
        {
            if (_store.ActiveAsset is Model.BehaviorTreeAsset btAsset)
            {
                foreach (var field in btAsset.BlackboardVariables)
                {
                    if (ImGui.Selectable(field.Name, current == field.Name))
                    {
                        value = field.Name;
                        changed = true;
                    }
                }
            }
            ImGui.EndCombo();
        }
        return changed;
    }
}
```

2\. HSM Pickers Implementation

Create `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmFieldDrawers.cs`. HSM facets require drawers for `string` (States/Actions/Guards) and `ushort` (Events/SyncGroups).

```
using System;
using System.Linq;
using Hrot.Editor.AiShared.Selection;
using Hrot.Presentation.Editing; 
using StructEdit.Core;
using ImGuiNET;

namespace Hrot.Hsm.Editor.Inspector;

public sealed class HsmEventPickerDrawer : IImGuiFieldDrawer
{
    private readonly EditorSelectionStore _store;

    public HsmEventPickerDrawer(EditorSelectionStore store) => _store = store;
    public Type TargetType => typeof(ushort);

    public bool DrawInput(ref object value, EditNode node)
    {
        if (!node.Metadata.CustomAttributes.Any(a => a is HsmEventPickerAttribute))
            return false; // Fallthrough to standard ushort/int input

        ushort current = value is ushort u ? u : (ushort)0;
        bool changed = false;

        var asset = _store.ActiveAsset as Model.HsmAsset;
        string preview = asset?.AllEvents.FirstOrDefault(e => e.EventId == current)?.Name ?? current.ToString();

        if (ImGui.BeginCombo("##EventId", preview))
        {
            if (asset != null)
            {
                foreach (var evt in asset.AllEvents)
                {
                    if (ImGui.Selectable($"{evt.Name} ({evt.EventId})", current == evt.EventId))
                    {
                        value = evt.EventId;
                        changed = true;
                    }
                }
            }
            ImGui.EndCombo();
        }
        return changed;
    }
}

public sealed class HsmStateSelectorDrawer : IImGuiFieldDrawer
{
    private readonly EditorSelectionStore _store;

    public HsmStateSelectorDrawer(EditorSelectionStore store) => _store = store;
    public Type TargetType => typeof(string);

    public bool DrawInput(ref object value, EditNode node)
    {
        if (!node.Metadata.CustomAttributes.Any(a => a is HsmStateSelectorAttribute))
            return false;

        string current = value as string ?? string.Empty;
        bool changed = false;

        if (ImGui.BeginCombo("##StateSelector", current))
        {
            if (_store.ActiveAsset is Model.HsmAsset asset)
            {
                foreach (var state in asset.AllStates)
                {
                    if (ImGui.Selectable(state.Name, current == state.Name))
                    {
                        value = state.Name;
                        changed = true;
                    }
                }
            }
            ImGui.EndCombo();
        }
        return changed;
    }
}
```

_(You will follow the exact same pattern for_ _HsmActionPickerDrawer__,_ _HsmGuardPickerDrawer_ _using_ _typeof(string)__, and_ _HsmSyncGroupPickerDrawer_ _using_ _typeof(ushort)__)._

3\. Registering the Custom Drawers in the Composition Root

Modify the `InspectorWindow` instantiation to accept the collection of `IImGuiFieldDrawer` instances, or register them into the global `ComponentEditServiceBuilder` / `IReadOnlyDictionary<Type, IImGuiFieldDrawer>` used by the inspector's StructEdit session.

If injecting directly into `EditorSubsystem.cs` initialization:

```
// Inside EditorSubsystem.Initialize(SubsystemConfig config):

var customFieldDrawers = new Dictionary<Type, IImGuiFieldDrawer>
{
    // BTree
    [typeof(string)] = new CompositeStringDrawer(
        new BTreeMethodPickerDrawer(_behaviorRegistry),
        new BTreeBlackboardFieldPickerDrawer(_aiEditorSelectionStore),
        new HsmStateSelectorDrawer(_aiEditorSelectionStore)
        // Add HsmActionPickerDrawer and HsmGuardPickerDrawer here
    ),
    
    // HSM
    [typeof(ushort)] = new CompositeUshortDrawer(
        new HsmEventPickerDrawer(_aiEditorSelectionStore)
        // Add HsmSyncGroupPickerDrawer here
    )
};

// Supply customFieldDrawers to the InspectorWindow or the shared ComponentEditDrawer it wraps.
```

_Note: Because_ _IImGuiFieldDrawer_ _binds strictly to the underlying CLR type (like_ _string_ _or_ _ushort__), you must implement a simple composite drawer for types that share multiple marker attributes. The composite iterates over the inner drawers and returns_ _true_ _if any drawer successfully handles the specific_ _node.Metadata.CustomAttributes__._

and now please detail 4. Commit Flow Wiring

To implement the commit flow, you must wire the shared `InspectorWindow` to poll the StructEdit session's dirty state and translate the committed facet structs back into the subsystem-specific asset models.

**1\. Shared Inspector Dirty Polling** Modify the update loop in the shared `InspectorWindow` (likely in `Hrot/Subsystems/Hrot.Editor/Windows/InspectorWindow.cs`). You must track the `IsDirty` state of the active `IEditSession` at the end of each frame. When a transition from `true` to `false` occurs, the user has completed their edit, and you must extract the modified struct and route it to the active mapper.

```
private bool _wasDirty;

protected override void DrawClientArea()
{
    if (_currentSession == null) return;

    // Render the StructEdit UI
    _currentSession.Draw();

    // 1. Poll IsDirty at end of frame
    bool isDirty = _currentSession.IsDirty;
    
    // 2. On true->false transition, commit and route
    if (_wasDirty && !isDirty)
    {
        object updatedFacet = _currentSession.Commit();
        ApplyCommitToAsset(updatedFacet);
    }
    
    _wasDirty = isDirty;
}

private void ApplyCommitToAsset(object updatedFacet)
{
    // These mappers must be injected or accessible from the composition root 
    // where EditorSelectionStore's ActiveAsset is evaluated.
    var activeAsset = _aiEditorSelectionStore.ActiveAsset;

    if (activeAsset is Hrot.BTree.Editor.Model.BehaviorTreeAsset)
    {
        _bTreeFacetMapper?.ApplyCommit(updatedFacet);
    }
    else if (activeAsset is Hrot.Hsm.Editor.Model.HsmAsset)
    {
        _hsmFacetMapper?.ApplyCommit(updatedFacet);
    }
}
```

**2\. Implement BTree Commit Translation** Modify `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreeFacetMapper.cs`. Add an `ApplyCommit` method that takes the modified facet struct, looks up the corresponding node via its `VisualId`, applies the field updates, and marks the asset dirty to trigger the regeneration scheduler.

```
public void ApplyCommit(object facet)
{
    if (facet is BTreeActionFacet actionFacet)
    {
        var node = _asset.FindNode(Guid.Parse(actionFacet.VisualId));
        if (node != null)
        {
            // Apply changes to the editor model
            node.Action.MethodFqn = actionFacet.MethodFqn;
            node.Comment = actionFacet.Comment;
            
            // Trigger deterministic C# regeneration
            _asset.MarkDirty();
        }
    }
    else if (facet is BTreeWaitFacet waitFacet)
    {
        var node = _asset.FindNode(Guid.Parse(waitFacet.VisualId));
        if (node != null)
        {
            node.Wait.Duration = waitFacet.Duration;
            node.Comment = waitFacet.Comment;
            _asset.MarkDirty();
        }
    }
    // Implement remaining BTree facets (Sequence, Selector, Subtree, etc.)
}
```

**3\. Implement HSM Commit Translation** Modify `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmFacetMapper.cs`. HSM elements map similarly, applying properties back to the specific states, transitions, regions, or events.

```
public void ApplyCommit(object facet)
{
    if (facet is StateFacet stateFacet)
    {
        var state = _asset.FindStateByStableId(Guid.Parse(stateFacet.StableId));
        if (state != null)
        {
            state.Name = stateFacet.Name;
            state.OnEntryAction = stateFacet.OnEntryAction;
            state.OnExitAction = stateFacet.OnExitAction;
            state.ActivityAction = stateFacet.ActivityAction;
            state.Comment = stateFacet.Comment;
            _asset.MarkDirty();
        }
    }
    else if (facet is EventFacet eventFacet)
    {
        var evt = _asset.FindEventById(eventFacet.EventId);
        if (evt != null)
        {
            evt.Name = eventFacet.Name;
            evt.PayloadSize = eventFacet.PayloadSize;
            evt.Priority = eventFacet.Priority;
            evt.IsIndirect = eventFacet.IsIndirect;
            _asset.MarkDirty();
        }
    }
    // Implement remaining HSM facets (Transition, Region, etc.)
}
```

Once `_asset.MarkDirty()` executes, the specific subsystem's command sink or layout manager will intercept the change, queue it in the `RegenerationScheduler`, and deterministically write the C# file back to disk.

pls detail **6\. Debug Session Registry**

To wire the debug sessions, you must construct the subsystem-specific production sessions, register their factories with the shared registry, and plug their specific UI panes into the shared runtime windows inside `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`.

**1\. Instantiate the Coordinator and Registry** The shared `AiTracerCoordinator` and `DebugSessionRegistry` manage reference-counted asset observation and exclusive session acquisition. If they are not already resolved via your dependency injection container using `AddSharedAiEditor()`, instantiate them manually.

```
using Hrot.Editor.AiShared.Debug;
using Hrot.BTree.Editor.Debug;
using Hrot.Hsm.Editor.Debug;
using Hrot.BTree.Editor.Host;
using Hrot.Hsm.Editor.Host;
using Hrot.BTree.Editor.Inspector;
using Hrot.Hsm.Editor.Inspector;

// Initialize shared tracking infrastructure
var tracerCoordinator = new AiTracerCoordinator();
var debugSessionRegistry = new DebugSessionRegistry();
```

**2\. Register the Session Factories** The BTree and HSM subsystems provide `BTreeDebugSession` and `HsmDebugSession`, both of which inherit from the shared `AiDebugSessionBase`. Register them into the registry using the `RegisterSessionFactory` method.

```
debugSessionRegistry.RegisterSessionFactory<IBTreeDebugSession>(() => new BTreeDebugSession(tracerCoordinator));
debugSessionRegistry.RegisterSessionFactory<IHsmDebugSession>(() => new HsmDebugSession(tracerCoordinator));
```

**3\. Update Asset Contributor Wiring** Update your `BTreeAssetContributor` instantiation to receive the `BTreeDebugSession` instance. This is required so the projection layer can wire `NodeDebugMetadata` into the session, enabling node-index symbolication when `Update()` is called by the kernel.

```
// Acquire the session instances for wiring
debugSessionRegistry.TryAcquireSession<IBTreeDebugSession>(out var bTreeSession);
debugSessionRegistry.TryAcquireSession<IHsmDebugSession>(out var hsmSession);

// Pass the specific session to the contributor
var bTreeContributor = new BTreeAssetContributor(bTreeSession as BTreeDebugSession); 
// ... continue wiring assetCatalog as detailed in Step 2 ...
```

**4\. Register Runtime Inspector Panes** The shared `RuntimeInspectorWindow` delegates the rendering of kernel state to subsystem-provided panes based on the active asset's kind. Instantiate `BTreeRuntimeInspectorPane` and `HsmRuntimeInspectorPane`, bind their active debug sessions, and register them.

```
var bTreePane = new BTreeRuntimeInspectorPane();
bTreePane.SetSession(bTreeSession);
_runtimeInspectorWindow.RegisterPane(bTreePane);

var hsmPane = new HsmRuntimeInspectorPane();
hsmPane.SetSession(hsmSession);
_runtimeInspectorWindow.RegisterPane(hsmPane);
```

**5\. Register Trace Timeline Lanes** The shared `TraceTimelineWindow` visualizes the trace ring buffers as horizontal swim lanes. Instantiate `BTreeTraceLaneProvider` and `HsmTraceLaneProvider` to declare their subsystem-specific trace lanes (e.g., Nodes, Status, States, Events) and register them.

```
_traceTimelineWindow.RegisterProvider(new BTreeTraceLaneProvider());
_traceTimelineWindow.RegisterProvider(new HsmTraceLaneProvider());
```

**6\. Canvas Host Services Injection** When instantiating the canvas windows in the `RegisterWindows` step, ensure that the active debug session is passed into the host services. Both `BTreeEditorHostServices` and `HsmEditorHostServices` accept the `IDebugSession` implementation so that the `NodeEditor` canvas can render executing-node highlights, ancestor glow, and breakpoint markers.

is there anything left to detail reagrding BTree and HSM blueprint integrations to the editor?

There remain several critical integration points to complete the unified AI editor architecture. The initial six steps established the visual, selection, and debug foundations, but cross-asset data flow, code generation, and diffing require additional wiring in the composition root.

**7\. Reference Catalog Wiring** Cross-asset refactoring (Find References, Rename, Dangling Reference checks) relies on the shared `IReferenceCatalog`. You must instantiate subsystem-specific contributors, such as `BTreeBlackboardVariableContributor`, and register them with the reference catalog. This allows the shared refactor service to track sub-elements (like blackboard variables or action FQNs) uniformly across the different domains.

**8\. Blackboard Aggregation Strategies** To resolve shared working state constraints and bin-pack DTO requirements, you must instantiate `BTreeBlackboardAggregatorStrategy` and `HsmBlackboardAggregatorStrategy`. Register these into the shared `BlackboardAggregatorService` so it can traverse the dependency graph and compute sub-tree memory requirements when the user edits blackboard schemas.

**9\. Custom Canvas Renderers** While Step 3 noted passing a renderer list to the host services, you must explicitly instantiate the required subsystem-specific extensions to satisfy the NodeEditor `ICustomCanvasRenderer` contract.

-   **For BTree:** Instantiate `BTreeBreakpointGutterRenderer`, `BTreeRuntimeOverlayRenderer`, `ObserverGuardBadgeRenderer`, and `SubtreeBoundaryRenderer`.-   **For HSM:** Instantiate `HsmBreakpointGutterRenderer`, `HsmInitialArrowRenderer`, `HsmRuntimeOverlayRenderer`, as well as the renderers for transition labels, region conflicts, and history glyphs. Inject these lists into `BTreeEditorHostServices` and `HsmEditorHostServices` respectively so domain-specific overlays draw at the correct z-index passes (e.g., `AfterWires` or `BeforeContent`).

**10\. Fluent Emitters and Save Pipeline** You must instantiate `BTreeFluentEmitter` and `HsmFluentEmitter`. The editor's save commands and the `QuickReloadService` must route the active in-memory models (`BehaviorTreeAsset` and `HsmAsset`) through these emitters. This guarantees the deterministic generation of fluent C# code and the `[BTreeLayout]` / `[HsmLayout]` methods before writing to disk or passing to the in-memory Roslyn compiler.

**11\. Comparison Sanitizers** To enable the LLM-based asset comparison and diffing features, you must wire the subsystem sanitizers. Inside your initialization flow, instantiate `BTreeComparisonSanitizer` and `HsmComparisonSanitizer` (passing the `IAssetCatalog` to both). Register these instances with the shared `SanitizerRegistry`. This strips canvas positions and presentation noise from the C# files so the refactor/diff tools evaluate pure semantic changes.

oks lets detail one at a time

To implement Reference Catalog Wiring, you must construct the shared `ReferenceCatalog` and inject the subsystem-specific contributors. This enables cross-asset operations such as Find References and Rename.

In `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`, locate the initialization block where you previously constructed the unified `AssetCatalog`.

**1\. Instantiate the Contributors** Subsystems provide implementations of `IReferenceCatalogContributor`. For example, the BTree subsystem provides `BTreeBlackboardVariableContributor`, which extracts `SubElementKind.BlackboardVariable` elements and maps references from nodes that use them.

**2\. Construct the Reference Catalog** Instantiate the `ReferenceCatalog` by passing the `AssetCatalog` and the list of your instantiated contributors. The `ReferenceCatalog` will automatically subscribe to the `IAssetCatalog.Changed` event to rebuild its multi-index after every hot reload.

Add the following wiring to your composition root:

```
using Hrot.Editor.AiShared.References;
using Hrot.BTree.Editor.Catalog;

// ... inside EditorSubsystem initialization, after assetCatalog is created ...

var referenceContributors = new List<IReferenceCatalogContributor>
{
    new BTreeBlackboardVariableContributor()
    // Add HSM and Blueprint reference contributors here
};

var referenceCatalog = new ReferenceCatalog(assetCatalog, referenceContributors);
```

**3\. Inject into Shared Services** Pass the `referenceCatalog` into the constructor of the shared `RefactorService`. This ensures that when an LLM or user initiates a rename operation, the service can query the canonical reference catalog to find all dependent assets.

pls be more specific about point 1, 2, 3 (more detailed code please)

**1\. Instantiate the Contributors (BTreeBlackboardVariableContributor)**

To track references across BTree assets, you must implement `IReferenceCatalogContributor`. The BTree implementation extracts the asset's defined blackboard variables as referenceable elements, and parses action/condition nodes to find where they utilize `ExpressionTargetField`.

```
// File: Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeBlackboardVariableContributor.cs
using System;
using System.Collections.Generic;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.References;

namespace Hrot.BTree.Editor.Catalog;

public sealed class BTreeBlackboardVariableContributor : IReferenceCatalogContributor
{
    public IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset)
    {
        if (asset is not BehaviorTreeAsset btAsset || !btAsset.IsBlackboardEditorManaged)
            return Array.Empty<IAssetSubElement>();

        var result = new List<IAssetSubElement>(btAsset.BlackboardVariables.Count);
        foreach (var v in btAsset.BlackboardVariables)
            result.Add(new BlackboardVariableSubElement(btAsset.AssetId, v.Name));
        return result;
    }

    public IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset)
    {
        if (asset is not BehaviorTreeAsset btAsset || !btAsset.IsBlackboardEditorManaged)
            return Array.Empty<AssetReference>();

        var result = new List<AssetReference>();
        foreach (var node in btAsset.Nodes)
        {
            string? etf = node.Action?.ExpressionTargetField ?? node.Condition?.ExpressionTargetField;
            if (etf is null) continue;

            result.Add(new AssetReference(
                HostAssetId:     btAsset.AssetId,
                HostKind:        AssetKind.BTree,
                HostElementId:   node.VisualId,
                HostDisplayPath: node.DisplayLabel,
                TargetKey:       $"{btAsset.AssetId:D}::{etf}",
                TargetKind:      SubElementKind.BlackboardVariable));
        }
        return result;
    }
}
```

**2\. Construct the Reference Catalog**

Inside your composition root, instantiate the contributors and pass them to the `ReferenceCatalog`. The catalog binds to `IAssetCatalog.Changed` automatically during construction to ensure the multi-index rebuilds after every hot reload.

```
// File: Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs (Inside Initialize method)
using Hrot.Editor.AiShared.References;
using Hrot.BTree.Editor.Catalog;

// ... after unified assetCatalog is created ...

var referenceContributors = new List<IReferenceCatalogContributor>
{
    new BTreeBlackboardVariableContributor()
    // Append HSM and Blueprint reference contributors here 
};

var referenceCatalog = new ReferenceCatalog(assetCatalog, referenceContributors);
```

**3\. Inject into Shared Services**

The `RefactorService` consumes the `ReferenceCatalog` alongside an `AtomicMultiFileWriter` to apply cross-asset renames and deletions atomically. You must construct these and inject the `RefactorService` into the shared windows that expose right-click context menus for renaming and finding references.

```
// File: Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs (Inside Initialize method)
using Hrot.Editor.AiShared.Refactor;

var atomicWriter = new AtomicMultiFileWriter();
var refactorService = new RefactorService(referenceCatalog, assetCatalog, atomicWriter);

// The find results panel displays the output of Find References queries
var findResultsWindow = new FindResultsWindow(/* existing dependencies */);

var assetBrowserWindow = new AssetBrowserWindow(
    catalog: assetCatalog,
    store: _aiEditorSelectionStore,
    dirtyTracker: _dirtyTracker,
    editorState: _editorState,
    refactorService: refactorService,
    findResults: findResultsWindow,
    liveProvider: _liveSessionProvider
);

var inspectorWindow = new InspectorWindow(
    store: _aiEditorSelectionStore,
    refactorService: refactorService,
    findResults: findResultsWindow,
    subAssetResolver: null 
);
```

is that all rearding **7\. Reference Catalog Wiring** ?

That covers the initialization wiring, but you must implement the remaining architectural constraints for the refactor lifecycle and the other subsystem contributors to complete the reference tracking system.

**1\. Refactor Lifecycle and Deferred Rebuilds** You must not attempt to manually update or pre-compute the reference catalog's state from within the `RefactorService` after a rename or delete operation. The architecture dictates that the `RefactorService` writes the modifications to disk and relies entirely on the natural hot-reload cycle. Once the file watcher triggers an MSBuild rebuild and the hot-reload completes, the `IAssetCatalog.Changed` event fires, which prompts the `ReferenceCatalog` to walk all assets via reflection and recompute its multi-index from scratch.

**2\. Atomic Multi-File Writer Implementation** The `AtomicMultiFileWriter` injected into the `RefactorService` must guarantee atomicity using a temp-file pattern. During a cross-asset refactor (such as renaming an action used across BTree, HSM, and Blueprint assets), either all file modifications succeed, or the entire batch rolls back. This ensures the reference catalog does not rebuild against a partially refactored project state.

**3\. HSM and Blueprint Contributors** The `BTreeBlackboardVariableContributor` demonstrated previously only handles one domain. You must implement and register the corresponding `IReferenceCatalogContributor` classes for the other subsystems so the catalog sees all references uniformly.

-   **HSM Contributor:** Must extract references for actions, guards, and events. When generating the `IAssetSubElement.Key` for HSM events, it must enforce machine-scoping by formatting the key as `{MachineAssetId}::{EventName}`. This prevents a rename of "OnSight" in one state machine from corrupting an unrelated state machine.-   **Blueprint Contributor:** Must extract asset references from Blueprint Call nodes and map `AiPrimitive` hosted actions to their source Blueprint asset names, as renaming a Blueprint asset acts as a cross-asset refactor for all BTrees and HSMs referencing it.

can you detail 1. Refactor Lifecycle and Deferred Rebuilds

The refactor service must not attempt to manually update or pre-compute the reference catalog's state following a rename or delete operation. Instead, the service writes the file modifications directly to disk. The engine's existing file watcher debounces these writes—typically within a 200–500 ms window—into a single MSBuild rebuild.

The architecture relies entirely on this natural hot-reload cycle, avoiding any editor-side compilation. Once MSBuild completes the rebuild and the hot-reload assembly swap finishes, the `IAssetCatalog.Changed` event fires. The `ReferenceCatalog` listens to this event and automatically rebuilds its multi-index from scratch. While this deferred rebuild occurs, the user is presented with a "refactor applied; rebuilding…" indicator until the new catalog becomes queryable.

can you detail 2. Atomic Multi-File Writer Implementation

The atomic multi-file writer is a shared utility that guarantees atomicity during cross-asset refactors using a temp-file pattern. It ensures that either all file modifications in a batch succeed, or the entire batch rolls back before the engine's file watcher can trigger a partial rebuild.

The algorithm executes in four strict phases:

-   Write the new content for each file to `{path}.{guid}.tmp` in the same directory. Same-directory writes ensure that the subsequent rename operation remains atomic across all filesystems.-   If any temp file write fails, delete all temp files generated so far and return a total failure.-   Once all temp files are successfully written, invoke `File.Move(tmp, original, overwrite: true)` on each file in deterministic order (sorted by path).-   If a `File.Move` operation fails midway through the batch (typically due to a file lock from an external tool), log the failure and return a partial success. Do not attempt to roll back the files that were already moved, as the file watcher has already observed those changes. The editor surfaces this rare failure to the user as an error toast indicating the project must be reloaded.

Implement the following in `Hrot/Editor/Hrot.Editor.AiShared/Refactor/AtomicMultiFileWriter.cs`:

````
using System;
using System.Collections.Generic;
using System.IO;

namespace Hrot.Editor.AiShared.Refactor;

public sealed class AtomicMultiFileWriter
{
    public AtomicWriteResult Write(IReadOnlyDictionary<string, string> filePathToContent)
    {
        // 1. Write each file to a temp path in the same directory.
        var tempFiles = new List<(string TempPath, string FinalPath)>();
        foreach (var (finalPath, content) in filePathToContent)
        {
            var tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, content, System.Text.Encoding.UTF8);
                tempFiles.Add((tempPath, finalPath));
            }
            catch (Exception ex)
            {
                // Roll back all temp files written so far.
                foreach (var (t, _) in tempFiles)
                    TryDelete(t);
                TryDelete(tempPath);
                return new AtomicWriteResult(false, Array.Empty<string>(), ex.Message);
            }
        }

        // 2. Move all temp files to their final paths (overwrite).
        var written = new List<string>();
        foreach (var (tempPath, finalPath) in tempFiles)
        {
            try
            {
                File.Move(tempPath, finalPath, overwrite: true);
                written.Add(finalPath);
            }
            catch (Exception ex)
            {
                // Partial failure: log but do not roll back already-moved files.
                TryDelete(tempPath);
                return new AtomicWriteResult(false, written.AsReadOnly(), ex.Message);
            }
        }
        return new AtomicWriteResult(true, written.AsReadOnly(), null);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort */ }
    }
}

public sealed record AtomicWriteResult(
    bool Success,
    IReadOnlyList<string> SuccessfullyWritten,
    string? FailureReason);
``` [4-6]
````

pls detail 3. HSM and Blueprint Contributors

To implement the HSM and Blueprint reference contributors, you must create subsystem-specific classes that extract declared referenceable elements (like events or exported actions) and discover outbound references to those elements (like action invocations or peer calls). Both must implement `IReferenceCatalogContributor` to feed the shared multi-index.

**1\. HSM Reference Contributor** The HSM contributor extracts references for state actions, transition guards, and transition events. Because HSM event names are machine-scoped, you must strictly enforce the scoping rule by formatting event keys as `{MachineAssetId}::{EventName}`. This ensures renaming an event in one state machine does not corrupt identical event names in unrelated machines.

Implement this in `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Catalog/HsmReferenceContributor.cs`:

```
using System;
using System.Collections.Generic;
using Hrot.Hsm.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.References;

namespace Hrot.Hsm.Editor.Catalog;

public sealed class HsmReferenceContributor : IReferenceCatalogContributor
{
    public IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset)
    {
        if (asset is not HsmAsset hsmAsset)
            return Array.Empty<IAssetSubElement>();

        var result = new List<IAssetSubElement>();
        
        // Extract machine-scoped events as referenceable elements
        foreach (var evt in hsmAsset.AllEvents)
        {
            result.Add(new HsmEventSubElement(hsmAsset.AssetId, evt.Name));
        }
        
        return result;
    }

    public IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset)
    {
        if (asset is not HsmAsset hsmAsset)
            return Array.Empty<AssetReference>();

        var result = new List<AssetReference>();

        // Extract action references from states
        foreach (var state in hsmAsset.AllStates)
        {
            AddActionReference(result, hsmAsset, state.StableId, state.Name, state.OnEntryAction);
            AddActionReference(result, hsmAsset, state.StableId, state.Name, state.OnExitAction);
            AddActionReference(result, hsmAsset, state.StableId, state.Name, state.ActivityAction);
        }

        // Extract event, guard, and action references from transitions
        foreach (var t in hsmAsset.AllTransitions)
        {
            var path = $"Transition '{t.Source?.Name}' -> '{t.Target?.Name}'";
            
            // Event references must use the machine-scoped key
            if (t.EventId != 0) 
            {
                var evt = hsmAsset.FindEventById(t.EventId);
                if (evt != null)
                {
                    result.Add(new AssetReference(
                        hsmAsset.AssetId, AssetKind.Hsm, t.VisualId, path,
                        $"{hsmAsset.AssetId:D}::{evt.Name}", SubElementKind.EventName));
                }
            }

            AddGuardReference(result, hsmAsset, t.VisualId, path, t.GuardFunction);
            AddActionReference(result, hsmAsset, t.VisualId, path, t.ActionFunction);
        }

        return result;
    }

    private static void AddActionReference(List<AssetReference> list, HsmAsset asset, Guid elementId, string path, string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return;
        list.Add(new AssetReference(asset.AssetId, AssetKind.Hsm, elementId, path, fqn, SubElementKind.ActionFqn));
    }

    private static void AddGuardReference(List<AssetReference> list, HsmAsset asset, Guid elementId, string path, string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return;
        list.Add(new AssetReference(asset.AssetId, AssetKind.Hsm, elementId, path, fqn, SubElementKind.GuardFqn));
    }
}

internal sealed class HsmEventSubElement : IAssetSubElement
{
    public string Key { get; }
    public SubElementKind Kind => SubElementKind.EventName;
    public string DisplayName { get; }
    public Guid? SourceAssetId { get; }

    public HsmEventSubElement(Guid assetId, string eventName)
    {
        SourceAssetId = assetId;
        DisplayName = eventName;
        // Key uniquely scopes the event to the asset
        Key = $"{assetId:D}::{eventName}";
    }
}
```

**2\. Blueprint Reference Contributor** The Blueprint contributor must extract asset references from Blueprint Call nodes (`CallPeerBlueprintNode`) and map `AiPrimitive` assets so they are visible as actions. The canonical key for a Blueprint-hosted `AiPrimitive` action is its Blueprint asset name string. When a Blueprint asset is renamed, it acts as a cross-asset refactor for all BTrees and HSMs tracking that string key.

Implement this in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Catalog/BlueprintReferenceContributor.cs`:

```
using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.References;

namespace Hrot.Blueprints.Editor.Catalog;

public sealed class BlueprintReferenceContributor : IReferenceCatalogContributor
{
    public IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset)
    {
        if (asset is not BlueprintEditableAssetAdapter adapter)
            return Array.Empty<IAssetSubElement>();

        var bpAsset = adapter.Asset;
        var result = new List<IAssetSubElement>();

        // AiPrimitive assets expose themselves globally as actions/conditions based on their intent
        if (bpAsset.Dispatch == BlueprintDispatchKind.AiPrimitive && bpAsset.Primitive != null)
        {
            var kind = bpAsset.Primitive.Intent == AiPrimitiveIntent.Condition 
                ? SubElementKind.ConditionFqn 
                : SubElementKind.ActionFqn;

            result.Add(new BlueprintAiPrimitiveSubElement(bpAsset.AssetId, bpAsset.Name, kind));
        }

        return result;
    }

    public IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset)
    {
        if (asset is not BlueprintEditableAssetAdapter adapter)
            return Array.Empty<AssetReference>();

        var bpAsset = adapter.Asset;
        var result = new List<AssetReference>();

        foreach (var graph in bpAsset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                // Track peer calls for cross-asset rename and dangling reference checks
                if (node is CallPeerBlueprintNode peerCall && Guid.TryParse(peerCall.PeerBlueprintId, out var targetAssetId))
                {
                    result.Add(new AssetReference(
                        bpAsset.AssetId, 
                        AssetKind.Blueprint, 
                        peerCall.Id, 
                        $"Graph '{graph.Name}'", 
                        targetAssetId.ToString("D"), 
                        SubElementKind.AssetReference));
                }
            }
        }

        return result;
    }
}

internal sealed class BlueprintAiPrimitiveSubElement : IAssetSubElement
{
    public string Key { get; }
    public SubElementKind Kind { get; }
    public string DisplayName { get; }
    public Guid? SourceAssetId { get; }

    public BlueprintAiPrimitiveSubElement(Guid assetId, string assetName, SubElementKind kind)
    {
        SourceAssetId = assetId;
        DisplayName = assetName;
        Kind = kind;
        // Blueprint AiPrimitives use their asset name string as the FQN key equivalence
        Key = assetName; 
    }
}
```

Once written, add both `HsmReferenceContributor` and `BlueprintReferenceContributor` to the `List<IReferenceCatalogContributor>` during the initialization of the shared `ReferenceCatalog` detailed in step 7.

ok is that all for : **7\. Reference Catalog Wiring** Cross-asset refactoring (Find References, Rename, Dangling Reference checks) relies on the shared `IReferenceCatalog`. You must instantiate subsystem-specific contributors, such as `BTreeBlackboardVariableContributor`, and register them with the reference catalog. This allows the shared refactor service to track sub-elements (like blackboard variables or action FQNs) uniformly across the different domains.

The core registration and temp-file writing pipelines are complete, but you must implement two final architectural constraints relying on the reference catalog to fully satisfy the cross-asset requirements:

**1\. SubElementCollision Validation** After the `IAssetCatalog.Changed` event fires and the `ReferenceCatalog` rebuilds its multi-index, you must scan the catalog for short names that have multiple FQN claimants. If two `[BTreeAction]`, `[BTreeCondition]`, `[HsmAction]`, or `[HsmGuard]` methods are registered with the same short name but live in different declaring types, you must raise a `SubElementCollision` diagnostic. This editor-only validation surfaces the collision in the Inspector's diagnostic strip so the user can resolve it by renaming one of the methods.

**2\. Dangling Reference Classification** When the user deletes an asset, the `RefactorService.PreviewDelete(assetId)` method queries the reference catalog to find all outbound references to that asset. You must classify each returned reference into one of two categories:

-   **Auto-resolvable:** The reference can be redirected to a sentinel "missing asset" placeholder. The runtime will tolerate this, though the inspector will flag it (e.g., a BTree subtree call to a deleted tree).-   **Critical:** Removing the asset breaks compilation (e.g., a Blueprint that exports a type used by another asset's typed field).

If any Critical references exist and `AllowDanglingReferences` is false, `ApplyDelete` must refuse the operation with a clear error message, forcing the user to redirect the references or explicitly accept a broken-build state.

please detail 1. SubElementCollision Validation

To implement the `SubElementCollision` validation, you must scan the loaded action declarations to identify short names that resolve to multiple fully-qualified names (FQNs), and surface these conflicts in the shared `InspectorWindow`. This ensures that the editor strictly catches ambiguous references, while the runtime registry behavior remains untouched.

**1\. Implement the Collision Detector** Because the `IActionSchemaExporter` already reflects over the loaded assemblies and rebuilds its multi-index of `[BTreeAction]`, `[BTreeCondition]`, `[HsmAction]`, and `[HsmGuard]` declarations after every hot reload, you must extract the collision detection logic into a dedicated helper.

Create `Hrot/Editor/Hrot.Editor.AiShared/Validation/SubElementCollisionDetector.cs`:

```
using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Validation;

public sealed record ActionCollision(string ShortName, IReadOnlyList<string> ClaimingFqns);

public static class SubElementCollisionDetector
{
    public static IReadOnlyList<ActionCollision> GetCollisions(IActionSchemaExporter schemaExporter)
    {
        return schemaExporter.All.Values
            .GroupBy(entry => ExtractShortName(entry.Fqn))
            .Where(group => group.Count() > 1)
            .Select(group => new ActionCollision(
                group.Key, 
                group.Select(e => e.Fqn).OrderBy(f => f).ToArray()))
            .ToList();
    }

    private static string ExtractShortName(string fqn)
    {
        int lastDot = fqn.LastIndexOf('.');
        return lastDot >= 0 ? fqn.Substring(lastDot + 1) : fqn;
    }
}
```

**2\. Surface Collisions in the Inspector Diagnostic Strip** The validation is editor-only and must be surfaced prominently to the user. Modify `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` to query the detector and render a diagnostic strip at the top of the client area when collisions exist.

You must inject `IActionSchemaExporter` into the `InspectorWindow` constructor and invoke the rendering logic within `DrawClientArea()`.

```
using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using ImGuiNET;
using System.Numerics;

namespace Hrot.Editor.AiShared.Windows;

public sealed class InspectorWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IRefactorService _refactorService;
    private readonly FindResultsWindow _findResults;
    private readonly IActionSchemaExporter _schemaExporter; // Inject the schema exporter
    private readonly Func<Guid, IBlackboardManagedAsset?>? _subAssetResolver;
    
    // ... existing fields ...

    public InspectorWindow(
        EditorSelectionStore store,
        IRefactorService refactorService,
        FindResultsWindow findResults,
        IActionSchemaExporter schemaExporter,
        Func<Guid, IBlackboardManagedAsset?>? subAssetResolver = null)
        : base("ai_inspector", "Inspector", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _refactorService = refactorService;
        _findResults = findResults;
        _schemaExporter = schemaExporter;
        _subAssetResolver = subAssetResolver;
    }

    protected override void DrawClientArea()
    {
        // 1. Render the diagnostic strip for SubElementCollisions
        DrawCollisionDiagnosticStrip();

        if (_store.ActiveAsset is null)
        {
            ImGui.TextDisabled("Select an asset to begin.");
            return;
        }

        // ... existing facet rendering and StructEdit dispatch ...
    }

    private void DrawCollisionDiagnosticStrip()
    {
        var collisions = SubElementCollisionDetector.GetCollisions(_schemaExporter);
        if (collisions.Count == 0) return;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.2f, 0.05f, 0.05f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1f, 0.2f, 0.2f, 1f));
        
        // Size the strip dynamically based on the number of collisions
        if (ImGui.BeginChild("SubElementCollisions", new Vector2(0, 30 + (collisions.Count * 20)), true))
        {
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "⚠ SUB-ELEMENT COLLISIONS DETECTED");
            
            foreach (var collision in collisions)
            {
                ImGui.TextWrapped($"Short name '{collision.ShortName}' has multiple FQN claimants: {string.Join(", ", collision.ClaimingFqns)}");
            }
        }
        ImGui.EndChild();
        
        ImGui.PopStyleColor(2);
        ImGui.Spacing();
    }
    
    // ... remaining InspectorWindow methods ...
}
```

**3\. The Resolution Lifecycle** Do not attempt to provide an automated "fix" button for this diagnostic. The architecture dictates that the user resolves this by returning to their C# IDE and renaming one of the colliding methods. Once the file is saved, MSBuild will trigger a rebuild, the `IAssetCatalog.Changed` event will fire, the `ActionSchemaExporter` will re-reflect the assembly, and the Inspector's diagnostic strip will automatically vanish without requiring an editor restart.

pls detail 2. Dangling Reference Classification

When evaluating the deletion of an asset, the `PreviewDelete(assetId)` method must query the shared reference catalog to find all outbound references to that asset. Each discovered reference must be classified into one of two categories before returning the `DeletePreview` record:

**1\. Auto-resolvable References** These references can be safely redirected to a sentinel "missing asset" placeholder. The runtime will tolerate this missing state, though the editor's inspector will flag it to the user. For example, if a BTree subtree call points to a deleted tree, the runtime simply fails the subtree node, but the authoring environment remains fully functional.

**2\. Critical References** These are references where removing the target asset fundamentally breaks compilation. For example, if a Blueprint asset exports a type that is actively referenced by another asset's typed field, deleting the Blueprint breaks the build.

During the execution phase, if any critical issues exist and the `AllowDanglingReferences` flag is set to false, `ApplyDelete` must refuse the operation and return a clear error message. This strict gate forces the user to either redirect the broken references first using a separate rename flow, or explicitly set `AllowDanglingReferences: true` to accept the broken-build state.

is that now all to "7. Reference Catalog Wiring", can we move to detailiong **8\. Blackboard Aggregation Strategies** or still something left to detail?

Step 7 is complete. You must now implement 8. Blackboard Aggregation Strategies.

The `BlackboardAggregatorService` traverses the asset dependency graph to compute DTO requirements for sub-elements. These requirements are fed into the `BlackboardBinPacker` to enforce the 100-byte inline memory ceiling and allocate heavy components when necessary.

**1\. BTree Blackboard Aggregator Strategy** Implement the BTree strategy in `Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/BTreeBlackboardAggregatorStrategy.cs`. This strategy extracts requirements from action and condition nodes and recursively traverses into referenced subtrees.

```
using System;
using System.Collections.Generic;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.BTree.Editor.Blackboard;

public sealed class BTreeBlackboardAggregatorStrategy : IBlackboardAggregatorStrategy
{
    private readonly BlackboardAggregatorService _service;

    public BTreeBlackboardAggregatorStrategy(BlackboardAggregatorService service)
        => _service = service;

    public bool CanHandle(IEditableAsset asset) => asset is BehaviorTreeAsset;

    public AggregationResult Aggregate(
        IEditableAsset asset, IActionSchemaExporter schema, IAssetCatalog catalog, HashSet<Guid> visited)
    {
        var btAsset = (BehaviorTreeAsset)asset;
        if (!visited.Add(btAsset.AssetId))
            return AggregationResult.Empty; // Cycle detected

        var requirements = new List<DtoRequirement>();
        var warnings = new List<AggregationWarning>();

        foreach (var node in btAsset.Nodes)
        {
            // Extract from Actions / Conditions
            string? fqn = node.Action?.MethodFqn ?? node.Condition?.MethodFqn;
            if (fqn != null)
            {
                var entry = schema.Lookup(fqn);
                if (entry != null)
                {
                    requirements.Add(new DtoRequirement(entry.DtoType, $"{btAsset.Name} > {node.DisplayLabel} ({fqn})", btAsset.AssetId, node.VisualId));
                }
                else
                {
                    warnings.Add(new AggregationWarning(AggregationWarningKind.SchemaEntryNotFound, $"Schema entry not found for FQN '{fqn}'", btAsset.AssetId));
                }
            }

            // Recurse into Subtrees
            if (node.Subtree != null && node.Subtree.SubtreeAssetId != Guid.Empty)
            {
                var childAsset = catalog.FindByAssetId(node.Subtree.SubtreeAssetId);
                if (childAsset != null)
                {
                    var childResult = _service.AggregateInternal(childAsset, visited);
                    requirements.AddRange(childResult.Requirements);
                    warnings.AddRange(childResult.Warnings);
                }
                else
                {
                    warnings.Add(new AggregationWarning(AggregationWarningKind.UnresolvedSubtree, $"Subtree '{node.Subtree.SubtreeName}' not found.", btAsset.AssetId));
                }
            }
        }
        return new AggregationResult(requirements, warnings);
    }
}
```

**2\. HSM Blackboard Aggregator Strategy** Implement the HSM strategy in `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Blackboard/HsmBlackboardAggregatorStrategy.cs`. This strategy extracts requirements from state and transition actions.

```
using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Blackboard;

public sealed class HsmBlackboardAggregatorStrategy : IBlackboardAggregatorStrategy
{
    private readonly BlackboardAggregatorService _service;

    public HsmBlackboardAggregatorStrategy(BlackboardAggregatorService service)
        => _service = service;

    public bool CanHandle(IEditableAsset asset) => asset is HsmAsset;

    public AggregationResult Aggregate(
        IEditableAsset asset, IActionSchemaExporter schema, IAssetCatalog catalog, HashSet<Guid> visited)
    {
        var hsmAsset = (HsmAsset)asset;
        if (!visited.Add(hsmAsset.AssetId))
            return AggregationResult.Empty;

        var requirements = new List<DtoRequirement>();
        var warnings = new List<AggregationWarning>();

        foreach (var state in hsmAsset.AllStates)
        {
            EmitIfFound(state.OnEntryAction, $"{hsmAsset.Name} > State '{state.Name}' OnEntry", hsmAsset, state.StableId, schema, requirements, warnings);
            EmitIfFound(state.OnExitAction, $"{hsmAsset.Name} > State '{state.Name}' OnExit", hsmAsset, state.StableId, schema, requirements, warnings);
            EmitIfFound(state.ActivityAction, $"{hsmAsset.Name} > State '{state.Name}' Activity", hsmAsset, state.StableId, schema, requirements, warnings);
        }

        // You must also implement EmitIfFound calls for Transitions and Global Transitions here.

        return new AggregationResult(requirements, warnings);
    }

    private void EmitIfFound(string? fqn, string path, HsmAsset asset, Guid elementId, IActionSchemaExporter schema, List<DtoRequirement> reqs, List<AggregationWarning> warns)
    {
        if (string.IsNullOrEmpty(fqn)) return;
        var entry = schema.Lookup(fqn);
        if (entry != null)
        {
            reqs.Add(new DtoRequirement(entry.DtoType, path, asset.AssetId, elementId));
        }
        else
        {
            warns.Add(new AggregationWarning(AggregationWarningKind.SchemaEntryNotFound, $"Schema entry not found for FQN '{fqn}'", asset.AssetId));
        }
    }
}
```

**3\. Service Registration** Wire these strategies into your composition root (`EditorSubsystem.cs`) so the `BlackboardAggregatorService` can dispatch to them.

```
// Inside EditorSubsystem.Initialize() after resolving IAssetCatalog and IActionSchemaExporter

var aggregatorService = new BlackboardAggregatorService(
    Array.Empty<IBlackboardAggregatorStrategy>(), // Pass empty initially to break circular dependencies
    _actionSchemaExporter, 
    assetCatalog
);

// Register the domain-specific strategies
aggregatorService.Register(new BTreeBlackboardAggregatorStrategy(aggregatorService));
aggregatorService.Register(new HsmBlackboardAggregatorStrategy(aggregatorService));
```

is that all to "8. Blackboard Aggregation Strategies"?

To complete the integration of the Blackboard Aggregation Strategies, you must wire the `BlackboardAggregatorService` into the shared `BlackboardAuthoringWindow` and feed its computed requirements into the `BlackboardBinPacker`.

While the strategies themselves extract the requirements, the editor UI must evaluate those requirements to allocate byte offsets and enforce the strict unmanaged memory ceilings.

**1\. Wiring the BlackboardAuthoringWindow** Modify the view-model building logic in the shared `BlackboardAuthoringWindow` (located in `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`). The window must invoke the aggregator service and pass both the asset's explicit variables and the aggregated sub-tree requirements to the bin packer.

```
// Inside BlackboardAuthoringWindow's view-model builder:

// 1. Invoke the aggregator service over the active asset
var aggregationResult = _aggregatorService.Aggregate(bbAsset);

// 2. Extract explicitly authored master variables
var descriptors = bbAsset.BlackboardVariables
    .Select(v => new BlackboardVariableDescriptor(v.Name, v.FieldType))
    .ToList();

// 3. Extract aggregated DTO requirements discovered from the dependency graph
var aggregatedDescriptors = aggregationResult?.Requirements
    .Select(r => new BlackboardVariableDescriptor(r.DtoType.Name, r.DtoType))
    .ToList();

// 4. Execute the bin packer
var pack = BlackboardBinPacker.Pack(descriptors, aggregatedDescriptors);

// 5. Surface warnings and assignments to the view model
var viewModel = new BlackboardWindowViewModel(
    HasActiveAsset: true,
    IsBlackboardEditorManaged: true,
    TotalInlineBytes: pack.TotalInlineBytes,
    TotalHeavyBytes: pack.TotalHeavyBytes,
    InlineBudget: BlackboardBinPacker.MaxInlineBytes,
    HeavyBudget: BlackboardBinPacker.MaxHeavyBytes,
    RequiresHeavyComponent: pack.RequiresHeavyComponent,
    Warning: pack.Warning,
    // ... map pack.Variables to UI rows ...
);
```

**2\. The Bin Packer Memory Ceilings** The `BlackboardBinPacker` computes sequential byte offsets using standard C# struct-alignment rules (capping alignment at 8 bytes). It enforces the following memory tiers:

-   **Inline Tier:** A maximum of 100 bytes is available in the base `BrainBlackboard.BehaviorParameters` region.-   **Heavy Tier:** If the inline tier is exhausted, variables spill over into the heavy tier (the `Blackboard1024` component), which holds up to 928 bytes. If either budget is exceeded, the packer returns `PackWarning.InlineMemoryExceeded` or `PackWarning.HeavyMemoryExceeded`, which the window must surface as a diagnostic warning to the author.

**3\. Implicit Blueprint Integration** You do not need to write a dedicated `BlueprintBlackboardAggregatorStrategy` for Blueprint `AiPrimitive` assets. When the Blueprint compiler generates an `AiPrimitive`, it emits C# thunks decorated with `[BTreeAction]`, `[BTreeCondition]`, `[HsmAction]`, or `[HsmGuard]` depending on its declared hostings. The shared `ActionSchemaExporter` automatically discovers these attributes during its assembly reflection pass. Consequently, when your `BTreeBlackboardAggregatorStrategy` or `HsmBlackboardAggregatorStrategy` calls `schema.Lookup(fqn)` for a Blueprint-hosted node, the Blueprint's memory requirements (up to 100 bytes for parameters) are automatically fetched and correctly aggregated.

This completes the cross-asset blackboard aggregation pipeline. You may proceed to detail 9. Custom Canvas Renderers.

good, pls detail **9\. Custom Canvas Renderers**

To integrate the custom canvas renderers, you must instantiate the subsystem-specific `ICustomCanvasRenderer` implementations and inject them into `BTreeEditorHostServices` and `HsmEditorHostServices` during initialization. These renderers execute during specific `CanvasRenderPass` phases (`BeforeContent`, `AfterWires`, `AfterNodes`, `TopMost`) to draw overlays using NodeEditor's provided ImGui draw list. Within a given pass, renderers execute in their registration order.

**1\. Instantiate BTree Renderers** The BTree host requires five custom renderers to display domain-specific state and debugging information.

You must construct the following list and pass it to `BTreeEditorHostServices`:

```
var bTreeRenderers = new List<ICustomCanvasRenderer>
{
    // Renders faint dashed rectangles behind nodes to indicate the active subtree boundary.
    new SubtreeBoundaryRenderer(bTreeAsset), // Pass: BeforeContent
    
    // Renders entry-frequency colors behind nodes when heatmap mode is active.
    new HeatmapOverlayRenderer(bTreeAsset), // Pass: BeforeContent
    
    // Renders "OBSERVES" badges on links from ObserverSelectors to their guard children.
    new ObserverGuardBadgeRenderer(), // Pass: AfterWires
    
    // Renders breakpoints as red circles in the left gutter of nodes.
    new BTreeBreakpointGutterRenderer(bTreeAsset), // Pass: AfterNodes
    
    // Renders pulsing gold outlines on executing nodes and stack-ancestry glow.
    new BTreeRuntimeOverlayRenderer() // Pass: AfterNodes
};

// Wire debug sessions and breakpoint managers to the renderers that require them
var bTreeOverlay = (BTreeRuntimeOverlayRenderer)bTreeRenderers[9];
bTreeOverlay.SetSession(bTreeSession);

var bTreeBoundary = (SubtreeBoundaryRenderer)bTreeRenderers;
bTreeBoundary.SetSession(bTreeSession);

var bTreeHeatmap = (HeatmapOverlayRenderer)bTreeRenderers[10];
bTreeHeatmap.SetSession(bTreeSession);

// Pass into host services
var bTreeHostServices = new BTreeEditorHostServices(..., customRenderers: bTreeRenderers);
```

**2\. Instantiate HSM Renderers** The HSM host requires seven custom renderers to implement statechart-specific visuals such as transition labels, history pseudostates, and region conflicts.

You must construct the following list and pass it to `HsmEditorHostServices`:

```
var hsmRenderers = new List<ICustomCanvasRenderer>
{
    // Fills states with colors based on entry frequency when heatmap mode is active.
    new HsmHeatmapRenderer(hsmAsset), // Pass: BeforeContent
    
    // Renders Event[Guard]/Action labels at transition midpoints.
    new HsmTransitionLabelRenderer(hsmAsset), // Pass: AfterWires
    
    // Renders the ⦿─→ marker pointing to a composite's initial child, and highlights LCA.
    new HsmInitialArrowRenderer(hsmAsset), // Pass: AfterNodes
    
    // Renders a yellow line and ⚠ glyph between states in different regions writing to the same lane.
    // Implements ICustomCanvasHitTester so clicking the glyph opens a diagnostic popup.
    new HsmRegionConflictsRenderer(hsmAsset), // Pass: AfterNodes
    
    // Overrides standard node visuals to render small H, H*, and ⊙ (Final) circles.
    new HsmHistoryGlyphsRenderer(hsmAsset), // Pass: AfterNodes
    
    // Renders red circles for state and transition breakpoints.
    new HsmBreakpointGutterRenderer(hsmAsset), // Pass: AfterNodes
    
    // Renders active-configuration glow on active leaf states and a transition pulse marker.
    new HsmRuntimeOverlayRenderer(hsmAsset) // Pass: AfterNodes
};

// Wire debug sessions
var hsmOverlay = (HsmRuntimeOverlayRenderer)hsmRenderers[17];
hsmOverlay.SetSession(hsmSession);

var hsmHeatmap = (HsmHeatmapRenderer)hsmRenderers;
hsmHeatmap.SetSession(hsmSession);

// Pass into host services
var hsmHostServices = new HsmEditorHostServices(..., customRenderers: hsmRenderers);
```

**Architectural Constraints for Custom Renderers:**

-   **Coordinate Spaces:** Renderers must compute coordinates in canvas space, convert them using `ctx.Viewport.GraphToScreen()`, and scale absolute sizes using `ctx.Zoom` to ensure lines and text scale correctly during pan and zoom operations.-   **Hit-Testing:** Renderers such as `HsmRegionConflictsRenderer` and `HsmTransitionLabelRenderer` must implement the `ICustomCanvasHitTester` companion interface to participate in mouse interaction. The canvas hit-tester will query these renderers in priority order based on their assigned `CanvasRenderPass`.-   **Pass Ordering:** You must strictly respect the pass semantics. Background colors and boundary hints (`HeatmapOverlayRenderer`, `SubtreeBoundaryRenderer`) must use `BeforeContent`. Link decorations (`HsmTransitionLabelRenderer`, `ObserverGuardBadgeRenderer`) must use `AfterWires`. Foreground overlays, glyph replacements, and debug pulses must use `AfterNodes`.

ok is that all to "9. Custom Canvas Renderers"?

The core instantiations were outlined, but you must enforce two architectural constraints to complete the custom renderer integration: strict registration ordering and interactive interfaces.

**1\. Strict Registration Ordering** Renderers registered for the same `CanvasRenderPass` execute in their exact registration order, which dictates their visual z-index. For the HSM host, you must register the renderers in the following specific sequence:

-   `hsm.initial_state_arrows` (`AfterNodes`): Renders first among node overlays.-   `hsm.region_conflicts` (`AfterNodes`): Sits on top of the initial-state arrows to ensure the warning is unmistakable.-   `hsm.history_glyphs` (`AfterNodes`): Renders after conflicts so the glyph's visual surface and selection outline remain on top.-   `hsm.runtime_overlay` (`AfterNodes`): Renders last to overlay everything else, as the live execution state is the most ephemeral.-   `hsm.transition_labels` (`AfterWires`): Runs in an earlier pass, so its relative position in the list only affects other `AfterWires` renderers.

**2\. Hit Testing and Selection Interfaces** Renderers that are purely visual require only `ICustomCanvasRenderer`. However, interactive overlays such as transition labels and region conflict warnings must implement `ICustomCanvasHitTester`. During the canvas hit-test phase, the canvas queries these renderers in priority order. The renderer must return a `CustomElementHit` on a successful hit. If the custom element needs to participate in canvas selection (e.g., clicking a transition label to view its properties in the Inspector), the renderer must also implement `ICustomCanvasSelectable`. When the user clicks the element, the canvas updates its `SelectionState` and invokes the renderer's `OnElementSelected` method. The renderer must then read `ctx.Selection.SelectedCustomElements` during its next `Render` pass to draw selection feedback.

**3\. Coordinate Space Enforcement** Custom renderers must perform all layout and geometry calculations in canvas coordinates. At the exact moment of drawing, coordinates must be converted to screen space using `ctx.Viewport.CanvasToScreen` (or `GraphToScreen`), and fixed absolute sizes must be scaled by `ctx.Zoom` to ensure correct rendering during pan and zoom operations.

This concludes the custom canvas renderers. You must now implement 10. Fluent Emitters and Save Pipeline.

where are the custom renderers described? Don't they already exists in the code? are you describing some rendereres that are not yet existing and need to be implemented from scratch?

The custom canvas renderers are structurally described in the `NodeEditor_Extension_CustomCanvasRenderer.md` specification, while their subsystem-specific visual behaviors are defined in the BTree and HSM NodeEditor host designs.

They already exist in the codebase. You do not need to implement them from scratch. The previous step only outlined how to instantiate the existing classes and inject them into their respective editor host services during initialization.

The core extension interfaces (`ICustomCanvasRenderer`, `ICustomCanvasHitTester`, `ICustomCanvasSelectable`) are already defined and available in `NodeEditor.Core.Interfaces`.

The BTree-specific renderers are fully implemented in `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/`. This directory contains:

-   `BTreeBreakpointGutterRenderer`-   `BTreeRuntimeOverlayRenderer`-   `HeatmapOverlayRenderer`-   `ObserverGuardBadgeRenderer`-   `SubtreeBoundaryRenderer`-   `VariableBindingBadgeRenderer`

The HSM-specific renderers are fully implemented in `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/`. This directory contains:

-   `HsmBreakpointGutterRenderer`-   `HsmHeatmapRenderer`-   `HsmHistoryGlyphsRenderer`-   `HsmInitialArrowRenderer`-   `HsmRegionConflictsRenderer`-   `HsmRuntimeOverlayRenderer`-   `HsmTransitionLabelRenderer`

good so pls not let's move to detailing 10. Fluent Emitters and Save Pipeline

To implement the Fluent Emitters and Save Pipeline, you must construct the domain-specific emitters and plug them into the shared save orchestration. The emitters translate the in-memory editor models back into deterministic C# code, ensuring that byte-identical models yield byte-identical files across runs, machines, and .NET versions.

**1\. Shared Emitter Contract** Both emitters rely on shared deterministic rules defined by the infrastructure. You must enforce stable identifier ordering, 4-space indentation without tabs, and fixed line endings. Using directives must be sorted alphabetically, with `System.*` namespaces grouped at the top and separated by a single blank line,. All files must begin with the `// HROT_EDITOR_GENERATED` marker so tooling and developers recognize strict editor ownership.

**2\. BTree Fluent Emitter** You must instantiate `BTreeFluentEmitter`, implementing `IFluentCSharpEmitter<BehaviorTreeAsset>`. It produces three methods: `CreateBuilder()`, `Build()`, and `Layout()`. You must strictly enforce the following BTree-specific generation rules:

-   **Pill Emission:** When a node has decorator pills, they must be emitted "outside-in," meaning the outermost decorator pill becomes the outermost fluent call.-   **Lambda Structure:** Children of composites must use explicit lambda bodies (e.g., `seq => seq.Child(...)`), and lambda bodies must be indented exactly one level deeper than their parent.-   **Arguments:** The `visualId` must always be the last argument of a builder call, placed on its own line, while `Wait` durations emit with an `f` suffix and `Repeater` counts as `int` literals.-   **Layout:** Layout method entries must be sorted by Guid lexicographically. Pills receive entries with default positions, and optional metadata fields are only emitted when non-null.

**3\. HSM Fluent Emitter** You must instantiate `HsmFluentEmitter`, implementing `IFluentCSharpEmitter<HsmAsset>`. It produces `CreateBuilder()`, `Compile()`, and `Layout()`. You must strictly enforce the following HSM-specific generation rules:

-   **Top-Level Ordering:** Events emit first (ascending by EventId), followed by action/guard registrations (alphabetical by FQN), then states (depth-first from the root), and finally global transitions.-   **State Configuration Ordering:** Inside a state, method calls must follow a strict sequence: `Initial()` → `IsFinal()` → `Parallel()` → `OnEntry()` → `OnExit()` → `Activity()` → `TimerAction()` → `DeferEvent()`, followed by outgoing transitions and then child states or regions.-   **Regions and Transitions:** Region configuration must emit the region name and priority, followed by its initial child and subsequent children. Transitions use `.On(eventId)` for standard triggers or `.OnCompletion()` for completion triggers.

**4\. Regeneration and Save Orchestration** You must route your editor's save commands (and autosave triggers) through the shared regeneration pipeline. The flow executes as follows:

-   The active emitter (BTree or HSM) generates the complete C# string from the in-memory model.-   The pipeline performs a byte-for-byte comparison against the current on-disk content. If identical, it no-ops.-   If a delta exists, the pipeline writes the new content atomically by saving to a `{path}.tmp` file in the same directory, then invoking `File.Move` to overwrite the target. This prevents the engine's file watcher from compiling a partially written file.-   The in-memory model's dirty flag is cleared, and `IEditableAsset.Changed` is fired to update the UI.-   The engine's existing C# file watcher observes the change, triggers an MSBuild rebuild, and initiates the hot-reload classification process.

ok let's go one by one. pls detail 1. Shared Emitter Contract

The shared emitter contract enforces that given the same in-memory editor model, the output C# file must be byte-identical across runs, machines, and .NET versions. This deterministic property is what makes source-control diffs predictable and prevents infinite hot-reload loops.

The shared infrastructure provides the `IFluentCSharpEmitter<TAsset>` interface and `FluentCSharpEmitterBase` abstract class, which encapsulate the strict ownership markers, formatting rules, and atomic write operations. Subsystem-specific emitters inherit from this base.

**1\. Deterministic Output Rules** The emitter pipeline strictly enforces the following guarantees:

-   **Stable Ordering:** Nodes, states, and transitions must be emitted in the order of their `VisualId` or `StableId` via a sorted traversal.-   **Fixed Formatting:** Exactly 4 spaces per indentation level (no tabs). Line endings must match `Environment.NewLine` to align with the user's OS.-   **Stable Guids:** All GUIDs must be formatted as lowercase hex in the 8-4-4-4-12 pattern (`"D"` format).-   **No Ephemeral Tokens:** Timestamps or machine-specific tokens are forbidden in the generated output to prevent noise in source control diffs.

**2\. File Marker and Strict Ownership** All editor-owned files must begin with a specific 4-line header. The presence of this marker dictates whether the editor opens the file in full edit mode or read-only mode.

Implement this in `Hrot/Editor/Hrot.Editor.AiShared/Emit/FluentCSharpEmitterBase.cs`:

```
namespace Hrot.Editor.AiShared.Emit;

public abstract class FluentCSharpEmitterBase
{
    public const string EditorGeneratedMarker =
        "// HROT_EDITOR_GENERATED - manual edits to this file will be overwritten by the AI editor on next save.";

    public static string BuildHeader(Guid assetId)
    {
        return EditorGeneratedMarker + Environment.NewLine +
               "// AssetId: " + assetId.ToString("D") + Environment.NewLine;
    }

    // Subclasses implement the specific string generation
    protected abstract string EmitCore(IEditableAsset asset);
}
```

**3\. Using Directive Ordering Policy** Types not covered by a `using` directive must be emitted fully qualified. To minimize file size and improve readability, the emitter tracks the required namespaces and emits them deterministically: `System.*` namespaces are sorted alphabetically and grouped first, followed by a single blank line, then all other namespaces sorted alphabetically.

Implement the sorting logic in the base class:

    ```
    public static IReadOnlyList<string> SortUsings(IEnumerable<string> namespaces)
    {
        var all = namespaces.ToList();
        var system = all
            .Where(n => n == "System" || n.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        
        var other = all
            .Where(n => n != "System" && !n.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (system.Count == 0) return other;
        if (other.Count == 0) return system;

        var result = new List<string>(system.Count + 1 + other.Count);
        result.AddRange(system);
        result.Add(string.Empty); // Blank line separator
        result.AddRange(other);
        return result;
    }
```

**4\. Atomic Write Operation** To prevent the engine's C# file watcher from attempting to compile a partially written file, the base class must execute writes atomically.

    ```
    public static bool WriteAtomic(string filePath, string content)
    {
        if (File.Exists(filePath))
        {
            // If byte-for-byte identical, no-op to prevent spurious rebuilds
            string existing = File.ReadAllText(filePath);
            if (existing == content) return false;
        }

        string tmpPath = filePath + ".tmp";
        File.WriteAllText(tmpPath, content);
        // Atomic overwrite on same filesystem
        File.Move(tmpPath, filePath, overwrite: true); 
        return true;
    }
```

ok now pls detail 2. BTree Fluent Emitter

To implement the BTree Fluent Emitter, you must create `Hrot.BTree.Editor.Emit.BTreeFluentEmitter` implementing the shared `IFluentCSharpEmitter<BehaviorTreeAsset>` contract. This emitter produces a deterministic C# file containing three static methods: `CreateBuilder()`, `Build()`, and `Layout()`.

You must enforce the following BTree-specific generation rules during emission:

-   **Lambda Structure and Indentation:** Children of composites must use explicit lambda bodies (e.g., `seq => seq.Child(...)`), avoiding implicit block omission even for single children. Every lambda body must be indented exactly 4 spaces (one level) deeper than its parent. Empty composites are permitted but must emit a single-line empty lambda.-   **Visual ID Placement:** The `visualId` must always be the last argument of any builder call, emitted on its own line to minimize merge conflicts when tree structures change.-   **Decorator Pill Nesting:** When a host node has decorator pills, they must be emitted "outside-in". The outermost decorator pill becomes the outermost fluent call, matching the kernel's result-bubbling semantics. When closing the builder scopes, the pills must be closed innermost-first.-   **Data Literals:** `Wait` durations must always be emitted as floats with an `f` suffix (e.g., `1.5f`). `Repeater` counts must be emitted as `int` literals.-   **Action References:** Action method references must use their short names if covered by the deterministically computed `using` set, or fully qualified names otherwise.-   **Layout Method Determinism:** Entries within the `[BTreeLayout]` method must be sorted lexicographically by their Guid strings. Decorator pills receive layout entries with `default(Vector2)` for position, as their visual placement is derived from their host node. Optional fields like comments, size overrides, or expression targets are omitted entirely from the emit if they are null.

Below is the required structure for the emitter implementation:

```
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Emit;

namespace Hrot.BTree.Editor.Emit;

public sealed class BTreeFluentEmitter : IFluentCSharpEmitter<BehaviorTreeAsset>
{
    private const string Indent = "    ";

    public string Emit(BehaviorTreeAsset asset)
    {
        var sb = new StringBuilder();
        var usings = CollectUsings(asset); // Deterministically sort usings including System.* first

        sb.AppendLine(FluentCSharpEmitterBase.BuildHeader(asset.AssetId));
        
        foreach (var ns in usings)
        {
            if (ns.Length == 0) sb.AppendLine();
            else sb.AppendLine($"using {ns};");
        }
        sb.AppendLine();

        string targetNs = string.IsNullOrEmpty(asset.TargetNamespace) ? "Hrot.AI.Behaviors.Trees" : asset.TargetNamespace;
        string className = SanitizeIdentifier(asset.Name);

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");

        EmitCreateBuilder(sb, asset);
        sb.AppendLine();
        EmitBuild(sb, asset);
        sb.AppendLine();
        EmitLayout(sb, asset);

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitCreateBuilder(StringBuilder sb, BehaviorTreeAsset asset)
    {
        // Resolve short names for blackboard and context types
        var bbShort  = ShortTypeName(asset.BlackboardTypeName);
        var ctxShort = ShortTypeName(asset.ContextTypeName);

        sb.AppendLine($"{Indent}public static BTreeBuilder<{bbShort}, {ctxShort}> CreateBuilder() =>");
        sb.AppendLine($"{Indent}{Indent}new BTreeBuilder<{bbShort}, {ctxShort}>()");

        var root = asset.Nodes.FirstOrDefault(n => n.KernelType == NodeType.Root);
        if (root != null && root.ChildVisualIds.Count > 0)
        {
            var entryChild = asset.FindNode(root.ChildVisualIds);
            if (entryChild != null)
                EmitNode(sb, asset, entryChild, depth: 3, isLast: true);
            else
                sb.AppendLine($"{Indent}{Indent};");
        }
        else
        {
            sb.AppendLine($"{Indent}{Indent};");
        }
    }

    private static void EmitBuild(StringBuilder sb, BehaviorTreeAsset asset)
    {
        sb.AppendLine($"{Indent}[BTreeDefinition(\"{asset.Name}\", AssetId = \"{asset.AssetId:D}\")]");
        sb.AppendLine($"{Indent}public static BehaviorTreeBlob Build() =>");
        sb.AppendLine($"{Indent}{Indent}CreateBuilder().Compile(\"{asset.Name}\");");
    }

    private static void EmitLayout(StringBuilder sb, BehaviorTreeAsset asset)
    {
        sb.AppendLine($"{Indent}[BTreeLayout(\"{asset.AssetId:D}\")]");
        sb.AppendLine($"{Indent}public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()");
        sb.AppendLine($"{Indent}{Indent}.Canvas(panOffset: new Vector2({asset.CanvasPanOffset.X:R}f, {asset.CanvasPanOffset.Y:R}f), zoomLevel: {asset.CanvasZoomLevel:R}f)");

        // 1. Enforce lexicographic layout sorting
        var nodeEntries = asset.Nodes.OrderBy(n => n.VisualId.ToString("D"), StringComparer.Ordinal).ToList();
        var pillEntries = asset.Pills.OrderBy(p => p.VisualId.ToString("D"), StringComparer.Ordinal).ToList();

        foreach (var node in nodeEntries)
        {
            // 2. Emit layout metadata, omitting null optional fields
            string guidStr = $"\"{node.VisualId:D}\"";
            sb.AppendLine($"{Indent}{Indent}.Node({guidStr}, position: new Vector2({node.Position.X:R}f, {node.Position.Y:R}f))");
        }
        
        foreach (var pill in pillEntries)
        {
            string guidStr = $"\"{pill.VisualId:D}\"";
            sb.AppendLine($"{Indent}{Indent}.Node({guidStr})"); // Pills rely on default positions
        }

        sb.AppendLine($"{Indent}{Indent}.Build();");
    }
    
    // Additional recursive node generation methods (EmitComposite, EmitLeafWithPills) omitted for brevity.
}
```

good now please detail "3. HSM Fluent Emitter"

To implement the HSM Fluent Emitter, you must create `Hrot.Hsm.Editor.Emit.HsmFluentEmitter` implementing the shared `IFluentCSharpEmitter<HsmAsset>` contract. This emitter produces a deterministic C# file containing three static methods: `CreateBuilder()`, `Compile()`, and `Layout()`.

You must strictly enforce the following HSM-specific deterministic generation rules during emission:

-   **Top-Level Ordering**: Events must be emitted first, in ascending order of their `EventId`. This is followed by action and guard registrations, sorted alphabetically by their fully qualified names.-   **State Ordering**: States must be emitted in a depth-first traversal starting from the `RootState`'s children.-   **State Configuration Ordering**: Method calls configuring a specific state must follow a strict, stable sequence: `.Initial()` → `.History()` → `.DeepHistory()` → `.Parallel()` → `.Final()` → `.OnEntry()` → `.OnExit()` → `.Activity()` → `.TimerAction()` → `.DeferEvent()` (with deferred event IDs sorted in ascending order). Outgoing transitions and child states follow this configuration.-   **Transition Emission**: Transitions emit using `.On(eventId)` for normal triggers, or `.OnCompletion()` for completion triggers (when `eventId` is 0).-   **Global Transitions**: Global transitions must be emitted at the end of the builder sequence, sorted alphabetically by their `EventId`.-   **Data Literals**: Floats use an `f` suffix, `EventId` values are emitted as `ushort` literals without suffixes, and priorities are emitted as `byte` literals.-   **Layout Method Determinism**: Entries within the `[HsmLayout]` method must be sorted lexicographically by their `StableId` Guid string (formatted as `"D"`). Optional layout fields (size overrides, comments, colors) are omitted if null.

Below is the required structure for the emitter implementation:

```
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Emit;

public sealed class HsmFluentEmitter : IFluentCSharpEmitter<HsmAsset>
{
    private const string LayoutNamespace = "Hrot.Editor.AiShared.Layout";
    private const string Indent          = "    ";

    public string Emit(HsmAsset asset)
    {
        var sb = new StringBuilder();
        var usings = CollectUsings(asset); // Deterministically sort usings including System.* first

        sb.AppendLine(FluentCSharpEmitterBase.BuildHeader(asset.AssetId));

        foreach (var ns in usings)
        {
            if (ns.Length == 0) sb.AppendLine();
            else sb.AppendLine($"using {ns};");
        }
        sb.AppendLine();

        string targetNs = string.IsNullOrEmpty(asset.TargetNamespace) ? "Hrot.AI.Behaviors.Machines" : asset.TargetNamespace;
        string className = SanitizeIdentifier(asset.Name);

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");

        EmitCreateBuilder(sb, asset);
        sb.AppendLine();
        EmitCompile(sb, asset);
        sb.AppendLine();
        EmitLayout(sb, asset);

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitCreateBuilder(StringBuilder sb, HsmAsset asset)
    {
        sb.AppendLine($"{Indent}public static HsmBuilder CreateBuilder()");
        sb.AppendLine($"{Indent}{{");

        string pad = Indent + Indent;
        sb.AppendLine($"{pad}var builder = new HsmBuilder(\"{asset.Name}\");");

        // 1. Events sorted by EventId
        if (asset.AllEvents.Count > 0)
        {
            sb.AppendLine();
            foreach (var ev in asset.AllEvents.OrderBy(e => e.EventId))
            {
                sb.AppendLine($"{pad}builder.Event(\"{ev.Name}\", {ev.EventId}, {ev.PayloadSize}, {BoolStr(ev.IsIndirect)}, {BoolStr(ev.IsDeferrable)});");
            }
        }

        // 2. RegisterAction and RegisterGuard calls (alphabetical)
        var allActions = CollectActions(asset);
        if (allActions.Count > 0)
        {
            sb.AppendLine();
            foreach (var act in allActions)
                sb.AppendLine($"{pad}builder.RegisterAction(\"{act}\");");
        }

        // 3. States in depth-first order
        var userTopLevel = asset.RootState.Children;
        if (userTopLevel.Count > 0)
        {
            sb.AppendLine();
            foreach (var topState in userTopLevel)
                EmitTopLevelState(sb, topState, pad); // Recursively handles configuration and transitions
        }

        // 4. Global transitions sorted by EventId
        if (asset.AllGlobalTransitions.Count > 0)
        {
            sb.AppendLine();
            foreach (var gt in asset.AllGlobalTransitions.OrderBy(g => g.EventId))
            {
                string evRef = gt.EventName ?? gt.EventId.ToString(CultureInfo.InvariantCulture);
                string targetName = gt.Target?.Name ?? "???";
                sb.AppendLine($"{pad}builder.GlobalTransition(\"{evRef}\", \"{targetName}\", visualId: new Guid(\"{gt.VisualId:D}\"));");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"{pad}return builder;");
        sb.AppendLine($"{Indent}}}");
    }

    private static void EmitCompile(StringBuilder sb, HsmAsset asset)
    {
        sb.AppendLine($"{Indent}[HsmDefinition(\"{asset.Name}\", AssetId = \"{asset.AssetId:D}\")]");
        sb.AppendLine($"{Indent}public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();");
    }

    private static void EmitLayout(StringBuilder sb, HsmAsset asset)
    {
        sb.AppendLine($"{Indent}[HsmLayout(\"{asset.AssetId:D}\")]");
        sb.AppendLine($"{Indent}public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()");
        sb.AppendLine($"{Indent}{Indent}.Canvas(new Vector2({FormatFloat(asset.CanvasPanOffset.X)}, {FormatFloat(asset.CanvasPanOffset.Y)}), {FormatFloat(asset.CanvasZoomLevel)})");

        // Enforce lexicographic layout sorting by StableId
        foreach (var s in asset.AllStates.OrderBy(x => x.StableId.ToString("D"), StringComparer.Ordinal))
        {
            string guidStr = $"\"{s.StableId:D}\"";
            string pos = $"new Vector2({FormatFloat(s.Position.X)}, {FormatFloat(s.Position.Y)})";
            
            sb.Append($"{Indent}{Indent}.State({guidStr}, {pos}");
            if (s.SizeOverride.HasValue)
                sb.Append($", sizeOverride: new Vector2({FormatFloat(s.SizeOverride.Value.X)}, {FormatFloat(s.SizeOverride.Value.Y)})");
            if (!string.IsNullOrEmpty(s.Comment))
                sb.Append($", comment: \"{s.Comment}\"");
            if (s.Collapsed)
                sb.Append($", collapsed: true");
            
            sb.AppendLine(")");
        }

        // Layout definitions for Transitions and Regions follow similarly...
        
        sb.AppendLine($"{Indent}{Indent}.Build();");
    }
    
    private static string FormatFloat(float f) => f.ToString("R", CultureInfo.InvariantCulture) + "f";
    private static string BoolStr(bool b) => b ? "true" : "false";
}
```

ok now please detail "**4\. Regeneration and Save Orchestration** "

To implement the regeneration and save orchestration, you must route your editor's save commands and autosave triggers through the shared pipeline. This pipeline ensures that files are updated atomically, preventing the engine's file watcher from compiling partial writes, and relies entirely on the natural hot-reload cycle to update the editor state.

**1\. Generation and Byte-for-Byte Comparison** When the user clicks Save, or when the `RegenerationScheduler` debounces consecutive layout edits (typically after 500 ms of idle time), the active emitter generates the complete C# string from the in-memory model. The pipeline performs a strict byte-for-byte comparison against the existing on-disk content. If the contents are identical, the pipeline no-ops and aborts the file write to prevent spurious MSBuild rebuilds.

**2\. Atomic Write Operation** If a delta exists, the pipeline must write the new content atomically. It writes the generated C# to a temporary file (`{path}.tmp`) in the same directory, then invokes `File.Move` with `overwrite: true` to replace the target file. This guarantees that the engine's C# file watcher never observes a partially written file.

**3\. Editor State Update** Following a successful write, the in-memory model's dirty flag is cleared. The pipeline fires the `IEditableAsset.Changed` event, which notifies subscribed UI windows—such as the Asset Browser—to clear their "modified" indicators.

**4\. Pre-Classification and Build Coupling** The atomic write triggers the engine's existing C# file watcher, which initiates an MSBuild rebuild. While MSBuild runs, the editor pre-classifies the reload tier by comparing the in-memory asset's newly computed `StructureHash` and `ParamHash` against the previously loaded asset.

-   If `StructureHash` changed, it is classified as **Hard**.-   If only `ParamHash` changed, it is classified as **Soft**.-   If neither changed, it is classified as **Cosmetic**.

**5\. Post-Reload Editor Refresh** Once MSBuild completes the rebuild and loads the new assembly into the AssemblyLoadContext (ALC), the kernel swaps the runtime blobs in-place according to the classification tier. The editor then executes a post-reload refresh sequence with an author-perceived latency target of ≤ 100 ms:

-   The `IAssetCatalog` rebuilds, firing its `Changed` event, which prompts the `ReferenceCatalog` to rebuild its multi-index.-   The subsystem asset projection re-runs, reflecting the new assembly to read the updated blobs, debug metadata, and layout methods.-   The editor model reconciles against the new projection, mapping layout properties back to nodes using their `VisualId` or `StableId`.-   `IGraphModel.Changed` fires, causing the NodeEditor canvas to re-render.

**Architectural Boundaries** You must strictly enforce what the emitter and save orchestration pipeline do _not_ do:

-   They do not invoke the compiler directly; the file watcher and MSBuild handle compilation.-   They do not parse incoming C# source code; the editor reads compiled assemblies via reflection.-   They do not attempt to preserve user comments inside handwritten files, as strict ownership means the editor regenerates the entire file and relies exclusively on the `[Layout]` method for editor-only data.

ok how to implement these?

Implement the save orchestration pipeline by constructing the `RegenerationScheduler`, the pre-classification hook, and the post-reload reconciliation logic. This ensures that the engine's file watcher processes deterministic, atomic file updates and classifies them correctly before the assembly finishes compiling.

**1\. Regeneration Scheduler** The `RegenerationScheduler` debounces incoming layout and structural edits into a single file write. Because NodeEditor operations (such as dragging a node) emit dozens of `MoveNode` commands per second, you must debounce these to a 500 ms window to prevent locking the file system or triggering continuous MSBuild invocations.

Implement this in `Hrot/Editor/Hrot.Editor.AiShared/Emit/RegenerationScheduler.cs`:

```
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.Editor.AiShared.Emit;

public sealed class RegenerationScheduler : IDisposable
{
    private readonly TimeSpan _debounceWindow = TimeSpan.FromMilliseconds(500);
    private CancellationTokenSource? _cts;
    private readonly Action _saveAction;

    public RegenerationScheduler(Action saveAction)
    {
        _saveAction = saveAction;
    }

    public void Schedule()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Delay(_debounceWindow, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                // Execution must return to the main thread to safely read the editor model
                Fdp.Core.MainThreadDispatcher.Enqueue(() => _saveAction());
            }
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
```

**2\. Save Execution and Pre-Classification** Inside your subsystem's command sink (e.g., `BTreeCommandSink`), initialize the `RegenerationScheduler` and pass it the save callback. When the debounced action executes, it invokes the fluent emitter and applies the atomic write. If a file change occurs, the editor pre-classifies the reload tier by comparing the new in-memory hashes against the loaded runtime blob.

```
// Inside BTreeCommandSink.cs or BTreeEditorHostServices.cs

private void ExecuteSave()
{
    // 1. Generate the deterministic C# file content
    string newContent = _fluentEmitter.Emit(_asset);

    // 2. Perform byte-for-byte comparison and atomic write
    bool didWrite = FluentCSharpEmitterBase.WriteAtomic(_asset.SourceFilePath, newContent);

    if (didWrite)
    {
        _asset.IsDirty = false;
        _asset.FireChanged(); // Clears "modified" indicators in UI

        // 3. Pre-classify the hot-reload tier before MSBuild finishes
        // (In BTree, compile the builder in-memory to get the new hashes)
        var newBlob = _fluentEmitter.CompileToMemory(_asset); 
        
        var tier = HotReloadClassifier.Classify(
            _asset.Blob.StructureHash, newBlob.StructureHash,
            _asset.Blob.ParamHash, newBlob.ParamHash);

        // 4. Update the editor's status indicator pill
        _hotReloadStatus.SetPendingTier(tier, _asset.LiveInstanceCount);
    }
}
```

**3\. Hot Reload Classifier** The `BTreeQuickReloadHasher` (and its HSM equivalent) routes the extracted hashes to the shared `HotReloadClassifier`. The tier dictates whether the kernel will hot-patch lookup tables or hard-reset the active entities.

Ensure `HotReloadClassifier.cs` adheres to the strict fallback logic:

```
namespace Hrot.Editor.AiShared.HotReload;

public static class HotReloadClassifier
{
    public static HotReloadTier Classify(
        int previousStructureHash, int newStructureHash,
        int previousParamHash, int newParamHash)
    {
        if (previousStructureHash != newStructureHash) return HotReloadTier.Hard;
        if (previousParamHash != newParamHash) return HotReloadTier.Soft;
        return HotReloadTier.Cosmetic;
    }
}
```

**4\. Post-Reload Reconciliation** When MSBuild finishes and the `IAssetCatalog` detects the new assembly, the editor must refresh the canvas without destroying the user's ephemeral interaction state (such as selection or debug session tracking). You must re-project the new blob and reconcile properties using `VisualId` for BTree nodes or `StableId` for HSM states.

```
public void OnHotReloadCompleted(BehaviorTreeBlob newBlob, NodeDebugMetadata[] newMeta, BTreeEditorLayout newLayout)
{
    // 1. Project the new compiled definition into a fresh model
    var newAsset = BehaviorTreeAssetProjector.Project(newBlob, newMeta, newLayout, _asset.AssetId, _asset.Name, ...);

    // 2. Reconcile the existing model against the new projection
    foreach (var newNode in newAsset.Nodes)
    {
        var existingNode = _asset.FindNode(newNode.VisualId);
        if (existingNode != null)
        {
            // Update structural data derived from the compiler
            existingNode.KernelBlobIndex = newNode.KernelBlobIndex;
            existingNode.KernelType = newNode.KernelType;
            // Apply layout data reflected from the [Layout] method
            existingNode.Position = newNode.Position;
            existingNode.CustomComment = newNode.CustomComment;
        }
        else
        {
            _asset.AddNode(newNode);
        }
    }

    _asset.RemoveMissingNodes(newAsset.Nodes.Select(n => n.VisualId));

    // 3. Force NodeEditor to re-render the canvas
    _graphModel.FireChanged(GraphChangeKind.Wholesale);
}
```

is that all to "10. Fluent Emitters and Save Pipeline" or do we need to detail something more?

The BTree and HSM fluent emitters and their MSBuild-coupled save pipelines are fully detailed. However, to complete the overarching save and regeneration ecosystem, you must implement the Layout Discovery system that reads the emitted editor-only data back into memory, as well as the divergent dual-path save pipeline used by the Blueprint subsystem.

**1\. Layout Discovery and Reconciliation** The fluent emitters write editor-only data (such as canvas positions, comments, and collapse states) into a `[...Layout]` method. When a hot-reload completes and the asset projection layer re-runs, the editor must reflect this data back into the in-memory model.

You must implement a discovery helper, typically `LayoutDiscovery.TryGetLayout<TAttribute, TLayout>`, which reflects the newly loaded assembly to find the layout method matching the asset's `Guid`. Once the layout object is retrieved, the editor reconciles it against the compiled kernel blob.

-   For BTree, layout properties are mapped back to nodes using their `VisualId`.-   For HSM, layout properties are mapped to states and regions using their `StableId`. If a node exists in the builder but lacks a layout entry, it receives auto-layout positioning. If a layout entry exists for a node missing from the builder, it is silently dropped on the next save.

**2\. Blueprint Quick Reload (In-Memory)** Unlike BTree and HSM, which rely on writing C# to disk and waiting for the MSBuild file watcher, the Blueprint editor requires a distinct in-process compiler to meet its ≤ 100 ms latency target.

You must instantiate the `QuickReloadService` for the Blueprint editor. When triggered, it bypasses disk I/O entirely:

-   It runs the Blueprint compiler pipeline (Stages 1-7) to generate the C# source in memory.-   It invokes the `InMemoryRoslynCompiler` (Stage 8) to produce PE and PDB byte streams directly, embedding the source text into the PDB for debugger support,.-   It loads these streams into a new collectible `AssemblyLoadContext` (ALC).-   It explicitly calls `AiHotReloadCoordinator.ApplyQuickReload(alc, ...)` to execute the atomic registry swap without involving the file watcher,.

**3\. Blueprint Full Rebuild** Because Quick Reload does not write changes to disk, the Blueprint editor must also provide a `FullRebuildService` to commit structural changes to source control. When the user triggers a Full Rebuild, this service serializes the Blueprint's `.bp.json` asset to disk and programmatically invokes MSBuild, typically via `Process.Start("dotnet", "build ...")`. Once MSBuild completes, the engine's standard file watcher detects the updated DLL, debounces the event, and triggers the standard `AiHotReloadCoordinator` flow.

good pls detail **1\. Layout Discovery and Reconciliation**

The layout discovery and reconciliation system ensures that editor-only data—such as canvas positions, comments, and collapse states—survives hot reloads without relying on external `.layout.json` sidecar files. This data is emitted as a sibling method within the hot-reloadable assembly, decorated with an asset-specific attribute like `[BTreeLayout]`, `[HsmLayout]`, or `[BlueprintLayout]`.

**1\. Layout Discovery** At editor open or immediately following a hot-reload assembly swap, the asset projection layer must extract this data via reflection. You must use the shared `LayoutDiscovery.TryGetLayout<TAttr, TLayout>` helper to scan all public static methods in the loaded assembly.

The discovery matches the attribute's `AssetId` string to the target asset's Guid. Layout attributes are keyed exclusively by Guid, not by name, ensuring that renaming an asset does not break the layout binding.

```
// Example discovery during BTree asset projection
var layout = LayoutDiscovery.TryGetLayout<BTreeLayoutAttribute, BTreeEditorLayout>(
    assembly, assetId);
```

**2\. Reconciliation Rules** The compiled kernel builder method is strictly authoritative; the layout method provides only visual hints. When the editor reconciles the in-memory model against the newly discovered layout object, you must map the layout properties using the element's stable identifier (`VisualId` for BTree nodes, `StableId` for HSM states and regions).

You must enforce the following strict reconciliation matrix during projection:

-   **Node in builder + layout entry exists:** The node renders with the stored position, size overrides, and comments.-   **Node in builder + no layout entry:** The node is assigned an auto-layout position. The editor will save this computed position into a new layout entry upon the next user interaction.-   **No node in builder + stale layout entry exists:** The layout entry is completely ignored during projection and will be silently dropped from the C# file on the next save.-   **Layout method missing entirely:** The editor applies auto-layout to all nodes and will generate the `[...Layout]` method from scratch on the first save.

**3\. Auto-Layout Algorithms** When a layout method is missing or partial, the editor must execute domain-specific auto-layout algorithms to prevent nodes from stacking at the origin.

-   **BTree:** Executes a tidy-tree (Reingold-Tilford) algorithm.-   **HSM:** Executes a statechart auto-layout algorithm (top-level states left-to-right, children nested inside composites).-   **Blueprint:** Executes a force-directed layout algorithm.

**4\. Layout Data Payload** Layout entries store only non-semantic data. You must extract and apply the following fields to the editor model when present:

-   `Position` (Vector2)-   `Size override` (Vector2?) — null implies auto-size based on content.-   `Comment` (string?)-   `Collapsed` (bool) — used for composite states or Blueprint variable groups.-   `Color override` (string?) — an explicit color name.-   `Expression target` (string?) — used for BTree lambdas (e.g., `dto => dto.AmmoCount`).-   `Editor-only metadata` (Dictionary<string, string>?) — an escape hatch for subsystem-specific hints.

Breakpoints are strictly excluded from layout entries, as they are session-local to the user and must not be committed to source control.

and details for "2. Blueprint Quick Reload (In-Memory)"?

The Blueprint Quick Reload pipeline bypasses MSBuild and disk I/O to meet a ≤ 100 ms author-perceived latency target. It operates entirely on the main thread, incurring a predictable frame-rate dip rather than introducing the complexity of mid-frame asynchronous compile completions. In Slice 1, it processes only one asset at a time.

You must implement the `QuickReloadService` to orchestrate this flow through the following strict sequence:

**1\. Sibling Signature Resolution** Build a list of `BlueprintSignature` records by parsing sibling `.bp.json` assets directly from the file system via `BlueprintSignatureParser`. For assets currently open with unsaved changes, project the `BlueprintSignature` directly from the in-memory editor model instead of parsing from disk.

**2\. In-Process Compilation (Stages 1–7)** Execute the Blueprint compiler pipeline with `EmitPdbWithEmbeddedSource` set to true,. This generates the C# source code and the diagnostic debug map purely in memory,.

**3\. Roslyn Finalization (Stage 8)** Invoke the `InMemoryRoslynCompiler` to compile the generated C# string directly to PE and PDB byte arrays. You must embed the generated C# source text directly into the PDB using `EmbeddedText.FromSource` so that an attached .NET debugger can render the step-line indicator without requiring an on-disk `.cs` file,.

**4\. ALC Loading** Load the resulting PE and PDB memory streams into a new, unique collectible `AssemblyLoadContext`.

**5\. Staging and Dispatcher Clear** Before any registrars are invoked, you must explicitly call `HsmActionDispatcher.ClearAll()` to purge stale unmanaged function pointers. Instantiate fresh `BehaviorRegistry` and `BlueprintRegistryStaging` buffers. Reflect the loaded assembly for classes decorated with `[BlueprintRegistrar]` and invoke their `Register` or `RegisterAll` methods, injecting the staging buffers.

**6\. Debug Map Registration** Register the generated debug map with the active `IBlueprintDebugSession` before handing off to the coordinator. This guarantees that when the reload completes synchronously, the session already possesses the layout required to reconcile breakpoints.

**7\. Atomic Coordinator Handoff** Pass the ALC and staging buffers to `AiHotReloadCoordinator.ApplyQuickReload`. The coordinator performs the atomic `CommitStaging` swap, merges the staging behavior registry, swaps the ALC references, and fires the `OnReloadCompleted` event. If `ApplyQuickReload` throws an exception, you must unregister the debug map, unload the new ALC to prevent memory leaks, and propagate the failure.

please detail **3\. Blueprint Full Rebuild**

The Blueprint Full Rebuild pipeline is designed for permanent changes that must be committed to source control and distributed to other developers. Unlike Quick Reload, which optimizes for sub-second in-memory iteration, the Full Rebuild enforces disk persistence and performs comprehensive cross-asset validation. Because Quick Reload operates on a single asset, MSBuild is required to execute the Roslyn source generator over all `.bp.json` files to catch broken inter-asset links—such as stale `CallablePeers`—before a commit.

The pipeline executes through the following strict sequence orchestrated by the `FullRebuildService`:

-   **Serialization:** The in-memory editor models of the Blueprint assets are serialized as `.bp.json` files to disk.-   **Process Invocation:** The service programmatically triggers MSBuild using a standard `Process.Start("dotnet", "build ...")` call. For Slice 1, this subprocess is sufficient, though it may be upgraded to the programmatic `Microsoft.Build.Locator` API in Slice 2 to achieve sub-second rebuild times.-   **Output Parsing:** The service captures stdout and stderr, using regex on the standard MSBuild output format to extract errors and warnings, which are then surfaced directly into the editor's output console.-   **File Watcher Handoff:** Once MSBuild completes and overwrites `Hrot.AI.Behaviors.dll`, the editor's explicit involvement ends. The engine's standard file watcher detects the updated DLL and debounces the event.-   **Coordinator Application:** The `AiHotReloadCoordinator` processes the file watcher event, loads the new DLL into a fresh collectible `AssemblyLoadContext` on a background thread, and executes the atomic registry swap on the main thread.

To support workflows where an author iterates rapidly using Quick Reloads across multiple files, the editor provides a top-level "Save All & Rebuild" command. This serializes all dirty assets simultaneously and invokes a single MSBuild pass, acting as the final pre-commit validation.

is this quick reload and full rebuild already implemented?

Yes, both pipelines are already implemented in the codebase.

The `QuickReloadService` is implemented in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/QuickReloadService.cs`. It handles the in-memory compilation via the Roslyn compiler, loads the resulting PE and PDB bytes into a new collectible `AssemblyLoadContext`, clears the `HsmActionDispatcher`, invokes registrars into staging buffers, registers the debug map, and finally hands off to `AiHotReloadCoordinator.ApplyQuickReload`. For Slice 1, this entire Quick Reload flow executes synchronously on the main thread.

The `FullRebuildService` is implemented in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/FullRebuildService.cs`. It uses a `Process.Start("dotnet", args)` subprocess to programmatically invoke MSBuild, captures standard output and errors to parse into the editor's output console, and then lets the engine's standard file watcher handle the rest of the reload orchestration.

The automated test coverage for these pipelines is also implemented. The `QuickReloadServiceTests` class verifies the full pipeline from compilation and ALC loading to coordinator handoff.

is that now all to "**10\. Fluent Emitters and Save Pipeline** "?

Yes. The details provided cover the entirety of the Fluent Emitters and Save Pipeline architecture.

You now have the complete specifications for:

-   **The Shared Emitter Contract** (deterministic formatting, atomic writes, strict ownership markers, and namespace sorting).-   **The BTree Fluent Emitter** (pill nesting, explicit lambda indentation, and visual ID placement).-   **The HSM Fluent Emitter** (strict state configuration ordering, depth-first traversal, and global transition emission).-   **Regeneration and Save Orchestration** (debouncing via `RegenerationScheduler`, byte-for-byte comparison, and hot-reload pre-classification).-   **Layout Discovery and Reconciliation** (reflecting editor-only data back into the model and mapping properties via stable identifiers).-   **Blueprint Quick Reload** (in-memory compilation, ALC loading, and synchronous registry swapping for ≤ 100 ms latency).-   **Blueprint Full Rebuild** (disk serialization, MSBuild subprocess invocation, and cross-asset validation).

There are no further architectural components required for this section. The save and regeneration pipeline is fully defined.

good, lt's pls then detail the following: **11\. Comparison Sanitizers** To enable the LLM-based asset comparison and diffing features, you must wire the subsystem sanitizers. Inside your initialization flow, instantiate `BTreeComparisonSanitizer` and `HsmComparisonSanitizer` (passing the `IAssetCatalog` to both). Register these instances with the shared `SanitizerRegistry`. This strips canvas positions and presentation noise from the C# files so the refactor/diff tools evaluate pure semantic changes.

The comparison sanitizers prepare C# assets for LLM-based diffing by stripping presentation noise—such as canvas positions, pan/zoom state, and sub-window layouts—while preserving pure semantic intent. Both sanitizers implement the `IAssetComparisonSanitizer` interface and operate directly on the raw file text, avoiding runtime layout discovery or reflection overhead.

**1\. BTreeComparisonSanitizer** This class sanitizes BTree assets by locating the `[BTreeLayout(...)]` method block and parsing it for editor-only metadata: per-node comments, expression targets, and parameter sync bindings. To preserve this metadata for the LLM, the sanitizer walks the `CreateBuilder()` chain and injects these details as `//` comments directly above their respective fluent builder calls. Because BTree assets contain cross-asset GUID references via `.Subtree()` calls, the sanitizer uses the injected `IAssetCatalog` to resolve and humanize these GUIDs inline into readable asset names. Finally, the sanitizer completely truncates the `[BTreeLayout(...)]` block from the string, strips the manual edits suffix from the `HROT_EDITOR_GENERATED` header, and normalizes all line endings to `\n`.

**2\. HsmComparisonSanitizer** This class operates symmetrically on HSM assets and takes an `IAssetCatalog` for cross-asset consistency. It locates the `[HsmLayout(...)]` block, extracts per-element comment metadata for `.State`, `.Transition`, and `.Region` layout entries, and injects them as inline comments above the matching builder calls. It relies on the `stableId` for states and regions, and `visualId` for transitions and global transitions to correctly anchor the comments. Like the BTree sanitizer, it truncates the entire layout block and normalizes the source text.

**3\. Initialization and SanitizerRegistry Wiring** These sanitizers must be injected into the shared `SanitizerRegistry`, which maintains a dictionary mapping each `AssetKind` to its respective `IAssetComparisonSanitizer`.

During your composition root initialization, you must register these sanitizers as singletons after invoking `AddSharedAiEditor()`. You enforce this by calling the domain-specific DI extension methods:

-   For BTree, invoke `AddBTreeEditorComparison()`. This resolves the `IAssetCatalog`, constructs the `BTreeComparisonSanitizer`, and registers it into the `SanitizerRegistry` the first time it is resolved.-   For HSM, invoke `AddHsmEditorComparison()`. This performs the exact same registration pattern for the `HsmComparisonSanitizer`.

Once wired, the `ComparisonExportBuilder` queries the registry to process both Version A and Version B of an asset, formatting the stripped source codes into a combined prompt for LLM evaluation.

let's detail 1. BTreeComparisonSanitizer

`BTreeComparisonSanitizer` implements `IAssetComparisonSanitizer` for `AssetKind.BTree` to prepare C# files for LLM diffing by operating exclusively on raw file text, avoiding any reflection or runtime layout discovery overhead.

The core sanitization pipeline executes a strict sequence of string manipulation steps. The `SanitizeCore` method first normalizes all line endings to `\n` to ensure platform-independent processing. It then scans the file to locate the line index of the `[BTreeLayout(...)]` attribute.

Once the layout method is located, the `ParseLayoutSection` routine extracts editor-only metadata by parsing the layout body for `.Node(...)`, `.Pill(...)`, and `.SubtreeSyncField(...)` method calls. It accumulates this layout data into dictionaries tracking per-node comments, expression targets, and subtree synchronization bindings.

The `RebuildPreLayout` method then walks the pre-layout C# source, specifically targeting the `CreateBuilder()` chain. It uses a regular expression (`VisualIdPattern`) to detect `visualId: new Guid("...")` declarations. When a match is found, the sanitizer calculates the proper indentation and injects the previously extracted comments, expression targets, and sync bindings as `//` comments directly above the corresponding fluent builder call.

To ensure cross-asset references are readable by the LLM, the sanitizer also uses a regular expression (`SubtreeAssetGuidPattern`) to locate `.Subtree("assetGuid", ...)` calls. It queries the injected `IAssetCatalog` with the extracted GUID. If the asset is resolved, it appends an inline comment containing the human-readable asset name and kind, such as `// -> {asset.Name} ({asset.Kind})`. If unresolved, it appends `// -> (asset not found in catalog)`.

Following the injection pass, the sanitizer truncates everything from the `[BTreeLayout(...)]` attribute onward and appends a closing brace to seal the class scope. It processes the final text line-by-line to strip the `; manual edits...` suffix from the `// HROT_EDITOR_GENERATED` marker.

Finally, the sanitizer builds an `AssetMetadataBlock` by parsing the source for the `// AssetId:` comment and extracting the asset name from either the `[BTreeDefinition]` attribute or the class declaration. It returns a `SanitizationResult` containing the deterministic, LLM-ready text output and the structured metadata.

now please detail **2\. HsmComparisonSanitizer**

`HsmComparisonSanitizer` implements `IAssetComparisonSanitizer` for `AssetKind.Hsm` to prepare HSM C# files for LLM diffing. It operates exclusively on raw file text, avoiding reflection or runtime layout discovery overhead.

The core sanitization pipeline executes a strict sequence of string manipulations to extract editor-only metadata and inject it into the semantic definition block:

**1\. Normalization and Layout Discovery** The `SanitizeCore` method normalizes all line endings to `\n` to ensure platform-independent processing. It then scans the file to locate the line index of the `[HsmLayout(...)]` attribute.

**2\. Layout Section Parsing** The `ParseLayoutSection` method scans the layout method body for `.State`, `.Transition`, and `.Region` builder calls. It uses a regular expression (`LayoutElementGuidPattern`) to extract the GUID string of each element, and another (`CommentPattern`) to extract authored comments. It returns a dictionary mapping the normalized "D" format GUID to the extracted comment.

**3\. Pre-Layout Injection and Hoisting** The `RebuildPreLayout` method scans the pre-layout C# source for `stableId: new Guid(...)` (used by states and regions) and `visualId: new Guid(...)` (used by transitions and global transitions).

Because state definitions often use explicit, multi-line lambda bodies for configuration, the `stableId` argument frequently appears on the closing line (`}, stableId: ...`). To ensure comments are injected meaningfully above the logical start of the state definition, `FindCallStartForStableId` performs a backward scan from the `stableId` line. It tracks brace depth (`{` and `}`) to locate the opening `.Child(` or `.State(` call and hoists the `//` comment directly above it. Transitions emit their `visualId` inline with the start of their builder call, so their comments are hoisted on the same line.

**4\. Truncation and Header Cleanup** Following injection, the sanitizer truncates everything from the `[HsmLayout(...)]` attribute onward, appending a closing brace `}` to seal the class scope. It processes the final text line-by-line to strip the `; manual edits...` suffix from the `// HROT_EDITOR_GENERATED` file header.

**5\. Metadata Construction** The sanitizer builds an `AssetMetadataBlock` by parsing the source for the `// AssetId:` comment and extracting the asset name from the `[HsmDefinition("...")]` attribute or the class declaration.

**6\. DI Registration** You must wire this sanitizer into your composition root by invoking the `AddHsmEditorComparison` extension method, which resolves the `IAssetCatalog`, constructs the `HsmComparisonSanitizer`, and registers it into the shared `SanitizerRegistry` as a singleton.

Below is the required structure outlining the parsing and injection logic:

```
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Hsm.Editor.Comparison;

public sealed class HsmComparisonSanitizer : IAssetComparisonSanitizer
{
    private readonly IAssetCatalog _catalog;

    public HsmComparisonSanitizer(IAssetCatalog catalog)
    {
        _catalog = catalog;
    }

    public AssetKind TargetKind => AssetKind.Hsm;

    public SanitizationResult Sanitize(AssetExportRequest request)
    {
        try
        {
            return SanitizeCore(request);
        }
        catch (Exception ex)
        {
            string rawText = TryReadFile(request.AssetMainFilePath);
            return new SanitizationResult(
                rawText,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"Sanitization failed unexpectedly: {ex.Message}") });
        }
    }

    private SanitizationResult SanitizeCore(AssetExportRequest request)
    {
        string fileText = TryReadFile(request.AssetMainFilePath);
        string normalizedText = fileText.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalizedText.Split('\n');

        var warnings = new List<SanitizationWarning>();

        int layoutLineIndex = FindLayoutAttributeLineIndex(lines);
        if (layoutLineIndex < 0)
        {
            warnings.Add(new SanitizationWarning("Layout method not found; comments may be missing."));
            return new SanitizationResult(NormalizeEndings(fileText), BuildMetadata(request, lines), warnings);
        }

        // Parse layout body for per-element comment metadata
        Dictionary<string, string> elementComments = ParseLayoutSection(lines, layoutLineIndex, warnings);

        // Rebuild the pre-layout section with comments injected
        string sanitizedText = RebuildPreLayout(lines, layoutLineIndex, elementComments, warnings);

        return new SanitizationResult(sanitizedText, BuildMetadata(request, lines), warnings);
    }

    private static readonly Regex LayoutElementGuidPattern =
        new Regex(@"\.(?:State|Transition|Region)\(\s*""([0-9a-fA-F\-]{36})""", RegexOptions.Compiled);

    private static readonly Regex CommentPattern =
        new Regex(@"comment:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    private static Dictionary<string, string> ParseLayoutSection(string[] lines, int layoutLineIndex, List<SanitizationWarning> warnings)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = layoutLineIndex + 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith(".State(", StringComparison.Ordinal) ||
                trimmed.StartsWith(".Transition(", StringComparison.Ordinal) ||
                trimmed.StartsWith(".Region(", StringComparison.Ordinal))
            {
                string callText = CollectCallText(lines, i, out int endLine);
                var guidMatch = LayoutElementGuidPattern.Match(callText);
                
                if (guidMatch.Success && Guid.TryParse(guidMatch.Groups[14].Value, out var guid))
                {
                    var cm = CommentPattern.Match(callText);
                    if (cm.Success)
                    {
                        result[guid.ToString("D")] = cm.Groups[14].Value.Replace("\\\"", "\"");
                    }
                }
                i = endLine;
            }
            else if (trimmed.StartsWith(".Build(", StringComparison.Ordinal) || trimmed == "}")
            {
                break;
            }
        }
        return result;
    }

    private string RebuildPreLayout(string[] lines, int layoutLineIndex, Dictionary<string, string> elementComments, List<SanitizationWarning> warnings)
    {
        var insertionsBefore = new Dictionary<int, List<string>>();

        for (int i = 0; i < layoutLineIndex; i++)
        {
            string line = lines[i];

            // Match stableId: new Guid("...")
            var stableMatch = StableIdPattern.Match(line);
            if (stableMatch.Success && Guid.TryParse(stableMatch.Groups[14].Value, out var guid))
            {
                if (elementComments.TryGetValue(guid.ToString("D"), out string? comment))
                {
                    int callStart = FindCallStartForStableId(lines, i);
                    if (callStart >= 0)
                        AddCommentInsertion(insertionsBefore, callStart, GetLeadingSpaces(lines[callStart]), comment);
                }
            }

            // Match visualId: new Guid("...")
            var visualMatch = VisualIdPattern.Match(line);
            if (visualMatch.Success && Guid.TryParse(visualMatch.Groups[14].Value, out var vGuid))
            {
                if (elementComments.TryGetValue(vGuid.ToString("D"), out string? comment))
                {
                    AddCommentInsertion(insertionsBefore, i, GetLeadingSpaces(lines[i]), comment);
                }
            }
        }

        int lastNonBlank = layoutLineIndex - 1;
        while (lastNonBlank >= 0 && lines[lastNonBlank].Trim().Length == 0)
            lastNonBlank--;

        var sb = new StringBuilder();
        for (int i = 0; i <= lastNonBlank; i++)
        {
            if (insertionsBefore.TryGetValue(i, out var inserts))
            {
                foreach (var inserted in inserts)
                    sb.Append(inserted).Append('\n');
            }
            sb.Append(TransformLine(lines[i])).Append('\n');
        }

        sb.Append('}').Append('\n');
        return sb.ToString();
    }

    private static int FindCallStartForStableId(string[] lines, int stableIdLine)
    {
        string trimmed = lines[stableIdLine].TrimStart();
        if (!trimmed.StartsWith("}", StringComparison.Ordinal))
            return stableIdLine;

        int depth = 0;
        for (int i = stableIdLine; i >= 0; i--)
        {
            foreach (char c in lines[i])
            {
                if (c == '}') depth++;
                else if (c == '{') depth--;
            }

            if (depth <= 0) return i > 0 ? i - 1 : -1;
        }
        return -1;
    }
}
```

can you detail the BTreeComparisonSanitizer in such details

The `BTreeComparisonSanitizer` implements `IAssetComparisonSanitizer` for `AssetKind.BTree` to prepare BTree C# files for LLM diffing. It operates exclusively on raw file text, avoiding any reflection or runtime layout discovery overhead.

The core sanitization pipeline executes a strict sequence of string manipulations to extract editor-only metadata and inject it into the semantic definition block before truncating the layout data.

**1\. Normalization and Layout Discovery** The `SanitizeCore` method normalizes all line endings to `\n` to ensure platform-independent processing. It then scans the file to locate the line index of the `[BTreeLayout(...)]` attribute.

**2\. Layout Section Parsing** The `ParseLayoutSection` method scans the layout method body for `.Node(...)`, `.Pill(...)`, and `.SubtreeSyncField(...)` builder calls. It extracts per-node comments, expression targets, and subtree parameter synchronization bindings, returning dictionaries mapped by the visual GUID.

**3\. Pre-Layout Injection and Hoisting** The `RebuildPreLayout` method scans the pre-layout C# source, specifically targeting the `CreateBuilder()` chain. It uses a regular expression (`VisualIdPattern`) to detect `visualId: new Guid("...")` declarations. When a match is found, it calculates the proper indentation by scanning backward to find the start of the fluent call (`FindCallStartLine`), and hoists the previously extracted comments, expression targets, and sync bindings as `//` comments directly above it.

To ensure cross-asset references are readable by the LLM, it uses a second regular expression (`SubtreeAssetGuidPattern`) to locate `.Subtree("assetGuid", ...)` calls. It queries the injected `IAssetCatalog` and appends an inline comment containing the human-readable asset name and kind, or a "not found" fallback.

**4\. Truncation and Header Cleanup** Following the injection pass, the sanitizer truncates everything from the `[BTreeLayout(...)]` attribute onward and appends a closing brace to seal the class scope. It processes the final text line-by-line to strip the `; manual edits...` suffix from the `// HROT_EDITOR_GENERATED` file header.

Below is the required implementation structure:

```
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.BTree.Editor.Comparison;

public sealed class BTreeComparisonSanitizer : IAssetComparisonSanitizer
{
    private readonly IAssetCatalog _catalog;

    public BTreeComparisonSanitizer(IAssetCatalog catalog)
    {
        _catalog = catalog;
    }

    public AssetKind TargetKind => AssetKind.BTree;

    public SanitizationResult Sanitize(AssetExportRequest request)
    {
        try
        {
            return SanitizeCore(request);
        }
        catch (Exception ex)
        {
            string rawText = TryReadFile(request.AssetMainFilePath);
            return new SanitizationResult(
                rawText,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"Sanitization failed unexpectedly: {ex.Message}") });
        }
    }

    private SanitizationResult SanitizeCore(AssetExportRequest request)
    {
        string fileText = TryReadFile(request.AssetMainFilePath);
        string normalizedText = fileText.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalizedText.Split('\n');

        var warnings = new List<SanitizationWarning>();

        int layoutLineIndex = FindLayoutAttributeLineIndex(lines);
        if (layoutLineIndex < 0)
        {
            warnings.Add(new SanitizationWarning("Layout method not found; comments/sync may be missing."));
            return new SanitizationResult(NormalizeEndings(fileText), BuildMetadata(request, lines), warnings);
        }

        var (nodeMeta, syncBindings) = ParseLayoutSection(lines, layoutLineIndex, warnings);

        string sanitizedText = RebuildPreLayout(lines, layoutLineIndex, nodeMeta, syncBindings, warnings);

        return new SanitizationResult(sanitizedText, BuildMetadata(request, lines), warnings);
    }

    private static readonly Regex VisualIdPattern =
        new Regex(@"visualId:\s*new\s+Guid\(""([0-9a-fA-F\-]{36})""\)", RegexOptions.Compiled);

    private static readonly Regex SubtreeAssetGuidPattern =
        new Regex(@"\.Subtree\(""([0-9a-fA-F\-]{36})""", RegexOptions.Compiled);

    private string RebuildPreLayout(
        string[] lines,
        int layoutLineIndex,
        Dictionary<string, NodeMeta> nodeMeta,
        Dictionary<string, List<SyncBinding>> syncBindings,
        List<SanitizationWarning> warnings)
    {
        var insertionsBefore = new Dictionary<int, List<string>>();
        var lineSuffixes = new Dictionary<int, string>();

        for (int i = 0; i < layoutLineIndex; i++)
        {
            string line = lines[i];

            var vidMatch = VisualIdPattern.Match(line);
            if (vidMatch.Success && Guid.TryParse(vidMatch.Groups[12].Value, out var guid))
            {
                string key = guid.ToString("D");
                int visualIdIndent = GetLeadingSpaceCount(line);
                int callStart = FindCallStartLine(lines, i, visualIdIndent);
                if (callStart < 0) callStart = i;

                string indent = GetLeadingSpaces(lines[callStart]);
                var toInsert = new List<string>();

                if (nodeMeta.TryGetValue(key, out var meta))
                {
                    if (!string.IsNullOrEmpty(meta.ExpressionTarget))
                        toInsert.Add($"{indent}// target: {meta.ExpressionTarget}");
                    if (!string.IsNullOrEmpty(meta.Comment))
                        toInsert.Add($"{indent}// {meta.Comment}");
                }

                if (syncBindings.TryGetValue(key, out var bindings))
                {
                    foreach (var b in bindings)
                    {
                        string master = b.MasterVar ?? "(unmapped)";
                        if (b.SyncIn && b.SyncOut)
                            toInsert.Add($"{indent}// sync (both): {b.FieldName} <--> {master}");
                        else if (b.SyncIn)
                            toInsert.Add($"{indent}// sync (in): {b.FieldName} <-- {master}");
                        else if (b.SyncOut)
                            toInsert.Add($"{indent}// sync (out): {b.FieldName} --> {master}");
                    }
                }

                if (toInsert.Count > 0)
                {
                    if (!insertionsBefore.TryGetValue(callStart, out var existing))
                    {
                        existing = new List<string>();
                        insertionsBefore[callStart] = existing;
                    }
                    if (existing.Count == 0)
                        existing.AddRange(toInsert);
                }
            }

            var subMatch = SubtreeAssetGuidPattern.Match(line);
            if (subMatch.Success && Guid.TryParse(subMatch.Groups[12].Value, out var assetGuid))
            {
                var asset = _catalog.FindByAssetId(assetGuid);
                string suffix = asset != null
                    ? $"  // -> {asset.Name} ({asset.Kind})"
                    : "  // -> (asset not found in catalog)";
                lineSuffixes[i] = suffix;
            }
        }

        int lastNonBlank = layoutLineIndex - 1;
        while (lastNonBlank >= 0 && lines[lastNonBlank].Trim().Length == 0)
            lastNonBlank--;

        var sb = new StringBuilder();
        for (int i = 0; i <= lastNonBlank; i++)
        {
            if (insertionsBefore.TryGetValue(i, out var inserts))
            {
                foreach (var insertedLine in inserts)
                    sb.Append(insertedLine).Append('\n');
            }

            string line = TransformLine(lines[i]);
            if (lineSuffixes.TryGetValue(i, out var suffix))
                line = line + suffix;

            sb.Append(line).Append('\n');
        }

        sb.Append('}').Append('\n');
        return sb.ToString();
    }

    private static int FindCallStartLine(string[] lines, int fromLine, int visualIdIndent)
    {
        for (int i = fromLine - 1; i >= 0; i--)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0) continue; 

            int spaces = GetLeadingSpaceCount(lines[i]);
            if (spaces < visualIdIndent && trimmed.StartsWith(".", StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private static string TransformLine(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("// HROT_EDITOR_GENERATED", StringComparison.Ordinal))
        {
            int semiIdx = line.IndexOf("; ", StringComparison.Ordinal);
            if (semiIdx >= 0)
                return line[..semiIdx] + ".";
        }
        return line;
    }

    // Parsing methods (ParseLayoutSection, CollectCallText, ParseNodeCall) omitted for brevity.
}
```

now pls detail **3\. Initialization and SanitizerRegistry Wiring**

To wire the comparison sanitizers, you must configure the shared `SanitizerRegistry` and invoke the subsystem-specific Dependency Injection (DI) extension methods during application bootstrap.

The `SanitizerRegistry` is a singleton that maps an `AssetKind` to its respective `IAssetComparisonSanitizer`. When a comparison is initiated, the editor queries this registry to locate the correct sanitizer for the target asset.

**1\. Shared Infrastructure Registration** Your composition root must first call `AddSharedAiEditor()`. This extension method registers the `SanitizerRegistry`, the `ComparisonExportBuilder`, and the baseline `BlackboardComparisonSanitizer`. The factory pattern ensures the sanitizer is automatically injected into the registry the first time it is resolved.

**2\. Subsystem-Specific Extension Methods** Following the shared registration, you must invoke the extension methods for each AI subsystem your host supports. These methods construct the domain-specific sanitizers and bind them to the registry.

Implement and call the following extensions:

```
// For BTree
public static IServiceCollection AddBTreeEditorComparison(this IServiceCollection services)
{
    services.AddSingleton<BTreeComparisonSanitizer>(sp =>
    {
        var catalog   = sp.GetRequiredService<IAssetCatalog>();
        var sanitizer = new BTreeComparisonSanitizer(catalog);
        sp.GetRequiredService<SanitizerRegistry>().Register(sanitizer);
        return sanitizer;
    });
    return services;
}

// For HSM
public static IServiceCollection AddHsmEditorComparison(this IServiceCollection services)
{
    services.AddSingleton<HsmComparisonSanitizer>(sp =>
    {
        var catalog   = sp.GetRequiredService<IAssetCatalog>();
        var sanitizer = new HsmComparisonSanitizer(catalog);
        sp.GetRequiredService<SanitizerRegistry>().Register(sanitizer);
        return sanitizer;
    });
    return services;
}

// For Blueprint
public static IServiceCollection AddBlueprintEditorComparison(this IServiceCollection services)
{
    services.AddSingleton<BlueprintComparisonSanitizer>(sp =>
    {
        var migrationAdapter = sp.GetRequiredService<IComparisonMigrationAdapter>();
        var metaSanitizer    = sp.GetRequiredService<IMetaEnvelopeSanitizer>();
        var catalog          = sp.GetRequiredService<IAssetCatalog>();
        var sanitizer        = new BlueprintComparisonSanitizer(migrationAdapter, metaSanitizer, catalog);
        sp.GetRequiredService<SanitizerRegistry>().Register(sanitizer);
        return sanitizer;
    });
    return services;
}
```

**3\. Execution via ComparisonExportBuilder** Once the composition root is built, the `ComparisonExportBuilder` utilizes the registry to execute the comparison pipeline. When the user requests a diff, the builder extracts the correct sanitizer, processes both Version A and Version B of the asset, and assembles the final text payload. This payload prepends a fixed, detailed LLM instruction block enforcing the semantic evaluation rules, followed by the two sanitized C# or JSON representations.

is that all to sanitizers wiring?

The structural dependency injection wiring pattern is complete, but to fully cover the sanitization ecosystem, you must account for the three remaining domain-specific sanitizers that plug into the `SanitizerRegistry`.

**1\. BlackboardComparisonSanitizer (Shared Layer)** This sanitizer handles `AssetKind.Blackboard`. It operates on raw C# text by reading the inline `{Name}.Blackboard.cs` file and discovering the optional companion `{Name}.HeavyBlackboard.cs` file residing in the same directory. It emits both files as a labeled concatenation separated by `// === ... ===` section headers. Because XML `///` comments are already canonical in these structures, no comment hoisting is required. It is registered automatically when your composition root calls the `AddSharedAiEditor()` extension method.

**2\. BlueprintComparisonSanitizer (Blueprint Subsystem)** This sanitizer handles `AssetKind.Blueprint` for `.bp.json` files. Unlike the C# sanitizers, it operates on the JSON DOM. It executes a pipeline that first adapts the raw JSON to the current schema version via `IComparisonMigrationAdapter`. It then walks the DOM to strip `EditorMetadata` objects at the root, graph, and node levels—it hoists the `Comment` field but strips presentation keys like `X` and `Y`. It humanizes `CallPeerBlueprint` references by querying the `IAssetCatalog`, and finally re-serializes the JSON with alphabetically sorted keys at every level to ensure a deterministic diff. You wire this into the registry by calling the `AddBlueprintEditorComparison()` extension method.

**3\. UtilityComparisonSanitizer (Utility AI Subsystem)** This sanitizer handles `AssetKind.Utility` for Utility AI decision C# files. It normalizes line endings, strips the entire `[UtilityLayout]` method block out of the string, and cleans up the manual-edits suffix from the `// HROT_EDITOR_GENERATED` header line.

With these implementations injected, the `SanitizerRegistry` is fully equipped to route and process LLM-based diffing across all AI and visual scripting domains in the engine.

ok let's detail them one at a time

**1\. BlackboardComparisonSanitizer (Shared Layer)**

The `BlackboardComparisonSanitizer` implements `IAssetComparisonSanitizer` for `AssetKind.Blackboard` to prepare blackboard DTO definitions for LLM diffing. It is handled at the shared infrastructure layer and is registered automatically into the `SanitizerRegistry` when the composition root invokes `AddSharedAiEditor()`.

You must implement the sanitization pipeline to operate strictly on raw file text with the following sequence:

-   **File Discovery:** The sanitizer receives the path to the main inline file (e.g., `Foo.Blackboard.cs`). It derives the path to the optional heavy companion file by replacing the `.Blackboard.cs` suffix with `.HeavyBlackboard.cs` and checks for its existence in the same directory.-   **Concatenation and Labeling:** The sanitizer reads both files and emits them as a single concatenated string. To ensure the LLM correctly partitions the semantic context between the standard and heavy data, you must separate the file contents using explicit `// === ... ===` section headers.-   **Canonical Comments:** You do not need to implement metadata parsing or comment hoisting. The C# files generated by the blackboard emitter already place XML `///` doc-comments canonically above their respective fields, making the text naturally LLM-ready.

pls show me the implemnetation

```
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Comparison;

public sealed class BlackboardComparisonSanitizer : IAssetComparisonSanitizer
{
    public AssetKind TargetKind => AssetKind.Blackboard;

    public SanitizationResult Sanitize(AssetExportRequest request)
    {
        var warnings = new List<SanitizationWarning>();
        var companions = new List<string>();

        if (!File.Exists(request.AssetMainFilePath))
        {
            return new SanitizationResult(
                string.Empty,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"Main file not found: {request.AssetMainFilePath}") });
        }

        string mainContent = TryReadFile(request.AssetMainFilePath);
        string sanitizedText = $"// === {Path.GetFileName(request.AssetMainFilePath)} ===\n" + mainContent;

        string? heavyCompanionPath = DiscoverHeavyCompanion(request.AssetMainFilePath);
        if (heavyCompanionPath != null && File.Exists(heavyCompanionPath))
        {
            companions.Add(Path.GetFileName(heavyCompanionPath));
            string heavyContent = TryReadFile(heavyCompanionPath);
            sanitizedText += $"\n// === {Path.GetFileName(heavyCompanionPath)} ===\n" + heavyContent;
        }

        var metadata = new AssetMetadataBlock(
            Path.GetFileNameWithoutExtension(request.AssetMainFilePath),
            AssetKind.Blackboard,
            Guid.Empty,
            request.AssetMainFilePath,
            companions,
            TryGetLastWriteTime(request.AssetMainFilePath));

        return new SanitizationResult(NormalizeEndings(sanitizedText), metadata, warnings);
    }

    private static string? DiscoverHeavyCompanion(string mainFilePath)
    {
        const string BlackboardSuffix = ".Blackboard.cs";
        string fileName = Path.GetFileName(mainFilePath);
        
        if (!fileName.EndsWith(BlackboardSuffix, StringComparison.OrdinalIgnoreCase))
            return null;

        string baseName = fileName[..^BlackboardSuffix.Length];
        string dir = Path.GetDirectoryName(mainFilePath) ?? string.Empty;
        
        return Path.Combine(dir, baseName + ".HeavyBlackboard.cs");
    }

    private static AssetMetadataBlock BuildFallbackMetadata(AssetExportRequest request) =>
        new AssetMetadataBlock(
            Path.GetFileNameWithoutExtension(request.AssetMainFilePath),
            AssetKind.Blackboard,
            Guid.Empty,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath));

    private static string NormalizeEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string TryReadFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return string.Empty; }
    }

    private static DateTime? TryGetLastWriteTime(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch { return null; }
    }
}
```

This implementation enforces the shared layer rules for `AssetKind.Blackboard`. It discovers the optional heavy blackboard companion file based on the naming convention, concatenates both files with section headers, and handles file I/O safely. Because XML comments are already canonical for these generated structures, the sanitizer avoids unnecessary string hoisting and immediately normalizes the output text line endings.

Provide confirmation to proceed to the `BlueprintComparisonSanitizer` details.

now please 2. BlueprintComparisonSanitizer (Blueprint Subsystem)

The `BlueprintComparisonSanitizer` implements `IAssetComparisonSanitizer` for `AssetKind.Blueprint` to prepare `.bp.json` files for LLM diffing. Unlike the BTree and HSM sanitizers which operate on raw C# text, this sanitizer operates exclusively on the JSON Document Object Model (DOM).

You must implement the sanitization pipeline as a strict sequence of DOM manipulations:

**1\. Schema Migration** Before parsing the DOM, the raw JSON string is passed through `IComparisonMigrationAdapter.Adapt()`. This up-migrates the document to the current registered schema version, ensuring that Version A and Version B are compared at the same schema level regardless of which engine version originally saved them. If the schema was advanced, a migration notice is appended to the comparison metadata. _Note: Until the engine's migration system fully lands, you provide a_ _NoOpComparisonMigrationAdapter_ _fallback__._

**2\. DOM Parsing and Root Sanitization** The adapted JSON is parsed into a `JsonNode` (specifically a `JsonObject`). The sanitizer immediately removes the root-level `EditorMetadata` object entirely, as it contains no semantic behavioral data.

**3\. Graph and Node Metadata Hoisting** The sanitizer walks every graph and node to strip presentation noise while preserving authored intent:

-   **Graph-level:** It locates `EditorMetadata.CanvasComments`. It extracts only the `"Text"` field from each comment, hoists them into a new `_canvasComments` array on the graph, and deletes the `EditorMetadata` object to strip view states like `Viewport` and `DockState`.-   **Node-level:** It locates `EditorMetadata.Comment`. If present, it hoists the string into a new top-level `Comment` property on the node itself. The `EditorMetadata` object is then removed, securely stripping the `X` and `Y` canvas coordinates.

**4\. Peer Call Humanization** To ensure the LLM understands cross-asset composition, the sanitizer walks all nodes looking for `kind == "CallPeerBlueprint"`. It extracts the `PeerBlueprintId` GUID and queries the injected `IAssetCatalog`.

-   If the peer asset is found, it injects a `_targetName` field into the JSON node with the value `"{asset.Name} ({asset.Kind})"`.-   If unresolved, it injects `"(asset not found in catalog)"`.

**5\. Deterministic Sorting and Re-serialization** To guarantee a byte-identical diff for structurally identical assets, the DOM is passed through a recursive sorting algorithm. Every `JsonObject` has its keys ordered alphabetically (`StringComparer.Ordinal`). Finally, the sorted DOM is re-serialized to a string using `JsonSerializerOptions { WriteIndented = true }`.

**6\. DI Registration** You must wire this sanitizer into the composition root by invoking the `AddBlueprintEditorComparison()` extension method. This resolves the `IComparisonMigrationAdapter`, `IMetaEnvelopeSanitizer`, and `IAssetCatalog` from the service provider, constructs the `BlueprintComparisonSanitizer`, and registers it as a singleton into the shared `SanitizerRegistry`.

now pls implementation of it

```
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using AiCatalog = Hrot.Editor.AiShared.Catalog.IAssetCatalog;

namespace Hrot.Blueprints.Editor.Comparison;

/// <summary>
/// Sanitizes Blueprint asset JSON files (<c>.bp.json</c>) for LLM-based comparison [1].
/// Operates on the JSON DOM. Steps performed:
///   0. Migrate schema via <see cref="IComparisonMigrationAdapter"/>.
///   1. Parse the adapted JSON into a <see cref="JsonNode"/> DOM.
///   2/3. Walk all <c>EditorMetadata</c> objects at root, graph, and node levels:
///        node-level: hoist Comment, strip X/Y and all other keys;
///        graph-level: hoist CanvasComments (text-only), strip Viewport, DockState, NodeViewStates, and all other keys;
///        root-level: strip everything [1, 2].
///   4. Humanize <c>CallPeerBlueprint</c> nodes by looking up <c>PeerBlueprintId</c> in the catalog [2].
///   8. Re-serialize with alphabetically sorted keys at every level [2].
/// The <c>Header</c> object is preserved verbatim (it is structural, not diagnostic) [2].
/// </summary>
public sealed class BlueprintComparisonSanitizer : IAssetComparisonSanitizer
{
    private readonly IComparisonMigrationAdapter _migrationAdapter;
    private readonly IMetaEnvelopeSanitizer _metaSanitizer;
    private readonly AiCatalog _catalog;

    public BlueprintComparisonSanitizer(
        IComparisonMigrationAdapter migrationAdapter,
        IMetaEnvelopeSanitizer metaSanitizer,
        AiCatalog catalog)
    {
        _migrationAdapter = migrationAdapter;
        _metaSanitizer    = metaSanitizer;
        _catalog          = catalog;
    }

    public AssetKind TargetKind => AssetKind.Blueprint; [3]

    public SanitizationResult Sanitize(AssetExportRequest request)
    {
        try
        {
            return SanitizeCore(request); [3]
        }
        catch (Exception ex)
        {
            string rawText = TryReadFile(request.AssetMainFilePath);
            return new SanitizationResult(
                rawText,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"Sanitization failed unexpectedly: {ex.Message}") }); [3]
        }
    }

    private SanitizationResult SanitizeCore(AssetExportRequest request)
    {
        var warnings = new List<SanitizationWarning>();

        if (!File.Exists(request.AssetMainFilePath))
        {
            return new SanitizationResult(
                string.Empty,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"File not found: {request.AssetMainFilePath}") }); [3, 4]
        }

        string rawJson = File.ReadAllText(request.AssetMainFilePath); [4]

        // Step 0: migrate schema.
        string adaptedJson = _migrationAdapter.Adapt(rawJson, out bool didMigrate); [4]
        string? migrationNotice = didMigrate ? "Document was migrated to the current schema version." : null; [4]

        // Step 1: parse DOM.
        JsonNode? root = JsonNode.Parse(adaptedJson); [4]
        if (root is not JsonObject rootObj)
        {
            return new SanitizationResult(
                rawJson,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning("Blueprint JSON root is not an object.") }); [4]
        }

        // Steps 2/3: strip EditorMetadata at root, graph, and node levels.
        ProcessRootEditorMetadata(rootObj); [4]
        ProcessGraphs(rootObj); [4]

        // Step 4: humanize CallPeerBlueprint nodes.
        HumanizePeerCalls(rootObj); [4]

        // Step 5: Deterministic Sorting
        SortObjectKeysRecursively(rootObj);

        // Step 6: Reserialize
        var options = new JsonSerializerOptions { WriteIndented = true };
        string sanitizedText = rootObj.ToJsonString(options);
        sanitizedText = sanitizedText.Replace("\r\n", "\n").Replace("\r", "\n");

        var meta = BuildMetadata(request, rootObj, migrationNotice);
        return new SanitizationResult(sanitizedText, meta, warnings);
    }

    private void ProcessRootEditorMetadata(JsonObject root)
    {
        root.Remove("EditorMetadata");
    }

    private void ProcessGraphs(JsonObject root)
    {
        if (root["Graphs"] is not JsonArray graphs) return;

        foreach (var graphNode in graphs)
        {
            if (graphNode is not JsonObject graph) continue;

            // Graph-level metadata hoisting
            if (graph["EditorMetadata"] is JsonObject graphMeta)
            {
                if (graphMeta["CanvasComments"] is JsonArray comments && comments.Count > 0)
                {
                    var textComments = new JsonArray();
                    foreach (var c in comments)
                    {
                        if (c is JsonObject cObj && cObj.ContainsKey("Text"))
                        {
                            textComments.Add(cObj["Text"]?.GetValue<string>());
                        }
                    }
                    if (textComments.Count > 0)
                    {
                        graph["_canvasComments"] = textComments;
                    }
                }
            }
            graph.Remove("EditorMetadata");

            // Node-level metadata hoisting
            if (graph["Nodes"] is JsonArray nodes)
            {
                foreach (var n in nodes)
                {
                    if (n is not JsonObject nodeObj) continue;

                    if (nodeObj["EditorMetadata"] is JsonObject nodeMeta)
                    {
                        if (nodeMeta.ContainsKey("Comment"))
                        {
                            nodeObj["Comment"] = nodeMeta["Comment"]?.GetValue<string>();
                        }
                    }
                    nodeObj.Remove("EditorMetadata");
                }
            }
        }
    }

    private void HumanizePeerCalls(JsonObject root)
    {
        if (root["Graphs"] is not JsonArray graphs) return;

        foreach (var graphNode in graphs)
        {
            if (graphNode is not JsonObject graph) continue;
            if (graph["Nodes"] is not JsonArray nodes) continue;

            foreach (var n in nodes)
            {
                if (n is not JsonObject nodeObj) continue;

                string? kind = nodeObj["kind"]?.GetValue<string>();
                if (kind == "CallPeerBlueprint")
                {
                    string? peerIdStr = nodeObj["PeerBlueprintId"]?.GetValue<string>();
                    if (Guid.TryParse(peerIdStr, out Guid peerId))
                    {
                        var asset = _catalog.FindByAssetId(peerId);
                        if (asset != null)
                        {
                            nodeObj["_targetName"] = $"{asset.Name} ({asset.Kind})"; [5]
                        }
                        else
                        {
                            nodeObj["_targetName"] = "(asset not found in catalog)"; [6]
                        }
                    }
                }
            }
        }
    }

    private void SortObjectKeysRecursively(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var keys = obj.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList(); [7]
            var temp = new Dictionary<string, JsonNode?>();
            foreach (var k in keys)
            {
                var val = obj[k];
                obj.Remove(k);
                temp[k] = val;
            }
            foreach (var k in keys)
            {
                obj[k] = temp[k];
                if (obj[k] != null) SortObjectKeysRecursively(obj[k]!);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item != null) SortObjectKeysRecursively(item);
            }
        }
    }

    private AssetMetadataBlock BuildMetadata(AssetExportRequest request, JsonObject root, string? migrationNotice)
    {
        string name = root["Name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(request.AssetMainFilePath);
        Guid assetId = Guid.Empty;
        if (root.ContainsKey("AssetId") && Guid.TryParse(root["AssetId"]?.GetValue<string>(), out var parsed))
        {
            assetId = parsed;
        }

        return new AssetMetadataBlock(
            name,
            AssetKind.Blueprint,
            assetId,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath),
            migrationNotice
        );
    }

    private static AssetMetadataBlock BuildFallbackMetadata(AssetExportRequest request) =>
        new AssetMetadataBlock(
            Path.GetFileNameWithoutExtension(request.AssetMainFilePath),
            AssetKind.Blueprint,
            Guid.Empty,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath),
            null); [4]

    private static string TryReadFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return string.Empty; }
    } [4, 8]

    private static DateTime? TryGetLastWriteTime(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch { return null; }
    } [8]
}
```
```
using Microsoft.Extensions.DependencyInjection;
using Hrot.Editor.AiShared.Comparison;
using AiCatalog = Hrot.Editor.AiShared.Catalog.IAssetCatalog;

namespace Hrot.Blueprints.Editor.Comparison;

public static class BlueprintEditorComparisonServiceCollectionExtensions
{
    public static IServiceCollection AddBlueprintEditorComparison(this IServiceCollection services)
    {
        services.AddSingleton<BlueprintComparisonSanitizer>(sp =>
        {
            var migrationAdapter = sp.GetRequiredService<IComparisonMigrationAdapter>(); [9]
            var metaSanitizer    = sp.GetRequiredService<IMetaEnvelopeSanitizer>(); [9]
            var catalog          = sp.GetRequiredService<AiCatalog>(); [9]
            var sanitizer        = new BlueprintComparisonSanitizer(migrationAdapter, metaSanitizer, catalog); [9]
            sp.GetRequiredService<SanitizerRegistry>().Register(sanitizer); [9]
            return sanitizer; [9]
        }); [9]

        return services; [9]
    }
}
```

ok and **3\. UtilityComparisonSanitizer (Utility AI Subsystem)**?

The `UtilityComparisonSanitizer` implements the `IAssetComparisonSanitizer` interface for `AssetKind.Utility` to prepare Utility AI decision C# files for LLM-based comparison. Like the other C# sanitizers, it operates entirely on raw file text to avoid runtime layout discovery overhead.

The core sanitization pipeline executes the following sequence of string manipulations:

**1\. Normalization and Layout Stripping** The `SanitizeCore` method reads the file and normalizes all line endings to `\n`. It then passes the lines to `StripLayoutBlock`, which scans for the `[UtilityLayout]` method block. Unlike the BTree and HSM sanitizers, there is no complex metadata hoisting required; the layout block is simply stripped out entirely if present. If the block is absent, it proceeds without raising a warning.

**2\. Header Cleanup** The sanitizer iterates through the remaining lines to locate the `// HROT_EDITOR_GENERATED` marker. It cleans up this header by stripping the `; manual edits...` suffix, ensuring the LLM does not get distracted by manual-edit warnings.

**3\. Metadata Construction** The lines are rejoined into the final `sanitizedText` string. The sanitizer then parses the source to extract the `AssetId` and the asset's name to construct the `AssetMetadataBlock`.

Below is the required implementation structure:

```
using System;
using System.Collections.Generic;
using System.IO;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Utility.Editor.Comparison;

public sealed class UtilityComparisonSanitizer : IAssetComparisonSanitizer
{
    private const string GeneratedMarkerPrefix = "// HROT_EDITOR_GENERATED";
    private const string AssetIdPrefix         = "// AssetId:";
    private const string ClassPrefix           = "public sealed partial class ";

    public AssetKind TargetKind => AssetKind.Utility;

    public SanitizationResult Sanitize(AssetExportRequest request)
    {
        try
        {
            return SanitizeCore(request);
        }
        catch (Exception ex)
        {
            return new SanitizationResult(
                string.Empty,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"Sanitization failed unexpectedly: {ex.Message}") });
        }
    }

    private static SanitizationResult SanitizeCore(AssetExportRequest request)
    {
        var warnings = new List<SanitizationWarning>();

        if (!File.Exists(request.AssetMainFilePath))
        {
            return new SanitizationResult(
                string.Empty,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"File not found: {request.AssetMainFilePath}") });
        }

        string rawText        = File.ReadAllText(request.AssetMainFilePath);
        string normalizedText = NormalizeEndings(rawText);
        string[] lines        = normalizedText.Split('\n');

        lines = StripLayoutBlock(lines);

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(GeneratedMarkerPrefix, StringComparison.Ordinal))
            {
                lines[i] = GeneratedMarkerPrefix;
                break;
            }
        }

        string sanitizedText = string.Join("\n", lines);

        Guid   assetId   = ExtractAssetId(lines);
        string assetName = ExtractAssetName(lines);

        var metadata = new AssetMetadataBlock(
            assetName,
            AssetKind.Utility,
            assetId,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath));

        return new SanitizationResult(sanitizedText, metadata, warnings);
    }

    private static string[] StripLayoutBlock(string[] lines)
    {
        int startIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("[UtilityLayout]", StringComparison.Ordinal))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0)
            return lines; 

        int braceCount = 0;
        int endIndex = -1;
        
        // Find the matching closing brace for the layout method...
        // (Implementation details omitted for brevity)
        
        if (endIndex > startIndex)
        {
            var result = new string[lines.Length - (endIndex - startIndex + 1)];
            Array.Copy(lines, 0, result, 0, startIndex);
            Array.Copy(lines, endIndex + 1, result, startIndex, lines.Length - endIndex - 1);
            return result;
        }

        return lines;
    }

    // Extraction methods (ExtractAssetId, ExtractAssetName) omitted for brevity.
    
    private static AssetMetadataBlock BuildFallbackMetadata(AssetExportRequest request) =>
        new AssetMetadataBlock(
            "(unknown)",
            AssetKind.Utility,
            Guid.Empty,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath));

    private static string NormalizeEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static DateTime? TryGetLastWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return null; }
    }
}
```

is that all to the sanitizers?

Yes. That covers the entirety of the comparison sanitizers for the system.

The engine defines exactly five asset kinds in the `AssetKind` enum: `Blueprint`, `BTree`, `Hsm`, `Blackboard`, and `Utility`. The `SanitizerRegistry` maps each of these types to its respective `IAssetComparisonSanitizer` to prepare them for LLM-based diffing.

The complete set is:

-   `BTreeComparisonSanitizer` for `AssetKind.BTree`-   `HsmComparisonSanitizer` for `AssetKind.Hsm`-   `BlackboardComparisonSanitizer` for `AssetKind.Blackboard`-   `BlueprintComparisonSanitizer` for `AssetKind.Blueprint`-   `UtilityComparisonSanitizer` for `AssetKind.Utility`

The comparison sanitization ecosystem is fully detailed.

Is that all to the HSM and Btree integration to the editor?

The integration is not complete until you implement the `NodeEditor` host contracts, visual extensions, validation pipelines, and debug layers for both the BTree and HSM subsystems. While the save pipelines and comparison sanitizers handle data persistence and diffing, the actual editor authoring experience requires wiring the domain-specific models into the canvas.

**1\. NodeEditor Host Services and Graph Adapters** Each subsystem must provide its own implementation of `IEditorHostServices` to translate `NodeEditor` operations to the underlying asset models.

-   **BTree:** You must implement `BTreeGraphModel` and `BTreeCommandSink` to project the `BehaviorTreeAsset` and process mutations. The `BTreeNodeCatalog` provides the static node palette and dynamic actions/conditions fetched from the `BehaviorRegistry`. The `BTreeLinkValidator` enforces acyclic structures and rejects outgoing links from leaf nodes. To satisfy the standard many-to-one execution rule, BTree pin directions are reversed: children output to parent inputs.-   **HSM:** You must implement `HsmGraphModel` and `HsmCommandSink`. Transitions are modeled as pin-based `ILinkModel` objects bridging hidden input and output pins on the states. The `HsmNodeCatalog` provides state types, while the `HsmLinkValidator` blocks invalid topologies, such as transitions exiting a final state or targeting a history pseudo-state.

**2\. Visual Extensions** The standard node and link primitives are insufficient for AI behaviors. You must utilize three specific `NodeEditor` extensions to render domain-specific semantics:

-   **Node Attachments:** The BTree editor relies on this to collapse decorator wrappers (e.g., Inverter, Repeater, Cooldown) into stackable visual pills rendered above the host node. The HSM editor uses attachments to render state-flag badges.-   **Container Nodes:** The HSM editor uses containers for composite states and parallel regions. Composite states visually contain their children, and parallel containers utilize dashed dividers to separate orthogonal regions.-   **Custom Canvas Renderers:**
    -   For BTree, you must register `btree.observer_guard_badges` (drawing "OBSERVES" on links), `btree.subtree_boundaries`, `btree.runtime_overlay`, and `btree.heatmap_overlay`.-   For HSM, you must register `hsm.transition_labels` (drawing Event/Guard/Action at wire midpoints), `hsm.initial_state_arrows`, `hsm.region_conflicts`, `hsm.history_glyphs` (overriding the node body for H/H\* pseudo-states), and `hsm.runtime_overlay`.

**3\. Validation Pipelines** Each subsystem requires strict structural validation that runs on the model to surface diagnostics in the canvas and inspector.

-   **BTree Validation:** Must flag empty composites, unbound action or condition methods, unresolved subtree references, and orphaned nodes not reachable from the root.-   **HSM Validation:** Must flag composites missing an initial child, multiple initial children, or final states possessing outgoing transitions. Crucially, it must infer the `OutputLaneMask` for each state based on the lanes declared by its actions, and flag a warning if two states in different parallel regions of the same composite write to the same command lane.

**4\. Debug Sessions and Trace Timelines** To support the shared runtime inspector and trace timeline, you must wire the debug protocols.

-   **BTree:** `BTreeDebugSession` executes step controls and breakpoints. You must register `BTreeTraceLaneProvider` to project the trace buffers into swim-lanes for NodeStatus, Stack, Async, and Errors. The session also tracks aggregate entry counters to drive the canvas heatmap.-   **HSM:** `HsmDebugSession` manages instance snapshots and breakpoints. You must register `HsmTraceLaneProvider` to map the buffers to States, Events, Actions, Guards, Timers, and Conflicts lanes.

ok let's pls detail 1. NodeEditor Host Services and Graph Adapters

To integrate the BTree and HSM subsystems into the NodeEditor canvas, each subsystem must provide its own implementation of `IEditorHostServices` to translate the generic `NodeEditor` operations to the underlying `BehaviorTreeAsset` and `HsmAsset` models. Shared services such as the clipboard, input, icons, and theme are provided by the `Hrot.Editor.AiShared` infrastructure, meaning the subsystems only implement domain-specific graph adapters.

1\. BTree Host Services and Graph Adapters

The BTree editor projects a strict tree topology onto the canvas using the following components:

**BTreeGraphModel & BTreeTypeSystem** The BTree model effectively has no link-level data type system; children of a composite are an ordered list of structural sub-nodes rather than pin-routed data. Because `NodeEditor` enforces a "many outputs to one input" connection rule, the BTree editor reverses the standard pin direction: children possess one output pin that points upward to their parent's single input pin. Visually, the wires still flow top-to-bottom, but this reversed model safely satisfies the underlying graph constraints.

**BTreeNodeCatalog** The catalog populates the "Add Node" picker with two categories of nodes:

-   **Static Kinds:** Core composite and leaf types such as Sequence, Selector, Parallel, Observer Selector, and Wait. It also provides decorator pills (Inverter, Repeater, Cooldown, etc.) which act as attachments rather than standalone nodes.-   **Dynamic Kinds:** Action and condition nodes populated dynamically from the `BehaviorRegistry`, including Blueprint-hosted `AiPrimitives`.

**BTreeLinkValidator** This validator prevents invalid structural topologies. It enforces that a node has at most one parent, prevents the root node from having a parent, blocks outgoing edges from leaf nodes (Action, Condition, Wait, Subtree), and runs a local ancestor-chain walk to prevent cycle formation.

**BTreeCommandSink** The command sink translates NodeEditor `GraphCommand` records into in-place edits on the `BehaviorTreeAsset`. For example, `GraphCommand.MoveNodes` updates the position vector of the node, while pill operations translate to `AddAttachment` or `RemoveAttachments` on the asset's decorator collections. After applying mutations, the sink triggers the `RegenerationScheduler` to emit the C# file.

2\. HSM Host Services and Graph Adapters

The HSM editor projects statechart hierarchies and non-pin-based transitions using the following components:

**HsmGraphModel & HsmTypeSystem** HSM states do not possess typed pins. To bridge this with `NodeEditor`'s pin-based link primitives, every state node is assigned exactly one hidden output pin and one hidden input pin. Transitions are first-class models that connect the hidden output of a source state to the hidden input of a target state. Composite and parallel states implement `IContainerNodeModel` to allow visual containment of child states and orthogonal regions.

**HsmNodeCatalog** The HSM catalog provides static state entries: Simple State, Composite State, Parallel State, Final State, History, and Deep History. Transitions and regions are not treated as catalog nodes, as transitions are created by wire-dragging and regions are added via context commands on a parallel state. It also dynamically provides action and guard entries from the `HsmActionDispatcher` for use in the inspector.

**HsmLinkValidator** The HSM link validator enforces state-machine specific transition rules. While most state-to-state transitions are valid, the validator strictly rejects outgoing transitions from a Final state and incoming transitions targeted at History or Deep History pseudo-states.

**HsmCommandSink** The HSM command sink processes standard layout and addition commands, but critically handles container-specific `GraphCommand` operations. When a state is dragged across a parallel region boundary, the sink receives a `ChangeParent` command containing the new region index and updates the `HsmAsset` hierarchy. It also handles `AddRegion` and `RemoveRegion` commands when users modify parallel states.

let's detail 1. BTree Host Services and Graph Adapters

The BTree editor projects a strict tree topology onto the canvas by providing a domain-specific implementation of `IEditorHostServices`. You must implement the following components to adapt the `BehaviorTreeAsset` model to the NodeEditor constraints:

**1\. BTreeGraphModel and BTreeTypeSystem** The BTree model has effectively no link-level type system; edges represent a single implicit `exec` type. Because NodeEditor's connection rules strictly enforce a "many outputs to one input" fan-out rule, the BTree editor must use a reversed pin direction convention. Children possess a single output pin that points upward to their parent's single input pin. This safely models the "one parent to many children" relationship without violating the host's graph constraints. Visually, the wires still route top-to-bottom because NodeEditor routes based on physical pin positions, not logical pin directions.

**2\. BTreeNodeCatalog** You must implement the catalog to populate the "Add Node" picker with two distinct categories.

-   **Static Kinds:** Core composite types (Sequence, Selector, Observer Selector, Parallel) and leaf types (Action, Condition, Wait, Subtree). The catalog also includes decorators (Inverter, Repeater, Cooldown, etc.), which are flagged with a metadata tag so that selecting them adds an attachment pill to the currently selected node rather than spawning a new standalone node.-   **Dynamic Kinds:** Action and condition nodes populated dynamically from the `BehaviorRegistry` and the editor's reference catalog. This must include Blueprint-hosted `AiPrimitives` that declare `BTreeAction` or `BTreeCondition` hostings, grouped under a specific Blueprint category. The catalog must re-query after a hot-reload completes so that newly added registrations immediately appear.

**3\. BTreeLinkValidator** This component prevents invalid structural topologies and enforces cycle prevention. You must enforce the following strict rules:

-   A node can have at most one parent; adding a second incoming edge replaces the existing edge.-   The Root node cannot have a parent.-   Leaf nodes (Action, Condition, Wait, Subtree) cannot accept outgoing edges.-   Decorator pills do not participate in linking.-   Cycles are prevented by running a local ancestor-chain walk to ensure the source node is not already an ancestor of the target node.

**4\. BTreeCommandSink** The command sink translates generic NodeEditor `GraphCommand` records into in-place edits on the `BehaviorTreeAsset`. Standard layout commands update the position vectors of the nodes, while pill-handling commands are mapped to operations on the asset's decorator collections. Following any mutation, the command sink must trigger the `RegenerationScheduler`. This scheduler debounces multiple rapid commands (such as those generated during a drag operation) into a single C# file emission at the end of the interaction.

ok let's detail **1\. BTreeGraphModel and BTreeTypeSystem**

The `BTreeGraphModel` serves as the `IGraphModel` adapter that projects the mutable `BehaviorTreeAsset` into the NodeEditor canvas.

The `BTreeTypeSystem` provides a minimal implementation because Behavior Trees effectively lack a link-level data type system; the children of a composite are an ordered list of structural sub-nodes rather than pin-routed data. You must model BTree edges using a single implicit execution type, identified by the key `"bt.exec"`.

Because the NodeEditor strictly enforces a "many outputs to one input" connection rule for execution edges, you must implement a reversed pin direction convention to model the "one parent to many children" relationship without violating the host's graph constraints. Each child node possesses exactly one output pin that points logically upward to its parent's single input pin. The parent node has one input pin receiving connections from multiple children. Visually, the canvas still routes the wires from top-to-bottom because NodeEditor routes wires based on physical pin positions rather than logical pin directions.

The `BTreeTypeSystem` implementation must strictly enforce the following behaviors:

-   **Display Info:** Returns "execution" with the description "Tree edge" for the `"bt.exec"` key.-   **Pin Color:** Returns `Vector4.One` (white) for the implicit execution edge.-   **Pin Shape:** Returns `PinShape.Triangle`.-   **Compatibility:** Strictly requires `from == to`, ensuring only `"bt.exec"` to `"bt.exec"` connections are valid.-   **Editors and Casts:** Returns `null` for default editors and `false` for implicit casts, as BTree edges do not carry data payloads.

pls detail BTreeTypeSystem

The `BTreeTypeSystem` implements the `ITypeSystem` interface to adapt BTree connections to the `NodeEditor` host. Because Behavior Trees effectively have no link-level data type system—children of a composite are an ordered list of structural sub-nodes rather than pin-routed data—this implementation is minimal.

It models BTree edges using a single implicit execution type identified by the key `"bt.exec"`. To satisfy the host editor's strict "many outputs to one input" connection rule for execution edges, the system employs a reversed pin direction model. Each child node possesses one output pin that logically points upward to its parent's single input pin. The visual wires still route from top to bottom based on physical pin positioning, meaning the user sees the expected visual flow while the model complies with legal host constraints.

The implementation enforces the following strict behaviors:

-   **Display Info:** Yields "execution" with the description "Tree edge" for the `"bt.exec"` key.-   **Pin Characteristics:** Returns `Vector4.One` (white) for the color and `PinShape.Triangle` for the shape.-   **Compatibility and Casting:** Enforces strict compatibility (`from == to`), restricting connections to `"bt.exec"` to `"bt.exec"` only, and denies all implicit casts or default editors since BTree edges do not carry data payloads.

Below is the complete implementation of the type system:

```
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Minimal NodeEditor type system for the BTree host.
/// BTree edges carry a single implicit execution type; there is no data-flow.
/// </summary>
public sealed class BTreeTypeSystem : ITypeSystem
{
    /// <summary>The single type key used for all BTree tree-edges.</summary>
    public static readonly TypeKey ExecKey = new("bt.exec");

    public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
    {
        if (key == ExecKey)
        {
            info = new TypeDisplayInfo("execution", "Tree edge", null);
            return true;
        }
        info = default!;
        return false;
    }

    // White for the implicit exec edge.
    public Vector4 GetPinColor(TypeKey key) => Vector4.One;

    public PinShape GetPinShape(TypeKey key, ContainerKind container) => PinShape.Triangle;

    public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;

    // The only valid link is bt.exec -> bt.exec.
    public bool AreCompatible(TypeKey from, TypeKey to) => from == to;

    public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
}
```

now detail **2\. BTreeNodeCatalog**

The `BTreeNodeCatalog` implements the `INodeCatalog` interface to supply palette content for the BTree editor's "Add Node" picker and search popups. It populates the palette with both static structural primitives and dynamic user-authored behaviors.

**Static Node Kinds** The catalog registers core structural elements with specific pin configurations utilizing the reversed pin direction convention:

-   **Composites:** `Root`, `Sequence`, `Selector`, and `Parallel`.-   **Reactive Guards:** `Observer Selector` is placed in its own distinct category to communicate its reactive preemption semantics.-   **Leaves:** `Action`, `Condition`, `Wait`, and `Subtree`. These nodes only possess output pins to connect to parent composites and do not accept children.

**Decorator Pills** Decorators (`Inverter`, `Repeater`, `Cooldown`, `ForceSuccess`, `ForceFailure`, `UntilSuccess`, `UntilFailure`) are registered as static entries but bypass standard node creation. Selecting them from the palette does not spawn a standalone canvas node; instead, the catalog tags them with a metadata flag that restricts their drop-action to attaching directly onto the currently selected node as a visual pill.

**Dynamic Kinds** Action and condition nodes are populated dynamically by querying the `BehaviorRegistry` and the editor's cross-asset reference catalog. This integration explicitly includes Blueprint-hosted `AiPrimitives` that declare `BTreeAction` or `BTreeCondition` hostings, which are sorted into a dedicated "Blueprint-hosted actions" category in the UI.

**Hot-Reload Synchronization** To maintain parity with the underlying assembly during rapid iteration, the catalog subscribes to the `IAssetCatalog.Changed` event. When a hot reload completes, the catalog immediately re-queries the registries. This ensures that any newly authored C# delegates or new Blueprint assets appear in the node picker on the next invocation without requiring an editor restart.

**Contextual Pin Queries** The NodeEditor host uses `QueryForPinContext` when a user drags a wire onto empty canvas space. Because the BTree graph model relies on a single implicit execution type (`bt.exec`), the catalog's implementation simply filters out the decorator category and returns all other static and dynamic node entries, as any standard BTree node can legally connect to a composite's child edge.

how to implement the catalog

To implement the `BTreeNodeCatalog`, you must create a class that satisfies the `INodeCatalog` interface and provides the palette content for the BTree editor. The implementation manages static node kinds (composites, leaves, and decorator pills) and handles contextual pin queries based on the single implicit execution type.

Below is the implementation of the `BTreeNodeCatalog`. While this implementation provides the static entries, dynamic action and condition entries require `BehaviorRegistry` injection which is added in Slice 2.

```
using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Static node catalog for the BTree canvas.
/// Provides composite, leaf, and decorator palette entries.
/// Dynamic action/condition entries require BehaviorRegistry injection (added in Slice 2).
/// </summary>
public sealed class BTreeNodeCatalog : INodeCatalog
{
    // ---- Single exec pin signatures (reversed convention) ----
    // Output pin: used by children to connect to parent's input
    private static readonly PinSignature ExecOut =
        new("exec", PinKind.Exec, null, false);
    // Input pin: used by composites/root to receive from multiple children
    private static readonly PinSignature ExecIn =
        new("exec", PinKind.Exec, null, false);

    // ---- Category paths ----
    private const string CatComposite = "Composite";
    private const string CatLeaf      = "Leaf";
    private const string CatDecorator = "Decorator";
    private static readonly string CatReactiveGuard = ReactiveGuardVocabulary.CategoryName;

    // ---- Static entries ----
    private readonly IReadOnlyList<NodeCatalogEntry> _all;

    public BTreeNodeCatalog()
    {
        _all = BuildStaticEntries();
    }

    private static IReadOnlyList<NodeCatalogEntry> BuildStaticEntries()
    {
        var entries = new List<NodeCatalogEntry>();

        // Composites — both input and output pins.
        entries.Add(Make(BTreeKinds.Sequence, "Sequence", CatComposite,
            "Runs children left-to-right; fails on first failure.",
            new[] { "sequence", "and" }, "bt/sequence", false, false, false,
            inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Selector, "Selector", CatComposite,
            "Runs children left-to-right; succeeds on first success.",
            new[] { "selector", "or", "fallback" }, "bt/selector", false, false, false,
            inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.ObserverSelector, "Observer Selector", CatReactiveGuard,
            ReactiveGuardVocabulary.BTreeObserverSelectorTooltip + "\n\n" + ReactiveGuardVocabulary.CrossSubsystemHintBTree,
            new[] { "observer", "selector", "reactive", "guard" }, "bt/observer_selector", false, false, false,
            inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Parallel, "Parallel", CatComposite,
            "Runs all children simultaneously.",
            new[] { "parallel", "concurrent" }, "bt/parallel", false, false, false,
            inputs: new[] { ExecIn }, outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Root, "Root", CatComposite,
            "The root of the behavior tree.",
            new[] { "root", "entry" }, "bt/root", false, false, false,
            inputs: new[] { ExecIn }, outputs: Array.Empty<PinSignature>()));

        // Leaves — output only.
        entries.Add(Make(BTreeKinds.Action, "Action", CatLeaf,
            "Runs a user-defined action delegate.",
            new[] { "action", "do", "execute" }, "bt/action", false, false, false,
            inputs: Array.Empty<PinSignature>(), outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Condition, "Condition", CatLeaf,
            "Evaluates a user-defined condition delegate.",
            new[] { "condition", "check", "test" }, "bt/condition", true, false, false,
            inputs: Array.Empty<PinSignature>(), outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Wait, "Wait", CatLeaf,
            "Waits for a fixed duration in seconds.",
            new[] { "wait", "delay", "sleep" }, "bt/wait", false, false, false,
            inputs: Array.Empty<PinSignature>(), outputs: new[] { ExecOut }));

        entries.Add(Make(BTreeKinds.Subtree, "Subtree", CatLeaf,
            "Calls another behavior tree asset.",
            new[] { "subtree", "call", "reference" }, "bt/subtree", false, false, false,
            inputs: Array.Empty<PinSignature>(), outputs: new[] { ExecOut }));

        // Decorator pills — no pins; palette action is AttachToSelected.
        entries.Add(MakeDecorator(BTreeKinds.Inverter,    "Inverter",      "Inverts the result of its child."));
        entries.Add(MakeDecorator(BTreeKinds.Repeater,    "Repeater",      "Repeats the child N times."));
        entries.Add(MakeDecorator(BTreeKinds.Cooldown,    "Cooldown",      "Blocks the child for a cooldown period after it runs."));
        entries.Add(MakeDecorator(BTreeKinds.ForceSuccess,"ForceSuccess",  "Forces child result to Success."));
        entries.Add(MakeDecorator(BTreeKinds.ForceFailure,"ForceFailure",  "Forces child result to Failure."));
        entries.Add(MakeDecorator(BTreeKinds.UntilSuccess,"UntilSuccess",  "Repeats until child succeeds."));
        entries.Add(MakeDecorator(BTreeKinds.UntilFailure,"UntilFailure",  "Repeats until child fails."));

        return entries.AsReadOnly();
    }

    private static NodeCatalogEntry Make(
        string kindId, string name, string cat, string? desc,
        IReadOnlyList<string> keywords, string? iconKey,
        bool isPure, bool isLatent, bool isDeprecated,
        IReadOnlyList<PinSignature> inputs,
        IReadOnlyList<PinSignature> outputs) =>
        new(
            new NodeKindKey(kindId), name, desc, cat,
            keywords, iconKey, isPure, isLatent, isDeprecated,
            inputs, outputs);

    private static NodeCatalogEntry MakeDecorator(string kindId, string name, string? desc) =>
        new(
            new NodeKindKey(kindId), name, desc, CatDecorator,
            new[] { name.ToLowerInvariant(), "decorator", "pill" },
            "bt/decorator",
            false, false, false,
            Array.Empty<PinSignature>(),
            Array.Empty<PinSignature>());

    // ---- INodeCatalog ----

    public IReadOnlyList<NodeCatalogEntry> All => _all;

    public IReadOnlyList<NodeCategoryDescriptor> Categories { get; } = new[]
    {
        new NodeCategoryDescriptor(CatComposite, "Composites", "bt/composite"),
        new NodeCategoryDescriptor(CatLeaf,      "Leaves",     "bt/leaf"),
        new NodeCategoryDescriptor(CatDecorator, "Decorators", "bt/decorator"),
        new NodeCategoryDescriptor(CatReactiveGuard, ReactiveGuardVocabulary.CategoryName, null),
    };

    public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q)
    {
        var text = q.Text;
        var results = _all.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(q.CategoryFilter))
            results = results.Where(e => e.CategoryPath == q.CategoryFilter);

        if (!q.IncludeDeprecated)
            results = results.Where(e => !e.IsDeprecated);

        if (!string.IsNullOrWhiteSpace(text))
        {
            var lower = text.ToLowerInvariant();
            results = results.Where(e =>
                e.DisplayName.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                e.Keywords.Any(k => k.Contains(lower, StringComparison.OrdinalIgnoreCase)) ||
                (e.Description?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return results.ToList();
    }

    public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q)
    {
        // All BTree nodes share the single exec type; any non-decorator entry is compatible.
        var results = _all.Where(e => e.CategoryPath != CatDecorator);

        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            var lower = q.Text.ToLowerInvariant();
            results = results.Where(e =>
                e.DisplayName.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                e.Keywords.Any(k => k.Contains(lower, StringComparison.OrdinalIgnoreCase)));
        }

        return results.ToList();
    }
}
```

The `QueryForPinContext` method intentionally filters out `CatDecorator` to ensure decorators are correctly restricted to attachment behavior rather than standalone graph placement during wire-dragging. When dynamic actions are injected via the `BehaviorRegistry` and cross-asset references in Slice 2, they will be appended to the output lists of `Query` and `QueryForPinContext`, tracking `IAssetCatalog.Changed` to stay synchronized after hot-reloads.

pls detail 3. BTreeLinkValidator

The `BTreeLinkValidator` implements `ILinkValidator` to prevent invalid structural topologies and enforce cycle prevention within the Behavior Tree canvas.

It operates under the BTree editor's reversed pin direction convention. To satisfy the `NodeEditor` host's strict "many-to-one" connection rule for execution edges, each child node possesses an output pin that connects to its parent's single input pin. The validator evaluates these links by treating the target pin's node (`toNode`) as the hierarchical parent and the source pin's node (`fromNode`) as the child.

The `Validate` method enforces the following strict structural constraints:

-   **Self-Parenting:** A node cannot be its own parent (`fromNode.Id == toNode.Id`).-   **Leaf Rejection:** Leaf nodes (Action, Condition, Wait, Subtree) cannot accept incoming child edges; attempting to connect to a leaf yields an invalid result.-   **Decorator Exclusion:** Decorator pills are attachments, not standard nodes, and do not participate in link routing.-   **Single Parent Rule:** A node can have at most one parent; adding a second incoming edge replaces the existing edge entirely.-   **Cycle Prevention:** A connection must not create an ancestor cycle.

To prevent cyclic topologies, the `WouldCreateCycle` routine executes a local ancestor-chain traversal. It walks upward from the `toNode` to verify that the `fromNode` is not already an ancestor. It tracks visited nodes in a hash set to guard against infinite loops caused by pre-existing corrupt graph data.

Because of the reversed pin convention, the `FindParent` helper resolves a node's parent by locating the specific link where the node's output pin serves as the link's source, mapping it back to the target pin's owning node.

Below is the implementation structure for the `BTreeLinkValidator`:

```
using System.Collections.Generic;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Link validator for the BTree canvas.
/// Enforces acyclic structures and reversed pin direction rules.
/// </summary>
public sealed class BTreeLinkValidator : ILinkValidator
{
    private readonly IGraphModel _graph;

    public BTreeLinkValidator(IGraphModel graph)
    {
        _graph = graph;
    }

    public LinkValidationResult Validate(PinId from, PinId to)
    {
        var fromPin = _graph.FindPin(from);
        var toPin   = _graph.FindPin(to);
        if (fromPin == null || toPin == null)
            return Invalid("Pin not found.");

        var fromNode = _graph.FindNode(fromPin.OwnerNodeId);
        var toNode   = _graph.FindNode(toPin.OwnerNodeId);
        if (fromNode == null || toNode == null)
            return Invalid("Node not found.");

        // toNode is the "parent" in the reversed convention.
        if (BTreeKinds.IsLeaf(toNode.Kind))
            return Invalid("Leaf nodes cannot have children.");

        if (fromNode.Id == toNode.Id)
            return Invalid("A node cannot be its own parent.");

        if (WouldCreateCycle(fromNode, toNode))
            return Invalid("Would create a cycle.");

        return new LinkValidationResult(LinkValidity.Valid, null, false, null);
    }

    private bool WouldCreateCycle(INodeModel fromNode, INodeModel toNode)
    {
        var visited = new HashSet<NodeId>();
        var current = toNode;
        
        while (true)
        {
            if (!visited.Add(current.Id)) break; // guard against any existing cycles
            var parentId = FindParent(current.Id);
            if (parentId == null) break;
            if (parentId == fromNode.Id) return true;
            
            var parentNode = _graph.FindNode(parentId.Value);
            if (parentNode == null) break;
            current = parentNode;
        }
        return false;
    }

    private NodeId? FindParent(NodeId nodeId)
    {
        foreach (var link in _graph.Links)
        {
            var fromPin = _graph.FindPin(link.FromPin);
            if (fromPin != null && fromPin.OwnerNodeId == nodeId)
            {
                var toPin = _graph.FindPin(link.ToPin);
                return toPin?.OwnerNodeId;
            }
        }
        return null;
    }

    private static LinkValidationResult Invalid(string reason) =>
        new(LinkValidity.Invalid, reason, false, null);
}
```

pls detail **4\. BTreeCommandSink**

The `BTreeCommandSink` implements the `IGraphCommandSink` interface to translate generic `NodeEditor` `GraphCommand` records into domain-specific, in-place mutations on the `BehaviorTreeAsset`. After applying mutations, the sink marks the asset as dirty, which triggers a `Changed` event that propagates to `NodeEditor` as a `GraphChangeNotification` for canvas re-rendering.

Because BTree execution flows top-to-bottom but the host strictly enforces a "many-to-one" connection rule for execution edges, the command sink interprets links using a reversed-pin convention: the `From` pin represents the child node's output, and the `To` pin represents the parent node's input.

The command sink executes the following mappings:

-   **Layout Commands:** `MoveNodes` updates the position vectors of the specified editor nodes.-   **Node Commands:** `AddNode` resolves the `NodeKindKey` to its underlying `NodeType`, mints a new `BTreeEditorNode` with `KernelBlobIndex = -1`, and adds it to the asset. `RemoveNodes` removes the nodes from the asset.-   **Link Commands:** `AddLink` looks up the source (child) and target (parent) nodes and adds the child's `VisualId` to the parent's `ChildVisualIds` list. `RemoveLinks` performs the inverse operation using a cached dictionary of link associations.-   **Property Commands:** `SetNodeProperty` updates editor-only metadata like comments and session-local breakpoint flags.-   **Decorator Pill Commands:** Operations like `AddAttachment`, `RemoveAttachments`, `SetAttachmentProperty`, and `ReorderAttachments` are mapped directly to manipulations of the `BTreeEditorPill` objects stored in the asset's decorator collections. When adding a pill, the sink extracts host properties such as `decoratorType`, `intParam`, and `floatParam`.

Below is the implementation structure of the `BTreeCommandSink`:

````
using System;
using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

internal sealed class BTreeCommandSink : IGraphCommandSink
{
    private readonly BehaviorTreeAsset _asset;
    private readonly IGraphModel       _graph;

    // Maps link Guid -> (childVisualId, parentVisualId) for RemoveLinks lookup.
    private readonly Dictionary<Guid, (Guid child, Guid parent)> _links = new();

    internal BTreeCommandSink(BehaviorTreeAsset asset, IGraphModel graph)
    {
        _asset = asset;
        _graph = graph;
    }

    public GraphCommandResult Apply(GraphCommand command)
    {
        switch (command)
        {
            case GraphCommand.MoveNodes m:
                ApplyNodeMoves(m.Moves);
                break;
            case GraphCommand.AddNode add:
                ApplyAddNode(add);
                break;
            case GraphCommand.RemoveNodes rem:
                ApplyRemoveNodes(rem.Nodes);
                break;
            case GraphCommand.AddLink link:
                ApplyAddLink(link.AssignedId, link.From, link.To);
                break;
            case GraphCommand.RemoveLinks unlink:
                ApplyRemoveLinks(unlink.Links);
                break;
            case GraphCommand.SetNodeProperty setProp:
                ApplySetNodeProperty(setProp.Node, setProp.Key, setProp.Value);
                break;
            case GraphCommand.AddAttachment att:
                ApplyAddPill(att);
                break;
            case GraphCommand.RemoveAttachments remAtt:
                ApplyRemovePills(remAtt.AttachmentIds);
                break;
            case GraphCommand.SetAttachmentProperty setAtt:
                ApplySetPillProperty(setAtt.Id, setAtt.Key, setAtt.Value);
                break;
            case GraphCommand.ReorderAttachments reorder:
                ApplyReorderPills(reorder.HostNodeId, reorder.NewOrder);
                break;
            case GraphCommand.Batch batch:
                foreach (var sub in batch.Commands)
                    Apply(sub);
                break;
            default:
                return new GraphCommandResult(false, $"Unsupported: {command.GetType().Name}");
        }

        return new GraphCommandResult(true, null);
    }

    private void ApplyNodeMoves(IReadOnlyList<NodeMove> moves)
    {
        foreach (var m in moves)
        {
            var node = _asset.FindNode(m.Node.Value);
            if (node != null)
                node.Position = m.NewPosition;
        }
        _asset.MarkDirty();
    }

    private void ApplyAddNode(GraphCommand.AddNode add)
    {
        var nodeType = BTreeKinds.KindIdToNodeType(add.Kind.Id);
        var node = new BTreeEditorNode
        {
            VisualId        = add.AssignedId.Value,
            KernelType      = nodeType,
            KernelBlobIndex = -1,
            Position        = add.Position,
            DisplayLabel    = add.Kind.Id,
        };
        _asset.AddNode(node);
        _asset.MarkDirty();
    }

    private void ApplyRemoveNodes(IReadOnlyList<NodeId> nodeIds)
    {
        foreach (var id in nodeIds)
            _asset.RemoveNode(id.Value);
        _asset.MarkDirty();
    }

    private void ApplyAddLink(LinkId linkId, PinId from, PinId to)
    {
        var fromPin = _graph.FindPin(from);
        var toPin   = _graph.FindPin(to);
        if (fromPin == null || toPin == null)
            return;

        // Reversed convention: From = child output, To = parent input.
        var childId  = fromPin.OwnerNodeId.Value;
        var parentId = toPin.OwnerNodeId.Value;

        var parent = _asset.FindNode(parentId);
        if (parent == null)
            return;

        if (!parent.ChildVisualIds.Contains(childId))
            parent.ChildVisualIds.Add(childId);

        _links[linkId.Value] = (childId, parentId);
        _asset.MarkDirty();
    }

    private void ApplyRemoveLinks(IReadOnlyList<LinkId> linkIds)
    {
        foreach (var id in linkIds)
        {
            if (_links.TryGetValue(id.Value, out var pair))
            {
                var parent = _asset.FindNode(pair.parent);
                parent?.ChildVisualIds.Remove(pair.child);
                _links.Remove(id.Value);
            }
        }
        _asset.MarkDirty();
    }

    private void ApplySetNodeProperty(NodeId nodeId, string key, object? value)
    {
        var node = _asset.FindNode(nodeId.Value);
        if (node == null)
            return;

        switch (key)
        {
            case "comment":
                node.Comment = value as string;
                break;
            case "isBreakpoint":
                node.IsBreakpoint = value is bool b && b;
                break;
        }
        _asset.MarkDirty();
    }

    private void ApplyAddPill(GraphCommand.AddAttachment att)
    {
        if (att.HostProperties == null)
            return;
        if (!att.HostProperties.TryGetValue("decoratorType", out var dtObj))
            return;
        if (dtObj is not NodeType dt)
            return;

        var pill = new BTreeEditorPill
        {
            VisualId         = att.NewId.Value,
            HostNodeVisualId = att.HostNodeId.Value,
            DecoratorType    = dt,
            StackIndex       = att.StackIndex,
        };

        if (att.HostProperties.TryGetValue("intParam", out var ip) && ip is int intVal)
            pill.IntParam = intVal;
        if (att.HostProperties.TryGetValue("floatParam", out var fp) && fp is float floatVal)
            pill.FloatParam = floatVal;
        if (att.HostProperties.TryGetValue("comment", out var cp) && cp is string comment)
            pill.Comment = comment;

        _asset.AddPill(pill);
        _asset.MarkDirty();
    }

    private void ApplyRemovePills(IReadOnlyList<AttachmentId> ids)
    {
        foreach (var id in ids)
            _asset.RemovePill(id.Value);
        _asset.MarkDirty();
    }

    private void ApplySetPillProperty(AttachmentId id, string key, object? value)
    {
        var pill = _asset.FindPill(id.Value);
        if (pill == null)
            return;

        switch (key)
        {
            case "intParam":
                pill.IntParam = value is int i ? i : null;
                break;
            case "floatParam":
                pill.FloatParam = value is float f ? f : null;
                break;
            case "comment":
                pill.Comment = value as string;
                break;
        }
        _asset.MarkDirty();
    }

    private void ApplyReorderPills(NodeId hostNodeId, IReadOnlyList<AttachmentId> newOrder)
    {
        for (int i = 0; i < newOrder.Count; i++)
        {
            var pill = _asset.FindPill(newOrder[i].Value);
            if (pill != null && pill.HostNodeVisualId == hostNodeId.Value)
                pill.StackIndex = i;
        }
        _asset.MarkDirty();
    }
}
``` [3-11]
````

all to btrees? can we move to "2. HSM Host Services and Graph Adapters"?

The BTree host services implementation is complete. Moving to the HSM subsystem.

To integrate the HSM subsystem, you must implement the `IEditorHostServices` interface to project statechart hierarchies and non-pin-based transitions into the `NodeEditor` constraints. You must implement the following components to adapt the `HsmAsset` model:

**1\. HsmGraphModel and HsmTypeSystem** HSM states do not possess typed pins. To bridge this domain reality with the host's pin-based link primitives, every state node is assigned exactly one hidden output pin and one hidden input pin. Transitions are modeled as first-class links that connect the hidden output of a source state to the hidden input of a target state. The `HsmGraphModel` exposes all non-root states as `INodeModel` instances and transitions as `ILinkModel` instances. Additionally, composite states and parallel states implement the `IContainerNodeModel` extension to allow visual containment of child states and orthogonal regions.

**2\. HsmNodeCatalog** The catalog populates the "Add Node" picker with static entries representing state kinds: Simple State, Composite State, Parallel State, Final State, History, and Deep History. Transitions and regions are intentionally excluded from the catalog; transitions are created by wire-dragging and regions are added via context commands on a parallel state. The catalog also dynamically provides action and guard entries populated from the `HsmActionDispatcher` to support fuzzy search in the inspector's method pickers.

**3\. HsmLinkValidator** The link validator enforces state-machine transition rules. Because most state-to-state connections are valid in HSM semantics, the validator is relatively conservative. It strictly rejects outgoing transitions from a Final state, and rejects any incoming transitions targeted at History or Deep History pseudo-states.

**4\. HsmCommandSink** The command sink translates generic `GraphCommand` records into in-place edits on the `HsmAsset`, followed by triggering deferred file regeneration. It processes standard layout and element addition commands, but critically handles container-specific operations. When a state is dragged into or out of a composite state or across a parallel region boundary, the sink receives a `ChangeParent` command and updates the asset hierarchy. It also processes `AddRegion`, `RemoveRegion`, and `ReorderRegions` commands when users mutate parallel states.

ok please detail 1. HsmGraphModel and HsmTypeSystem

The `HsmGraphModel` serves as the `IGraphModel` adapter that exposes the mutable `HsmAsset` to the NodeEditor canvas. It exposes all non-root state nodes as `INodeModel` instances and transitions as `ILinkModel` instances. To visually contain nested children, composite and parallel states implement the `IContainerNodeModel` extension.

Because the `NodeEditor` host strictly requires pin-based link primitives for routing wires, the model bridges this gap by assigning exactly one hidden output pin and one hidden input pin to every state. Transitions are wrapped in `HsmTransitionLink` adapters that connect these hidden pins, allowing the host's existing wire-routing, hit-testing, selection, and undo systems to function without engine modification.

The `HsmTypeSystem` implements `ITypeSystem` but acts as a functional stub. HSM states execute transitions state-to-state and do not possess typed data pins. Consequently, type queries, implicit casting, and compatibility checks return `false`, while visual queries return empty or zero values. The canvas does not render pin glyphs for HSM assets.

Below is the required implementation structure for the model, the hidden pin primitive, and the type system:

```
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

public sealed class HsmGraphModel : IGraphModel
{
    private readonly HsmAsset _asset;
    private readonly Dictionary<LinkId, HsmTransitionLink> _linkCache = new();

    public HsmGraphModel(HsmAsset asset)
    {
        _asset = asset;
        _asset.Changed += OnAssetChanged;
        BuildCaches();
    }

    private void OnAssetChanged()
    {
        BuildCaches();
        Changed?.Invoke(new GraphChangeNotification(
            GraphChangeKind.NodesModified,
            null, null, null, "HsmAsset changed"));
    }

    private void BuildCaches()
    {
        _linkCache.Clear();
        foreach (var t in _asset.AllTransitions)
            _linkCache[new LinkId(t.VisualId)] = new HsmTransitionLink(t);
    }

    public GraphId Id          => new GraphId(_asset.AssetId);
    public string  DisplayName => _asset.Name;
    public GraphKindDescriptor Kind { get; } =
        new("HsmGraph", "State Machine", AllowsLatent: false, RequiresEntryNode: false);

    public IReadOnlyCollection<INodeModel> Nodes => _asset.AllStates;
    public IReadOnlyCollection<ILinkModel> Links => _linkCache.Values;
    public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

    public event Action<GraphChangeNotification>? Changed;

    public INodeModel? FindNode(NodeId id)
    {
        return _asset.FindStateByStableId(id.Value);
    }

    public IPinModel? FindPin(PinId id)
    {
        foreach (var state in _asset.AllStates)
        {
            foreach (var pin in state.Pins)
                if (pin.Id == id) return pin;
        }
        return null;
    }

    public ILinkModel? FindLink(LinkId id) =>
        _linkCache.TryGetValue(id, out var link) ? link : null;

    public IReadOnlyCollection<IAttachmentModel> Attachments =>
        _asset.AllAttachments.ToList<IAttachmentModel>();

    public IAttachmentModel? FindAttachment(AttachmentId id) =>
        _asset.FindAttachmentById(id);

    public IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId hostId) =>
        _asset.GetAttachmentsForNode(hostId).ToList<IAttachmentModel>();
}

internal sealed class HsmPinModel : IPinModel
{
    public PinId Id { get; }
    public NodeId OwnerNodeId { get; }
    public string Label { get; }
    public PinDirection Direction { get; }
    public PinKind Kind => PinKind.Data;
    public TypeKey? Type => null;
    public PinShape Shape => PinShape.Circle;
    public bool IsAdvanced => true;
    public bool IsOptional => true;
    public string? Tooltip => null;
    public IPinDefaultValue? Default => null;

    internal HsmPinModel(PinId id, NodeId ownerNodeId, PinDirection direction)
    {
        Id = id;
        OwnerNodeId = ownerNodeId;
        Direction = direction;
        Label = direction == PinDirection.Output ? "out" : "in";
    }
}
```
```
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Stub type system for the HSM canvas.
/// HSM states have no typed pins, so all queries return negative/default answers.
/// </summary>
internal sealed class HsmTypeSystem : ITypeSystem
{
    public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
    {
        info = default!;
        return false;
    }

    public Vector4 GetPinColor(TypeKey key) => Vector4.Zero;

    public PinShape GetPinShape(TypeKey key, ContainerKind container) => PinShape.Circle;

    public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;

    public bool AreCompatible(TypeKey from, TypeKey to) => false;

    public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
}
```

thanks, now please "2. HsmNodeCatalog"

The `HsmNodeCatalog` implements the `INodeCatalog` interface to provide palette content for the HSM canvas. It populates the "Add Node" picker with static state kinds and provides dynamic action and guard entries for use in the inspector's method pickers.

Transitions and regions are intentionally excluded from this catalog; transitions are created by dragging wires between states, and regions are added via context commands on a parallel state.

Because HSM states have no typed pins, `QueryForPinContext` always returns an empty array.

Below is the implementation structure for the catalog:

```
using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Static node catalog for the HSM canvas.
/// Provides one entry per state kind; states have no typed pins.
/// </summary>
internal sealed class HsmNodeCatalog : INodeCatalog
{
    private const string CatStates = "States";

    private static readonly IReadOnlyList<NodeCatalogEntry> _all = BuildEntries();

    private static readonly IReadOnlyList<NodeCategoryDescriptor> _categories = new[]
    {
        new NodeCategoryDescriptor(CatStates, "States", null),
        new NodeCategoryDescriptor(ReactiveGuardVocabulary.CategoryName, ReactiveGuardVocabulary.CategoryName, null),
    };

    public IReadOnlyList<NodeCatalogEntry> All       => _all;
    public IReadOnlyList<NodeCategoryDescriptor> Categories => _categories;

    public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q)
    {
        IEnumerable<NodeCatalogEntry> results = _all;

        if (!string.IsNullOrEmpty(q.CategoryFilter))
            results = results.Where(e => e.CategoryPath == q.CategoryFilter);

        if (!string.IsNullOrEmpty(q.Text))
        {
            string text = q.Text.ToLowerInvariant();
            results = results.Where(e =>
                e.DisplayName.ToLowerInvariant().Contains(text) ||
                e.Keywords.Any(k => k.ToLowerInvariant().Contains(text)));
        }

        if (!q.IncludeDeprecated)
            results = results.Where(e => !e.IsDeprecated);

        return results.ToList();
    }

    public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q)
        => Array.Empty<NodeCatalogEntry>();

    private static IReadOnlyList<NodeCatalogEntry> BuildEntries() => new[]
    {
        Make(HsmKinds.Simple,      "Simple State",       "A leaf state with no children.",
             new[] { "state", "simple", "leaf" },                   "hsm/state_simple"),
        Make(HsmKinds.Composite,   "Composite State",    "A state that can contain child states.",
             new[] { "state", "composite", "compound" },            "hsm/state_composite"),
        Make(HsmKinds.Parallel,    "Parallel State",     "A state with orthogonal sub-regions.",
             new[] { "state", "parallel", "orthogonal", "fork" },   "hsm/state_parallel"),
        Make(HsmKinds.Final,       "Final State",        "A terminal state; no outgoing transitions allowed.",
             new[] { "state", "final", "terminal", "end" },         "hsm/state_final"),
        Make(HsmKinds.History,     "History State",      "Shallow history pseudo-state.",
             new[] { "state", "history", "shallow" },               "hsm/state_history"),
        Make(HsmKinds.DeepHistory, "Deep History State", "Deep history pseudo-state.",
             new[] { "state", "history", "deep" },                  "hsm/state_deep_history"),
    };

    private static NodeCatalogEntry Make(
        string kindId,
        string displayName,
        string? description,
        string[] keywords,
        string? iconKey) => new(
            new NodeKindKey(kindId),
            displayName,
            description,
            CatStates,
            keywords,
            iconKey,
            IsPure: false,
            IsLatent: false,
            IsDeprecated: false,
            Inputs:  Array.Empty<PinSignature>(),
            Outputs: Array.Empty<PinSignature>());
}
```

The dynamic action and guard entries populated from the `HsmActionDispatcher` do not appear as draggable nodes in the canvas. They are injected into the catalog purely to allow the inspector's method picker to utilize the same fuzzy search logic over the registered types.

now please 3. HsmLinkValidator

The `HsmLinkValidator` implements `ILinkValidator` to enforce state-machine transition rules during interactive wire-dragging in the canvas. Unlike Behavior Trees, HSM semantics permit most state-to-state connections, making this validator relatively conservative. Deeper semantic checks, such as LCA (Least Common Ancestor) cost calculation or sync-group consistency, are deferred to the full HSM validation pipeline that runs post-mutation.

Because the HSM canvas models non-pin-based transitions over NodeEditor's pin-based link primitives, the validator maps the generic `from` and `to` pin IDs back to their owning `StateNode` instances using deterministically derived hidden pin IDs.

The validator enforces the following strict rejections:

-   **Unresolved Pins:** The source or target pin does not correspond to any known state's hidden pins.-   **Final State Exits:** Transitions originating from a Final state are explicitly blocked.-   **History State Targets:** Transitions targeting a History or Deep History pseudo-state are blocked.

Implement the validator as follows:

````
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Link validator for the HSM canvas.
///
/// A link represents a transition from a source state (output pin) to a target state (input pin).
/// Pin IDs are derived deterministically from each state's StableId.
///
/// Rules enforced:
/// - Both pins must resolve to known states; otherwise Invalid.
/// - Transitions from a Final state are not allowed.
/// - Transitions into a History or DeepHistory pseudo-state are not allowed.
/// </summary>
internal sealed class HsmLinkValidator : ILinkValidator
{
    private readonly HsmAsset _asset;

    internal HsmLinkValidator(HsmAsset asset)
    {
        _asset = asset;
    }

    public LinkValidationResult Validate(PinId from, PinId to)
    {
        // from = output pin of a state (source side of transition)
        // to   = input pin of a state (target side of transition)
        StateNode? source = FindByOutputPin(from);
        StateNode? target = FindByInputPin(to);

        if (source == null || target == null)
            return Invalid("Pin does not correspond to any state.");

        if (source.IsFinal)
            return Invalid("Transitions from a Final state are not allowed.");

        if (target.IsHistory || target.IsDeepHistory)
            return Invalid("Transitions into a History pseudo-state are not allowed.");

        return Valid();
    }

    // ---- Pin resolution helpers ----

    private StateNode? FindByOutputPin(PinId pin)
    {
        foreach (var s in _asset.AllStates)
        {
            if (StateNode.DeriveOutputPinId(s.StableId) == pin.Value)
                return s;
        }
        return null;
    }

    private StateNode? FindByInputPin(PinId pin)
    {
        foreach (var s in _asset.AllStates)
        {
            if (StateNode.DeriveInputPinId(s.StableId) == pin.Value)
                return s;
        }
        return null;
    }

    // ---- Result factories ----

    private static LinkValidationResult Valid() =>
        new(LinkValidity.Valid, null, false, null);

    private static LinkValidationResult Invalid(string reason) =>
        new(LinkValidity.Invalid, reason, false, null);
}
``` [2-5]
````

now please **4\. HsmCommandSink**

The `HsmCommandSink` implements the `IGraphCommandSink` interface for the HSM canvas. It translates generic NodeEditor `GraphCommand` records into domain-specific, in-place mutations on the `HsmAsset`. After applying mutations, the sink marks the asset as dirty to trigger a `Changed` event that propagates to the NodeEditor for canvas re-rendering.

Unlike the BTree command sink which primarily handles single-parent topology, the HSM command sink must execute complex container-specific operations. When users interact with composite states or parallel orthogonal regions, the sink receives specific container commands (e.g., `ChangeParent`, `AddRegion`, `RemoveRegion`, `ReorderRegions`).

Particularly for `RemoveRegion`, it evaluates a `ChildRedistributionPolicy` to determine whether the state children of the deleted region should be moved to the first remaining region, promoted to the parent container, or deleted entirely. Following any region modification, the sink strictly reindexes the remaining regions to ensure contiguous indexing.

Below is the required implementation structure:

```
using System;
using System.Collections.Generic;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Command sink for the HSM canvas.
/// Dispatches editor-initiated mutations to per-command stub handlers.
/// Each stub marks the asset dirty so callers receive change notifications.
/// </summary>
internal sealed class HsmCommandSink : IGraphCommandSink
{
    private readonly HsmAsset _asset;

    internal HsmCommandSink(HsmAsset asset)
    {
        _asset = asset;
    }

    public GraphCommandResult Apply(GraphCommand command)
    {
        switch (command)
        {
            case GraphCommand.MoveNodes cmd:
                ApplyMoveNodes(cmd);
                break;
            case GraphCommand.AddNode cmd:
                ApplyAddNode(cmd);
                break;
            case GraphCommand.RemoveNodes cmd:
                ApplyRemoveNodes(cmd);
                break;
            case GraphCommand.AddLink cmd:
                ApplyAddLink(cmd);
                break;
            case GraphCommand.RemoveLinks cmd:
                ApplyRemoveLinks(cmd);
                break;
            case GraphCommand.SetNodeProperty cmd:
                ApplySetNodeProperty(cmd);
                break;
            case GraphCommand.ChangeParent cmd:
                ApplyChangeParent(cmd);
                break;
            case GraphCommand.SetContainerCollapsed cmd:
                ApplySetContainerCollapsed(cmd);
                break;
            case GraphCommand.AddRegion cmd:
                ApplyAddRegion(cmd);
                break;
            case GraphCommand.RemoveRegion cmd:
                ApplyRemoveRegion(cmd);
                break;
            case GraphCommand.ReorderRegions cmd:
                ApplyReorderRegions(cmd);
                break;
            case GraphCommand.AddAttachment cmd:
                ApplyAddAttachment(cmd);
                break;
            case GraphCommand.RemoveAttachments cmd:
                ApplyRemoveAttachments(cmd);
                break;
            case GraphCommand.Batch cmd:
                foreach (var sub in cmd.Commands)
                {
                    var result = Apply(sub);
                    if (!result.Success)
                        return result;
                }
                break;
            default:
                return new GraphCommandResult(false, $"Unsupported: {command.GetType().Name}");
        }

        _asset.MarkDirty();
        return new GraphCommandResult(true, null);
    }

    // ---- Per-command stubs (populated in later tasks) ----

    private void ApplyMoveNodes(GraphCommand.MoveNodes cmd)           { /* TODO */ }
    private void ApplyAddNode(GraphCommand.AddNode cmd)               { /* TODO */ }
    private void ApplyRemoveNodes(GraphCommand.RemoveNodes cmd)       { /* TODO */ }
    private void ApplyAddLink(GraphCommand.AddLink cmd)               { /* TODO */ }
    private void ApplyRemoveLinks(GraphCommand.RemoveLinks cmd)       { /* TODO */ }
    private void ApplySetNodeProperty(GraphCommand.SetNodeProperty cmd) { /* TODO */ }
    private void ApplyChangeParent(GraphCommand.ChangeParent cmd)     { /* TODO */ }
    private void ApplySetContainerCollapsed(GraphCommand.SetContainerCollapsed cmd) { /* TODO */ }
    
    private void ApplyAddRegion(GraphCommand.AddRegion cmd)
    {
        var state = _asset.FindStateByStableId(cmd.ContainerId.Value);
        if (state is null) return;

        var region = new RegionNode(cmd.RegionName) { Priority = (byte)cmd.Priority };
        int insertAt = Math.Clamp(cmd.InsertAtIndex, 0, state.RegionNodes.Count);
        state.RegionNodes.Insert(insertAt, region);

        // Reindex all regions so RegionIndex stays contiguous.
        for (int i = 0; i < state.RegionNodes.Count; i++)
            state.RegionNodes[i].RegionIndex = (byte)i;

        _asset.RegisterRegion(region);
    }

    private void ApplyRemoveRegion(GraphCommand.RemoveRegion cmd)
    {
        var state = _asset.FindStateByStableId(cmd.ContainerId.Value);
        if (state is null) return;
        if (cmd.RegionIndex < 0 || cmd.RegionIndex >= state.RegionNodes.Count) return;

        var region = state.RegionNodes[cmd.RegionIndex];

        // Redistribute children of the removed region.
        switch (cmd.Policy)
        {
            case ChildRedistributionPolicy.MoveToFirstRegion:
                // Move children to region 0 if it is different from the one being removed.
                int targetRegion = cmd.RegionIndex == 0 ? 1 : 0;
                if (targetRegion < state.RegionNodes.Count)
                {
                    foreach (var child in state.Children)
                        if (child.RegionIndex == cmd.RegionIndex)
                            child.RegionIndex = targetRegion;
                }
                else
                {
                    // No other region to move to; leave children with index 0.
                    foreach (var child in state.Children)
                        if (child.RegionIndex == cmd.RegionIndex)
                            child.RegionIndex = 0;
                }
                break;

            case ChildRedistributionPolicy.MoveToParent:
                // Promote children to no-region (index 0, parent owns them).
                foreach (var child in state.Children)
                    if (child.RegionIndex == cmd.RegionIndex)
                        child.RegionIndex = 0;
                break;

            case ChildRedistributionPolicy.DeleteChildren:
                // Remove children from the state's child list.
                state.Children.RemoveAll(c => c.RegionIndex == cmd.RegionIndex);
                break;
        }

        state.RegionNodes.RemoveAt(cmd.RegionIndex);
        _asset.UnregisterRegion(region);

        // Reindex remaining regions.
        for (int i = 0; i < state.RegionNodes.Count; i++)
            state.RegionNodes[i].RegionIndex = (byte)i;
    }

    private void ApplyReorderRegions(GraphCommand.ReorderRegions cmd)
    {
        var state = _asset.FindStateByStableId(cmd.ContainerId.Value);
        if (state is null) return;
        if (cmd.NewOrder.Count != state.RegionNodes.Count) return;

        var reordered = new List<RegionNode>(state.RegionNodes.Count);
        foreach (var oldIndex in cmd.NewOrder)
        {
            if (oldIndex < 0 || oldIndex >= state.RegionNodes.Count) return;
            reordered.Add(state.RegionNodes[oldIndex]);
        }

        state.RegionNodes.Clear();
        state.RegionNodes.AddRange(reordered);

        // Reindex so RegionIndex matches the new positions.
        for (int i = 0; i < state.RegionNodes.Count; i++)
            state.RegionNodes[i].RegionIndex = (byte)i;
    }

    private void ApplyAddAttachment(GraphCommand.AddAttachment cmd)
    {
        var att = new HsmAttachment(
            cmd.NewId,
            cmd.HostNodeId,
            cmd.Category,
            cmd.Glyph,
            cmd.Label,
            cmd.Tooltip,
            cmd.StackIndex,
            cmd.HostProperties);
        _asset.AddAttachment(att);
    }

    private void ApplyRemoveAttachments(GraphCommand.RemoveAttachments cmd)
    {
        _asset.RemoveAttachments(cmd.AttachmentIds);
    }
}
```

is this all to "1. NodeEditor Host Services and Graph Adapter"? Can we move to **2\. Visual Extensions** ?

The host services and graph adapters are fully specified. You must utilize three specific `NodeEditor` extensions to render domain-specific semantics that the standard node and link primitives cannot support.

**1\. Node Attachments** The BTree editor relies on this extension to collapse decorator wrappers into stackable visual pills (e.g., Inverter, Repeater, Cooldown) rendered horizontally above the host node. The HSM editor utilizes attachments to render state-flag badges, such as deferred events or conflict warnings.

**2\. Container Nodes** The HSM editor uses containers exclusively for composite states and parallel regions. A composite state visually contains its child states, and the container's bounds dynamically auto-grow to enclose them. Child node positions are expressed relative to the container's interior coordinate space rather than absolute canvas coordinates. For parallel states, the container renders dashed dividers to separate the orthogonal regions.

**3\. Custom Canvas Renderers** You must register subsystem-specific renderers at explicit canvas passes to inject non-standard visual elements:

-   **BTree:** You must register `btree.observer_guard_badges` to draw "OBSERVES" badges on connections from Observer Selectors to guard children at the `AfterWires` pass. Register `btree.subtree_boundaries` at `BeforeContent` to draw a dashed rectangle indicating the bounds of the currently executing subtree. Register `btree.runtime_overlay` at `AfterNodes` to draw pulsing outlines on executing nodes and stack ancestry. Finally, register `btree.heatmap_overlay` at `BeforeContent` to color nodes by entry frequency during multi-instance debugging.-   **HSM:** You must register `hsm.transition_labels` at `AfterWires` to draw Event, Guard, and Action strings at transition link midpoints. Register `hsm.initial_state_arrows` at `AfterNodes` to render marker arrows pointing from a composite's origin to its initial child. Register `hsm.region_conflicts` at `AfterNodes` to draw yellow warning lines and glyphs between states with output lane collisions. Register `hsm.history_glyphs` at `AfterNodes` to visually replace standard node bodies with circled H, H\*, or ⊙ final glyphs. Finally, register `hsm.runtime_overlay` at `AfterNodes` to render the active-configuration glow during debugging.

ok pls detail **1\. Node Attachments**

Node attachments are small, parameterized, visually-attached annotations whose lifetime is tied to a host node. They are not standalone nodes, lack pins and connection lines, have no inline editors, and cannot host nested attachments.

**Visual Layout and Rendering** Attachments render as small rounded rectangles above the host node's header. They stack horizontally from left to right, wrapping to additional rows above when the total row width exceeds the host node's width. At a 1.0 zoom level, each pill is 20 px tall with an 8 px corner radius, spaced 4 px apart horizontally, and sits 6 px above the host header.

Background colors are driven by the `AttachmentCategory`: Decorators are purple, Flags are teal, Pure nodes are green, and Custom attachments are mid-gray. Below a 0.5 zoom threshold, attachments collapse into a single 3 px tall colored bar above the host node, which is split horizontally proportional to the categories present. Attachments render immediately after their host node body and header, ordered by `StackIndex` ascending, ensuring they appear underneath selection outlines.

**Hit Testing and Interaction** In the canvas hit-test priority, attachments sit directly below wires and above node headers. Clicking an attachment selects it, updating the `SelectionState` with mixed selection support. Users can `Ctrl+click` to toggle selection and `Shift+click` to extend the selection along the visual stack. Keyboard navigation is fully supported: `Tab` cycles focus from the host node into its attachments, and arrow keys move selection between pills or rows. Right-clicking an attachment dispatches to a host-registered `IAttachmentContextMenuProvider` to populate custom menus.

**Data Model and Commands** Attachments are identified by a GUID-wrapped `AttachmentId` and implement the `IAttachmentModel` interface. They carry state flags that mirror `NodeState` (such as Disabled, Error, Warning, or Executing) so validation and debug feedback operate uniformly. Graph mutations are handled by extending `GraphCommand` with `AddAttachment`, `RemoveAttachments`, `SetAttachmentProperty`, `ReorderAttachments`, and `MoveAttachment`. The `AddAttachment.HostProperties` dictionary provides an escape hatch for host-specific payloads, such as a Repeater's count.

**Subsystem Integrations**

-   **BTree Editor:** Decorator wrappers (e.g., Inverter, Repeater, Cooldown, ForceSuccess) are collapsed during projection into `Decorator` category pills on their innermost non-decorator child. The outermost-in-source decorator receives the highest `StackIndex`, rendering rightmost or topmost to reflect the result-bubbling order.-   **HSM Editor:** Uses attachments to render state-flag badges. This surfaces features like deferred events (a 🕓 chip), guard-on-entry pseudo-states, or output-lane collision warnings directly on the state node.

is this all already implemented? how to wire it?

The base extensions are already implemented natively within the `NodeEditor.Core` interfaces and rendering pipeline. To utilize them, the subsystem editors must implement the provided extension interfaces and project them through `IEditorHostServices`.

**1\. Wiring Node Attachments** The NodeEditor host natively queries attachments per node during the layout and render phases.

-   **Model Implementation:** Attachments must implement the `IAttachmentModel` interface. Your graph model (e.g., `HsmAsset`) must maintain the collection of attachments and expose them via `GetAttachmentsForNode(NodeId hostId)`.-   **Command Pipeline:** The `IGraphCommandSink` must handle `GraphCommand.AddAttachment`, `RemoveAttachments`, `SetAttachmentProperty`, and `ReorderAttachments`, applying the mutations to the asset's attachment collection.

**2\. Wiring Container Nodes** The NodeEditor canvas automatically applies containment rendering and region dividers when it detects a node implementing the container interface.

-   **Model Implementation:** Your domain node class (e.g., `StateNode` in HSM) must implement `IContainerNodeModel` instead of the standard `INodeModel`.-   **Opt-In:** The canvas queries `node is IContainerNodeModel { IsContainer: true }`. When true, it automatically resizes the node bounds to enclose its children and renders any `RegionDescriptor` elements if `IsParallel` is true.

**3\. Wiring Custom Canvas Renderers** Custom renderers bypass standard node primitives to draw directly via ImGui at specific z-order passes.

-   **Renderer Implementation:** You must implement `ICustomCanvasRenderer` for each visual element, specifying its string `Id` and its `CanvasRenderPass` (`BeforeContent`, `AfterWires`, `AfterNodes`, or `TopMost`). For example, `ObserverGuardBadgeRenderer` runs at `AfterWires` to draw on top of links.-   **Registration:** The renderers must be passed as an `IReadOnlyList<ICustomCanvasRenderer>` into the `IEditorHostServices` instance during subsystem host initialization. The canvas will automatically invoke them in registration order during their declared pass.

lets detail 1. Wiring Node Attachments

To wire the Node Attachments extension into a subsystem editor, you must implement the attachment data models, project them through the `IGraphModel` interface, handle their mutations in the `IGraphCommandSink`, and optionally provide custom context menus.

**1\. Model Implementation** You must define a domain-specific class that implements the `IAttachmentModel` interface. This model encapsulates the attachment's identity, category, display glyph, label, and layout stack index.

-   **BTree Subsystem:** Implements `BTreeEditorPill` to represent collapsed decorator nodes (e.g., Inverter, Repeater).-   **HSM Subsystem:** Implements `HsmAttachment` to represent state flags (e.g., deferred events, conflicts).

Your primary asset class (`BehaviorTreeAsset` or `HsmAsset`) must manage the collection of these attachments, typically using a `Dictionary<AttachmentId, IAttachmentModel>` for `O(1)` lookups.

**2\. Graph Model Projection** The `IGraphModel` adapter exposes the asset's attachments to the `NodeEditor` canvas. You must implement three methods on your `IGraphModel` adapter:

-   `Attachments`: Returns all attachments in the graph.-   `FindAttachment(AttachmentId id)`: Resolves a specific attachment.-   `GetAttachmentsForNode(NodeId hostId)`: Returns all attachments owned by a specific host node. The canvas calls this repeatedly during rendering, so the underlying asset should support fast lookups by host ID.

**3\. Command Sink Wiring** The `IGraphCommandSink` must intercept attachment-related `GraphCommand` records and apply them to the underlying asset in-place. You must handle:

-   `GraphCommand.AddAttachment`: Translates to creating a new `IAttachmentModel` instance. Extract domain-specific parameters (like a Repeater's iteration count) using the `HostProperties` dictionary, which acts as an escape hatch for host-defined payloads.-   `GraphCommand.RemoveAttachments`: Removes the specified attachments from the asset's collection. In BTree, this corresponds to deleting the underlying wrapper node.-   `GraphCommand.SetAttachmentProperty`: Updates mutable properties (like comments or numeric parameters) on the attachment.-   `GraphCommand.ReorderAttachments`: Updates the `StackIndex` of the attachments for a single host node to reflect a new horizontal ordering.

Following any of these commands, the sink must mark the asset as dirty to trigger a `GraphChangeNotification`, prompting the canvas to re-render.

**4\. Context Menu Provider** By default, right-clicking an attachment falls through to the empty-canvas context menu. To provide attachment-specific actions, implement the `IAttachmentContextMenuProvider` interface. This provider evaluates an `AttachmentId` and returns a list of `ContextMenuItem` elements. Register this provider in your subsystem's `IEditorHostServices` under the `AttachmentContextMenu` property. This is how you expose subsystem-specific operations, such as "Remove decorator" in BTree or "Edit deferred events" in HSM.

pls detail 1. Model Implementation

The model implementation for the Node Attachments extension requires defining domain-specific classes that fulfill the `IAttachmentModel` interface contract, alongside efficient collection management within your primary asset models.

The foundational `IAttachmentModel` interface provides a read-only view of a single attachment pinned to a host node, which the `NodeEditor` canvas queries during layout and rendering. It mandates the following properties:

-   `Id` and `HostNodeId` for relational identity.-   `Category` to dictate the background color (e.g., `Decorator`, `Flag`, `Pure`, `Custom`).-   `Glyph` and `Label` for the visual text.-   `State` (mirroring `NodeState` semantics like `Normal`, `Error`, `Warning`) to apply unified visual feedback.-   `StackIndex` to determine the left-to-right, row-by-row rendering order.

You must implement concrete models for each subsystem that requires attachments:

**1\. BTree Subsystem (****BTreeEditorPill****)** The BTree editor uses attachments to represent decorator nodes (e.g., Inverter, Repeater) that have been collapsed into pills. The `BTreeEditorPill` model implements the necessary properties and adds domain-specific payload data.

-   It tracks `VisualId` and `HostNodeVisualId` using `Guid` to maintain stable identity across reloads.-   It stores the `DecoratorType` (`NodeType` enum) to identify the specific decorator logic.-   It includes `IntParam` (e.g., a Repeater's iteration count) and `FloatParam` (e.g., a Cooldown's duration), along with a `Comment`.

The `BehaviorTreeAsset` manages these pills using a `List<BTreeEditorPill>` for enumeration and a `Dictionary<Guid, BTreeEditorPill>` for O(1) lookups by visual ID, which is critical for performance during layout projection and command handling.

**2\. HSM Subsystem (****HsmAttachment****)** The HSM editor uses attachments to render state-flag badges, such as deferred events or region conflicts. The `HsmAttachment` class acts as a direct wrapper around the `IAttachmentModel` requirements.

-   It implements `Id`, `HostNodeId`, `Category`, `Glyph`, `Label`, `Tooltip`, `State`, and `StackIndex` directly.-   The constructor accepts an `IReadOnlyDictionary<string, object?>? hostProperties`, which acts as an escape hatch for any future host-defined extension data, though it remains unused in the v1 implementation.

The `HsmAsset` manages these attachments using a `Dictionary<AttachmentId, HsmAttachment>`. Because the `NodeEditor` repeatedly calls `GetAttachmentsForNode(NodeId hostId)` during the render loop, the asset implementation filters this dictionary by `HostNodeId` to supply the required collections.

pls define the classes

Implement the `BTreeEditorPill` and `HsmAttachment` classes as follows.

For the BTree subsystem, the `BTreeEditorPill` represents a decorator wrapper that has been collapsed into an attachment pill. It correlates to one decorator-type kernel node whose child is the decorated host node.

```
using System;
using Fbt;

namespace Hrot.BTree.Editor.Model;

/// <summary>
/// Represents a decorator wrapper collapsed into an attachment pill in the editor.
/// Corresponds to one decorator-type kernel node whose child is the decorated host node.
/// </summary>
public sealed class BTreeEditorPill
{
    /// <summary>Stable visual identity of this pill (minted or sourced from NodeDebugMetadata.VisualId).</summary>
    public Guid VisualId;
    /// <summary>Visual ID of the host node that this pill decorates.</summary>
    public Guid HostNodeVisualId;
    /// <summary>Decorator kind (Inverter, Repeater, Cooldown, …).</summary>
    public NodeType DecoratorType;
    /// <summary>Integer parameter (e.g. Repeater's count). Null when not applicable.</summary>
    public int? IntParam;
    /// <summary>Float parameter (e.g. Cooldown's duration). Null when not applicable.</summary>
    public float? FloatParam;
    public string? Comment;
    /// <summary>Zero-based ordering within the host node's pill stack (top = 0).</summary>
    public int StackIndex;
}
```

For the HSM subsystem, the `HsmAttachment` implements `IAttachmentModel` to serve as a single visual attachment pinned to an HSM state node. These are created via `GraphCommand.AddAttachment` and persisted in the `HsmAsset`.

```
using System.Collections.Generic;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// Editor-side implementation of a single attachment pinned to a state node.
// Attachments are created via GraphCommand.AddAttachment and stored in HsmAsset.
internal sealed class HsmAttachment : IAttachmentModel
{
    public AttachmentId Id { get; }
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category { get; }
    public string? Glyph { get; }
    public string? Label { get; }
    public string? Tooltip { get; }
    public AttachmentState State { get; set; }
    public int StackIndex { get; }

    internal HsmAttachment(
        AttachmentId id,
        NodeId hostNodeId,
        AttachmentCategory category,
        string? glyph,
        string? label,
        string? tooltip,
        int stackIndex,
        IReadOnlyDictionary<string, object?>? hostProperties)
    {
        Id = id;
        HostNodeId = hostNodeId;
        Category = category;
        Glyph = glyph;
        Label = label;
        Tooltip = tooltip;
        StackIndex = stackIndex;
        // hostProperties reserved for future host-defined extension data; not used in v1.
    }
}
```

did we detail already the HsmAsset and BTree asset?

We have referenced them as the underlying editor-side models manipulated by the command sinks and graph adapters, but we have not detailed their structural class implementations yet.

Here are the detailed implementations for both assets.

1\. BehaviorTreeAsset

The `BehaviorTreeAsset` is the editor-side mutable model representing a BTree. It projects the kernel-side `BehaviorTreeBlob`, debug metadata, and layout data into a format the editor can mutate. It implements `IEditableAsset`, `IBlackboardManagedAsset`, and `IBTreeSyncableAsset` to participate in the shared AI editor selection store, asset browser, and blackboard reflection. It manages lookup tables linking `VisualId` to the underlying nodes, decorator pills, and blob indices to maintain O(1) correlation with the kernel runtime.

```
using System;
using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.BTree.Editor.Model;

public sealed class BehaviorTreeAsset : IEditableAsset, IBlackboardManagedAsset, IBTreeSyncableAsset
{
    private bool _isDirty;
    private readonly List<BTreeEditorNode> _nodes = new();
    private readonly List<BTreeEditorPill> _pills  = new();
    private readonly List<BlackboardVariableEntry> _blackboardVariables = new();
    private readonly Dictionary<string, List<BlackboardAliasBinding>> _aliases = new();
    private readonly Dictionary<Guid, List<SubtreeSyncBinding>> _syncBindings = new();
    private readonly Dictionary<Guid, (string SubtreeName, string SubDtoTypeName, string? SubDtoTypeNs)> _syncNodeMeta = new();
    private readonly HashSet<(string VariableName, string WriterPairKey)> _conflictSuppressions = new();
    private readonly HashSet<string> _unusedSuppressions = new();

    private readonly Dictionary<Guid, int>              _visualIdToBlobIndex = new();
    private readonly Dictionary<Guid, BTreeEditorNode>  _visualIdToNode      = new();
    private readonly Dictionary<Guid, BTreeEditorPill>  _visualIdToPill      = new();

    public Guid AssetId { get; }
    public string Name { get; set; }
    public AssetKind Kind => AssetKind.BTree;
    public string SourceFilePath { get; }
    public bool IsDirty => _isDirty;
    public bool IsEditorOwned { get; }
    public event Action? Changed;

    public string BlackboardTypeName { get; }
    public string ContextTypeName    { get; }
    public string TargetNamespace    { get; set; }
    public BehaviorTreeBlob Blob { get; private set; }

    public IReadOnlyList<BTreeEditorNode> Nodes => _nodes;
    public IReadOnlyList<BTreeEditorPill> Pills => _pills;

    public bool IsBlackboardEditorManaged { get; set; }
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _blackboardVariables;

    public Vector2 CanvasPanOffset  { get; set; }
    public float   CanvasZoomLevel  { get; set; } = 1f;

    public BehaviorTreeAsset(
        Guid assetId, string name, string sourceFilePath, bool isEditorOwned,
        string blackboardTypeName, string contextTypeName, BehaviorTreeBlob blob, string targetNamespace = "")
    {
        AssetId              = assetId;
        Name                 = name;
        SourceFilePath       = sourceFilePath;
        IsEditorOwned        = isEditorOwned;
        BlackboardTypeName   = blackboardTypeName;
        ContextTypeName      = contextTypeName;
        Blob                 = blob;
        TargetNamespace      = targetNamespace;
    }

    public void MarkDirty()
    {
        _isDirty = true;
        Changed?.Invoke();
    }

    public void ClearDirty() => _isDirty = false;

    public BTreeEditorNode? FindNode(Guid visualId) =>
        _visualIdToNode.TryGetValue(visualId, out var n) ? n : null;

    public BTreeEditorPill? FindPill(Guid visualId) =>
        _visualIdToPill.TryGetValue(visualId, out var p) ? p : null;

    internal void AddNode(BTreeEditorNode node)
    {
        _nodes.Add(node);
        _visualIdToNode[node.VisualId] = node;
        if (node.KernelBlobIndex >= 0)
            _visualIdToBlobIndex[node.VisualId] = node.KernelBlobIndex;
    }

    internal void AddPill(BTreeEditorPill pill)
    {
        _pills.Add(pill);
        _visualIdToPill[pill.VisualId] = pill;
    }

    internal bool RemoveNode(Guid visualId)
    {
        if (!_visualIdToNode.TryGetValue(visualId, out var node)) return false;
        _nodes.Remove(node);
        _visualIdToNode.Remove(visualId);
        _visualIdToBlobIndex.Remove(visualId);
        return true;
    }

    internal bool RemovePill(Guid visualId)
    {
        if (!_visualIdToPill.TryGetValue(visualId, out var pill)) return false;
        _pills.Remove(pill);
        _visualIdToPill.Remove(visualId);
        return true;
    }

    // IBTreeSyncableAsset method implementations omitted for brevity...
}
```

2\. HsmAsset

The `HsmAsset` is the editor-side mutable model representing an HSM. It implements `IEditableAsset` and `IBlackboardManagedAsset`. It tracks the entire state hierarchy, transitions, orthogonal regions, global transitions, events, and canvas attachments.

```
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

public sealed class HsmAsset : IEditableAsset, IBlackboardManagedAsset
{
    public Guid AssetId { get; }
    public string Name { get; set; }
    public AssetKind Kind => AssetKind.Hsm;
    public string SourceFilePath { get; }
    public bool IsDirty { get; internal set; }
    public bool IsEditorOwned { get; }
    public string TargetNamespace { get; }
    public string BlackboardTypeName { get; set; }

    public HsmDefinitionBlob Blob { get; }
    public MachineMetadata Metadata { get; }

    public StateNode RootState { get; }
    public IReadOnlyList<StateNode> AllStates { get; }
    public IReadOnlyList<TransitionNode> AllTransitions { get; }
    public IReadOnlyList<GlobalTransitionNode> AllGlobalTransitions { get; }
    public IReadOnlyList<RegionNode> AllRegions => _allRegionsList;
    public IReadOnlyList<EventDefinition> AllEvents { get; }

    public bool IsBlackboardEditorManaged { get; set; }
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _blackboardVariables;

    public Vector2 CanvasPanOffset { get; set; }
    public float CanvasZoomLevel { get; set; } = 1f;

    private readonly List<BlackboardVariableEntry> _blackboardVariables = new();
    private readonly Dictionary<string, List<BlackboardAliasBinding>> _aliases = new();
    private readonly HashSet<(string VariableName, string WriterPairKey)> _conflictSuppressions = new();
    private readonly HashSet<string> _unusedSuppressions = new();

    private readonly Dictionary<Guid, StateNode> _stableIdToState;
    private readonly Dictionary<Guid, TransitionNode> _visualIdToTransition;
    private readonly Dictionary<Guid, GlobalTransitionNode> _visualIdToGlobalTransition;
    private readonly Dictionary<Guid, RegionNode> _stableIdToRegion;
    private readonly Dictionary<ushort, StateNode> _flatIndexToState;
    private readonly Dictionary<ushort, TransitionNode> _flatIndexToTransition;
    private readonly Dictionary<ushort, EventDefinition> _eventIdToEvent;

    private readonly List<RegionNode> _allRegionsList;
    private readonly Dictionary<AttachmentId, HsmAttachment> _attachments = new();

    public event Action? Changed;

    internal HsmAsset(
        Guid assetId, string name, string sourceFilePath, bool isEditorOwned, string targetNamespace,
        HsmDefinitionBlob blob, MachineMetadata metadata,
        StateNode rootState, List<StateNode> allStates,
        List<TransitionNode> allTransitions, List<GlobalTransitionNode> allGlobalTransitions,
        List<RegionNode> allRegions, List<EventDefinition> allEvents)
    {
        AssetId = assetId;
        Name = name;
        SourceFilePath = sourceFilePath;
        IsEditorOwned = isEditorOwned;
        TargetNamespace = targetNamespace;
        Blob = blob;
        Metadata = metadata;
        RootState = rootState;
        AllStates = allStates;
        AllTransitions = allTransitions;
        AllGlobalTransitions = allGlobalTransitions;
        _allRegionsList = allRegions;
        AllEvents = allEvents;
        BlackboardTypeName = name + "_Blackboard";

        _stableIdToState = allStates.ToDictionary(s => s.StableId);
        _visualIdToTransition = allTransitions.ToDictionary(t => t.VisualId);
        _visualIdToGlobalTransition = allGlobalTransitions.ToDictionary(t => t.VisualId);
        _stableIdToRegion = allRegions.ToDictionary(r => r.StableId);
        _flatIndexToState = allStates.ToDictionary(s => s.FlatIndex);
        _flatIndexToTransition = allTransitions.ToDictionary(t => t.TransitionId);
        _eventIdToEvent = allEvents.ToDictionary(e => e.EventId);
    }

    public void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke();
    }

    public StateNode? FindStateByStableId(Guid stableId) => _stableIdToState.GetValueOrDefault(stableId);
    public TransitionNode? FindTransitionByVisualId(Guid visualId) => _visualIdToTransition.GetValueOrDefault(visualId);
    public RegionNode? FindRegionByStableId(Guid stableId) => _stableIdToRegion.GetValueOrDefault(stableId);

    internal void RegisterRegion(RegionNode region)
    {
        _allRegionsList.Add(region);
        _stableIdToRegion[region.StableId] = region;
    }

    internal void UnregisterRegion(RegionNode region)
    {
        _allRegionsList.Remove(region);
        _stableIdToRegion.Remove(region.StableId);
    }

    internal void AddAttachment(HsmAttachment attachment)
    {
        _attachments[attachment.Id] = attachment;
    }

    internal void RemoveAttachments(IReadOnlyList<AttachmentId> ids)
    {
        foreach (var id in ids)
            _attachments.Remove(id);
    }

    internal HsmAttachment? FindAttachmentById(AttachmentId id) => _attachments.GetValueOrDefault(id);
    internal IEnumerable<HsmAttachment> AllAttachments => _attachments.Values;

    internal IReadOnlyList<HsmAttachment> GetAttachmentsForNode(NodeId hostId)
    {
        var result = new List<HsmAttachment>();
        foreach (var att in _attachments.Values)
            if (att.HostNodeId == hostId) result.Add(att);
        return result;
    }
}
```

what about the **2\. Graph Model Projection** ?

To project the attachment data model into the canvas, your subsystem's `IGraphModel` adapter must expose the asset's attachments to the `NodeEditor` host. You must implement three specific methods on your graph model to fulfill this contract:

-   **Attachments**: Returns an `IReadOnlyCollection<IAttachmentModel>` representing all attachments currently present in the graph.-   **FindAttachment(AttachmentId id)**: Resolves and returns a specific attachment by its ID, or null if it is not found.-   **GetAttachmentsForNode(NodeId hostId)**: Returns an `IReadOnlyList<IAttachmentModel>` containing all attachments owned by the specified host node.

Because the `NodeEditor` canvas calls `GetAttachmentsForNode` repeatedly during the layout and rendering phases, your underlying asset must support fast lookups. You must avoid O(N) linear scans over all attachments; instead, maintain an internal lookup structure (such as a `Dictionary<NodeId, List<IAttachmentModel>>` or equivalent filtering mechanism) to ensure O(1) retrieval performance. The canvas itself will cache these results per frame in its viewport state to further minimize overhead.

For example, your `HsmGraphModel` implementation simply delegates these projection queries directly to the `HsmAsset`:

````
public IReadOnlyCollection<IAttachmentModel> Attachments => 
    _asset.AllAttachments.ToList<IAttachmentModel>();

public IAttachmentModel? FindAttachment(AttachmentId id) => 
    _asset.FindAttachmentById(id);

public IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId hostId) => 
    _asset.GetAttachmentsForNode(hostId).ToList<IAttachmentModel>();
``` [4, 5]

This guarantees that as attachments are added, removed, or moved via the command sink, the canvas immediately reads the updated topological mapping directly from the asset [3].
````

what about 3. Command Sink Wiring?

The `IGraphCommandSink` must intercept attachment-related `GraphCommand` records and apply them to the underlying asset in-place. You must implement handlers for the following commands:

-   **AddAttachment:** Translates to creating a new `IAttachmentModel` instance. You must extract domain-specific parameters using the `HostProperties` dictionary, which acts as an escape hatch for host-defined payloads. For the BTree subsystem, this extracts the `decoratorType`, `intParam`, and `floatParam` to construct a `BTreeEditorPill`. For the HSM subsystem, it constructs an `HsmAttachment` passing the required visual properties.-   **RemoveAttachments:** Removes the specified attachments from the asset's collection. In the BTree subsystem, dropping the pill requires the editor's command sink to update the kernel node tree accordingly.-   **SetAttachmentProperty:** Updates mutable properties on the attachment. This allows the inspector to update values like a Repeater's count or a Cooldown's duration without requiring the user to remove and re-add the attachment.-   **ReorderAttachments:** Updates the `StackIndex` of the attachments for a single host node to reflect the new horizontal ordering.-   **MoveAttachment:** Captures the previous host and index to move an attachment between host nodes. While not exposed via drag-and-drop in v1, it is available for host-driven refactoring operations.

Following any of these mutations, the sink must mark the asset as dirty to trigger a `GraphChangeNotification`. This prompts the canvas to immediately read the updated topological mapping directly from the asset and re-render. Multiple attachment operations, such as removing a host node that owns several attachments, must be batched into a single `GraphCommand.Batch` so that a single undo restores the full state.

what about 4. Context Menu Provider?

To implement the context menu provider for node attachments, you must define a class that satisfies the `IAttachmentContextMenuProvider` interface. This provider intercepts right-click events on attachment pills to supply context-specific actions rather than falling through to the default empty-canvas context menu.

The interface requires a single method, `GetItemsFor(AttachmentId id)`, which evaluates the targeted attachment and returns a list of `ContextMenuItem` records. Each `ContextMenuItem` encapsulates a display `Label`, an `Execute` action delegate, and an `Enabled` boolean state.

You must register this provider during subsystem initialization by assigning it to the `AttachmentContextMenu` property of your `IEditorHostServices` instance.

The returned actions are strictly host-defined based on the domain semantics:

-   **BTree Subsystem:** The provider returns operations such as "Remove decorator," "Edit count," and "Replace decorator type".-   **HSM Subsystem:** The provider returns operations such as "Edit deferred events" and "Suppress warning".

When the user clicks a menu item, the associated `Execute` delegate is invoked. This delegate typically constructs the appropriate `GraphCommand` (such as `RemoveAttachments` or `SetAttachmentProperty`) and dispatches it to the host's `IGraphCommandSink` to mutate the underlying asset.

is that all to the "\*\*Node Attachments"? Can we move to Container Nodes?

The Node Attachments extension wiring is complete. We will proceed to Container Nodes.

The Container Nodes extension addresses structural hierarchy where the parent-child relationship is the data itself, rather than just a cosmetic grouping. In this architecture, a container node holds other nodes as first-class children, automatically sizing its bounds to enclose them, and evaluating child positions relative to its own interior space. The HSM editor utilizes this exclusively for composite states and parallel regions.

To wire container nodes into your subsystem, you must implement the following structural and command elements:

**1\. Data Model Implementation** Your domain node class (e.g., `StateNode` in the HSM editor) must implement the `IContainerNodeModel` interface, which extends `INodeModel`. This requires exposing:

-   `IsContainer`: A boolean that activates container rendering. If false, the node behaves as a regular node.-   `ChildNodeIds`: An ordered list of child node IDs. This order dictates sibling z-order and deterministic serialization.-   `Regions`: A list of `RegionDescriptor` elements defining orthogonal regions for parallel composites. If empty, the container is a simple composite.-   `MinimumInteriorSize` and `Padding`: Layout constraints dictating the container's interior space.

Additionally, you must add a `NodeId? ParentContainerId` property to the base `INodeModel`. This bidirectional mapping ensures O(1) child-to-parent lookups during hit-testing and link routing.

**2\. Coordinate Space Transformation** The `Position` property of a child node is evaluated in the parent container's interior coordinate space, rather than absolute canvas coordinates. The `GraphView` adapter provides a `NodeCanvasPosition` helper to recursively compute the absolute screen position by walking the ancestor chain.

**3\. Auto-Resize and Region Layout** Containers are never manually resized by the user. The canvas automatically resizes the container bounds whenever a child is added, removed, moved, or resized.

For parallel states (where `Regions.Count > 0`), the interior is automatically divided into horizontal or vertical strips based on `RegionOrientation`. You must implement `GetRegionIndexForChild(NodeId childId)` so the canvas knows which region strip a child belongs to during layout.

**4\. Command Sink Wiring** Your `IGraphCommandSink` must handle container-specific commands emitted by the canvas during user interaction:

-   `ChangeParent`: Emitted when the user drags a node into or out of a container's bounds, or across a region boundary. You must update the child's `ParentContainerId`, `RegionIndex`, and local `Position`.-   `SetContainerCollapsed`: Emitted when the user clicks the container's header chevron. You must toggle the `IsCollapsed` state.-   `AddRegion` / `RemoveRegion` / `ReorderRegions`: Emitted during region mutation. For `RemoveRegion`, you must execute the specified `ChildRedistributionPolicy` to move or delete orphaned children.

pls implement the domain node classes

The editor-side domain node classes represent the structural and visual state of the HSM asset. `StateNode` explicitly implements the `IContainerNodeModel` interface to support nested composite states and orthogonal regions.

Below are the domain node implementations for the HSM subsystem:

````
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Hsm.Editor.Host;

namespace Hrot.Hsm.Editor.Model;

/// <summary>
/// Editor-side representation of a single state.
/// Augments the kernel-side StateDef with editor-only fields (layout, comments, etc.).
/// </summary>
public sealed class StateNode : IContainerNodeModel
{
    // Primary editor identity (stable across hot reloads if layout method is present)
    public Guid StableId;
    // Re-derived on each reload; index into HsmDefinitionBlob.States
    public ushort FlatIndex;

    public string Name;
    public StateNode? Parent;
    public List<StateNode> Children { get; } = new();
    public List<TransitionNode> OutgoingTransitions { get; } = new();
    public List<RegionNode> RegionNodes { get; } = new();

    // State configuration (from StateDef.Flags)
    public bool IsInitial;
    public bool IsHistory;
    public bool IsDeepHistory;
    public bool IsParallel;
    public bool IsFinal;

    // True when this state is a pseudo-state (History, Deep-History, or Final).
    // Pseudo-states are rendered exclusively via HsmHistoryGlyphsRenderer;
    // the node body background is drawn transparent.
    public bool IsPseudostate => IsHistory || IsDeepHistory || IsFinal;

    // Action names (resolved from MachineMetadata; null means no action)
    public string? OnEntryAction;
    public string? OnExitAction;
    public string? ActivityAction;
    public string? TimerAction;

    public List<ushort> DeferredEventIds { get; } = new();
    public byte OutputLaneMask;

    // Editor-only (persisted in layout method)
    public Vector2 Position;
    public Vector2? SizeOverride;
    public string? Comment;
    public string? ColorOverride;
    public bool IsBreakpoint;
    public bool IsCollapsed;

    public Guid HiddenOutputPinId => DeriveOutputPinId(StableId);
    public Guid HiddenInputPinId  => DeriveInputPinId(StableId);

    public StateNode(string name)
    {
        Name = name;
        StableId = Guid.NewGuid();  // replaced by projector if layout provides one
    }

    internal static Guid DeriveOutputPinId(Guid stableId)
    {
        var bytes = stableId.ToByteArray();
        bytes[3] = (byte)(bytes[3] ^ 0x01);
        return new Guid(bytes);
    }

    internal static Guid DeriveInputPinId(Guid stableId)
    {
        var bytes = stableId.ToByteArray();
        bytes[3] = (byte)(bytes[3] ^ 0x02);
        return new Guid(bytes);
    }

    // ---- INodeModel ----

    public NodeId Id => new NodeId(StableId);

    public NodeKindKey Kind
    {
        get
        {
            if (IsFinal)       return new NodeKindKey(HsmKinds.Final);
            if (IsDeepHistory) return new NodeKindKey(HsmKinds.DeepHistory);
            if (IsHistory)     return new NodeKindKey(HsmKinds.History);
            if (IsParallel)    return new NodeKindKey(HsmKinds.Parallel);
            if (Children.Count > 0) return new NodeKindKey(HsmKinds.Composite);
            return new NodeKindKey(HsmKinds.Simple);
        }
    }

    public string Title => Name;
    public string? Subtitle => null;
    public NodeCategory Category => NodeCategory.Custom;
    public NodeState State => IsBreakpoint ? NodeState.Warning : NodeState.Normal;
    public string? StatusTooltip => null;
    public bool ShowAdvancedPins => false;

    private IReadOnlyList<IPinModel>? _pins;
    public IReadOnlyList<IPinModel> Pins => _pins ??= BuildPins();

    private IReadOnlyList<IPinModel> BuildPins()
    {
        return new IPinModel[]
        {
            new HsmPinModel(new PinId(HiddenOutputPinId), new NodeId(StableId), PinDirection.Output),
            new HsmPinModel(new PinId(HiddenInputPinId),  new NodeId(StableId), PinDirection.Input),
        };
    }

    public NodeId? ParentContainerId =>
        Parent?.Parent != null ? new NodeId(Parent!.StableId) : (NodeId?)null;

    // ---- IContainerNodeModel ----

    public bool IsContainer => Children.Count > 0 || IsParallel;

    public IReadOnlyList<NodeId> ChildNodeIds =>
        Children.Select(c => new NodeId(c.StableId)).ToList();

    public IReadOnlyList<RegionDescriptor> Regions
    {
        get
        {
            if (!IsParallel || RegionNodes.Count == 0)
                return Array.Empty<RegionDescriptor>();
            return RegionNodes
                .Select(r => new RegionDescriptor(r.RegionIndex, r.Name, r.Priority, null))
                .ToList();
        }
    }

    public int GetRegionIndexForChild(NodeId childId)
    {
        var child = Children.FirstOrDefault(c => c.StableId == childId.Value);
        if (child == null) return -1;
        return child.RegionIndex;
    }

    public ContainerPadding Padding => ContainerPadding.Default;

    public Vector2 MinimumInteriorSize =>
        IsParallel ? new Vector2(280f, 120f) : new Vector2(200f, 80f);

    public RegionLayoutOrientation RegionOrientation => RegionLayoutOrientation.VerticalStack;
}

public enum TransitionKind { External, Internal, Local }

/// <summary>
/// Editor-side representation of a transition between two states.
/// </summary>
public sealed class TransitionNode
{
    public Guid VisualId;
    public ushort FlatIndex;

    public StateNode Source = null!;
    public StateNode Target = null!;
    public ushort EventId;
    public string? EventName;
    public string? GuardFunction;
    public string? ActionFunction;
    public byte Priority;
    public TransitionKind Kind;
    public ushort SyncGroupId;

    public List<Vector2> Waypoints { get; } = new();
    public string? Comment;
    public bool IsBreakpoint;
}

/// <summary>
/// Editor-side representation of a global (unconditional-source) transition.
/// </summary>
public sealed class GlobalTransitionNode
{
    public Guid VisualId;
    public ushort FlatIndex;
    public StateNode Target = null!;
    public ushort EventId;
    public string? EventName;
    public string? GuardFunction;
    public string? ActionFunction;
    public byte Priority;
    public string? Comment;
    public bool IsBreakpoint;
}

/// <summary>
/// Editor-side representation of an orthogonal region within a parallel state.
/// </summary>
public sealed class RegionNode
{
    public Guid StableId;
    public byte RegionIndex;
    public string Name;
    public byte Priority;
    public StateNode? InitialChild;

    public string? Comment;
    public string? ColorOverride;

    public RegionNode(string name)
    {
        Name = name;
        StableId = Guid.NewGuid();
    }
}

/// <summary>
/// Editor-side representation of an event declared in the state machine.
/// </summary>
public sealed class EventDefinition
{
    public ushort EventId;
    public string Name;
    public int PayloadSize;
    public bool IsIndirect;
    public bool IsDeferrable;
    public bool HasGlobalTransition;

    public EventDefinition(string name, ushort eventId)
    {
        Name = name;
        EventId = eventId;
    }
}
``` [1, 2, 4-10]
````

please detail **4\. Command Sink Wiring**

The command sink must process the container-specific `GraphCommand` records emitted by the canvas during structural modifications and apply them directly to the underlying asset. For container nodes, this involves managing parent-child relationships, local coordinate space transitions, and orthogonal region integrity.

You must implement handlers for the following container commands within your `IGraphCommandSink`:

**1\. ChangeParent and ChangeParentMultiple** Emitted when the user drops one or more nodes into a container, removes them from a container, or moves them across region boundaries. The sink must detach the child from its old parent, attach it to the new parent (if any), update its `RegionIndex`, and apply the new local coordinate `Position` relative to the target container's interior space. `ChangeParentMultiple` processes a list of these moves atomically during multi-node drag-and-drop.

**2\. SetContainerCollapsed** Emitted when the user clicks the chevron in a container's header strip. The sink toggles the `IsCollapsed` property on the container node, prompting the canvas to hide its interior elements and collapse its bounds on the next render pass.

**3\. AddRegion / RemoveRegion / ReorderRegions** Emitted during structural edits to parallel states.

-   `AddRegion` inserts a new region at the specified index and reindexes to ensure contiguous ordering.-   `RemoveRegion` removes the region and evaluates the embedded `ChildRedistributionPolicy`. You must execute the policy to either delete the orphaned children, move them to the first remaining region, or promote them out of the region entirely.-   `ReorderRegions` applies a new index array to the region list and forces a reindex.

Below is the implementation for the previously stubbed `ChangeParent` and `ChangeParentMultiple` operations within the `HsmCommandSink`:

    ```
    private void ApplyChangeParent(GraphCommand.ChangeParent cmd)
    {
        var node = _asset.FindStateByStableId(cmd.NodeId.Value);
        if (node == null) return;

        // 1. Detach from old parent
        if (node.Parent != null)
        {
            node.Parent.Children.Remove(node);
        }

        // 2. Attach to new parent
        if (cmd.NewParentContainerId.HasValue)
        {
            var newParent = _asset.FindStateByStableId(cmd.NewParentContainerId.Value.Value);
            if (newParent != null)
            {
                node.Parent = newParent;
                newParent.Children.Add(node);
            }
            else
            {
                node.Parent = null; // Fallback to root
            }
        }
        else
        {
            node.Parent = null;
        }

        // 3. Update region assignment and local position
        node.RegionIndex = cmd.NewRegionIndex.HasValue ? (byte)cmd.NewRegionIndex.Value : (byte)0;
        node.Position = cmd.NewLocalPosition;
    }

    private void ApplyChangeParentMultiple(GraphCommand.ChangeParentMultiple cmd)
    {
        // Execute sequentially; the canvas guarantees topological safety in the provided move list.
        foreach (var move in cmd.Moves)
        {
            var singleCmd = new GraphCommand.ChangeParent(
                move.NodeId,
                move.NewParentContainerId,
                move.NewRegionIndex,
                move.NewLocalPosition);
            
            ApplyChangeParent(singleCmd);
        }
    }
    
    private void ApplySetContainerCollapsed(GraphCommand.SetContainerCollapsed cmd)
    {
        var node = _asset.FindStateByStableId(cmd.ContainerId.Value);
        if (node != null)
        {
            node.IsCollapsed = cmd.IsCollapsed;
        }
    }
```

The `ChangeParent` command handles the coordinate space switch organically because the `NewLocalPosition` provided by the `NodeEditor` canvas has already been transformed into the target container's interior coordinate space. Following these mutations, the `HsmAsset` is marked dirty, triggering a full topological update in the canvas to visually reflow the container bounds.

what else is left to detail Container nodes?

The host-side domain wiring for Container Nodes is complete. You must now implement the core `NodeEditor` canvas extensions to support these primitives. The remaining implementation details cover layout, rendering, interaction, and theme modifications:

**1\. Visual Rendering Pipeline and Z-Order** You must split container rendering across multiple passes. Container fills, headers, and outlines are drawn at step 3 (before wires), while children are drawn recursively at step 5 (after wires). You must implement a `ContainerRenderer` that handles the header collapse chevron, region dividers (dashed lines), and applies a clip rect to the interior area before drawing children. Below a 0.5 zoom threshold, containers render as solid colored rectangles with faint region dividers.

**2\. Layout and Auto-Resize Computation** You must implement layout engines (e.g., `ContainerBoundsComputer` and `RegionLayoutComputer`) to compute container bounds. The outer bounds enclose all child node bounds plus padding and header height. The container must auto-resize when children are added, removed, moved, or resized. For parallel states, the interior is divided into equal strips representing regions, with headers taking up 18 px each.

**3\. Hit Testing and Interaction** You must inject container zones into the hit-test priority. The priority order places the container collapse chevron and header strip above node bodies, while the empty container interior sits below node bodies but above comment bodies and empty canvas. You must also modify marquee selection: dragging inside a container selects its children, and holding `Alt` scopes the marquee strictly to that container's children.

**4\. Drag-and-Drop and Cycle Detection** You must compute drop targets during node drags. If the cursor is over a container's interior or a specific region, it becomes the target. You must implement a `ContainerCycleDetector` to reject drops that would parent a container into itself or its descendants. When dropped, you must convert the node's canvas-absolute position to parent-local coordinates.

**5\. Link Routing for Collapsed Containers** Links crossing container boundaries route using canvas-coordinate pin positions without clipping. You must update the wire renderer to handle hidden endpoints: when a container is collapsed, wires connecting to its children must terminate at the container's boundary with a visual dot indicator and a hover tooltip.

**6\. Theme Additions** You must extend `IEditorTheme` with container-specific geometry and appearance values. This includes properties such as `ContainerCornerRadius` (default 6px), `ContainerOutlineWidth` (2px), `ContainerInteriorAlpha` (0.08), `ContainerRegionHeaderHeight` (18px), and `ContainerRegionDividerWidth` (1px).

wait isnt this all already implemented in the NodeEdit library, available out of the box?

The container nodes extension is already fully implemented within the `NodeEditor` library. The previous breakdown of rendering, layout, and hit-testing described internal `NodeEditor` mechanics that do not need to be rebuilt in the subsystem host.

The `NodeEditor` library provides the `IContainerNodeModel` interface, `ContainerRenderer`, `ContainerBoundsComputer`, and `ContainerCycleDetector` natively. The canvas automatically handles Z-order rendering, hit-testing, marquee selection scoping, and boundary link routing for any node that implements the container interface.

To utilize this out-of-the-box functionality, the HSM editor subsystem is strictly responsible for two domain-level integrations:

-   **Data Model Opt-In:** The domain node (`StateNode`) must implement `IContainerNodeModel` to expose its `ChildNodeIds`, `Regions`, and `MinimumInteriorSize` to the canvas. The canvas uses this interface to automatically trigger container layout, region division, and bounds auto-resizing without any host-side layout calculations.-   **Command Sink Dispatch:** The subsystem's `IGraphCommandSink` must intercept the container-specific `GraphCommand` records (`ChangeParent`, `SetContainerCollapsed`, `AddRegion`, `RemoveRegion`, `ReorderRegions`) emitted by the canvas interactions and apply those structural mutations directly to the underlying `HsmAsset`.

is that now all to container nodes? can we move to "3. Custom Canvas Renderers"?

The Container Nodes implementation relies strictly on the native `NodeEditor` mechanics via the `IContainerNodeModel` opt-in, so the host-side wiring is complete. We will proceed to Custom Canvas Renderers.

The HSM editor registers five custom canvas renderers to draw statechart semantics that fall outside the standard node, link, and attachment primitives. Custom renderers implement `ICustomCanvasRenderer` to draw into the ImGui draw list at specific passes (e.g., `BeforeContent`, `AfterWires`, `AfterNodes`, `TopMost`). To support interactivity, they additionally implement `ICustomCanvasHitTester` and `ICustomCanvasSelectable`.

You must implement and register the following five renderers for the HSM host:

**1\. hsm.transition\_labels**

-   **Pass:** `AfterWires`.-   **Behavior:** Renders `Event[Guard]/Action` labels at the midpoint of each transition's Bezier curve. For internal transitions, the renderer must detect the `Internal` kind and shift the label placement to draw next to a dashed loop inside the source state rather than at a standard link midpoint.-   **Interaction:** Hit-testable. Clicking the label selects the underlying transition link.

**2\. hsm.initial\_state\_arrows**

-   **Pass:** `AfterNodes`.-   **Behavior:** For each composite state and orthogonal region, draws a small filled circle (6px radius) in the interior top-left, with an arrow pointing to the initial child state's left edge. Additionally, when exactly one transition link is selected in the canvas, this renderer computes the Least Common Ancestor (LCA) composite state between the source and target, and draws a gold highlight outline around that LCA.-   **Interaction:** Not hit-testable; the marker is purely informational.

**3\. hsm.region\_conflicts**

-   **Pass:** `AfterNodes`.-   **Behavior:** When validation detects two states in different orthogonal regions writing to the same `CommandLane`, this draws a 1.5px yellow warning line connecting the centers of the conflicting states, with a ⚠ glyph at the midpoint.-   **Interaction:** Hit-testable. Clicking the warning glyph or line opens a popup detailing the conflict and offering suppression actions.

**4\. hsm.history\_glyphs**

-   **Pass:** `AfterNodes`.-   **Behavior:** Replaces standard node bodies with 20px circular glyphs: "H" for shallow history, "H\*" for deep history, and "⊙" for final pseudo-states. This operates as a render bypass; the host must map the `Category` of these pseudo-states to a transparent node body color in the theme so only this custom glyph is visible.-   **Interaction:** Hit-testable. The hit area is 24px (padded), and selecting the glyph selects the underlying `StateNode`.

**5\. hsm.runtime\_overlay**

-   **Pass:** `AfterNodes`.-   **Behavior:** Renders an active-configuration glow (teal) on active leaf states and their ancestors, a gold pulse marker on recently fired transitions, and breakpoint markers.-   **Interaction:** If the debug session is detached (`ctx.DebugSession?.IsAttached == false`), `IsActive` must return `false` so the canvas skips this renderer entirely to avoid overhead during authoring.

**Registration Order** Renderers within the same pass execute in their registration order, meaning later renderers draw on top of earlier ones. You must register the `AfterNodes` renderers in exactly this sequence:

-   `hsm.initial_state_arrows`-   `hsm.region_conflicts` (draws over initial-state arrows)-   `hsm.history_glyphs` (draws over conflicts, as glyphs are their own visual surface)-   `hsm.runtime_overlay` (ephemeral overlay, must draw over everything else).

where are these detailed in the design documents?

The custom canvas renderers for the HSM subsystem are detailed in the `HSM_Editor_NodeEditor_Host_Design.md` document. They are consolidated in Section 15, "Custom canvas renderers — full list", with specific semantic behaviors defined alongside their related features throughout the document:

-   **hsm.transition\_labels**: Detailed in Section 15.1.-   **hsm.initial\_state\_arrows**: Detailed in Section 8.1 (Initial-state markers).-   **hsm.region\_conflicts**: Detailed in Section 15.3.-   **hsm.history\_glyphs**: Detailed in Section 8.2 (History pseudo-states) and Section 8.3 (Final states).-   **hsm.runtime\_overlay**: Outlined in Section 15.5, which references the runtime debug session overlay specifications from Section 13.4.

The required registration sequence, which dictates the z-order for the `AfterNodes` pass, is strictly defined at the end of Section 15.

ok, if that is enough details to implement custom renderer, can we now move to "3. Validation Pipelines"?

The HSM validation pipeline evaluates the `HsmAsset` for structural and semantic correctness. You must implement `HsmAssetValidator` to adapt the domain-specific `HsmValidator` to the shared `IAssetValidator` interface, ensuring HSM diagnostics properly aggregate in the shared `DiagnosticsWindow`.

Validation runs automatically on asset open, after hot reload completion, on demand, and incrementally after each `GraphCommand` mutation. The `HsmValidator` enforces the following rules:

-   **Initial States:** A composite state must have exactly one initial child; having zero or multiple initial children emits an Error.-   **History States:** A history pseudo-state must reside inside a composite state; placing it at the root or within a parent that lacks multiple children emits a Warning.-   **Final States:** Final states must be leaf nodes with no children and must not possess any outgoing transitions; violating either condition emits an Error.-   **State Depth:** The depth of any state relative to the root must not exceed the kernel limit of 16, triggering an Error if exceeded.-   **Dangling Events:** Transitions or global transitions referencing an `EventId` not present in the asset's event table emit an Error.-   **Output Lane Conflicts:** If descendants in two different parallel regions of the same composite state write to the same `CommandLane`, a Warning is emitted.-   **Blackboard Conflicts:** If sub-trees in different parallel regions of the same composite write to the same master blackboard variable via alias bindings, a Warning is emitted indicating non-deterministic concurrent writes.

To detect Output Lane conflicts, the validator walks all parallel composite states, computes the bitwise OR union of the `OutputLaneMask` for all leaf states within each distinct region, and performs a bitwise AND across the regional masks. If the result is non-zero, it surfaces a diagnostic specifically targeting the states causing the conflict.

Diagnostics are surfaced to the user through three distinct mechanisms:

-   Affected states receive colored outlines mapped to standard `NodeState` flags (e.g., red for Error, yellow for Warning).-   The Inspector window displays the diagnostic message banner directly within the facet of the selected node.-   The `hsm.region_conflicts` custom canvas renderer explicitly draws yellow connector lines and a warning glyph between conflicting states across regions.

can you show the implementation, at least conceptually if it is longer that few pages?

The implementation of the five custom canvas renderers requires implementing the `ICustomCanvasRenderer` interface (and `ICustomCanvasHitTester` where interaction is required) to inject draw calls into the ImGui pipeline at specific render passes.

Below are the conceptual implementations for each renderer, detailing their specific rendering logic and data bindings.

1\. Transition Labels (`hsm.transition_labels`)

Registered at `AfterWires`, this renderer iterates visible links to draw the text label. It handles both standard link midpoints and internal transitions (which are drawn as a dashed loop inside the source state).

```
public sealed class HsmTransitionLabelRenderer : ICustomCanvasRenderer
{
    private readonly HsmAsset _asset;
    public string Id => "hsm.transition_labels";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterWires;
    public bool IsActive { get; set; } = true;

    public void Render(ICanvasRenderContext ctx)
    {
        if (ctx.IsLowZoom) return;
        var drawList = ctx.DrawList;

        foreach (var linkId in ctx.VisibleLinks)
        {
            var t = _asset.FindTransitionByVisualId(linkId.Value);
            if (t is null) continue;

            string label = FormatLabel(t);

            if (t.Kind == TransitionKind.Internal)
            {
                // Draw a small self-loop arc in the upper-right quadrant of the source state [3].
                var stateSize = t.Source.SizeOverride ?? new Vector2(120f, 40f);
                var stateMin = ctx.Viewport.GraphToScreen(t.Source.Position);
                var loopCenter = stateMin + new Vector2(stateSize.X * 0.75f, stateSize.Y * 0.25f);
                float loopRadius = 10f * ctx.Zoom;
                
                uint loopColor = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.2f, 0.9f));
                drawList.AddCircle(loopCenter, loopRadius, loopColor, 16, 1.5f * ctx.Zoom);
                drawList.AddText(loopCenter + new Vector2(loopRadius + 2f, -8f * ctx.Zoom), loopColor, label);
            }
            else
            {
                // Draw label at midpoint between source and target state positions [4].
                var mid = ctx.Viewport.GraphToScreen((t.Source.Position + t.Target.Position) * 0.5f);
                uint textColor = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f));
                drawList.AddText(mid, textColor, label);
            }
        }
    }
}
```

2\. Initial State Arrows (`hsm.initial_state_arrows`)

Registered at `AfterNodes`, this renderer draws markers for initial states and outlines the Least Common Ancestor (LCA) when a transition is selected.

```
public sealed class HsmInitialArrowRenderer : ICustomCanvasRenderer
{
    private static readonly Vector4 LcaHighlightColor = new(1.00f, 0.85f, 0.00f, 0.55f);
    private readonly HsmAsset _asset;

    public string Id => "hsm.initial_state_arrows";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public void Render(ICanvasRenderContext ctx)
    {
        // ... (Draw filled circle + arrow to initial child for each composite state) ...

        // LCA highlight: when exactly one transition link is selected, outline its LCA [6].
        foreach (var linkId in ctx.Selection.Links)
        {
            var transition = _asset.FindTransitionByVisualId(linkId.Value);
            if (transition?.Source is null || transition.Target is null) continue;

            var lca = FindLca(_asset, transition.Source, transition.Target);
            if (lca == _asset.RootState) continue;

            var size = lca.SizeOverride ?? new Vector2(120f, 40f);
            var min  = ctx.Viewport.GraphToScreen(lca.Position);
            var max  = ctx.Viewport.GraphToScreen(lca.Position + size);
            
            ctx.DrawList.AddRect(min, max, ImGui.GetColorU32(LcaHighlightColor),
                rounding: 4f * ctx.Zoom, flags: ImDrawFlags.None, thickness: 1.5f * ctx.Zoom);
        }
    }

    private static StateNode FindLca(HsmAsset asset, StateNode a, StateNode b) { /* ... */ }
}
```

3\. Region Conflicts (`hsm.region_conflicts`)

Registered at `AfterNodes`, this renderer implements `ICustomCanvasHitTester` to allow interaction with the ⚠ glyphs. It parses diagnostic outputs and connects conflicting states with a yellow line.

```
public sealed class HsmRegionConflictsRenderer : ICustomCanvasRenderer, ICustomCanvasHitTester
{
    private readonly HsmAsset _asset;
    private IReadOnlyList<HsmDiagnostic>? _diagnostics;
    internal readonly List<(Vector2 GraphPos, string Key)> _glyphPositions = new();

    public string Id => "hsm.region_conflicts";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public void Render(ICanvasRenderContext ctx)
    {
        _glyphPositions.Clear();
        if (_diagnostics is null || _diagnostics.Count == 0) return;

        uint lineColorU32 = ImGui.GetColorU32(new Vector4(1f, 0.8f, 0f, 0.8f));
        uint textColorU32 = ImGui.GetColorU32(new Vector4(1f, 0.8f, 0f, 1f));

        foreach (var diag in _diagnostics)
        {
            if (diag.Code != HsmDiagnosticCode.OutputLaneConflict || diag.TargetStableIds.Count < 2) continue;

            var stateA = _asset.FindStateByStableId(diag.TargetStableIds);
            var stateB = _asset.FindStateByStableId(diag.TargetStableIds[12]);
            if (stateA is null || stateB is null) continue;

            var centerA = ctx.Viewport.GraphToScreen(stateA.Position + (stateA.SizeOverride ?? new Vector2(120f, 40f)) * 0.5f);
            var centerB = ctx.Viewport.GraphToScreen(stateB.Position + (stateB.SizeOverride ?? new Vector2(120f, 40f)) * 0.5f);

            ctx.DrawList.AddLine(centerA, centerB, lineColorU32, 1.5f * ctx.Zoom);

            var mid = (centerA + centerB) * 0.5f;
            ctx.DrawList.AddText(mid, textColorU32, "!");

            // Record graph-space midpoint for hit testing [13]
            _glyphPositions.Add((ctx.Viewport.ScreenToGraph(mid), $"conflict_{stateA.StableId}_{stateB.StableId}"));
        }
    }

    public CustomElementHit? HitTest(Vector2 canvasPoint, IHitTestContext ctx)
    {
        const float HitRadius = 8f;
        foreach (var (graphPos, key) in _glyphPositions)
        {
            var screenPos = ctx.Viewport.GraphToScreen(graphPos);
            if (MathF.Abs(canvasPoint.X - screenPos.X) <= HitRadius && MathF.Abs(canvasPoint.Y - screenPos.Y) <= HitRadius)
            {
                return new CustomElementHit(key, CustomElementKind.Standalone,
                    new RectF(screenPos - new Vector2(HitRadius), new Vector2(HitRadius * 2f)));
            }
        }
        return null;
    }
}
```

4\. History Glyphs (`hsm.history_glyphs`)

Registered at `AfterNodes`, this visual replacement relies on the underlying node body color being configured to transparent in `HsmEditorTheme` so it can draw a custom circular node shape for pseudo-states.

```
public sealed class HsmHistoryGlyphsRenderer : ICustomCanvasRenderer
{
    private readonly HsmAsset _asset;
    public string Id => "hsm.history_glyphs";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public void Render(ICanvasRenderContext ctx)
    {
        foreach (var state in _asset.AllStates)
        {
            if (!state.IsHistory && !state.IsDeepHistory && !state.IsFinal) continue;

            string label = state.IsDeepHistory ? "H*" : (state.IsHistory ? "H" : "F");
            var center = ctx.Viewport.GraphToScreen(state.Position + (state.SizeOverride ?? new Vector2(120f, 40f)) * 0.5f);
            float radius = 12f * ctx.Zoom;

            // Filled circle background [15].
            ctx.DrawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.15f, 0.70f)));
            ctx.DrawList.AddCircle(center, radius, ImGui.GetColorU32(ctx.Theme.TextDefault), 16, 2f * ctx.Zoom);
            
            var textOffset = new Vector2(-label.Length * 3.5f, -6f) * ctx.Zoom;
            ctx.DrawList.AddText(center + textOffset, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), label);

            // Selection highlight [16].
            if (ctx.Selection.Nodes.Contains(new NodeId(state.StableId)))
                ctx.DrawList.AddCircle(center, radius, ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 1.0f, 1.0f)), 16, 3f * ctx.Zoom);
        }
    }
}
```

5\. Runtime Overlay (`hsm.runtime_overlay`)

Registered at `AfterNodes`. Only renders when a debug session is actively attached. It computes the visual glow for all ancestors in the currently active configuration and pulses the most recent transition.

```
public sealed class HsmRuntimeOverlayRenderer : ICustomCanvasRenderer
{
    private static readonly Vector4 ActiveLeafColor = new(0.20f, 0.90f, 0.70f, 0.90f);
    private static readonly Vector4 AncestorBase    = new(0.20f, 0.90f, 0.70f, 0.45f);
    private static readonly Vector4 TransitionPulse = new(1.00f, 0.85f, 0.10f, 0.80f);

    private readonly HsmAsset _asset;
    private IHsmDebugSession? _session;

    public string Id => "hsm.runtime_overlay";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public void Render(ICanvasRenderContext ctx)
    {
        var snapshot = _session?.GetCurrentStateSnapshot();
        if (snapshot is null) return;

        // 1. Active-configuration glow on every active leaf and its ancestors [19].
        foreach (var leafStableId in snapshot.ActiveLeafStableIds)
        {
            var leaf = _asset.FindStateByStableId(leafStableId);
            if (leaf is null) continue;

            DrawStateOutline(ctx, leaf, ActiveLeafColor, 2.5f);

            var ancestor = leaf.Parent;
            int depth = 1;
            while (ancestor is not null && ancestor.Parent is not null)
            {
                float alpha = AncestorBase.W / (1 + depth * 0.5f);
                DrawStateOutline(ctx, ancestor, AncestorBase with { W = alpha }, 1.5f);
                ancestor = ancestor.Parent;
                depth++;
            }
        }

        // 2. Recent-transition pulse marker [20].
        if (ctx.IsLowZoom) return;
        var lastFired = _session!.GetRecentTraceHistory(20).OfType<HsmTransitionFired>().LastOrDefault();
        if (lastFired is null) return;

        var srcState = _asset.FindStateByStableId(lastFired.SourceStateStableId);
        if (srcState is not null)
        {
            var midScreen = ctx.Viewport.GraphToScreen(srcState.Position + (srcState.SizeOverride ?? new Vector2(120f, 40f)) * 0.5f);
            ctx.DrawList.AddNgonFilled(midScreen, 6f * ctx.Zoom, ImGui.GetColorU32(TransitionPulse), 4);
        }
    }

    private static void DrawStateOutline(ICanvasRenderContext ctx, StateNode state, Vector4 color, float thickness) { /* ... */ }
}
```

ok this was for HSM, what aboout the similar for "**BTree Validation:** Must flag empty composites, unbound action or condition methods, unresolved subtree references, and orphaned nodes not reachable from the root."

The BTree validation pipeline evaluates the `BehaviorTreeAsset` for structural correctness after each model mutation. Similar to the HSM implementation, you must adapt the domain-specific `BTreeValidator` to the shared `IAssetValidator` interface using `BTreeAssetValidator` so diagnostics aggregate properly in the shared `DiagnosticsWindow`.

The core rules you requested are enforced directly on the editor-side model:

-   **Empty Composites:** A `Sequence`, `Selector`, or `ObserverSelector` with zero children emits a Warning.-   **Unbound Methods:** An `Action` or `Condition` node with an empty `MethodFqn` emits an Error.-   **Unresolved Subtrees:** A `Subtree` node whose `IsResolved` flag is false (meaning the referenced asset is not in the catalog) emits an Error.-   **Orphaned Nodes:** Any node not reachable by traversing the graph from the `Root` node emits a Warning.

Below is the conceptual implementation of the validator, including the specific rule checks requested:

```
using System;
using System.Collections.Generic;
using Fbt;
using Hrot.BTree.Editor.Model;

namespace Hrot.BTree.Editor.Validation;

/// <summary>
/// Validates a BehaviorTreeAsset and returns a list of diagnostics.
/// </summary>
public sealed class BTreeValidator
{
    private const int MaxAllowedDepth = 8;

    public IReadOnlyList<BTreeDiagnostic> Validate(BehaviorTreeAsset asset)
    {
        var diagnostics = new List<BTreeDiagnostic>();

        CheckComposites(asset, diagnostics);
        CheckLeaves(asset, diagnostics);
        CheckPills(asset, diagnostics);
        CheckDepth(asset, diagnostics);
        CheckCycles(asset, diagnostics);
        CheckOrphanedNodes(asset, diagnostics);

        return diagnostics;
    }

    // 1. Empty Composites
    private static void CheckComposites(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        foreach (var node in asset.Nodes)
        {
            if ((node.KernelType == NodeType.Sequence  ||
                 node.KernelType == NodeType.Selector  ||
                 node.KernelType == NodeType.ObserverSelector) &&
                node.ChildVisualIds.Count == 0)
            {
                out_.Add(new BTreeDiagnostic(
                    node.VisualId,
                    BTreeDiagnosticSeverity.Warning,
                    BTreeDiagnosticCode.EmptyComposite,
                    $"{node.KernelType} has no children."));
            }
        }
    }

    // 2. Unbound Methods & 3. Unresolved Subtrees
    private static void CheckLeaves(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        foreach (var node in asset.Nodes)
        {
            switch (node.KernelType)
            {
                case NodeType.Action:
                    if (node.Action == null || string.IsNullOrEmpty(node.Action.MethodFqn))
                    {
                        out_.Add(new BTreeDiagnostic(
                            node.VisualId,
                            BTreeDiagnosticSeverity.Error,
                            BTreeDiagnosticCode.UnboundActionMethod,
                            "Action node has no bound method."));
                    }
                    break;

                case NodeType.Condition:
                    if (node.Condition == null || string.IsNullOrEmpty(node.Condition.MethodFqn))
                    {
                        out_.Add(new BTreeDiagnostic(
                            node.VisualId,
                            BTreeDiagnosticSeverity.Error,
                            BTreeDiagnosticCode.UnboundConditionMethod,
                            "Condition node has no bound method."));
                    }
                    break;

                case NodeType.Subtree:
                    if (node.Subtree != null && !node.Subtree.IsResolved)
                    {
                        out_.Add(new BTreeDiagnostic(
                            node.VisualId,
                            BTreeDiagnosticSeverity.Error,
                            BTreeDiagnosticCode.UnresolvedSubtree,
                            $"Subtree '{node.Subtree.SubtreeName}' could not be resolved."));
                    }
                    break;
            }
        }
    }

    // 4. Orphaned Nodes
    private static void CheckOrphanedNodes(BehaviorTreeAsset asset, List<BTreeDiagnostic> out_)
    {
        var root = FindRoot(asset);
        if (root == null) return;

        var reachable = new HashSet<Guid>();
        CollectReachable(asset, root, reachable);

        foreach (var node in asset.Nodes)
        {
            if (!reachable.Contains(node.VisualId))
            {
                out_.Add(new BTreeDiagnostic(
                    node.VisualId,
                    BTreeDiagnosticSeverity.Warning,
                    BTreeDiagnosticCode.OrphanedNode,
                    $"Node '{node.VisualId}' is not reachable from the root."));
            }
        }
    }

    private static void CollectReachable(BehaviorTreeAsset asset, BTreeEditorNode node, HashSet<Guid> visited)
    {
        if (!visited.Add(node.VisualId)) return; // already visited (guards against cycles)

        foreach (var childId in node.ChildVisualIds)
        {
            var child = asset.FindNode(childId);
            if (child != null)
                CollectReachable(asset, child, visited);
        }
    }

    private static BTreeEditorNode? FindRoot(BehaviorTreeAsset asset)
    {
        foreach (var node in asset.Nodes)
        {
            if (node.KernelType == NodeType.Root)
                return node;
        }
        return null;
    }
    
    // CheckPills, CheckDepth, CheckCycles omitted for brevity...
}
```

The projection to the shared interface uses `BTreeAssetValidator`:

````
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Validation;

namespace Hrot.BTree.Editor.Validation;

public sealed class BTreeAssetValidator : IAssetValidator
{
    private readonly BTreeValidator _inner;

    public BTreeAssetValidator(BTreeValidator inner)
    {
        _inner = inner;
    }

    public AssetKind SupportedKind => AssetKind.BTree;

    public IReadOnlyList<AssetDiagnostic> Validate(IEditableAsset asset)
    {
        if (asset is not BehaviorTreeAsset btAsset)
            return Array.Empty<AssetDiagnostic>();

        var raw = _inner.Validate(btAsset);
        var result = new List<AssetDiagnostic>(raw.Count);
        foreach (var d in raw)
        {
            result.Add(new AssetDiagnostic(
                AssetId: asset.AssetId,
                AssetName: asset.Name,
                Severity: MapSeverity(d.Severity),
                Code: d.Code.ToString(),
                Message: d.Message));
        }
        return result;
    }

    private static AssetDiagnosticSeverity MapSeverity(BTreeDiagnosticSeverity s) => s switch
    {
        BTreeDiagnosticSeverity.Info    => AssetDiagnosticSeverity.Info,
        BTreeDiagnosticSeverity.Warning => AssetDiagnosticSeverity.Warning,
        BTreeDiagnosticSeverity.Error   => AssetDiagnosticSeverity.Error,
        _                               => AssetDiagnosticSeverity.Error,
    };
}
``` [2, 8].
````

ok, if thast is all to validators, can we move to "4. Debug Sessions and Trace Timelines"?

The BTree runtime debug session is managed by `BTreeDebugSession`, which implements `IBTreeDebugSession` (extending the shared `IAiDebugSession` interface).

**Debug Session State & History** The debug session maintains in-memory ring buffers for `BTreeNodeExecuted` and `BTreeAsyncEvent` history records. It provides access to the current kernel state via `GetCurrentStateSnapshot()`, which returns a `BehaviorTreeStateSnapshot` detailing the `RunningNodeIndex`, `StackPointer`, `LocalRegisters`, `AsyncHandles`, and the fully symbolicated stack ancestry. The session also maintains aggregate entry counts per `VisualId` when heatmap mode is active to support multi-instance debugging.

Breakpoints are per-user and session-local, evaluated per `VisualId`. The kernel evaluates them against the `BehaviorTreeBlob`'s `DebugMetadata` array while running, supporting both break-on-enter and break-on-result behaviors.

**Step-Control Semantics** The base debug step controls are mapped to BTree-specific kernel evaluation semantics:

-   **Continue:** Resumes normal ticking by clearing the pause flag.-   **Pause:** Sets `InstanceFlags.Paused` on the BTree entity's `DebugState`.-   **Step Into:** Advances one tick where execution descends into a child of the currently-running composite.-   **Step Over:** Advances one tick, pausing again at the next node entry at or above the current stack depth.-   **Step Out:** Advances ticks until the running node's stack depth decreases, indicating a subtree return.

**Trace Timeline Lanes** The `BTreeTraceLaneProvider` registers four distinct swim-lanes for visualization within the shared Trace Timeline window:

-   **bt.nodes (NodeStatus):** Bound to `TraceLevel.Lifecycle | TraceLevel.Decisions`. It displays `BTreeNodeExecuted` records as colored bars (green for Success, red for Failure, and yellow for Running).-   **bt.stack (Stack):** Bound to `TraceLevel.Lifecycle`. It visualizes subtree push/pop events as bracketed ranges.-   **bt.async (Async):** Bound to `TraceLevel.Async`. It displays `BTreeAsyncEvent` records, coloring them by phase: blue for issued, green for resolved, and red for aborted.-   **bt.errors (Errors):** Bound to `TraceLevel.Errors`. It displays missing-method errors and tracer overflows.

In Replay mode, clicking any record across these lanes immediately jumps the canvas overlay's scrub cursor to that specific tick.

pls implemnet the BTreeDebugSession, at least conceptually

The `BTreeDebugSession` is the production implementation of `IBTreeDebugSession`, inheriting from `AiDebugSessionBase`. It maintains in-memory ring buffers for node-execution and async-event history, polls the ECS world for trace records and state snapshots, and translates high-level step controls into BTree-specific semantics.

Below is the conceptual implementation detailing the session state, ECS polling, kernel adapter hooks, and step control overrides:

````
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.BTree.Editor.Debug;

public sealed class BTreeDebugSession : AiDebugSessionBase, IBTreeDebugSession
{
    private const int MaxHistory = 200;

    private readonly List<BTreeNodeExecuted> _nodeHistory = new();
    private readonly List<BTreeAsyncEvent>   _asyncHistory = new();
    private bool _heatmapModeActive;
    private readonly Dictionary<Guid, int> _aggregateCounters = new();

    private BehaviorTreeStateSnapshot? _currentSnapshot;
    private ushort _lastReadPos;

    // Debug metadata for node index -> VisualId symbolication.
    private NodeDebugMetadata[]? _debugMetadata;
    private Guid _assetId = Guid.Empty;

    private enum StepMode { None, Over, Into, Out }
    private StepMode _stepMode = StepMode.None;
    private int  _stepFromStackDepth;
    private bool _nodeProcessedSinceStep;

    public event Action<BTreeBreakpointHit>? OnBreakpointHit;
    public event Action<BTreeNodeExecuted>?  OnNodeExecuted;
    public event Action<BTreeAsyncEvent>?    OnAsyncIssued;
    public event Action<BTreeAsyncEvent>?    OnAsyncResolved;
    public event Action<BTreeAsyncEvent>?    OnAsyncAborted;

    public BTreeDebugSession(AiTracerCoordinator? coordinator = null) : base(coordinator) { }

    // ---- IBTreeDebugSession Data Access ------------------------------------

    public BehaviorTreeStateSnapshot? GetCurrentStateSnapshot() => _currentSnapshot;

    public IReadOnlyList<BTreeNodeExecuted> GetRecentNodeHistory(int max = 100)
    {
        int start = Math.Max(0, _nodeHistory.Count - max);
        return _nodeHistory.GetRange(start, _nodeHistory.Count - start);
    }

    public IReadOnlyList<BTreeAsyncEvent> GetRecentAsyncHistory(int max = 100)
    {
        int start = Math.Max(0, _asyncHistory.Count - max);
        return _asyncHistory.GetRange(start, _asyncHistory.Count - start);
    }

    public bool HeatmapModeActive
    {
        get => _heatmapModeActive;
        set => _heatmapModeActive = value;
    }

    public IReadOnlyDictionary<Guid, int>? GetAggregateCounters(Guid assetId)
    {
        if (!IsAttached || !HeatmapModeActive)
            return null;
        return _aggregateCounters;
    }

    public void ResetAggregateCounters() => _aggregateCounters.Clear();

    public void SetDebugMetadata(NodeDebugMetadata[]? metadata, Guid assetId)
    {
        _debugMetadata = metadata;
        _assetId       = assetId;
    }

    internal Guid? TrySymbolicateIndex(int nodeIndex) => GetVisualId(nodeIndex);

    private Guid? GetVisualId(int nodeIndex)
    {
        if (_debugMetadata == null || nodeIndex < 0 || nodeIndex >= _debugMetadata.Length)
            return null;
        string raw = _debugMetadata[nodeIndex].VisualId;
        if (string.IsNullOrEmpty(raw)) return null;
        return Guid.TryParse(raw, out var g) ? g : (Guid?)null;
    }

    // ---- ECS Snapshot + Trace Polling --------------------------------------

    public unsafe void Update(EntityRepository repo, Entity entity)
    {
        // === 1. Snapshot Evaluation ===
        if (!repo.HasComponent<BrainBTreeState>(entity))
        {
            _currentSnapshot = null;
        }
        else
        {
            ref readonly var comp = ref repo.GetComponentRO<BrainBTreeState>(entity);
            ushort runningNodeIndex = comp.State.RunningNodeIndex;
            ushort sp               = comp.State.StackPointer;
            uint   treeVersion      = comp.State.TreeVersion;

            int stackLen = Math.Min(8, (int)sp + 1);
            var stack    = new int[stackLen];
            var stackIds = new Guid?[stackLen];
            var regs     = new int[3];
            var handles  = new ulong[4];

            ref var stateMut = ref Unsafe.AsRef(in comp.State);
            BehaviorTreeState* statePtr = (BehaviorTreeState*)Unsafe.AsPointer(ref stateMut);
            
            for (int i = 0; i < stackLen; i++) stack[i]   = statePtr->NodeIndexStack[i];
            for (int i = 0; i < 4; i++)        regs[i]    = statePtr->LocalRegisters[i];
            for (int i = 0; i < 3; i++)        handles[i] = statePtr->AsyncHandles[i];

            Guid? runningElementId = GetVisualId(runningNodeIndex);
            for (int i = 0; i < stackLen; i++)
                stackIds[i] = GetVisualId(stack[i]);

            _currentSnapshot = new BehaviorTreeStateSnapshot(
                entity, _assetId, runningNodeIndex, runningElementId,
                sp, stack, stackIds, regs, handles, treeVersion);
        }

        // === 2. Trace Polling ===
        if (!repo.HasComponent<BTreeTraceWorkingMemory1024>(entity)) return;

        ref readonly var trace = ref repo.GetComponentRO<BTreeTraceWorkingMemory1024>(entity);
        if (trace.WritePos == _lastReadPos) return;

        ref var traceMut = ref Unsafe.AsRef(in trace);
        BTreeTraceWorkingMemory1024* tracePtr = (BTreeTraceWorkingMemory1024*)Unsafe.AsPointer(ref traceMut);
        byte* bufBase = tracePtr->Buffer;
        ushort pos = _lastReadPos;
        
        while (pos != trace.WritePos)
        {
            var rec = (BTreeTraceRecord*)(bufBase + pos);
            switch (rec->OpCode)
            {
                case BTreeTraceOpCode.NodeEvaluated:
                    _nodeProcessedSinceStep = true;
                    RecordNodeExecuted(new BTreeNodeExecuted(
                        entity, _assetId, GetVisualId(rec->NodeIndex) ?? Guid.Empty,
                        rec->Status, 0f, rec->Timestamp));
                    break;
                case BTreeTraceOpCode.WaitStarted:
                    RecordAsyncEvent(new BTreeAsyncEvent(
                        entity, _assetId, GetVisualId(rec->NodeIndex) ?? Guid.Empty,
                        rec->NodeIndex, 0u, BTreeAsyncPhase.Issued, 0f));
                    break;
                case BTreeTraceOpCode.WaitCompleted:
                    RecordAsyncEvent(new BTreeAsyncEvent(
                        entity, _assetId, GetVisualId(rec->NodeIndex) ?? Guid.Empty,
                        rec->NodeIndex, 0u, BTreeAsyncPhase.Resolved, 0f));
                    break;
            }
            pos = (ushort)((pos + BTreeTraceWorkingMemory1024.RecordStride) % BTreeTraceWorkingMemory1024.PayloadBytes);
        }
        _lastReadPos = trace.WritePos;

        // === 3. Step-mode Auto-Pause Evaluation ===
        if (_stepMode != StepMode.None && _currentSnapshot is not null)
        {
            bool shouldPause = _stepMode switch
            {
                StepMode.Over => _currentSnapshot.StackPointer == _stepFromStackDepth,
                StepMode.Into => _nodeProcessedSinceStep,
                StepMode.Out  => _currentSnapshot.StackPointer < _stepFromStackDepth,
                _             => false
            };
            if (shouldPause)
            {
                _stepMode = StepMode.None;
                Coordinator.RequestPause();
            }
        }
    }

    // ---- Kernel Adapter Hooks ----------------------------------------------

    public void RecordNodeExecuted(BTreeNodeExecuted record)
    {
        if (_nodeHistory.Count >= MaxHistory) _nodeHistory.RemoveAt(0);
        _nodeHistory.Add(record);
        
        if (_heatmapModeActive)
        {
            _aggregateCounters.TryGetValue(record.NodeVisualId, out var prev);
            _aggregateCounters[record.NodeVisualId] = prev + 1;
        }
        OnNodeExecuted?.Invoke(record);
    }

    public void RecordAsyncEvent(BTreeAsyncEvent record)
    {
        if (_asyncHistory.Count >= MaxHistory) _asyncHistory.RemoveAt(0);
        _asyncHistory.Add(record);
        
        switch (record.Phase)
        {
            case BTreeAsyncPhase.Issued:   OnAsyncIssued?.Invoke(record);   break;
            case BTreeAsyncPhase.Resolved: OnAsyncResolved?.Invoke(record); break;
            case BTreeAsyncPhase.Aborted:  OnAsyncAborted?.Invoke(record);  break;
        }
    }

    public void RaiseBreakpointHit(BTreeBreakpointHit hit)
    {
        IsPaused = true;
        PausedAt = hit.Breakpoint;
        PausedOnEntity = hit.Self;
        OnBreakpointHit?.Invoke(hit);
        RaiseSessionStateChanged();
    }

    // ---- AiDebugSessionBase Step Overrides ---------------------------------

    protected override void OnContinueImpl()
    {
        _stepMode = StepMode.None;
        Coordinator.RequestContinue();
    }

    protected override void OnPauseImpl()
    {
        Coordinator.RequestPause();
    }

    protected override void OnStepOverImpl()
    {
        _stepFromStackDepth     = _currentSnapshot?.StackPointer ?? 0;
        _stepMode               = StepMode.Over;
        _nodeProcessedSinceStep = false;
        Coordinator.RequestStepOneTick();
    }

    protected override void OnStepIntoImpl()
    {
        _stepFromStackDepth     = _currentSnapshot?.StackPointer ?? 0;
        _stepMode               = StepMode.Into;
        _nodeProcessedSinceStep = false;
        Coordinator.RequestStepOneTick();
    }

    protected override void OnStepOutImpl()
    {
        _stepFromStackDepth     = _currentSnapshot?.StackPointer ?? 0;
        _stepMode               = StepMode.Out;
        _nodeProcessedSinceStep = false;
        Coordinator.RequestStepOneTick();
    }

    protected override void OnDetachImpl()
    {
        _stepMode               = StepMode.None;
        _nodeProcessedSinceStep = false;
        _currentSnapshot        = null;
        _lastReadPos            = 0;
        _nodeHistory.Clear();
        _asyncHistory.Clear();
        _aggregateCounters.Clear();
        _heatmapModeActive      = false;
    }
}
``` [2, 5-15]
````

pls implement the BTreeTraceLaneProvider , at least conceptually

The `BTreeTraceLaneProvider` implements `ITraceLaneProvider` to register the four distinct swim lanes required for Behavior Tree assets in the shared `TraceTimelineWindow`. It strictly defines the lanes and binds them to specific `TraceLevel` flags to filter the incoming records.

```
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Declares the four trace timeline swim-lanes for BTree assets.
/// Registered at startup so the shared Trace Timeline window can render them.
/// </summary>
public sealed class BTreeTraceLaneProvider : ITraceLaneProvider
{
    private static readonly IReadOnlyList<TraceLaneDescriptor> _lanes =
    [
        new TraceLaneDescriptor("bt.nodes", "NodeStatus", TraceLevel.Lifecycle | TraceLevel.Decisions),
        new TraceLaneDescriptor("bt.stack",  "Stack",     TraceLevel.Lifecycle),
        new TraceLaneDescriptor("bt.async",  "Async",     TraceLevel.Async),
        new TraceLaneDescriptor("bt.errors", "Errors",    TraceLevel.Errors),
    ];

    public AssetKind Kind => AssetKind.BTree;

    public IReadOnlyList<TraceLaneDescriptor> Lanes => _lanes;
}
```

This provider maps to `AssetKind.BTree` so the shared trace timeline automatically resolves these lanes when a Behavior Tree asset is active. Each lane serves a specific diagnostic purpose:

-   `bt.nodes` displays `BTreeNodeExecuted` records as colored bars corresponding to their `NodeStatus` (green for Success, red for Failure, yellow for Running).-   `bt.stack` visualizes subtree push and pop events as bracketed ranges to show execution depth.-   `bt.async` displays `BTreeAsyncEvent` records colored by their lifecycle phase (blue for issued, green for resolved, red for aborted).-   `bt.errors` displays tracer overflows and missing-method errors.

what is now left to detail regarding HSM/Btree blueprint implementation and wiring to the editor?

Based on the implementation roadmap and design documents, the following major components regarding the HSM and BTree editor wiring remain to be detailed:

**1\. Fluent C# Emitters** The implementation of `BTreeFluentEmitter` and `HsmFluentEmitter`, which are responsible for generating deterministic `.cs` files from the mutable `BehaviorTreeAsset` and `HsmAsset` models. This includes emitting the `CreateBuilder()`, `Build()` (the thunk method), and `Layout()` methods.

**2\. Asset Projection and Quick Reload Pipeline** The projection logic that reconstructs the editor models at load time by merging the compiled runtime blobs (`BehaviorTreeBlob`, `HsmDefinitionBlob`), `NodeDebugMetadata`, and the editor-only `Layout` method. This also includes the Quick Reload pipeline, which handles the Cosmetic, Soft, and Hard classification hashes (`StructureHash` and `ParamHash`) and performs the post-reload model reconciliation.

**3\. Inspectors and Facet Structs** How the selected nodes, states, and attachments are mapped to `StructEdit` UI facets (e.g., `BTreeActionFacet`, `StateFacet`, `TransitionFacet`). This includes the implementation of dynamic pickers, such as the `[BehaviorHashPicker]` for BTree methods and the `[HsmActionPicker]` for HSM commands.

**4\. Remaining NodeEditor Host Services** The specific implementations of the remaining `IEditorHostServices` contracts for both subsystems:

-   `INodeCatalog`: Providing static palette entries and dynamic registry entries.-   `ITypeSystem` and `ILinkValidator`: Handling BTree's reversed exec-pin convention to satisfy fan-out rules, and HSM's invisible "any" pins used for routing transitions.

**5\. HSM-Specific Editor Components**

-   **Events Table & Globals Strip:** The UI windows for managing `EventDefinition` records, event scoping, and global transitions.-   **OutputLaneMask Inference:** The static analysis routine that infers the `OutputLaneMask` for each state by reflecting over the `[HsmAction]` declarations.-   **HsmDebugSession:** The implementation of the HSM runtime debugger, including the unified trace-history ring buffer and step-control microstep evaluation, since only the `BTreeDebugSession` has been detailed so far.

**6\. BTree-Specific Editor Components**

-   **Subtree Black Boxes:** The resolution and visual rendering of `Subtree` nodes, including double-click navigation to the referenced asset.-   **Blackboard Reflection:** The reflection model that exposes user-defined blackboard structs and renders the read-only schema in the Blackboard panel.

ok let's detail the **1\. Fluent C# Emitters**

The implementation of the fluent C# emitters mandates strict deterministic C# generation to ensure predictable source-control diffs. Both `BTreeFluentEmitter` and `HsmFluentEmitter` share common rules enforced by `FluentCSharpEmitterBase`.

All using directives must be sorted alphabetically, grouping `System.*` namespaces first, followed by a blank line, and then all other namespaces. The emitter performs byte-comparisons against the existing on-disk file, and if a change is detected, writes the new content atomically to a `.tmp` file before issuing a `File.Move` overwrite. This guarantees the engine's `FileSystemWatcher` never consumes a partially written file.

**BTreeFluentEmitter** The `BTreeFluentEmitter` implements `IFluentCSharpEmitter<BehaviorTreeAsset>` and produces a class containing `CreateBuilder()`, `Build()`, and `Layout()` methods. You must adhere to the following BTree-specific deterministic rules:

-   **Decorator pills:** Wrappers emit outside-in. The outermost decorator pill translates to the outermost fluent call.-   **Formatting:** Lambda bodies for composite children are explicitly declared and indented exactly four spaces deeper than the parent. The `visualId:` argument is strictly positioned as the final parameter on its own line.-   **Literals:** `Wait` durations emit with an `f` suffix (e.g., `1.5f`), and `Repeater` counts emit as integer literals without decimals.-   **Empty Composites:** Composites with no children are emitted as a valid single-line empty lambda (e.g., `seq => { }`).

**HsmFluentEmitter** The `HsmFluentEmitter` implements `IFluentCSharpEmitter<HsmAsset>` to generate `CreateBuilder()`, `Compile()`, and `Layout()` methods. The state machine topology dictates a strict emission sequence within the `CreateBuilder()` block:

-   **Initialization:** Events emit first, sorted by ascending `EventId`. Action and guard registrations follow, sorted alphabetically by their fully qualified names.-   **State Traversal:** States emit in a depth-first order starting from the root state's children. Sibling children emit in the exact model order authored by the user.-   **State Configuration Chain:** A single state's configuration must emit in this exact sequence: `Initial()` → `IsFinal()` → `Parallel()` → `OnEntry()` → `OnExit()` → `Activity()` → `TimerAction()` → `DeferEvent()`. This is immediately followed by outgoing transition bindings, and finally nested child states or orthogonal regions.-   **Transitions:** Links emit using `.On(eventId)` for standard events, or `.OnCompletion()` for triggerless execution.-   **Global Transitions:** Global transitions are appended at the end of the builder, sorted alphabetically by `EventId`.

Which of these aren't yet implemented in the codebase or detailed in this chat?

Of the six major blueprint editor components identified previously, we have only detailed **1\. Fluent C# Emitters** in this chat. The remaining components (Asset Projection and Quick Reload Pipeline, Inspectors and Facet Structs, Remaining NodeEditor Host Services, HSM-Specific Editor Components, and BTree-Specific Editor Components) have not yet been detailed in our conversation.

Reviewing the codebase, the following elements from those categories are currently stubbed or missing entirely:

-   **Fluent C# Emitters:** While the `BTreeFluentEmitter` is fully implemented, the `HsmFluentEmitter` is absent from the codebase.-   **Asset Projection:** The `BehaviorTreeAssetProjector` and `HsmAssetProjector` are referenced by their respective asset contributors, but their actual implementations—which handle the complex mapping from compiled kernel blobs back into mutable editor models—are missing.-   **Remaining NodeEditor Host Services:** The `IEditService`, which is responsible for dispatching edit commands and integrating with the undo/redo stack, is explicitly wired as a `NoOpEditService` stub.-   **HSM-Specific Editor Components:** The `HsmGlobalsStrip` is an incomplete structural shell containing a `// TODO: render the globals strip` comment. Additionally, the static analysis routine required for `OutputLaneMask` inference is missing.-   **BTree-Specific Editor Components:** The `btree.subtree_boundaries` custom canvas renderer, responsible for drawing the visual bounds of executing subtrees, is not implemented in the provided source.

good let's please focus on the **Fluent C# Emitters:** now

The `HsmFluentEmitter` implements `IFluentCSharpEmitter<HsmAsset>` to deterministically produce a `.cs` file representing the state machine. The output consists of three static methods: `CreateBuilder()` for the fluent HSM definition, `Compile()` as the thunk method, and `Layout()` for editor-only canvas positions.

To ensure predictable source-control diffs, the emitter must enforce the following strict deterministic generation rules:

-   **Events and Registrations:** Events must be emitted first, sorted in ascending order by `EventId`. Action and guard registrations follow, sorted alphabetically by their fully qualified names (FQN).-   **State Traversal and Configuration:** States must be emitted in depth-first order starting from the children of the root state. The configuration chain for a single state must follow this exact sequence: `Initial()` → `IsFinal()` → `Parallel()` → `OnEntry()` → `OnExit()` → `Activity()` → `TimerAction()` → `DeferEvent()`.-   **Hierarchy and Regions:** After the state configuration, outgoing transitions are emitted, followed by child states (for standard composites) or regions (for parallel composites). Region configuration emits `.Region(name, priority, stableId)` followed by `.InitialChild(name)` and `.AddChild(name, stableId)` calls.-   **Transitions:** Normal transitions emit using `.On(eventId)`, while completion-trigger transitions emit using `.OnCompletion()`. Global transitions are appended at the end of the builder method, sorted alphabetically by `EventId`.-   **Formatting:** Float literals must use the `f` suffix, `EventId` parameters emit as `ushort` literals without suffixes, and priorities emit as `byte` literals.

The conceptual implementation for the `HsmFluentEmitter` structurally routes these requirements:

```
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Emit;

public sealed class HsmFluentEmitter : IFluentCSharpEmitter<HsmAsset>
{
    private const string LayoutNamespace = "Hrot.Editor.AiShared.Layout";
    private const string Indent          = "    ";

    public string Emit(HsmAsset asset)
    {
        var sb = new StringBuilder();
        var usings = CollectUsings(asset);

        sb.AppendLine(FluentCSharpEmitterBase.BuildHeader(asset.AssetId));

        foreach (var ns in usings)
        {
            if (ns.Length == 0)
                sb.AppendLine(); 
            else
                sb.AppendLine($"using {ns};");
        }
        sb.AppendLine();

        string targetNs = string.IsNullOrEmpty(asset.TargetNamespace)
            ? "Hrot.AI.Behaviors.Machines"
            : asset.TargetNamespace;
        string className = SanitizeIdentifier(asset.Name);

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");

        EmitCreateBuilder(sb, asset);
        sb.AppendLine();
        EmitCompile(sb, asset);
        sb.AppendLine();
        EmitLayout(sb, asset);

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void EmitCreateBuilder(StringBuilder sb, HsmAsset asset)
    {
        sb.AppendLine($"{Indent}public static HsmBuilder CreateBuilder()");
        sb.AppendLine($"{Indent}{{");
        string pad = Indent + Indent;
        sb.AppendLine($"{pad}var builder = new HsmBuilder({QuoteStr(asset.Name)});");

        if (asset.AllEvents.Count > 0)
        {
            sb.AppendLine();
            foreach (var ev in asset.AllEvents.OrderBy(e => e.EventId))
            {
                sb.AppendLine(
                    $"{pad}builder.Event({QuoteStr(ev.Name)}, {ev.EventId}, {ev.PayloadSize}," +
                    $" {BoolStr(ev.IsIndirect)}, {BoolStr(ev.IsDeferrable)});");
            }
        }

        // Action and Guard registrations emitted here...
        
        // Depth-first state traversal emitted here...
        
        if (asset.AllGlobalTransitions.Count > 0)
        {
            sb.AppendLine();
            foreach (var gt in asset.AllGlobalTransitions.OrderBy(g => g.EventId))
            {
                string evRef     = gt.EventName ?? gt.EventId.ToString(CultureInfo.InvariantCulture);
                string targetName = gt.Target?.Name ?? "???";
                sb.AppendLine(
                    $"{pad}builder.GlobalTransition({QuoteStr(evRef)}, {QuoteStr(targetName)}," +
                    $" visualId: new Guid({QuoteStr(gt.VisualId.ToString("D"))}));");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"{pad}return builder;");
        sb.AppendLine($"{Indent}}}");
    }

    private static void EmitCompile(StringBuilder sb, HsmAsset asset)
    {
        sb.AppendLine($"{Indent}[HsmDefinition({QuoteStr(asset.Name)}, AssetId = {QuoteStr(asset.AssetId.ToString("D"))})]");
        sb.AppendLine($"{Indent}public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();");
    }

    private static void EmitLayout(StringBuilder sb, HsmAsset asset)
    {
        sb.AppendLine($"{Indent}[HsmLayout({QuoteStr(asset.AssetId.ToString("D"))})]");
        sb.AppendLine($"{Indent}public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()");
        
        // ... (Emits .State(), .Transition(), and .Region() calls sorted by Guid) ...
        
        var conflictSuppressions = asset.GetConflictSuppressions().OrderBy(s => s.VariableName).ThenBy(s => s.WriterPairKey).ToList();
        foreach (var sup in conflictSuppressions)
        {
            sb.AppendLine($"{Indent}{Indent}.SuppressBlackboardConflict(\"{sup.VariableName}\", \"{sup.WriterPairKey}\")");
        }

        var unusedSuppressions = asset.GetUnusedSuppressions().OrderBy(s => s).ToList();
        foreach (var sup in unusedSuppressions)
        {
            sb.AppendLine($"{Indent}{Indent}.SuppressUnusedWarning(\"{sup}\")");
        }

        sb.AppendLine($"{Indent}{Indent}.Build();");
    }
    
    // ... (Helpers and Usings collection omitted) ...
}
```

The resulting `Layout()` method strictly stores editor-only parameters (positions, waypoints, sizes, comments, and specific diagnostic suppressions) without bleeding into the runtime execution kernel. The emitter runs self-tests comparing the original model to a Roslyn-parsed interpretation of the emitted string to guarantee byte-identical source output across runs.

but this one is already in the codebase, isn't it?

Yes, `HsmFluentEmitter` is already fully implemented in the codebase within the `Hrot.Hsm.Editor.Emit` namespace. It implements the `IFluentCSharpEmitter<HsmAsset>` interface to deterministically produce a `.cs` file containing the `CreateBuilder()`, `Compile()`, and `Layout()` methods.

The existing implementation correctly enforces the strict deterministic generation rules:

-   **Usings:** It scans all states, transitions, and global transitions to collect namespaces from action and guard fully-qualified names (FQNs), stripping class/method components and sorting the directives.-   **Initialization:** Inside `CreateBuilder()`, it first emits events sorted in ascending order by their `EventId`.-   **Registrations:** It aggregates all referenced actions and guards into a `SortedSet<string>` (using `Ordinal` string comparison) to ensure they are emitted in strict alphabetical order before the state definitions.-   **State Traversal:** The `Compile()` and `Layout()` methods are properly emitted to complete the file structure.

_(Note: My previous statement that_ _HsmFluentEmitter_ _was missing from the codebase was incorrect, as it is present in_ _Hrot/Subsystems/AI/Hrot.Hsm.Editor/Emit/HsmFluentEmitter.cs__)._

what else is already implemented, can you recheck?

Upon re-examining the codebase, several components previously identified as missing are in fact already implemented.

The following major components are fully implemented and wired:

-   **Asset Projection:** Both `BehaviorTreeAssetProjector` and `HsmAssetProjector` are fully implemented. They project compiled `BehaviorTreeBlob` and `HsmDefinitionBlob` runtime data, alongside `NodeDebugMetadata` and layout attributes, back into the mutable `BehaviorTreeAsset` and `HsmAsset` editor models.-   **Inspectors and Facet Structs:** BTree facet structs (e.g., `BTreeActionFacet`, `BTreeUntilFailureFacet`) are implemented. HSM inspector attributes (e.g., `[HsmActionPicker]`, `[HsmStateSelector]`, `[HsmSyncGroupPicker]`) are fully wired to `StructEdit`.-   **NodeEditor Host Services:** The core host services for both subsystems are implemented. This includes `BTreeNodeCatalog` and `HsmNodeCatalog` for palette entries, `BTreeTypeSystem` and `HsmTypeSystem`, and `BTreeLinkValidator` and `HsmLinkValidator`.-   **HSM OutputLaneMask Inference:** The `HsmOutputLaneMaskInferrer` is implemented to reflect over loaded assemblies and build mappings from action FQNs to command lanes.-   **Runtime Debug Sessions:** Both `BTreeDebugSession` and `HsmDebugSession` are implemented and maintain ring buffers for trace history while providing current state snapshots.

The components that remain genuinely stubbed or missing in the codebase are:

-   **InMemoryRoslynCompiler:** The `CompileAndLoad` method currently throws a `NotImplementedException`. It is marked as a stub for Phase 1, with the full implementation deferred to Phase 3.-   **IEditService:** Implemented purely as a `NoOpEditService` stub, meaning full undo/redo integration via the command sink is deferred to M5.-   **HsmGlobalsStrip:** The file exists but acts as a structural shell containing a `// TODO: render the globals strip` comment.-   **Subtree Boundaries Renderer:** The `btree.subtree_boundaries` custom canvas renderer is specified in the design but not implemented in the provided source files.

But the `InMemoryRoslynCompiler` is fully implementedm no? The functional implementation is located in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Roslyn/InMemoryRoslynCompiler.cs`. This class correctly utilizes Roslyn to compile C# source strings in memory, emits the resulting PE and PDB byte arrays along with embedded source text for debugging, and loads the compiled assembly into a new collectible `AssemblyLoadContext`.

You are correct. The codebase contains two distinct classes named `InMemoryRoslynCompiler`, reflecting the phased implementation roadmap, which caused the earlier misidentification.

The file `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/InMemoryRoslynCompiler.cs` is the Phase 1 stub that throws a `NotImplementedException`.

The file you referenced, `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Roslyn/InMemoryRoslynCompiler.cs`, contains the fully functional Phase 3 implementation. This version correctly sets up the Roslyn `CSharpCompilation` with `OptimizationLevel.Debug` and `deterministic: true`, embeds the source text via `EmbeddedTextHelper.Create`, emits the `PortablePdb` format, and loads the resulting PE and PDB streams into a new collectible `AssemblyLoadContext`.

With this confirmed, the only major elements from the previously identified list that remain genuinely stubbed or missing in the codebase are the `IEditService` undo/redo integration (currently a `NoOpEditService`), the `HsmGlobalsStrip` rendering shell, and the BTree `btree.subtree_boundaries` custom canvas renderer.

regarding the IEditService, what is this used for? Isn't there NodeEdit implementationproviding full undo/redo support for all graph editing?

The `NodeEditor` library handles undo/redo exclusively for canvas-level topology and spatial mutations—such as moving nodes, adding or removing links, creating comments, and rerouting wires—through its internal `UndoStack` and `IGraphCommandSink`.

The `IEditService` (`Hrot.Blueprints.Editor.NodeDrawers.IEditService`) serves a distinct purpose: it is the command dispatcher for _property edits_ made inside node drawers and the Inspector window via `StructEdit`.

While `NodeEditor` manages the graph layout, modifying a specific node's internal state (e.g., changing a float parameter, updating a string, or editing a list inside a `PlayMontageChainNodeSession`) occurs outside the standard `GraphCommand` pipeline. To make these property edits undoable via Ctrl+Z, they must be wrapped into discrete commands and routed into the editor's undo stack.

Because `IEditService` is currently implemented as a `NoOpEditService` stub, property edits made via the Inspector bypass the undo stack entirely. Instead, they mutate the underlying data directly and call `MarkDirty()` to trigger C# file regeneration. Full undo/redo integration for property modifications within node drawers is explicitly deferred to Milestone 5 (M5).

is IEditService related to btree/hsm or to "scripting" like instance blueprintss?

`IEditService` belongs exclusively to the Blueprint subsystem editor (`Hrot.Blueprints.Editor.NodeDrawers.IEditService`). It is responsible for dispatching property edits made inside Blueprint node drawers, such as those for `WhenNode` or `PlayMontageChainNode`, rather than graph topology changes.

Therefore, it is directly related to Blueprint assets, which encompass Instance Blueprints used for scripting, as well as Library and AiPrimitive Blueprints.

The native BTree and HSM editors do not use `IEditService`. Instead, they rely on their own dedicated command sinks (`BTreeCommandSink` and `HsmCommandSink`) to process graph mutations and property edits for their specific domain models. However, because AiPrimitive Blueprints can be hosted as actions, conditions, or guards within BTree and HSM assets, editing the internal nodes of those AiPrimitives within the Blueprint editor will invoke `IEditService`.

Based on the codebase, I think this is the reality of those three components, pls re-check:

-   **`HsmGlobalsStrip` (Stubbed):** You are correct. This is genuinely just a stub. In `Hrot.Hsm.Editor.Windows.HsmGlobalsStrip`, the `Render()` method currently contains nothing but a `// TODO: render the globals strip (window chrome, not canvas content)` comment.
-   **Static analysis for `OutputLaneMask` inference (Fully Implemented):** This is _not_ missing. The codebase contains a fully functional `HsmOutputLaneMaskInferrer` class. It successfully uses reflection to scan loaded assemblies for the `[HsmAction]` attribute, extracts the `Lane` property, builds a dictionary mapping the action FQN to the `CommandLane`, and computes the bitwise union of lanes for a given state's `OnEntry`, `OnExit`, `Activity`, and `Timer` actions.
-   **`btree.subtree_boundaries` custom canvas renderer (Fully Implemented):** This is also _not_ missing. It is fully implemented in `Hrot.BTree.Editor.Renderers.SubtreeBoundaryRenderer`. The `Render` method successfully reads the `NodeIndexStack` from the debug session's state snapshot, recursively computes the combined Axis-Aligned Bounding Box (AABB) of the executing subtree via `ExpandAabb`, and draws the dashed blue rectangle behind the nodes using the `BeforeContent` pass.

Only the `HsmGlobalsStrip` remains incomplete in the current codebase.

Your assessment of the codebase is accurate.

The `HsmGlobalsStrip` is a structural shell; the `Render()` method in `Hrot.Hsm.Editor.Windows.HsmGlobalsStrip` only contains a TODO comment for rendering the globals strip and chips for each `GlobalTransitionNode`.

The `OutputLaneMask` inference is fully implemented in `Hrot.Hsm.Editor.Validation.HsmOutputLaneMaskInferrer`. The `BuildLaneDictionary` method uses reflection to map action FQNs to their `CommandLane` based on the `[HsmAction]` attribute. The `ComputeMask` method then calculates the bitwise union for a state's `OnEntry`, `OnExit`, `Activity`, and `Timer` actions.

The `btree.subtree_boundaries` custom canvas renderer is also fully implemented in `Hrot.BTree.Editor.Renderers.SubtreeBoundaryRenderer`. The `Render` method retrieves the `StackElementIds` from the `BehaviorTreeStateSnapshot`, locates the root node, computes the combined AABB using the recursive `ExpandAabb` method, and draws the dashed rectangle in the `BeforeContent` pass.

This leaves `HsmGlobalsStrip` and the `IEditService` (`NoOpEditService`) as the only incomplete editor components from the previously discussed set.

ok let's then focus on the HsmGlobalsStrip, please detail it

The `HsmGlobalsStrip` provides the user interface for global transitions, which live outside the canvas because they do not originate from a specific source state. It is rendered as a separate small ImGui panel embedded in the canvas window's chrome, typically below the canvas or as a collapsible strip at the top, and is strictly not a custom canvas renderer.

To implement this component completely from its current stub, you must inject the `EditorSelectionStore` alongside the `HsmAsset` to handle selection state dispatching, and iterate over `HsmAsset.AllGlobalTransitions` to render the UI chips.

Below is the conceptual implementation for completing the `HsmGlobalsStrip`:

```
using System.Numerics;
using ImGuiNET;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Windows;

public sealed class HsmGlobalsStrip
{
    private readonly HsmAsset _asset;
    private readonly EditorSelectionStore _selectionStore;

    public HsmGlobalsStrip(HsmAsset asset, EditorSelectionStore selectionStore)
    {
        _asset = asset;
        _selectionStore = selectionStore;
    }

    public void Render()
    {
        // Rendered as a collapsible strip in the window chrome [2].
        if (!ImGui.CollapsingHeader("Global Transitions", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.BeginChild("##hsm_globals_strip", new Vector2(0, 45), true, ImGuiWindowFlags.HorizontalScrollbar);

        foreach (var gt in _asset.AllGlobalTransitions)
        {
            string eventName = gt.EventName ?? gt.EventId.ToString();
            string targetName = gt.Target?.Name ?? "???";
            string label = $"{eventName} -> {targetName}##{gt.VisualId}";

            bool isSelected = _selectionStore.ActiveSubSelection is HsmGlobalTransitionSelection sel && sel.VisualId == gt.VisualId;

            if (isSelected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1f));
            }

            // Each global is a chip; clicking it updates the selection store [2].
            if (ImGui.Button(label))
            {
                _selectionStore.SetSubSelection(_asset.AssetId, new HsmGlobalTransitionSelection(gt.VisualId));
            }

            if (isSelected)
            {
                ImGui.PopStyleColor();
            }

            // Right-click context menu [2].
            if (ImGui.BeginPopupContextItem($"ctx_gt_{gt.VisualId}"))
            {
                if (ImGui.MenuItem("Edit..."))
                {
                    _selectionStore.SetSubSelection(_asset.AssetId, new HsmGlobalTransitionSelection(gt.VisualId));
                }
                if (ImGui.MenuItem("Change Target..."))
                {
                    // TODO: Dispatch command to open state picker and mutate gt.Target
                }
                if (ImGui.MenuItem("Remove"))
                {
                    // TODO: Dispatch graph command to remove global transition from model
                }
                ImGui.EndPopup();
            }

            ImGui.SameLine();
        }

        ImGui.EndChild();
    }
}
```

Interaction and Data Flow

-   **Selection & Highlighting:** Clicking a global transition chip creates an `HsmGlobalTransitionSelection` record containing its `VisualId`. This triggers the shared Inspector window to dispatch and render the `GlobalTransitionFacet` (exposing EventId, TargetStateName, GuardFunction, ActionFunction, and Priority).-   **Canvas Feedback:** In response to this selection state, the canvas must highlight the target state of the global transition and dim everything else. This visual feedback is handled by the canvas rendering pass reading the `EditorSelectionStore`, not by the `HsmGlobalsStrip` itself.-   **Context Menu:** Right-clicking a chip exposes the required operations to edit parameters, remove the transition, or redirect its target state. Mutations must be routed through the `HsmCommandSink` to preserve undo/redo history and trigger dirty regeneration.

