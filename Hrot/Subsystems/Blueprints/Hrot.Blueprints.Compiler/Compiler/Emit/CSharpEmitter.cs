using System.Text;
// ⭐ CE-388 / Q74 D-F — the LINKED HsmActionKey; the one formula both this emitter and
//   HsmEmitCore hash the generated class name with. See the csproj note.
using Fdp.Toolkit.Behavior.Shared;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;
using AssetDispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal sealed class CSharpEmitter
{
    private readonly StringBuilder _sb = new();
    private readonly DebugMapBuilder _debugMap;
    private readonly EmissionContext _ctx;
    private int _indent;
    private int _currentLine = 1;

    public CSharpEmitter(EmissionContext ctx)
    {
        _ctx = ctx;
        _debugMap = new DebugMapBuilder(ctx.Asset.AssetId, ctx.Asset.BlueprintId, ctx.Asset.StructureHash);
    }

    public EmissionContext Ctx => _ctx;
    public int CurrentLine => _currentLine;

    public void Write(string text)
    {
        _sb.Append(text);
        foreach (char c in text)
            if (c == '\n') _currentLine++;
    }

    public void WriteLine(string line = "")
    {
        for (int i = 0; i < _indent; i++) _sb.Append("    ");
        _sb.Append(line);
        _sb.Append('\n');
        _currentLine++;
    }

    public void Indent() => _indent++;
    public void Outdent() => _indent = Math.Max(0, _indent - 1);

    public void EmitNodeStart(IrDebugAnnotation? debug)
    {
        var effectiveNodeId = debug?.NodeId ?? debug?.OriginNodeId;
        if (effectiveNodeId is null) return;
        // ⚠ The `NodeId ?? OriginNodeId` precedence above is unchanged and must stay: the clone's own
        // id keeps line→node 1:1. OriginNodeId is passed ALONGSIDE as a back-reference, which is what
        // makes node→line one-to-many and lets one breakpoint in a macro body arm every call site.
        _debugMap.RecordNodeStart(effectiveNodeId.Value, debug!.GraphId, _currentLine,
            debug.NodeKind, debug.DisplayName, debug.OriginNodeId, debug.OriginGraphId);
    }

    public void EmitNodeEnd(IrDebugAnnotation? debug)
    {
        var effectiveNodeId = debug?.NodeId ?? debug?.OriginNodeId;
        if (effectiveNodeId is null) return;
        _debugMap.RecordNodeEnd(effectiveNodeId.Value, _currentLine);
    }

    public (string Source, DebugMap DebugMap) Emit(IrAsset asset)
    {
        _debugMap.SetAssetName(asset.Name);
        _debugMap.SetGeneratedSourcePath($"generated/{asset.SanitizedName}_{asset.BlueprintId:X8}.cs");

        foreach (var graph in asset.Graphs)
        {
            _debugMap.AddGraph(new DebugGraphInfo(graph.Id, graph.Name, graph.Kind.ToString()));
            foreach (var field in graph.Inputs)
                _debugMap.AddPin(new DebugPinInfo(field.Id, graph.Id, field.Name, "Input", "Data",
                    StatementEmitter.TypeRefToCSharp(field.Type), field.Name));
            foreach (var field in graph.Outputs)
                _debugMap.AddPin(new DebugPinInfo(field.Id, graph.Id, field.Name, "Output", "Data",
                    StatementEmitter.TypeRefToCSharp(field.Type), field.Name));
        }

        // ⭐⭐ Batch 57 (S1) — this was gated on `Instance`, so an AiPrimitive contributed NOTHING to
        //    DebugMap.StateLayout and nothing to the variable pins: a whole dispatch kind invisible to
        //    the debugger. Batch 56 unified WHAT it walks; this lifts the gate to every kind that has
        //    state at all. (Library dispatch has no state struct.)
        if (asset.Dispatch != AssetDispatch.Library)
        {
            // ⚠ The container is the emitted local the expression must name — `ws` for TickCore's
            //   working state, `s` for an Instance's State. Same split as EmissionContext.StateVar.
            var container = asset.Dispatch == AssetDispatch.AiPrimitive ? "ws"
                          : asset.Dispatch == AssetDispatch.Behavior ? "__bb.St"   // ⭐ S2 — block-relative offsets
                          : "s";

            // 🔴🔴 "Do not emit metadata you cannot trust." The debug map is a COMPILE-TIME artefact:
            //    unlike the registrar it cannot emit `Marshal.OffsetOf<…>`, so when the baked offsets
            //    are guesses the only honest contribution is none. ⛔ And silence here is not merely
            //    tidy — it is REQUIRED: both readers (CaptureAiPrimitiveState, ReadInstanceState)
            //    PREFER StateLayout over StateFields, so a baked-and-wrong layout would SHADOW the
            //    runtime-derived dictionary that is correct. ⭐ Measured: no shipped asset takes the
            //    runtime path today, so this is a no-op on the corpus and a rail for what comes next.
            bool bakedOffsetsUsable = !LayoutFromRuntime(asset);

            foreach (var field in asset.StateDeclarations)
            {
                _debugMap.AddPin(new DebugPinInfo(field.Id, Guid.Empty, field.Name, "Output", "Variable",
                    StatementEmitter.TypeRefToCSharp(field.Type), $"{container}.{field.Name}"));
                if (bakedOffsetsUsable)
                    _debugMap.AddStateLayoutField(new StateLayoutField(
                        field.Name, field.Type.FullName, StructRelativeOffset(asset, field), field.Size));
            }
        }

        EmitFileHeader(asset);
        EmitUsings();
        WriteLine();

        switch (asset.Dispatch)
        {
            case AssetDispatch.Library:
                LibraryEmitter.EmitClass(this, asset);
                break;
            case AssetDispatch.AiPrimitive:
                AiPrimitiveEmitter.EmitClass(this, asset);
                break;
            case AssetDispatch.Instance:
            case AssetDispatch.Behavior:     // CE-446: a behaviour IS an Instance body (+ a status)
                InstanceEmitter.EmitClass(this, asset);
                break;
            default:
                throw new NotSupportedException($"Unknown dispatch kind: {asset.Dispatch}");
        }

        WriteLine();
        EmitRegistrarClass(asset);

        // Merge BreakpointTargets from all graphs into one flat dictionary.
        var bpTargets = new Dictionary<Guid, Guid>();
        foreach (var graph in asset.Graphs)
        {
            foreach (var kv in graph.BreakpointTargets)
                bpTargets[kv.Key] = kv.Value;
        }

        return (_sb.ToString(), _debugMap.Build(bpTargets));
    }

    private void EmitFileHeader(IrAsset asset)
    {
        WriteLine("// <auto-generated />");
        WriteLine($"// Asset: {asset.Name} ({asset.AssetId})");
        WriteLine($"// BlueprintId: 0x{asset.BlueprintId:X8}");
        WriteLine($"// StructureHash: 0x{asset.StructureHash:X16}");
        WriteLine();
    }

    private void EmitUsings()
    {
        WriteLine("using System;");
        WriteLine("using System.Runtime.CompilerServices;");
        WriteLine("using System.Runtime.InteropServices;");
        WriteLine("using System.Numerics;");
        WriteLine("using Fdp.Core;");
        WriteLine("using Fdp.Interfaces;");
        WriteLine("using Fdp.ModuleHost.Abstractions;");
        WriteLine("using Fdp.Toolkit.Blueprints;");
    }

    // ── BP-110: cross-asset call target resolution ────────────────────────────

    /// <summary>
    /// The generated class name for the sibling whose <see cref="BlueprintSignature.BlueprintId"/>
    /// is <paramref name="blueprintId"/>, or <see langword="null"/> when no sibling matches.
    /// </summary>
    internal string? ResolveSiblingClassName(int blueprintId)
    {
        foreach (var sig in _ctx.SiblingSignatures)
        {
            if (sig.BlueprintId == blueprintId)
                return $"{sig.SanitizedName}_{sig.BlueprintId:X8}_Bp";
        }
        return null;
    }

    private void EmitRegistrarClass(IrAsset asset)
    {
        var className = $"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";
        var registrarName = $"BlueprintRegistrar_{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";

        // I1: BTree-hosted AiPrimitive thunks must register into the FastBTree ActionRegistry
        // (string-keyed, the registry the Interpreter actually binds from), not the orphaned
        // int-keyed BehaviorRegistry side-table. The scanner injects this ActionRegistry the same
        // way it does for the JSON BTree bridge registrars.
        bool needsActionRegistry = asset.Hostings.Any(h =>
            h == AiPrimitiveHosting.BTreeAction || h == AiPrimitiveHosting.BTreeCondition);

        bool hasConditionMet = asset.Dispatch is AssetDispatch.Instance or AssetDispatch.Behavior &&
            asset.Graphs
                .SelectMany(g => g.Blocks)
                .SelectMany(b => b.Statements)
                .Any(s => s.Operation is IrOp_WhenConditionMetCheck);

        WriteLine("[global::Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrar]");
        WriteLine($"public static class {registrarName}");
        WriteLine("{");
        Indent();

        var paramParts = new System.Collections.Generic.List<string>
            { "global::Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging" };
        if (needsActionRegistry)
            paramParts.Add(
                "global::Fbt.Runtime.ActionRegistry<" +
                "byte, " +   // P4-②
                "global::Fdp.Toolkit.Behavior.BTreeContext> actionRegistry");
        // ⭐ CE-446: a blueprint behaviour registers into the BEHAVIOUR registry (the scanner injects it by type).
        if (asset.Dispatch == AssetDispatch.Behavior)
            paramParts.Add("global::Fdp.Toolkit.Behavior.BehaviorRegistry beh");
        if (hasConditionMet)
        {
            paramParts.Add("global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler predicateCompiler");
            paramParts.Add("global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry dtoRegistry");
        }
        var paramSig = string.Join(", ", paramParts);

        WriteLine($"public static unsafe void Register({paramSig})");
        WriteLine("{");
        Indent();

        switch (asset.Dispatch)
        {
            case AssetDispatch.Library:
                EmitLibraryRegistration(className, asset);
                break;
            case AssetDispatch.AiPrimitive:
                EmitAiPrimitiveRegistration(className, asset);
                break;
            case AssetDispatch.Instance:
                if (hasConditionMet)
                    WriteLine($"{className}.InitializePredicates(predicateCompiler, dtoRegistry);");
                EmitInstanceRegistration(className, asset);
                break;
            case AssetDispatch.Behavior:
                if (hasConditionMet)
                    WriteLine($"{className}.InitializePredicates(predicateCompiler, dtoRegistry);");
                EmitBehaviorRegistration(className, asset);
                break;
        }

        Outdent();
        WriteLine("}");
        Outdent();
        WriteLine("}");
    }

    private void EmitLibraryRegistration(string className, IrAsset asset)
    {
        WriteLine($"staging.Add({className}.BlueprintId, new global::Fdp.Toolkit.Blueprints.BlueprintDefinition");
        WriteLine("{");
        Indent();
        WriteLine($"Name = \"{asset.Name}\",");
        WriteLine("Kind = global::Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Library,");
        WriteLine($"StructureHash = {asset.StructureHash}UL,");
        WriteLine("StateSize = 0,");

        // G2/R2: expose each Function graph as a runtime-invocable LibraryFunctionDelegate keyed by
        // name, so a blueprint-authored resolver can be dispatched by name at the ingress seam. The
        // adapter blittably marshals inputs (declaration order) into the emitted static method and
        // writes its return value back out. Inputs/outputs are the StaticTypeRegistry scalar/vector/
        // Entity types (blittable), so MemoryMarshal.Read/Write is layout-safe.
        var functionGraphs = asset.Graphs.Where(g => g.Kind == IrGraphKind.Function).ToList();
        if (functionGraphs.Count > 0)
        {
            WriteLine("Functions = new global::System.Collections.Generic.Dictionary<string, global::Fdp.Toolkit.Blueprints.LibraryFunctionDelegate>(global::System.StringComparer.Ordinal)");
            WriteLine("{");
            Indent();
            foreach (var g in functionGraphs)
                EmitLibraryFunctionAdapter(className, g);
            Outdent();
            WriteLine("},");
        }

        // ⛔ CE-448 — no `Resolvers` index. A Library's only Construction graph belongs to a behaviour
        //   resolver asset, which the BEHAVIOUR's registrar calls through `ResolveBehavior` (LibraryEmitter);
        //   the reusable DTO→DTO resolvers this index published are retired (R-155).

        Outdent();
        WriteLine("});");
    }

    /// <summary>
    /// G2/R2: emits one <c>["Name"] = static (inputs, outputs, view, self, time) => {...}</c> entry
    /// that unpacks the function's blittable inputs from <c>inputs</c>, calls the emitted static
    /// method, and writes any return value to <c>outputs</c>.
    ///
    /// <para>
    /// BP-112: the write sites pass <c>in</c>, not <c>ref</c>. <c>MemoryMarshal.Write&lt;T&gt;</c>
    /// declares its value parameter <c>in T</c>, and passing <c>ref</c> to an <c>in</c> parameter is
    /// <b>CS9191</b> — a warning everywhere else, but every project here sets
    /// <c>TreatWarningsAsErrors</c>, so it failed the whole solution build. These two lines are the
    /// only emit sites in the compiler that write through <c>MemoryMarshal.Write</c>, which is why
    /// the failure was exclusive to <c>Library</c> dispatch.
    /// </para>
    /// </summary>
    private void EmitLibraryFunctionAdapter(string className, IrGraph graph)
    {
        bool hasStatusReturn = graph.Blocks.Any(b => b.Terminator is IrTerm_ReturnStatus);
        string returnType = LibraryEmitter.CSharpReturnType(graph, hasStatusReturn);

        WriteLine($"[\"{graph.Name}\"] = static (inputs, outputs, view, self, time) =>");
        WriteLine("{");
        Indent();

        var argNames = new System.Collections.Generic.List<string>();
        if (graph.Inputs.Count > 0)
            WriteLine("int __off = 0;");
        for (int i = 0; i < graph.Inputs.Count; i++)
        {
            string t = LibraryEmitter.CSharpType(graph.Inputs[i].Type);
            WriteLine($"{t} __in{i} = global::System.Runtime.InteropServices.MemoryMarshal.Read<{t}>(inputs.Slice(__off));");
            WriteLine($"__off += global::System.Runtime.CompilerServices.Unsafe.SizeOf<{t}>();");
            argNames.Add($"__in{i}");
        }

        string call = $"{className}.{graph.Name}({string.Join(", ", argNames)})";
        if (returnType == "void")
        {
            WriteLine($"{call};");
        }
        else if (graph.Outputs.Count > 1)
        {
            // BP-73: N outputs are written SEQUENTIALLY, element by element, mirroring the
            // `__off`-advancing walk that unpacks the inputs above.
            //
            // ⚠ NOT `MemoryMarshal.Write(outputs, in __r)` on the tuple itself. That would blit the
            // ValueTuple's CLR layout -- including whatever internal padding the runtime chose --
            // whereas the reader on the other side of this span walks fields back-to-back by
            // Unsafe.SizeOf<T> exactly as the input side does. The two would silently disagree for
            // any output list whose types have different alignments (e.g. (bool, float)), which is a
            // wrong-VALUES bug, not a compile error.
            WriteLine($"{returnType} __r = {call};");
            WriteLine("int __oo = 0;");
            for (int i = 0; i < graph.Outputs.Count; i++)
            {
                string t = LibraryEmitter.CSharpType(graph.Outputs[i].Type);
                WriteLine($"{t} __out{i} = __r.Item{i + 1};");
                WriteLine($"global::System.Runtime.InteropServices.MemoryMarshal.Write(outputs.Slice(__oo), in __out{i});");
                WriteLine($"__oo += global::System.Runtime.CompilerServices.Unsafe.SizeOf<{t}>();");
            }
        }
        else
        {
            WriteLine($"{returnType} __r = {call};");
            WriteLine("global::System.Runtime.InteropServices.MemoryMarshal.Write(outputs, in __r);");
        }

        Outdent();
        WriteLine("},");
    }

    private void EmitAiPrimitiveRegistration(string className, IrAsset asset)
    {
        WriteLine($"staging.Add({className}.BlueprintId, new global::Fdp.Toolkit.Blueprints.BlueprintDefinition");
        WriteLine("{");
        Indent();
        WriteLine($"Name = \"{asset.Name}\",");
        WriteLine("Kind = global::Fdp.Toolkit.Blueprints.BlueprintDispatchKind.AiPrimitive,");
        WriteLine($"StructureHash = {className}.StructureHash,");
        // ⭐⭐ Batch 57 (S1) — was a literal `0`, and `0` was not a placeholder, it was a wrong answer:
        // an AiPrimitive's working state is real bytes in Blackboard1024. Same expression the Instance
        // path uses, over this dispatch kind's own struct.
        WriteLine($"StateSize = {className}.StateSize,");
        // ⭐⭐⭐ CE-399 — and the PARAMS size, which the hosted-occurrence tier sizing needs and this
        //   registration never carried. The runtime attaches Align8(StateSize) + ParamsSize; without
        //   this the demand calculator saw only the first term. 📄 §13.8.
        WriteLine($"ParamsSize = {className}.ParamsSize,");
        // ⭐⭐ CE-388 / Q74 D-A2 — the DERIVED channel set, so a consumer can ask what this
        //   blueprint drives without re-deriving it. ⛔ Informational: the exit binding itself
        //   goes through the cleanup thunk's id, not through this list.
        EmitWritesChannelsBlock(asset);
        WriteLine($"AssetId = new Guid(\"{asset.AssetId}\"),");
        WriteLine($"StateClrType = typeof({className}.WorkingState),");
        // 🔴🔴 Batch 57 (S1) — the block that was missing ENTIRELY. Without it
        // `BlueprintDefinition.StateFields` is empty for every AiPrimitive asset, so
        // `BlueprintDebugSession.CaptureAiPrimitiveState` — written, shipped and named for this exact
        // case — silently reads nothing and returns. ⚠ A consumer with no producer, green for its whole
        // life because nothing ever asked it for a value. 32 shipped assets are (Parameter, WorkingState).
        EmitStateFieldsBlock(className, asset, "WorkingState");
        Outdent();
        WriteLine("});");

        // I1: register BTree thunks into the FastBTree ActionRegistry under the interpreter's
        // string-key scheme ({MethodFqn}@{offset}), so Interpreter.BindActions resolves them.
        // The action thunk (BTreeTick) is already NodeLogicDelegate-shaped (returns NodeStatus).
        // The condition thunk (BTreeEvaluate) returns bool, so wrap it into a NodeStatus-returning
        // adapter (Success/Failure) — the shape ActionRegistry stores conditions as.
        // offset 0: a standalone AiPrimitive owns its whole params region; when composed into a host
        // BTree the host assigns the param offset (I2) and bakes the matching key into the blob.
        const string fqnNs = "Hrot.AI.Behaviors.Generated";
        if (asset.Hostings.Contains(AiPrimitiveHosting.BTreeAction))
            WriteLine($"actionRegistry.Register(\"{fqnNs}.{className}.BTreeTick@0\", {className}.BTreeTick);");
        if (asset.Hostings.Contains(AiPrimitiveHosting.BTreeCondition))
            WriteLine(
                $"actionRegistry.RegisterCondition(\"{fqnNs}.{className}.BTreeEvaluate@0\", " +
                "static (ref byte bb, " +   // P4-②
                "ref global::Fbt.BehaviorTreeState st, ref global::Fdp.Toolkit.Behavior.BTreeContext ctx, int pi) => " +
                $"{className}.BTreeEvaluate(ref bb, ref st, ref ctx, pi) " +
                "? global::Fbt.NodeStatus.Success : global::Fbt.NodeStatus.Failure);");

        // ⛔ CE-445 (R-155): an AiPrimitive no longer carries its own resolver — actions and conditions read
        //    their host live and have none (DESIGN_Parameter_Model §P.4). HostedParamResolvers is retired.

        // Register HSM thunks via static calls (HsmActionDispatcher is a static unsafe class,
        // not injectable; Patch C1). The unmanaged function pointers are cast to IntPtr.
        if (asset.Hostings.Contains(AiPrimitiveHosting.HsmAction))
        {
            WriteLine($"global::Fhsm.Kernel.HsmActionDispatcher.RegisterAction(unchecked((ushort){className}.BlueprintId), (global::System.IntPtr)(delegate* <void*, void*, global::Fhsm.Kernel.Data.HsmCommandWriter*, void>)&{className}.HsmActivity);");

            // ⭐⭐⭐ CE-388 / Q74 D-B1 — register the RELEASE half under the SHARED id.
            //
            // 🔒 The id comes from Fdp.Toolkit.Behavior.Shared.HsmActionKey, which is a LINKED file
            //    (Q74 D-F, user ruling: "no duplicating the HsmActionKey formula, must be shared").
            //    HsmEmitCore computes the SAME expression to bake `.OnExitId(n)` into the blob.
            //
            // ⛔⛔ THIS IS THE COUPLING CE-403 MEASURED THE COST OF. If the two sides ever compute
            //    the id differently the state binds an action nothing is registered under, and the
            //    only symptom is that the channel is never released — no error anywhere. That is
            //    exactly break ③: the sibling C# table is keyed on the SHORT method name while an
            //    asset names its activity by FQN, so the lookup misses silently.
            WriteLine($"global::Fhsm.Kernel.HsmActionDispatcher.RegisterAction({HsmActionKey.ForExitCleanup(className)}, (global::System.IntPtr)(delegate* <void*, void*, global::Fhsm.Kernel.Data.HsmCommandWriter*, void>)&{className}.HsmExitCleanup);");
        }
        if (asset.Hostings.Contains(AiPrimitiveHosting.HsmGuard))
            WriteLine($"global::Fhsm.Kernel.HsmActionDispatcher.RegisterGuard(unchecked((ushort){className}.BlueprintId), (global::System.IntPtr)(delegate* <void*, void*, ushort, global::Fhsm.Kernel.Data.HsmCommandWriter*, bool>)&{className}.HsmGuard);");
    }

    /// <summary>
    /// Returns true when <paramref name="t"/> resolves to a C# type name that is referencable
    /// from the registrar class scope.  Synthesized internal-state structs generated inside the
    /// blueprint class have a <c>FullName</c> starting with <c>'_'</c> (mirroring the
    /// <c>_ when t.FullName.StartsWith("_")</c> arm of <see cref="StatementEmitter.TypeRefToCSharp"/>)
    /// and are NOT referencable outside the generated class, so they must be excluded from
    /// <c>StateFields</c>.
    /// </summary>
    private static bool IsReferencableStateFieldType(IrTypeRef t)
    {
        // FC-2/LV-5: a fixed-list field's synthesized `__List_…` wrapper IS referencable -- the
        // emit site qualifies it `{className}.__List_…` (the nested wrapper is public), so list
        // variables are descriptor-VISIBLE for the debugger/watch (LV-5).
        if (t.Capacity > 0) return true;

        // Unwrap arrays: an array of a synthesized type is also not referencable.
        var underlying = t.IsArray ? t.ElementType! : t;
        return !underlying.FullName.StartsWith("_");
    }

    /// <summary>
    /// ⭐⭐ <b>Batch 57 (<c>S1</c>) — ONE <c>StateFields</c> emitter, for both dispatch kinds that carry
    /// state.</b> Ruling 9 is <i>"no keeping two implementations for the same concept"</i>, and a
    /// second copy over <c>WorkingState</c> would have been exactly that — the AiPrimitive half was
    /// missing precisely because there was nothing to add it to.
    /// </summary>
    /// <param name="stateStructName">
    /// ⚠ The struct the descriptors describe, and the ONLY thing that differs: <c>State</c> for an
    /// Instance, <c>WorkingState</c> for an AiPrimitive. ⛔ Those names are ABI (see
    /// <c>AiPrimitiveEmitter</c>), so the emitter is parameterised by the name rather than the names
    /// being unified.
    /// </param>
    /// <summary>
    /// ⭐⭐ <c>CE-388</c> / <c>Q74 D-A2</c> — emits the DERIVED channel set onto the definition.
    ///
    /// <para>⛔ <b>Gated on the feature that needs it</b>, like every other emitter addition here:
    /// a blueprint that commands no channel emits NOTHING, so every asset authored before
    /// <c>CE-388</c> stays byte-identical. 📌 <c>E3b-0</c> learned this the hard way — emitting a
    /// const unconditionally once moved 11 baselines for assets that could not use it.</para>
    ///
    /// <para>⚠ An INCOMPLETE derivation emits nothing either, and that is NOT the same as "no
    /// channels": <c>BlueprintChannelDerivation</c> reports the opaque callees and the caller
    /// raises a diagnostic. ⛔ Never let an unknown reach a consumer dressed as an empty set.</para>
    /// </summary>
    private void EmitWritesChannelsBlock(IrAsset asset)
    {
        if (asset.Dispatch != AssetDispatch.AiPrimitive) return;

        var derived = BlueprintChannelDerivation.Derive(asset);
        if (!derived.IsComplete || derived.ChannelComponentFqns.Count == 0) return;

        var literals = string.Join(", ",
            System.Linq.Enumerable.Select(derived.ChannelComponentFqns, f => $"typeof(global::{f})"));
        WriteLine($"WritesChannels = new global::System.Type[] {{ {literals} }},");
    }

    private void EmitStateFieldsBlock(string className, IrAsset asset, string stateStructName)
    {
        // Batch 56 — the descriptors describe the state STRUCT, and since ruling 8 that struct holds the
        // whole state tier. A descriptor set built from one list would name fewer fields than the struct
        // has, which is the `BP-223` shape: a consumer that resolves nothing and reports nothing.
        var emittable = asset.StateDeclarations
            .Where(f => IsReferencableStateFieldType(f.Type))
            .ToList();

        if (emittable.Count == 0) return;

        // When every field's size is reliable (all primitives/curated types), the compiler's baked
        // offsets/sizes are correct → emit them as constants (byte-identical to before). If ANY field is
        // a project struct accepted via the AN2 fallback (size unknown to the reflection-less compiler),
        // the baked offsets of later fields are wrong — so emit offset/size via a RUNTIME query against
        // the real generated struct layout (Marshal.OffsetOf<T> + Unsafe.SizeOf<T>). Q#14 Option B.
        // FC-2/LV-1: scan ALL declarations, not just the emittable (descriptor-visible) subset -- a
        // synthesized `__List_…` field is EXCLUDED from StateFields (IsReferencableStateFieldType;
        // debugger visibility lands in LV-5) but still occupies state bytes with an unreliable
        // computed size, so any SCALAR field declared after it has a wrong baked offset and must
        // take the runtime offset path too (TypeLayout.OffsetOf — the managed offset, CE-2043).
        bool layoutFromRuntime = LayoutFromRuntime(asset);

        WriteLine("StateFields = new global::System.Collections.Generic.Dictionary<string, global::Fdp.Toolkit.Blueprints.BlueprintFieldDescriptor>(global::System.StringComparer.Ordinal)");
        WriteLine("{");
        Indent();
        foreach (var f in emittable)
        {
            // FC-2/LV-1: a fixed-list field's type is the PER-CLASS nested wrapper
            // (`__List_{Elem}_{N}`) -- this registration block runs OUTSIDE the class, so the bare
            // local name TypeRefToCSharp emits must be qualified here.
            var csharpType = StatementEmitter.TypeRefToCSharp(f.Type);
            if (f.Type.Capacity > 0)
                csharpType = $"{className}.{csharpType}";
            string offset = layoutFromRuntime
                ? $"global::Fdp.Core.TypeLayout.OffsetOf(typeof({className}.{stateStructName}), \"{f.Name}\")"   // ⭐ CE-2043 — managed, not interop
                : StructRelativeOffset(asset, f).ToString();
            string size = layoutFromRuntime
                ? $"global::System.Runtime.CompilerServices.Unsafe.SizeOf<{csharpType}>()"
                : f.Size.ToString();
            WriteLine($"[\"{f.Name}\"] = new global::Fdp.Toolkit.Blueprints.BlueprintFieldDescriptor(\"{f.Name}\", typeof({csharpType}), {offset}, {size}, \"\"),");
        }
        Outdent();
        WriteLine("},");
    }

    /// <summary>
    /// ⭐ The asset's baked offsets cannot be trusted when any declared type's size is a compiler
    /// guess (<c>SizeReliable == false</c> — the AN2 dotted-FQN fallback and the synthesized
    /// <c>__List_…</c> wrappers). ⚠ Hoisted out of <see cref="EmitStateFieldsBlock"/> because
    /// <see cref="Emit"/> needs the same answer <b>before</b> the registrar is written: the debug
    /// map's state layout is a COMPILE-TIME artefact and cannot carry a runtime query, so when this is
    /// true the only honest thing it can contribute is nothing.
    ///
    /// <para>
    /// ⭐ <b>Batch 60 (<c>W4</c>) — it now also decides whether the struct is DECLARED at those offsets</b>
    /// (<see cref="UseExplicitLayout"/>). ⚠ <c>GraphLocalSlots</c> joined the scan here: they are laid
    /// out in the SAME struct and an unreliable size among them makes every later offset a guess exactly
    /// as a state declaration would. Measured no-op on the corpus — <b>no shipped asset has any</b>.
    /// </para>
    /// </summary>
    /// <para>⭐ <c>CE-2053</c> (<c>DESIGN_Unified_Behaviour_Run</c> "S8n-2" ④) — <b>Parameters too.</b> They share the layout
    /// (the params region precedes the state, and a behaviour's <c>Block.In</c> precedes <c>St</c>), and
    /// <c>EmitParamsStruct</c>'s own contract is "stays Sequential under this regime". 🔴 Measured: a Parameter typed as a
    /// project struct the compile could not size (no oracle — every compile but the production generator) got the AN2
    /// 4-byte guess AND baked <c>[FieldOffset]</c>s, so <c>Demo_MissionPlan</c>'s 40-byte <c>Move</c> and <c>Fire</c> overlapped.</para>
    private static bool LayoutFromRuntime(IrAsset asset)
        => asset.StateDeclarations.Any(f => !f.Type.SizeReliable)
        || asset.GraphLocalSlots.Any(f => !f.Type.SizeReliable)
        || asset.Parameters.Any(f => !f.Type.SizeReliable);

    /// <summary>
    /// ⭐⭐⭐ <b><c>W4</c> — emit <c>LayoutKind.Explicit</c> + <c>[FieldOffset]</c> so the struct <b>IS</b>
    /// the computed layout instead of agreeing with it by luck.</b>
    ///
    /// <para>
    /// 🔴 <b>The luck was running out.</b> <c>FieldLayout.TypeAlignment</c> is
    /// <c>SizeBytes switch { 1 =&gt; 1, 2 =&gt; 2, &lt;= 4 =&gt; 4, _ =&gt; 8 }</c> — a guess from the size alone.
    /// It is right for every type the 42 shipped assets declare and <b>wrong for three the editor
    /// offers</b>: a 12-byte <c>Vector3</c> and a 16-byte <c>Quaternion</c> are 4-aligned in the CLR, a
    /// 32-byte <c>FixedString32</c> is a <c>fixed byte[32]</c> and 1-aligned. ⇒ a designer picking one
    /// from the type picker got descriptors pointing 4–8 bytes past the field. <c>U-8</c> promises every
    /// offered type compiles; this is what makes the promise true.
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b>Why it is gated on <see cref="LayoutFromRuntime"/> and MUST stay gated.</b> Explicit layout
    /// is only safe when every field's SIZE is exact. Under <c>Sequential</c> an under-estimated size
    /// merely pushes later fields down and the descriptors are recovered at runtime via
    /// <c>Marshal.OffsetOf</c>; under <c>Explicit</c> the oversized field would <b>overlap its
    /// neighbour</b> — two variables silently aliasing the same bytes, which is worse than the wrong
    /// offsets this change exists to fix. ⭐ The same gate also keeps the managed-reference hazard out:
    /// <c>LayoutKind.Explicit</c> throws <c>TypeLoadException</c> for a misaligned managed reference, and
    /// the only state types that reach the struct without passing <c>BP1503</c>'s unmanaged check are the
    /// AN2 "trust the dot" ones — which carry <c>SizeReliable = false</c> and therefore stay Sequential.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Alignment reliability is deliberately NOT a second predicate.</b> The switch only ever
    /// over-aligns (it never returns less than a real CLR alignment for any registered type), so a
    /// declared offset is always properly aligned; and once the offset is <i>declared</i> rather than
    /// <i>predicted</i>, being able to tell a good prediction from a bad one has no consumer left. What
    /// remains is padding efficiency, not correctness — filed, not fixed here.
    /// </para>
    /// </summary>
    internal static bool UseExplicitLayout(IrAsset asset) => !LayoutFromRuntime(asset);

    /// <summary>
    /// The <c>[FieldOffset]</c> value for <paramref name="f"/> — the same struct-relative number the
    /// descriptors carry, so the declaration and the descriptor cannot drift apart.
    /// </summary>
    internal static int FieldOffsetOf(IrAsset asset, IrField f) => StructRelativeOffset(asset, f);

    /// <summary>
    /// ⚠⚠ <b>The one asymmetry in the whole state-metadata path, and it bites silently.</b>
    /// <c>FieldLayout</c> lays an <b>Instance</b>'s state out from <b>16</b> — which IS a struct
    /// offset, because <c>State</c> opens with a 16-byte <c>BlueprintLatentCursor</c> — but lays an
    /// <b>AiPrimitive</b>'s out from <b>8</b>, which is <b>NOT</b> a struct offset: it is where the
    /// working state sits inside <c>Blackboard1024</c>, past the stored <c>StructureHash</c>. The
    /// <c>WorkingState</c> struct itself has no header at all.
    ///
    /// <para>
    /// ⛔ <c>BlueprintDebugSession.CaptureAiPrimitiveState</c> already reads at
    /// <c>8 + descriptor.OffsetBytes</c>, so a descriptor carrying the raw <c>IrField.Offset</c> would
    /// be off by exactly 8 — <b>plausible bytes from the wrong place</b>, which is worse than none.
    /// ⚠ The base cannot simply be changed to 0: it is hashed into <c>StructureHash</c> for all 32
    /// shipped AiPrimitive assets.
    /// </para>
    /// </summary>
    private static int StructRelativeOffset(IrAsset asset, IrField f)
        => f.Offset - (asset.Dispatch == AssetDispatch.AiPrimitive ? 8 : 0);

    /// <summary>
    /// ⭐⭐ <c>CE-446</c> (<c>Q77</c> §5.6) — a blueprint BEHAVIOUR registers a <c>BehaviorDefinition</c> by NAME on the third
    /// brain tier, exactly as a BTree/HSM registrar does (<c>BehaviorHash.FromName</c>). ⛔ It is NOT staged as an Instance:
    /// it is never attached, and its block is the root params slot the ingress allocates from <c>BlackboardLayoutType</c>.
    /// </summary>
    private void EmitBehaviorRegistration(string className, IrAsset asset)
    {
        WriteLine($"beh.Register(global::Fdp.Toolkit.Behavior.BehaviorHash.FromName(\"{asset.Name}\"), \"{asset.Name}\", "
                + "new global::Fdp.Toolkit.Behavior.BehaviorDefinition");
        WriteLine("{");
        Indent();
        WriteLine($"Name = \"{asset.Name}\",");
        WriteLine("BrainTier = global::Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBlueprint,");
        // ⭐ S2 (DESIGN_Unified_Behaviour_Run U-1) — the BLOCK is the root params slot; the brain state (cursor, When
        //   memory, suspended locals) is the root STATE slot, sized by the definition like a BTree cursor.
        WriteLine($"BlackboardLayoutType = typeof({className}.Block),");
        WriteLine($"BrainStateLayoutType = typeof({className}.Exec),");
        WriteLine($"BrainStateBytes = {className}.ExecSize,");
        // ⭐ S3 — the same two descriptions a BTree/HSM registrar publishes: the authored JSON contract (the Params
        //   struct IS the authored input, Q77 §5.11) and the Input manifest. ⇒ the watch pane, GET /behaviors and the
        //   inspector need no blueprint branch.
        if (asset.Parameters.Count > 0)
            WriteLine($"JsonParamsDtoType = typeof({className}.Params),");
        WriteLine($"ManagedBlackboardVariables = {className}.InputManifest(),");
        WriteLine($"ParseParams = {className}.BehaviorParseParams,");
        WriteLine($"BlueprintTick = {className}.BehaviorTick,");
        WriteLine($"BlueprintStructureHash = {className}.StructureHash,");
        // ⭐ S5d — each Run Behaviour site declares its hosted slot (ingress provisions it, recursively, S5b) …
        //   S6b — a site inside a fiber graph declares one slot per copy (copy 0 = the plain key).
        var siteKeys = InstanceEmitter.RunBehaviorSitesByGraph(asset)
            .SelectMany(t => Enumerable.Range(0, Lowering.Fibers.IsOwnFiber(t.Graph) ? Lowering.Fibers.Copies(t.Graph) : 1)
                .Select(k => (Key: InstanceEmitter.RunSiteKey(className, t.Site, k), t.Site.BehaviorName)))
            .ToList();
        if (siteKeys.Count > 0)
        {
            WriteLine("StatefulWorkingSlots = new global::Fdp.Toolkit.Behavior.StatefulSlotInfo[]");
            WriteLine("{");
            Indent();
            foreach (var (key, child) in siteKeys)
                WriteLine($"global::Fdp.Toolkit.Behavior.HostedSubtree.SiteSlot({key}, \"{child}\"),");
            Outdent();
            WriteLine("},");
        }
        Outdent();
        WriteLine("});");
        // … and binds its child AFTER Register: resolution is lazy and order-independent (CE-377); a ring is refused (S5c).
        foreach (var (key, child) in siteKeys)
            WriteLine($"global::Fdp.Toolkit.Behavior.HostedChildren.Register(beh, {key}, \"{child}\");");
    }

    private void EmitInstanceRegistration(string className, IrAsset asset)
    {
        var eventHandlers = asset.Graphs
            .Where(g => g.Kind == IrGraphKind.Event)
            .ToList();

        WriteLine($"staging.Add({className}.BlueprintId, new global::Fdp.Toolkit.Blueprints.BlueprintDefinition");
        WriteLine("{");
        Indent();
        WriteLine($"Name = \"{asset.Name}\",");
        WriteLine("Kind = global::Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,");
        WriteLine($"StructureHash = {className}.StructureHash,");
        WriteLine($"StateSize = {className}.StateSize,");
        WriteLine($"AssetId = new Guid(\"{asset.AssetId}\"),");
        WriteLine($"StateClrType = typeof({className}.State),");
        WriteLine($"InitDefault = {className}.InitDefault,");
        WriteLine($"Tick = {className}.TickThunk,");
        // ⭐⭐ Batch 70 / §3.3 — where the params region sits inside the payload, and how big it is.
        //    ⛔ Emitted rather than re-derived: `16` has one home (FieldLayout), and a runtime call
        //    site recomputing it is exactly the drift this seam exists to prevent.
        WriteLine($"ParamsOffset = {className}.ParamsOffset,");
        WriteLine($"ParamsSize = {className}.ParamsSize,");
        if (asset.Parameters.Count > 0)
            WriteLine($"ParseParams = {className}.ParseParams,");
        EmitStateFieldsBlock(className, asset, "State");
        if (eventHandlers.Count > 0)
        {
            WriteLine("EventHandlers = new global::System.Collections.Generic.Dictionary<string, global::Fdp.Toolkit.Blueprints.EventHandlerDelegate>(global::System.StringComparer.Ordinal)");
            WriteLine("{");
            Indent();
            // Q#14: key by the event IDENTITY (EventTypeFqn) — the FQN the runtime dispatch resolves to a
            // type-id — not the graph name (which is only the C# method suffix). Fallback to name for legacy
            // Event graphs that carry no event identity.
            // ⭐ CE-2011 (T-4) — one entry per key: two handlers of one event type share a group thunk (an indexer
            //   initialiser with a repeated key used to keep only the LAST, silently).
            foreach (var (key, handlers) in InstanceEmitter.EventHandlerGroups(asset))
                WriteLine($"[\"{key}\"] = {className}.{InstanceEmitter.EventTableThunk(handlers)},");
            Outdent();
            WriteLine("},");
        }
        Outdent();
        WriteLine("});");
    }
}
