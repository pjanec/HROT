using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class AiPrimitiveEmitter
{
    public static void EmitClass(CSharpEmitter e, IrAsset asset)
    {
        var className = $"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";

        e.WriteLine("namespace Hrot.AI.Behaviors.Generated;");
        e.WriteLine();
        e.WriteLine($"public static class {className}");
        e.WriteLine("{");
        e.Indent();

        e.WriteLine($"public const int BlueprintId = unchecked((int)0x{asset.BlueprintId:X8});");
        e.WriteLine($"public const ulong StructureHash = {asset.StructureHash}UL;");
        // ⭐ O7b — the CHILD's identity, for the occurrence key an HSM-hosted thunk computes. The HOST
        //   comes from the instance pointer the kernel passes (§24.9), so only this half is baked.
        // ⛔⛔ Emitted ONLY for an asset that actually declares HSM hosting. Emitting it for every
        //    asset moved 11 golden baselines for assets that cannot use it — the corpus must stay
        //    byte-identical wherever the feature is not used, which is the property O4 established
        //    and the thing that makes "did this change behaviour?" answerable.
        if (asset.Hostings.Contains(AiPrimitiveHosting.HsmAction) ||
            asset.Hostings.Contains(AiPrimitiveHosting.HsmGuard) ||
            asset.Hostings.Contains(AiPrimitiveHosting.BTreeAction) ||
            asset.Hostings.Contains(AiPrimitiveHosting.BTreeCondition))
        {
            e.WriteLine($"public static readonly global::System.Guid AssetId = new global::System.Guid(\"{asset.AssetId}\");");
        }
        e.WriteLine();

        EmitParamsStruct(e, asset);
        e.WriteLine();

        EmitWorkingStateStruct(e, asset);
        e.WriteLine();

        // ⭐ Batch 57 (S1) — the real size of the working state, mirroring InstanceEmitter's
        // `StateSize`. ⛔ The registrar used to write a literal `StateSize = 0`, which is not a
        // placeholder but a wrong answer: this struct occupies real bytes in Blackboard1024.
        e.WriteLine("public static int StateSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<WorkingState>();");
        e.WriteLine();

        EmitInitDefault(e, asset);
        e.WriteLine();

        EmitTickCore(e, asset);
        e.WriteLine();

        // BP-221 — a helper per non-main Function graph. InstanceEmitter has had this loop since
        // BATCH-03A; this emitter picked its main graph the same way and had no equivalent, while
        // StatementEmitter emitted `Func_<name>(...)` regardless — so an AiPrimitive that called one
        // of its own Function graphs produced CS0103 against a method nobody wrote. Reachable by
        // ordinary hand-authoring (place a second Function graph, place a FunctionCall); collapse
        // merely walked into it.
        var mainGraph = MainGraphOf(asset);
        foreach (var fg in asset.Graphs.Where(g => g.Kind == IrGraphKind.Function && g != mainGraph))
        {
            EmitPrimitiveFunctionMethod(e, asset, fg);
            e.WriteLine();
        }

        EmitThunks(e, asset, className);

        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitParamsStruct(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine("public struct Params");
        e.WriteLine("{");
        e.Indent();
        
        foreach (var f in asset.Parameters)
        {
            EmitComment(e, f.Comment);
            EmitStructField(e, CSharpType(f.Type), f.Name);
        }
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// ⭐⭐ Batch 56 / ruling 8 — the struct is still <b>called</b> <c>WorkingState</c> (that name is ABI,
    /// and <c>InlineActionLowering</c> emits it literally), but what it <b>holds</b> is the asset's ONE
    /// state tier, <see cref="IrAsset.StateDeclarations"/>. ⛔ It used to hold <c>asset.WorkingState</c>
    /// alone, so a <c>Variable</c> declaration on an AiPrimitive — legal since <c>U-12</c> retired
    /// <c>BP1024</c> — was bound by Stage 5 and then never emitted.
    /// </summary>
    private static void EmitWorkingStateStruct(CSharpEmitter e, IrAsset asset)
    {
        // FC-2/LV-1b (Q#19-E): a state field may be a fixed-capacity list -- emit the same
        // per-class nested wrapper structs the Instance State path uses (additive: assets without
        // list fields emit byte-identical output).
        InstanceEmitter.EmitListWrappers(e, asset.StateDeclarations);

        // ⭐⭐ W4 (Batch 60) — see CSharpEmitter.UseExplicitLayout. ⚠ The offsets written here are
        //    STRUCT-relative: FieldLayout lays an AiPrimitive's state out from 8, which is its position
        //    inside Blackboard1024 and not a struct offset. FieldOffsetOf does the same -8 rebase the
        //    descriptors take, so the declaration and the descriptor cannot disagree.
        bool explicitLayout = CSharpEmitter.UseExplicitLayout(asset);
        e.WriteLine(explicitLayout
            ? "[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit)]"
            : "[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine("public struct WorkingState");
        e.WriteLine("{");
        e.Indent();

        foreach (var f in asset.StateDeclarations)
        {
            EmitComment(e, f.Comment);
            if (explicitLayout) e.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset({CSharpEmitter.FieldOffsetOf(asset, f)})]");
            EmitStructField(e, CSharpType(f.Type), f.Name);
        }
        // BP-57 / Q27-A3 — suspending graphs' locals, appended AFTER the real fields so their offsets
        // continue the struct's layout (FieldLayout does the same arithmetic). Addressed by name only.
        foreach (var f in asset.GraphLocalSlots)
        {
            if (explicitLayout) e.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset({CSharpEmitter.FieldOffsetOf(asset, f)})]");
            EmitStructField(e, CSharpType(f.Type), f.Name);
        }
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// Emits one Sequential-layout struct field. Bool fields get <c>[MarshalAs(UnmanagedType.I1)]</c>:
    /// <c>Marshal.SizeOf</c>/<c>OffsetOf</c> default a bool to a 4-byte WIN32 BOOL, whereas the runtime
    /// <c>Unsafe.As</c> projection (and the host bin-packer's size math) treat it as 1 byte. Without I1
    /// the two models disagree, silently drifting offsets and corrupting AAR replay / partition-slot
    /// layout. Applies to both Params (inline, bin-packed at a baked offset) and WorkingState (partition
    /// slot sized at runtime via <c>Marshal.SizeOf&lt;WorkingState&gt;()</c>).
    /// </summary>
    private static void EmitStructField(CSharpEmitter e, string csType, string name)
    {
        if (csType == "bool")
            e.WriteLine("[global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.I1)]");
        e.WriteLine($"public {csType} {name};");
    }

    private static void EmitComment(CSharpEmitter e, string? comment)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            e.WriteLine("/// <summary>");
            var lines = comment!.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                e.WriteLine($"/// {line}");
            }
            e.WriteLine("/// </summary>");
        }
    }

    private static void EmitInitDefault(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("private static unsafe void InitDefaultWorkingState(WorkingState* dst)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("*dst = default;");
        // ⭐⭐ Batch 56 — the SILENT half. ⚠ And a coordinator error worth keeping: the Q32 draft asked
        // whether working state should have initial values "since it is per-run scratch". It always has —
        // these lines are `InstanceEmitter.EmitInitDefault`'s, over the other list. Reasoning from the
        // NAME instead of the code is what made two kinds look like two concepts for nineteen batches.
        // ⭐ BP-247 — value-based skip; see InstanceEmitter.EmitInitDefault.
        foreach (var f in asset.StateDeclarations.Where(f =>
            !Lowering.DefaultLiteral.IsSkippable(f.DefaultValueCSharp)))
        {
            e.WriteLine($"dst->{f.Name} = {f.DefaultValueCSharp};");
        }
        // FC-2/LV-1b (Q#19-B): declared initial length seeds Count over the zeroed slots -- the
        // partial init the whole-field DefaultValueCSharp path cannot express (review F2). Runs on
        // every hash-mismatch (re)init inside the generated thunks; the BlueprintCall host's inline
        // zero path leaves Count=0 (safe empty list; documented LV-1b limitation).
        foreach (var f in asset.StateDeclarations.Where(f => f.Type.Capacity > 0 && f.Type.InitialLength > 0))
        {
            e.WriteLine($"dst->{f.Name}.Count = {f.Type.InitialLength};");
        }
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickCore(CSharpEmitter e, IrAsset asset)
    {
        // I4: mark TickCore so the editor's reflection-based ActionSchemaExporter discovers this
        // blueprint AiPrimitive as a placeable AI action (DtoType is read from the first ref param,
        // `ref Params`). Flags mirror the compiler's hosting set so the exporter maps them to the
        // correct host graphs and marks conditions. Distinct from [BTreeAction]/[SharedAiAction] so
        // the FastBTree/Shared-AI generators never re-process this already-registered thunk.
        string B(bool v) => v ? "true" : "false";
        e.WriteLine(
            "[global::Fbt.Kernel.GeneratedAiPrimitiveAction("
            + $"bTreeAction: {B(asset.Hostings.Contains(AiPrimitiveHosting.BTreeAction))}, "
            + $"bTreeCondition: {B(asset.Hostings.Contains(AiPrimitiveHosting.BTreeCondition))}, "
            + $"hsmAction: {B(asset.Hostings.Contains(AiPrimitiveHosting.HsmAction))}, "
            + $"hsmGuard: {B(asset.Hostings.Contains(AiPrimitiveHosting.HsmGuard))}, "
            + $"blueprintCall: {B(asset.Hostings.Contains(AiPrimitiveHosting.BlueprintCall))})]");
        e.WriteLine("public static global::Fbt.NodeStatus TickCore(");
        e.Indent();
        e.WriteLine("ref Params p,");
        e.WriteLine("ref WorkingState ws,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("global::Fdp.Core.EntityRepository world,");
        e.WriteLine("float time)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();

        var mainGraph = MainGraphOf(asset);

        if (mainGraph != null)
            LibraryEmitter.EmitGraphBody(e, asset, mainGraph);

        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// BP-221 — the graph <c>TickCore</c> runs, and therefore the one graph that must NOT also be
    /// emitted as a helper. Factored out so <see cref="EmitTickCore"/> and the helper loop cannot
    /// disagree about which graph is the main one; they used to pick it with two copies of the same
    /// expression, which is how the loop came to be missing without anything noticing.
    /// </summary>
    private static IrGraph? MainGraphOf(IrAsset asset)
        => asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.AiPrimitiveMain)
           ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);

    /// <summary>
    /// BP-221 — an in-blueprint Function graph, as a private helper.
    ///
    /// <para>
    /// ⚠ <b>The signature is NOT <c>InstanceEmitter</c>'s.</b> An Instance helper takes
    /// <c>(ref State, view, ecb, self, time, deltaTime, instanceVersion)</c> because that is what an
    /// Instance body has in scope. <c>TickCore</c> has <c>(ref Params, ref WorkingState, self,
    /// world, time)</c> — no view, no ecb, no deltaTime, no instanceVersion — so the helper takes
    /// what the caller can actually pass, and <c>StatementEmitter</c>'s <c>IrOp_GraphCall</c> arm
    /// picks the matching argument list off the same dispatch.
    /// </para>
    ///
    /// <para>
    /// ⚠ <c>Params</c> is deliberately not threaded through: a helper graph's inputs arrive as real
    /// parameters, and <c>ws</c> carries everything stateful. Adding <c>ref Params</c> would make
    /// every helper depend on the primitive's DTO for no reader.
    /// </para>
    /// </summary>
    private static void EmitPrimitiveFunctionMethod(CSharpEmitter e, IrAsset asset, IrGraph graph)
    {
        // ⚠ Derived from the body, not assumed: an AiPrimitive graph lowers to NodeStatus
        // terminators, so declaring `void` here produced CS0127 the moment the body returned one.
        var retType   = LibraryEmitter.HelperReturnType(graph);
        var sanitized = Sanitizer.SanitizeName(graph.Name);

        var extraParams = graph.Inputs.Count > 0
            ? ", " + string.Join(", ", graph.Inputs.Select(f => LibraryEmitter.CSharpType(f.Type) + " " + f.Name))
            : "";

        e.WriteLine($"private static {retType} Func_{sanitized}(");
        e.Indent();
        e.WriteLine("ref WorkingState ws,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("global::Fdp.Core.EntityRepository world,");
        e.WriteLine($"float time{extraParams})");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        LibraryEmitter.EmitGraphBody(e, asset, graph);
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitThunks(CSharpEmitter e, IrAsset asset, string className)
    {
        foreach (var hosting in asset.Hostings)
        {
            switch (hosting)
            {
                case AiPrimitiveHosting.BTreeAction:
                    EmitBTreeActionThunk(e);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.BTreeCondition:
                    EmitBTreeConditionThunk(e);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.HsmAction:
                    EmitHsmActivityThunk(e);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.HsmGuard:
                    EmitHsmGuardThunk(e);
                    e.WriteLine();
                    break;
                case AiPrimitiveHosting.BlueprintCall:
                    EmitBlueprintCallThunk(e);
                    e.WriteLine();
                    break;
            }
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>W13</c> / <c>BP-251</c> (Batch 63) — the ONE parameter projection formula, repo-wide.</b>
    ///
    /// <para>
    /// 🔴 <b>This used to be a stride:</b> <c>bb.BehaviorParameters[paramIndex * Unsafe.SizeOf&lt;Params&gt;()]</c>.
    /// ⛔ <c>paramIndex</c> is <b>not</b> a slot the blueprint owns — <c>TreeCompiler:155</c> sets it from
    /// <c>GetOrAddMethodName(...)</c>, so it is the ordinal among <b>every distinct Action and Condition
    /// method name in the whole tree</b>. ⇒ the multiplier grows with TREE SIZE. Measured:
    /// <c>PlatoonHillAttack2</c> would put a 40-byte <c>Params</c> at index 5 ⇒ bytes <b>200…240 of a
    /// 100-byte buffer inside a 128-byte component</b> — past the component entirely.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>Why this is a ROUTE and not a delete.</b> The thunk is <b>not</b> vestigial: it is the
    /// architect-confirmed <i>blueprint-as-behavior</i> standalone hosting, opt-in per
    /// <see cref="AiPrimitiveHosting.BTreeAction"/>/<c>BTreeCondition</c> — <c>SLICE1-DESIGN §82</c>
    /// records the ruling (<i>"BTree owns layout, blueprint provides TickCore"</i>) and says the
    /// composition path <b>ignores</b> this thunk, while <c>SLICE2-DESIGN:52</c> says the thunk
    /// <i>"stays the standalone blueprint-as-behavior hosting."</i> ⇒ deleting it would remove a
    /// capability, not a mistake.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>What the <c>@0</c> in the registered key always meant.</b> Standalone hosting IS the
    /// single-method case, where the payload index is 0 — so <c>@0</c> was <b>truthful for the case the
    /// thunk exists for</b> and a lie only when bound anywhere else. ⛔ Nothing enforced that. Projecting
    /// at a literal 0 makes the key true <b>by construction</b> instead of by convention, which is the
    /// same invariant <c>W1</c>'s third rail states from the other end.
    /// </para>
    ///
    /// <para>
    /// ⚠ The <c>paramIndex</c> parameter stays in the signature: it is <c>NodeLogicDelegate</c>'s shape,
    /// not ours to change. ⭐ It is now unread — exactly as the bridge's per-node adapter leaves it.
    /// </para>
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ <c>E3a</c> / <c>CE-298</c> — <b>the SEED, and it runs ONCE per occurrence.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.4.
    ///
    /// <para>🔴 <b>What this used to be.</b> The identical expression, evaluated on EVERY dispatch as
    /// the LIVE params home: <c>Unsafe.As&lt;byte, Params&gt;(ref bb.BehaviorParameters[0] + 0)</c>.
    /// ⛔ A literal <c>0</c> shared by every occurrence on the entity ⇒ two HSM regions running actions
    /// overwrote each other, and two DIFFERENT blueprints type-punned each other's variable.
    /// 🔒 <b>User, <c>2026-09-21</c>:</b> <i>"forget the fact it is not in use now. it will be."</i></para>
    ///
    /// <para>⭐⭐ <b>Why the seed comes from HERE and not from zeros.</b> This is the ONLY place an
    /// authored value for this occurrence exists today — an HSM state's binding is a bare method-name
    /// string with no params of its own. ⇒ copying makes the move <b>byte-identical at the first
    /// dispatch</b>, so it is provably not a downgrade. ⛔ Seeding from <c>default</c> would hand every
    /// occurrence zeroed params where today it gets the behaviour's authored ones — §26.1, the rule
    /// <c>O7d</c> was reverted twice for.</para>
    ///
    /// <para>⚠ <b>Offset <c>0</c> is the seed's SOURCE, never the destination.</b> It is inherited from
    /// the model being retired and it dies at <c>E3b</c>, when <c>Q41-C1′</c>'s resolve hook gives a
    /// per-site authored value. ⛔ It is not a new dependency on the blackboard.</para>
    ///
    /// <para>⛔ <b>And the re-supply is <c>BehaviorIngressSystem</c>'s half</b>
    /// (<c>DetachHostedOccurrenceSlots</c>): without it a re-assign's new JSON would never reach the
    /// slot, because this seed only runs when the slot is created.</para>
    /// </summary>
    private static void EmitParamSeed(CSharpEmitter e, string offsetExpr, string hostExpr)
    {
        e.WriteLine("if (freshlyAttached)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("// E3a SEED (§28.4): the bytes this thunk read LIVE before params moved into the");
        e.WriteLine("//   slot. Copying them makes the move byte-identical at the first dispatch.");
        if (offsetExpr != "0")
        {
            e.WriteLine("// E3b-0 (§28.6): …and WHICH bytes is the STATE's own binding, not always the");
            e.WriteLine("//   first variable — that is what lets two parallel regions differ.");
            e.WriteLine($"int __seedOffset = {offsetExpr};");
            offsetExpr = "__seedOffset";
        }
        e.WriteLine("*__params = global::System.Runtime.CompilerServices.Unsafe.As<byte, Params>(");
        e.WriteLine("    ref global::System.Runtime.CompilerServices.Unsafe.AddByteOffset(");
        e.WriteLine($"        ref bb.BehaviorParameters[0], (nint){offsetExpr}));");
        EmitHostedResolve(e, hostExpr);
        e.WriteLine("InitDefaultWorkingState((WorkingState*)global::System.Runtime.CompilerServices.Unsafe.AsPointer(ref ws));");
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>Q41-C1′</c> for the hosted path — <b>the RESOLVE stage, at the child's activation.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.7.
    ///
    /// <para>🔴 <b>The pipeline <c>BehaviorParams.FromJson</c> specifies is bake → overlay → RESOLVE →
    /// write, and the RESOLVE stage has never been emitted anywhere.</b> ⭐ This is it, on the one path
    /// where <c>IHostVariableAccess</c> can be non-null — a hosted occurrence, which by definition has
    /// a host.</para>
    ///
    /// <para>⭐⭐ <b>Why it belongs in the SEED and not in <c>ParseParams</c>.</b> A resolver reads
    /// <c>world</c>, <c>self</c> and <c>host</c>, so its result depends on the OCCURRENCE's context.
    /// ⛔ The behaviour's <c>ParseParams</c> runs once per assign with <c>host: null</c> — running the
    /// resolve there and copying the result into every occurrence would be wrong by construction.</para>
    ///
    /// <para>⚠ <b>Resolve-ONCE</b> (§3.1): this sits inside <c>if (freshlyAttached)</c>, so it runs at
    /// activation and never on a steady-state dispatch. ⛔ Live re-binding is out of the model (<c>R-84</c>).</para>
    ///
    /// <para>⭐ <b>Free when unused</b> — <c>TryRun</c> is a dictionary miss for every asset with no
    /// registered resolver, which §3.1 says is the overwhelmingly common case.</para>
    /// </summary>
    private static void EmitHostedResolve(CSharpEmitter e, string hostExpr)
    {
        e.WriteLine("// C1′ (§28.7): the RESOLVE stage — the one place IHostVariableAccess is non-null.");
        e.WriteLine("global::Fdp.Toolkit.Behavior.HostedParamResolvers.TryRun(");
        e.WriteLine($"    AssetId, ref *__params, {WorldExprOf(hostExpr)}, {SelfExprOf(hostExpr)}, {hostExpr});");
    }

    /// <summary>The repository expression that goes with a given host expression.</summary>
    private static string WorldExprOf(string hostExpr) => hostExpr == "null" ? "ctx.World" : "world";

    /// <summary>The entity expression that goes with a given host expression.</summary>
    private static string SelfExprOf(string hostExpr) => hostExpr == "null" ? "ctx.Self" : "bridge->Self";

    private static void EmitBTreeActionThunk(CSharpEmitter e)
    {
        e.WriteLine("public static unsafe global::Fbt.NodeStatus BTreeTick(");
        e.Indent();
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.Components.BrainBlackboard bb,");
        e.WriteLine("ref global::Fbt.BehaviorTreeState state,");
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BTreeContext ctx,");
        e.WriteLine("int paramIndex)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        EmitStandaloneOccurrenceBody(e,
            "return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime);");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitBTreeConditionThunk(CSharpEmitter e)
    {
        e.WriteLine("public static unsafe bool BTreeEvaluate(");
        e.Indent();
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.Components.BrainBlackboard bb,");
        e.WriteLine("ref global::Fbt.BehaviorTreeState state,");
        e.WriteLine("ref global::Fdp.Toolkit.Behavior.BTreeContext ctx,");
        e.WriteLine("int paramIndex)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        EmitStandaloneOccurrenceBody(e,
            "return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime) == global::Fbt.NodeStatus.Success;");
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>O7d</c> — the occurrence-keyed body both STANDALONE BTree thunks share.
    ///
    /// <para>🔴 <b>What it replaces.</b> <c>GetComponentRW&lt;Blackboard1024&gt;(ctx.Self)</c> at a
    /// hard-coded <c>memory + 8</c> — one region for EVERY asset on the entity, in a component
    /// production adds to <b>no entity at all</b> (its two <c>AddComponent</c> sites are gated on
    /// <c>HeavyDtoType</c>, which nothing ever sets). ⇒ this thunk would have <b>thrown</b> the moment
    /// it was bound.</para>
    ///
    /// <para>⛔⛔ <b>ASSET-scoped, and that is forced.</b> 📐 The interpreter hands an action delegate
    /// only <c>node.PayloadIndex</c> — no node identity — so a single shared thunk cannot key itself
    /// per-occurrence. ⭐ Per-node IS the BRIDGE's job: it emits one adapter per node with the slot key
    /// BAKED. The standalone thunk is the degenerate single-occurrence case, which is exactly what the
    /// <c>@0</c> in its own registration key has always said.</para>
    /// </summary>
    private static void EmitStandaloneOccurrenceBody(CSharpEmitter e, string tail)
    {
        e.WriteLine("// O7d/E3a: this asset's OWN params AND working state, in the entity's occurrence");
        e.WriteLine("//          store — NOT Blackboard1024, and no longer the SHARED param region.");
        e.WriteLine("int occurrenceKey = global::Fdp.Toolkit.Behavior.OccurrenceSlots.StandaloneStateKeyFor(AssetId);");
        e.WriteLine("ref var ws = ref global::Fdp.Toolkit.Behavior.OccurrenceWorkingState.ResolveOrAttach<Params, WorkingState>(");
        e.WriteLine("    ctx.World, ctx.Self, occurrenceKey, StructureHash,");
        e.WriteLine("    global::Fdp.Toolkit.Blueprints.Partitioning.OccurrenceKind.Blueprint, out bool freshlyAttached, out Params* __params);");
        // ⭐ Offset 0, and TRUE BY CONSTRUCTION here: standalone hosting is the single-
        //   occurrence case (the `@0` in its own registration key). ⛔ No site to bind.
        EmitParamSeed(e, "0", "null");   // standalone: no host, so no host variables to read
        e.WriteLine("ref var p = ref *__params;");
        e.WriteLine(tail);
    }

    private static void EmitHsmActivityThunk(CSharpEmitter e)
    {
        e.WriteLine("public static unsafe void HsmActivity(void* instance, void* context, global::Fhsm.Kernel.Data.HsmCommandWriter* writer)");
        e.WriteLine("{");
        e.Indent();
        EmitHsmOccurrenceBody(e, "TickCore(ref p, ref ws, bridge->Self, world, world.SimulationTime);");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitHsmGuardThunk(CSharpEmitter e)
    {
        e.WriteLine("public static unsafe bool HsmGuard(void* instance, void* context, ushort eventId, global::Fhsm.Kernel.Data.HsmCommandWriter* writer)");
        e.WriteLine("{");
        e.Indent();
        EmitHsmOccurrenceBody(e,
            "return TickCore(ref p, ref ws, bridge->Self, world, world.SimulationTime) == global::Fbt.NodeStatus.Success;");
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>O7b</c> — the occurrence-keyed body both HSM thunks share.
    ///
    /// <para>🔴 <b>It replaces TWO defects at once.</b> ① <c>BP-297</c>/<c>E3</c>: the working state was
    /// <c>GetComponentRW&lt;Blackboard1024&gt;(self)</c> at a hard-coded <c>memory + 8</c> — one per
    /// ENTITY, so two concurrently-active regions aliased. ② <c>CE-297</c>: the params were read as
    /// <c>*(Params*)instance</c>, but the kernel passes the <b>HSM INSTANCE</b> there — the sibling
    /// generator ignores that pointer and projects from <c>BrainBlackboard</c>, which is what this now
    /// does, matching the BTree path.</para>
    /// </summary>
    private static void EmitHsmOccurrenceBody(CSharpEmitter e, string tail)
    {
        e.WriteLine("var bridge = (global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*)context;");
        e.WriteLine("var world = (global::Fdp.Core.EntityRepository)global::System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;");
        e.WriteLine();
        e.WriteLine("// CE-297: the SEED for params comes from the blackboard, NOT from the kernel's");
        e.WriteLine("//         instance pointer. E3a: it is a seed now, not the live home.");
        e.WriteLine("ref var bb = ref world.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.BrainBlackboard>(bridge->Self);");
        e.WriteLine("// E3b (§28.7): the HOST's params region, so a resolver can read the host's own");
        e.WriteLine("//   variables BY NAME through IHostVariableAccess — its first implementation.");
        e.WriteLine("byte* __hostParams = (byte*)global::System.Runtime.CompilerServices.Unsafe.AsPointer(");
        e.WriteLine("    ref bb.BehaviorParameters[0]);");
        e.WriteLine();
        e.WriteLine("// O7/E3: this occurrence's OWN params AND working state, keyed by the (region, state)");
        e.WriteLine("//        the kernel stamped (O6) and by the hosting machine's id from the instance header.");
        e.WriteLine("int occurrenceKey = global::Fdp.Toolkit.Behavior.HsmOccurrence.KeyFor(instance, AssetId, writer);");
        e.WriteLine("ref var ws = ref global::Fdp.Toolkit.Behavior.HsmOccurrence.ResolveOrAttach<Params, WorkingState>(");
        e.WriteLine("    world, bridge->Self, occurrenceKey, StructureHash, out bool freshlyAttached, out Params* __params);");
        EmitParamSeed(e, "global::Fdp.Toolkit.Behavior.HsmOccurrence.SeedParamsOffset(instance, writer)",
                      "global::Fdp.Toolkit.Behavior.HsmHostVariableAccess.For(instance, __hostParams, global::Fdp.Toolkit.Behavior.BehaviorConstants.MaxBehaviorParamByteSize)");
        e.WriteLine("ref var p = ref *__params;");
        e.WriteLine(tail);
    }

    private static void EmitBlueprintCallThunk(CSharpEmitter e)
    {
        e.WriteLine("public static global::Fbt.NodeStatus Call(");
        e.Indent();
        e.WriteLine("ref Params p,");
        e.WriteLine("ref WorkingState ws,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("global::Fdp.Core.EntityRepository world,");
        e.WriteLine("float time)");
        e.Outdent();
        e.WriteLine("    => TickCore(ref p, ref ws, self, world, time);");
    }

    private static string CSharpType(IrTypeRef t) => StatementEmitter.TypeRefToCSharp(t);
}
