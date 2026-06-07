QUESTIONS

**Q1. Schema-as-C#-struct as a first-class authoring artifact.**

We're considering adding a "blackboard schema" concept that is fully expressed as a hand-written C# struct decorated with `[BlackboardSchema(AssetId = "...")]` and `[StructLayout(Sequential)]`. The struct's fields define the schema; the visual editor reads them via Roslyn (mirroring the BTree/HSM editor pattern), and when the designer edits fields visually, the editor uses a Roslyn-rewriter to update the source `.cs` file in place. There would be no parallel `.json` representation — C# is canonical.

At runtime, the struct is used as the state type of an Instance Blueprint and projects directly onto the existing `BlueprintBlackboard{1024,4096,16384}` partition slot via `Unsafe.As<byte, T>`, identical to the existing generated state types from `.bp.json` Blueprints.

**Questions:**

(a) Are there technical issues with hand-authored structs (decorated with `[BlackboardSchema]` and registered via the existing `[BlueprintRegistrar]` mechanism) plugging into the partition allocator alongside generator-produced structs from `.bp.json`? My read of the runtime DD says no — the tier just sees bytes — but is there a subtler conflict around `InitDefault` generation, `StructureHash` computation, or hot-reload reconciliation that I'm missing?

(b) Is the editor-driven Roslyn-rewriter pattern (used today by BTree's `FluentCSharpEmitter`) appropriate for blackboard-struct edits, or is there a reason the BTree pattern wouldn't extend cleanly to struct-field insertion/removal/reordering?

(c) For the `StructureHash` that drives hot-reload soft/hard reconciliation: today this comes from `.bp.json` content hashing. For hand-authored structs, the natural computation is over the field list (names, types, ordering, `[StructLayout]` decisions). Is there a canonical helper for this, or would we compute it during the source generator pass?

------

**Q2. Two authoring paths for the same artifact.**

The proposal is that a blackboard schema can be authored either (a) directly by an engineer or AI coding agent as a hand-written `.cs` file, or (b) by a designer using the visual schema editor, which writes the equivalent `.cs` file via Roslyn-rewriter. Both produce the same artifact; both should appear identically in the visual editor's asset browser; either can be edited via the other path freely.

**Question:** Do you see any existing engine convention that would make this dual-authoring story messy — for example, a build-pipeline assumption that generated code lives in `obj/` and hand-written code lives in `src/`, which would create a discrimination point between the two authoring paths? Or is the "decorated `.cs` file in `src/` regardless of who wrote it" approach compatible with the build setup?

------

**Q3. The `EntityState` convention.**

We're planning to recommend (and surface in the editor) the pattern where one Instance Blueprint per entity, named `EntityState`, holds cross-cutting shared state, with other Blueprints declaring it in `callablePeers` to access it via pure-getter calls.

**Questions:**

(a) Is "name an Instance Blueprint `EntityState` and declare it as a peer" really the full pattern, or are there idiomatic details we should bake in — e.g., a slot-ordering hint to ensure `EntityState` ticks first within an entity, a recommended set of getter naming conventions, special treatment of its `Tick` graph (or expectation that it has none)?

(b) Are there any concerns with this becoming a soft-convention vs. something the editor/runtime enforces? My instinct is to leave it as a convention with editor support (template, peer-picker default, validator warning if multiple `EntityState`-named Blueprints exist on one entity) rather than enforce it at the runtime layer.



REPLIES

Q1

**(a) Technical issues with hand-authored structs in the partition allocator**

From the runtime perspective, there are no structural conflicts with the allocator itself. The `BlueprintBlackboardPartitions` allocator operates entirely on unmanaged pointers, `StateSize`, and alignment; it is completely blind to whether the struct was generated from `.bp.json` or hand-authored.

The friction point you will hit is the `InitDefault` delegate. For `.bp.json` assets, the compiler emits an `InitDefaultDelegate` that writes the authored JSON default values into the span. If the schema is a pure C# struct, extracting default field values requires either instantiating the struct via reflection at startup to capture the memory state, or writing a source generator that parses C# field initializers (e.g., `public float Health = 100f;`) to emit the initialization thunk. As long as you can satisfy the `InitDefault` contract and provide an accurate `StructureHash`, the runtime ticking and soft/hard hot-reload reconciliation will handle the hand-authored struct identically to a generated one.

**(b) Appropriateness of the BTree Roslyn-rewriter pattern**

The current BTree pattern is actually *not* a Roslyn syntax-rewriter; it is a deterministic string-builder (`FluentCSharpEmitter`) that enforces strict file ownership. It explicitly does not preserve user formatting or comments outside of specific layout attributes.

If you use the existing `FluentCSharpEmitter` pattern for a dual-authored C# struct, the visual editor will obliterate any custom methods, attributes, or comments a developer hand-wrote into that `.cs` file the moment the designer hits Save. To support true cooperative dual-authoring on a single file without data loss, you would need to implement a genuine Roslyn `CSharpSyntaxRewriter` pipeline. This is a significant step up in complexity from our current string-builder approach. This is exactly why the BTree editor currently treats the blackboard struct as a read-only reflection source and forces the user to edit the C# struct in their IDE.

**(c) Canonical helper for StructureHash computation**

We already have a canonical helper for this in the engine. You do not need to build a new one.

The `Fdp.Core.FlightRecorder.ComponentLayoutHasher` provides `ComputeHash(Type type)`, which calculates a deterministic FNV-1a 64-bit hash over the struct's instance field names, declaring types, and physical memory offsets via `Marshal.OffsetOf`. The engine already uses this exact mechanism to validate schema layouts for the Flight Recorder to prevent silent memory corruption on playback. You can invoke this at compile time (if using a generator) or via reflection at registry-load time to populate the Blueprint's `StructureHash`, which will perfectly drive the `BlueprintTickSystem`'s soft/hard reload reconciliation.







Q2

The engine's build pipeline does distinguish between source generator output and handwritten code, but this will not block your proposed approach. Currently, the Roslyn source generator emits `.g.cs` files to disk under the `CompilerGeneratedFilesOutputPath` (typically in the `obj/` directory). However, the build setup perfectly tolerates handwritten `.cs` files living in the `src/` tree, as handwritten AI code and generated behaviors are already designed to compile into the same assembly.

The actual friction for a dual-authoring story comes from the AI Editor's strict file ownership conventions, rather than the build pipeline. The shared infrastructure relies on a specific file marker (`// HROT_EDITOR_GENERATED`) placed at the top of editor-owned files. This creates a strict discrimination point for authoring paths:

- **If the marker is present:** The editor opens the file in full edit mode, but it assumes absolute ownership. When a designer hits save, the editor's fluent emitter completely regenerates the file, which will permanently obliterate any custom methods, formatting, or comments a developer hand-wrote into that file.
- **If the marker is absent:** The editor opens the file in read-only mode. While a user can click "Make Editable" to promote the file, the editor will append the marker on the next save and take full ownership thereafter, completely overwriting the file.

Therefore, while placing a decorated `.cs` file in the `src/` directory is fully compatible with the engine's build system, true cooperative dual-authoring on the exact same file is highly destructive under the current architecture. To make this work safely, you would have to abandon the editor's current deterministic string-builder emitter and replace it with a true Roslyn `CSharpSyntaxRewriter` that can surgically update fields while preserving surrounding hand-authored code.



Q3



**(a) Idiomatic details of the `EntityState` pattern**

While "name an Instance Blueprint `EntityState` and declare it as a peer" provides the mechanical linkage via `callablePeers`, there is a critical idiomatic detail you must account for: **attachment and slot-ordering dictate frame consistency**.

Our `BlueprintTickSystem` iterates through the partition allocator slots in exact slot-table order (index 0, 1, 2, etc.), which corresponds to the order the Blueprints were attached to the entity. If your `EntityState` Blueprint is attached first (slot 0) and your `CombatAI` Blueprint is attached second (slot 1), a synchronous peer call from `CombatAI` to `EntityState` will read the state updated in the *current* frame. Conversely, if `EntityState` is attached after the caller, the caller will read the state from the *previous* frame. Because of this strict CQRS within-frame consistency rule, the editor and scenario-load pipelines must guarantee that `EntityState` is always attached first.

Regarding the `Tick` graph, it is entirely expected and supported for an `EntityState` blueprint to omit it; the `Tick` method is explicitly optional for Instance Blueprints. Pure getter functions are the recommended convention for exposing the internal `State` struct fields to peers safely.

**(b) Soft-convention vs. Runtime enforcement**

Your instinct to treat this as a soft-convention with heavy editor-side UX support (templates, warnings, picker defaults) is the correct architectural decision.

Enforcing an `EntityState` singleton concept at the runtime layer would violate the design of the partition allocator. The `BlueprintBlackboard{1024,4096,16384}` components and the `BlueprintBlackboardPartitions` allocator operate purely on unmanaged memory, slicing the fixed payload into slots identified by a 32-bit `BlueprintId`. The runtime is deliberately blind to Blueprint names, semantics, or designated "primary" roles.

Baking `EntityState` rules into the C# kernel would pollute the ECS memory manager with domain logic. Maintaining the generic `callablePeers` synchronous lookup allows the runtime to remain fast and agnostic. Furthermore, we already have a roadmap item for Slice 2 to introduce a formal `'Shareable' Blueprint declaration`. This upcoming feature will naturally graduate your soft-convention into a first-class editor concept without requiring us to hardcode string names into the engine's validation or execution pipelines today.
