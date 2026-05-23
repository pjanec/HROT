
QUESTIONS

Q1 — Template persistence model. EQS query templates would naturally be authored by AI engineers and designers. You mention that user-changeable AI stuff compiles to C# and hot-reloads from assemblies. Should EQS templates follow this exact pattern — i.e., templates are written as C# code in a designer-edited source folder, compiled to an assembly at edit-time, and hot-reloaded — or do they need a separate persistence story? Concretely: where do templates live on disk, what's the format (C# source, JSON, IR), how are they edited, and how do they get into the registry at runtime?


Q2 — Comparison with blueprint persistence. How are blueprints saved today? Are they C# source files, serialized objects, or something else? Is there an editor tool that emits them, or is the workflow "hand-write the C#, save the file, hot-reload"? I want EQS templates to follow the same pattern so users don't learn two different authoring systems.


Q3 — Stable template IDs across refactors. EQS templates need stable identifiers because the Brain subscribes to a sensor by template ID, and that ID crosses the DDS wire. If a designer renames a template's C# class or moves it to a different namespace, what happens? Options I see: (a) ID is a class attribute ([EqsTemplate(Id = "FindCoverFromTarget")]) and doesn't change on refactor; (b) ID is derived from the fully-qualified type name and refactors break subscriptions; (c) ID is a hash of the canonical form and refactors that change semantics break subscriptions while pure renames don't. What's the existing pattern for blueprint IDs and refactoring survival?


Q4 — Hot reload semantics for templates. When a template is hot-reloaded mid-scenario, what happens to sensors currently subscribed to that template? Options: (a) sensors continue with the old template until they're unsubscribed; (b) all sensors using that template are forcibly re-evaluated under the new template at next tick; (c) hot-reload of templates is disallowed during a scenario. What's the convention?


Q5 — Generator and test type extensibility. New IEqsGenerator and IEqsTest implementations are essentially new code. Do they hot-reload the same way templates do? If so, are templates that reference a generator/test type that was removed in the hot-reload handled gracefully (e.g., templates fail validation and are removed from the registry), or is this a hard crash?



REPLIES


To maintain a unified authoring pipeline, EQS query templates should absolutely follow the exact same persistence and compilation pattern currently used by our Blueprint and AI behavior systems. You will not need a separate persistence story. 

Here is exactly how the architecture handles this:

**Format and Disk Location**
Templates are saved on disk as declarative JSON files (specifically using the `.bp.json` extension). They reside in the designer's standard project workspace, such as the `Subsystems/Hrot.AI.Behaviors/Blueprints/` directory. 

**How They Are Edited**
Designers and AI engineers do not hand-write the C# source. They author the templates visually using the engine's built-in node graph UI (`GraphEditorWindow`), which serializes the visual graph directly into the JSON format.

**Compilation to C#**
At edit-time, the engine relies on a Roslyn Incremental Source Generator (`BlueprintIncrementalGenerator`) hooked into the background MSBuild process. The generator intercepts the `.bp.json` files, parses the JSON, lowers the node graph into an Intermediate Representation (IR), and ultimately emits highly optimized, zero-allocation C# source code. This generated C# is compiled directly into the application's AI assembly (e.g., `Hrot.AI.Behaviors.dll`). 

**Runtime Registration and Hot-Reloading**
To get the templates into the registry at runtime, the engine uses the `AiHotReloadCoordinator`. This coordinator runs on a background thread and watches the target DLL for changes. When the background build finishes, the coordinator:
1. Loads the new assembly into an isolated, collectible `AssemblyLoadContext` (ALC).
2. Scans the assembly via reflection for generated static classes decorated with specific attributes like `[BlueprintRegistrar]`.
3. Extracts their `Register` or `RegisterAll` methods.
4. Queues a callback for the main thread to invoke these registrars, injecting the compiled execution delegates directly into the central staging registries before performing an atomic swap.


Q2

Today, blueprints are saved to disk as declarative JSON files using the `.bp.json` extension, not as hand-written C# source files. 

The authoring workflow relies entirely on a dedicated visual tool, the `GraphEditorWindow`, which allows designers to construct logic via a node-based interface. When a designer saves their work, the editor serializes the in-memory `BlueprintAsset` directly into this JSON format. 

You do not need to hand-write C# and manually hot-reload. Instead, the engine automates the translation to code: the `.bp.json` files are included in the project as MSBuild `AdditionalFiles`. A Roslyn component called the `BlueprintIncrementalGenerator` automatically intercepts these JSON files during the build process. It parses the JSON and passes the data to the `BlueprintCompiler`. The compiler validates the structure, lowers the graph into an intermediate representation, and emits highly optimized, zero-allocation C# source code behind the scenes. 

To ensure users do not have to learn two different authoring systems, EQS templates should simply adopt this exact same visual-to-JSON-to-C# pipeline, hooking into the existing `BlueprintAsset` schema and `GraphEditorWindow` workflow.


Q3

The existing pattern for blueprint IDs ensures refactoring survival by deriving a 32-bit integer identifier from a stable Asset GUID using an FNV-1a hash. Because your EQS templates will be authored visually and saved as `.bp.json` files, they will follow this exact mechanism, storing their `AssetId` GUID directly within the JSON document.

During edit-time compilation, the source generator extracts this GUID from the JSON file to compute the 32-bit `BlueprintId`. This integer is then embedded directly into the generated C# source code as a `const int`. 

If a designer renames an EQS template or moves it to a new category, only the string metadata in the JSON file is updated. The compiler will generate a new C# class name that reflects the new name (using the format `{SanitizedName}_{BlueprintId:X8}_Bp`), but the underlying GUID and the resulting integer ID will remain perfectly stable. This guarantees that network subscriptions and DDS references used by the Brain to poll sensors are never broken by pure renames.

For purely code-authored assets (such as state machines or behavior trees), the engine provides a similar attribute-based pattern. Developers can explicitly specify a stable GUID via the definition attribute (e.g., `[HsmDefinition(MachineName = "...", AssetId = "...")]`) to survive class renames. If omitted, the engine falls back to hashing the string name. However, for your visual EQS templates, the explicit GUID stored inside the `.bp.json` file will act as the single source of truth.


Q4

To answer your fourth question regarding hot reload semantics:

The engine's convention does not strictly lock into just option (a) or (b); instead, it uses a severity-based classification system that compares the old and new templates using two distinct hashes: the `StructureHash` and the `ParamHash`. Your EQS templates should follow this exact pattern.

When a template is hot-reloaded mid-scenario, the engine evaluates the hashes to determine the appropriate response:

**1. Soft Reload (Parameter Changes)**
If a designer only tweaks values (e.g., changing a radius from 10m to 15m or adjusting a scoring weight) without altering the template's node topology, the `StructureHash` remains identical but the `ParamHash` changes. The engine classifies this as a `SoftReload`. In this case, live sensors currently subscribed to the template will seamlessly continue their execution without dropping their current state, but they will immediately begin using the updated parameters on the next tick.

**2. Hard Reset (Structural Changes)**
If the designer adds, removes, or rewires nodes (e.g., adding a new `IEqsGenerator` or `IEqsTest`), the `StructureHash` changes. This triggers a `HardReset`. When this happens, the engine actively iterates over a span of all live instances utilizing that template and invokes a hard-reset delegate to wipe their runtime execution state. For your EQS sensors, following this convention means any in-flight queries would be forcibly abandoned, and the sensors would be completely reset to re-evaluate the new template from scratch on their next tick. 

Therefore, hot-reloading mid-scenario is fully supported (discarding option c), and the intrusiveness of the reload dynamically scales based on whether the designer made a cosmetic parameter tweak or a fundamental structural change.


Q5


Yes, new `IEqsGenerator` and `IEqsTest` implementations are standard C# code and will hot-reload exactly the same way as your templates. They are compiled directly into the AI assembly and managed by the same AssemblyLoadContext (ALC) hot-reloader. 

If a template references a generator or test type that was removed, it is handled gracefully and will absolutely not cause a hard crash. The engine guarantees this through two distinct safety mechanisms depending on the reload path:

**1. Full Rebuild (File-Watcher)**
If a designer saves a blueprint that references a deleted C# type, the compilation will fail during the background MSBuild process. Because the build fails, a new `.dll` is never emitted. The `FbtAssemblyHotReloader` never detects a file change, and the old ALC continues running the existing logic completely uninterrupted. Even if the assembly somehow loads but fails registration, the reloader catches the exception, fires an `OnReloadFailed` event, unloads the new ALC, and leaves the simulation running on the old ALC.

**2. Quick Reload (In-Memory)**
If the reload is triggered via the editor's in-memory `QuickReloadService`, the `BlueprintCompiler` will attempt to lower the graph and compile it using Roslyn. When it fails to resolve the missing generator or test type, it generates diagnostic errors and throws a `BlueprintCompileException`. The `QuickReloadService` explicitly catches this exception, safely aborts the reload, and returns a failed result. If the failure happens deeper during the atomic staging swap, the `AiHotReloadCoordinator` unloads the new patch ALC to prevent memory leaks, leaves the previous `_currentAlc` entirely intact, and propagates the failure to the UI.

In both scenarios, live sensors simply keep executing their previous, valid templates without any interruption or crash.

