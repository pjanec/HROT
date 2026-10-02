using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class InstanceEmitter
{
    // ⭐⭐ S2 (DESIGN_Unified_Behaviour_Run U-1) — a blueprint BEHAVIOUR's state is TWO structs: its blackboard block
    //   `Block { Params In; Vars St; }` (the root params slot — all a resolver or reader sees) and its brain state
    //   `Exec { Cursor; When memory; suspended locals }` (the root state slot). An Instance keeps its one `State`.
    private static bool IsBehavior(IrAsset a) => a.Dispatch == Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Behavior;
    private static string StateParamDecl(IrAsset a) => IsBehavior(a) ? "ref Block __bb, ref Exec __ex," : "ref State s,";
    private static string StateArgs(IrAsset a) => IsBehavior(a) ? "ref __bb, ref __ex" : "ref s";
    private static string VarsContainer(IrAsset a) => IsBehavior(a) ? "__bb.St" : "s";

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
        e.WriteLine();

        EmitStateStruct(e, asset);
        e.WriteLine();

        EmitVarIds(e, asset);
        e.WriteLine();

        var condMetOps = CollectConditionMetOps(asset);
        if (condMetOps.Count > 0)
        {
            e.WriteLine();
            EmitConditionMetFields(e, condMetOps);
            e.WriteLine();
            EmitInitializePredicates(e, condMetOps);
        }

        var eqsOps = CollectEqsResultOps(asset);
        if (eqsOps.Count > 0)
        {
            e.WriteLine();
            EmitEqsResultPrevStateStructs(e, eqsOps);
            EmitEqsConstFields(e, eqsOps);
        }

        var readEqsOps = CollectReadEqsResultOps(asset);
        if (readEqsOps.Count > 0)
        {
            e.WriteLine();
            EmitReadEqsResultHelpers(e, asset, readEqsOps);
        }

        var scoreDecisionOps = CollectScoreDecisionOps(asset);
        if (scoreDecisionOps.Count > 0)
        {
            e.WriteLine();
            EmitScoreDecisionHelpers(e, scoreDecisionOps);
        }

        var readRankedResultOps = CollectReadRankedResultOps(asset);
        if (readRankedResultOps.Count > 0)
        {
            e.WriteLine();
            EmitReadRankedResultHelpers(e, readRankedResultOps);
        }

        if (IsBehavior(asset))
        {
            // ⭐ S2 — the block (root params slot) and the brain state (root state slot) are sized separately.
            e.WriteLine("public static int StateSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<Block>();");
            e.WriteLine("public static int ExecSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<Exec>();");
        }
        else
            e.WriteLine("public static int StateSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<State>();");
        e.WriteLine();

        EmitParamsGeometry(e, asset);
        e.WriteLine();

        EmitInitDefault(e, asset);
        e.WriteLine();

        if (asset.Parameters.Count > 0)
        {
            EmitParseParams(e, asset);
            e.WriteLine();
        }

        foreach (var evtGraph in asset.Graphs.Where(g => g.Kind == IrGraphKind.Event))
        {
            EmitEventMethod(e, asset, evtGraph);
            e.WriteLine();
        }

        EmitTickMethod(e, asset);
        e.WriteLine();

        // Emit private helper methods for each non-Tick Function graph (BATCH-03A).
        var tickGraph = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function && g.Name == "Tick")
            ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);
        foreach (var fg in asset.Graphs.Where(g => g.Kind == IrGraphKind.Function && g != tickGraph))
        {
            EmitInstanceFunctionMethod(e, asset, fg);
            e.WriteLine();
        }

        // ⭐ CE-446: a BEHAVIOUR is registered as a behaviour, not an Instance — its entry points replace the thunk.
        if (asset.Dispatch == Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Behavior)
            EmitBehaviorEntryPoints(e, asset);
        else
            EmitTickThunk(e);
        e.WriteLine();

        // ⭐ S2 — a behaviour dispatches its events inline in BehaviorTick (two state refs cannot ride the shared
        //   Span<byte> handler delegate); the handler-table thunks are an Instance concern.
        foreach (var evtGraph in asset.Graphs.Where(g => g.Kind == IrGraphKind.Event))
        {
            EmitEventThunk(e, evtGraph, behaviour: IsBehavior(asset));
            e.WriteLine();
        }

        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// ⭐⭐ Batch 56 / ruling 8 — <c>State</c> holds the asset's ONE state tier,
    /// <see cref="IrAsset.StateDeclarations"/>. ⛔ It used to hold <c>Variables</c> alone, so a
    /// <c>WorkingState</c> declaration on an Instance — legal since <c>U-12</c> split <c>BP1031</c> —
    /// was bound by Stage 5 and then never emitted.
    /// </summary>
    private static void EmitStateStruct(CSharpEmitter e, IrAsset asset)
    {
        // ⚠ ONE wrapper pass over state AND params: EmitListWrappers dedupes within a call, so two
        //   calls sharing a `__List_…` shape would emit the type twice (CS0101). Byte-identical for
        //   every asset with no parameters, which is all 296 shipped Instances.
        EmitListWrappers(e, asset.Parameters.Count == 0
            ? asset.StateDeclarations
            : asset.StateDeclarations.Concat(asset.Parameters).ToList());
        EmitParamsStruct(e, asset);

        if (IsBehavior(asset))
        {
            EmitBehaviorStructs(e, asset);
            return;
        }

        // ⭐⭐ W4 (Batch 60) — when every size is exact, the struct is DECLARED at the computed offsets
        //    rather than left to agree with them. See CSharpEmitter.UseExplicitLayout for why this is
        //    gated and why alignment reliability is not a second predicate.
        bool explicitLayout = CSharpEmitter.UseExplicitLayout(asset);
        e.WriteLine(explicitLayout
            ? "[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit)]"
            : "[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine("public struct State");
        e.WriteLine("{");
        e.Indent();
        if (explicitLayout) e.WriteLine("[global::System.Runtime.InteropServices.FieldOffset(0)]");
        e.WriteLine("public global::Fdp.Toolkit.Blueprints.BlueprintLatentCursor Cursor;  // first 16 bytes");
        // ⭐⭐⭐ Batch 70 / DESIGN_Parameter_Model.md §3.3 — [Cursor 16][Params N][State M]. The params
        //   region is part of the ONE payload struct, so StateSize keeps meaning "the whole payload"
        //   and TryAttach/ChooseTier need no new arithmetic. ⛔ Emitted ONLY when the asset declares
        //   parameters: 296 shipped Instance assets declare none, and the field's absence keeps their
        //   generated text — and their StructureHash — byte-identical.
        if (asset.Parameters.Count > 0)
        {
            if (explicitLayout) e.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset({ParamsOffsetOf(asset)})]");
            e.WriteLine($"public Params Params;  // params region, {ParamsOffsetOf(asset)} .. + ParamsSize");
        }
        foreach (var f in asset.StateDeclarations)
        {
            if (explicitLayout) e.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset({CSharpEmitter.FieldOffsetOf(asset, f)})]");
            e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
        }
        // BP-57 / Q27-A3 — suspending graphs' locals, appended AFTER the real fields so their offsets
        // continue the struct's layout (FieldLayout does the same arithmetic). Addressed by name only.
        foreach (var f in asset.GraphLocalSlots)
        {
            if (explicitLayout) e.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset({CSharpEmitter.FieldOffsetOf(asset, f)})]");
            e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
        }
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// ⭐⭐ S2 (DESIGN_Unified_Behaviour_Run U-1) — a blueprint behaviour's TWO structs.
    /// <c>Block { Params In; Vars St; }</c> is the blackboard (the root params slot: Input prefix at 0, exactly like a
    /// BTree/HSM <c>{Asset}_Block</c>); <c>Exec { BlueprintLatentCursor Cursor; … }</c> is the brain state (the root state
    /// slot: the cursor, When memory and suspended-graph locals). ⛔ Nothing in <c>Exec</c> is reachable from a resolver.
    /// </summary>
    private static void EmitBehaviorStructs(CSharpEmitter e, IrAsset asset)
    {
        bool explicitLayout = CSharpEmitter.UseExplicitLayout(asset);
        string Layout(bool expl) => expl
            ? "[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit)]"
            : "[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]";
        var vars = asset.StateDeclarations;
        int varsBase = vars.Count > 0 ? vars[0].Offset : 0;

        if (vars.Count > 0)
        {
            e.WriteLine(Layout(explicitLayout));
            e.WriteLine("public struct Vars");
            e.WriteLine("{");
            e.Indent();
            foreach (var f in vars)
            {
                if (explicitLayout) e.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset({f.Offset - varsBase})]");
                e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
            }
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();
        }

        e.WriteLine(Layout(explicitLayout));
        e.WriteLine("public struct Block");
        e.WriteLine("{");
        e.Indent();
        if (asset.Parameters.Count > 0)
        {
            if (explicitLayout) e.WriteLine("[global::System.Runtime.InteropServices.FieldOffset(0)]");
            e.WriteLine("public Params In;   // the Input prefix — the authored parameters");
        }
        if (vars.Count > 0)
        {
            if (explicitLayout) e.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset({varsBase})]");
            e.WriteLine("public Vars St;     // the Variables");
        }
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();

        e.WriteLine(Layout(explicitLayout));
        e.WriteLine("public struct Exec");
        e.WriteLine("{");
        e.Indent();
        if (explicitLayout) e.WriteLine("[global::System.Runtime.InteropServices.FieldOffset(0)]");
        e.WriteLine("public global::Fdp.Toolkit.Blueprints.BlueprintLatentCursor Cursor;");
        foreach (var f in asset.GraphLocalSlots)
        {
            if (explicitLayout) e.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset({f.Offset})]");
            e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
        }
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// ⭐ Where an Instance's params region begins — after the 16-byte <c>BlueprintLatentCursor</c>.
    ///
    /// <para>
    /// ⛔⛔ <b>It asks <c>FieldLayout</c> rather than repeating the number.</b> A private
    /// <c>=&gt; 16</c> here was the first draft, and a revert probe caught what it costs: with the
    /// layout base reverted to <c>0</c>, the emitted <c>ParamsOffset</c> constant stayed <b>16</b>
    /// while the fields were laid at <b>0</b> — the declaration and the layout describing different
    /// memory, which is the drift this constant exists to prevent. ⭐ One home: <c>FieldLayout</c> lays
    /// the fields at it, this declares the struct at it, and the registration emits it onto
    /// <c>BlueprintDefinition.ParamsOffset</c> so no runtime call site re-derives it either.
    /// </para>
    /// </summary>
    private static int ParamsOffsetOf(IrAsset asset) => Lowering.FieldLayout.ParamsStructBase(asset);

    /// <summary>
    /// ⭐⭐ The nested <c>Params</c> struct — the Instance mirror of <c>AiPrimitiveEmitter</c>'s
    /// top-level one. ⚠ Its <c>[FieldOffset]</c>s are <b>struct-relative</b> (<c>f.Offset -
    /// ParamsOffset</c>), because <c>FieldLayout</c> lays params at their PAYLOAD offset.
    /// <para>
    /// ⭐ <c>Size</c> is declared too, so the struct occupies exactly the bytes the layout reserved and
    /// the state fields that follow cannot be pushed by CLR tail padding. Under the
    /// <c>LayoutFromRuntime</c> regime (a field whose size the compiler cannot know) it stays
    /// Sequential, exactly as <c>State</c> does — offsets are queried from the real type there.
    /// </para>
    /// </summary>
    private static void EmitParamsStruct(CSharpEmitter e, IrAsset asset)
    {
        if (asset.Parameters.Count == 0) return;

        bool explicitLayout = CSharpEmitter.UseExplicitLayout(asset);
        if (explicitLayout)
        {
            int size = ParamsRegionSize(asset);
            e.WriteLine("[global::System.Runtime.InteropServices.StructLayout("
                        + "global::System.Runtime.InteropServices.LayoutKind.Explicit, "
                        + $"Size = {size})]");
        }
        else
        {
            e.WriteLine("[global::System.Runtime.InteropServices.StructLayout("
                        + "global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        }
        e.WriteLine("public struct Params");
        e.WriteLine("{");
        e.Indent();
        foreach (var f in asset.Parameters)
        {
            if (explicitLayout)
                e.WriteLine($"[global::System.Runtime.InteropServices.FieldOffset({f.Offset - ParamsOffsetOf(asset)})]");
            e.WriteLine($"public {CSharpType(f.Type)} {f.Name};");
        }
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>The bytes the params region occupies, as <c>FieldLayout</c> reserved them.</summary>
    private static int ParamsRegionSize(IrAsset asset)
    {
        int end = ParamsOffsetOf(asset);
        foreach (var f in asset.Parameters)
            end = System.Math.Max(end, f.Offset + f.Size);
        return end - ParamsOffsetOf(asset);
    }

    /// <summary>
    /// ⭐⭐ <c>ParamsOffset</c> / <c>ParamsSize</c>, emitted so the runtime never re-derives them.
    /// ⚠ <c>ParamsSize</c> is <c>Unsafe.SizeOf&lt;Params&gt;()</c> rather than a baked number: the
    /// scratch buffer the attach path parses into must be the size the CLR actually gave the struct,
    /// not the size the compiler predicted.
    /// </summary>
    private static void EmitParamsGeometry(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine($"public const int ParamsOffset = {ParamsOffsetOf(asset)};");
        e.WriteLine(asset.Parameters.Count > 0
            ? "public static int ParamsSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<Params>();"
            : "public static int ParamsSize => 0;");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>DESIGN_Parameter_Model.md</c> §3.3 — <b>an Instance parses its params through the SAME
    /// pipeline a behaviour does.</b> The signature is <c>ParseParamsDelegate</c> verbatim: only the
    /// destination pointer differs (a behaviour passes <c>&amp;bb.BehaviorParameters[0]</c>, an
    /// Instance passes <c>slotPayload + ParamsOffset</c>). ⛔ No second delegate type (ruling 9).
    ///
    /// <para>
    /// ⭐ The body is <c>DEBT-AIB-021</c>'s decided shape, one mechanism for both hosts: <b>bake the
    /// declared defaults FIRST, then overlay a wrapper object keyed by parameter name.</b> An absent
    /// key leaves the default standing; an <b>unknown key is IGNORED</b> (what the curated path already
    /// does); <b>malformed JSON THROWS</b>, which is what makes parse-before-commit meaningful.
    /// </para>
    ///
    /// </summary>
    private static void EmitParseParams(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("public static unsafe void ParseParams(");
        e.Indent();
        e.WriteLine("string json,");
        e.WriteLine("byte* memory,");
        e.WriteLine("int capacity,");                 // CE-331 — the writable extent
        e.WriteLine("global::Fdp.Core.EntityRepository world,");
        e.WriteLine("global::Fdp.Core.Entity self)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        // ⭐⭐⭐ CE-331 — THE EMITTED PARSER CHECKS ITS OWN ROOM, which a bare `byte*` made
        //   impossible. `Unsafe.AsRef<Params>` reinterprets the pointer as the whole struct, so
        //   a region narrower than `sizeof(Params)` is corrupted by the very first write
        //   (`p = default`) — before any field is even parsed. ⇒ refuse, loudly, instead.
        e.WriteLine("if (capacity < sizeof(Params))");
        e.WriteLine("    throw new global::System.ArgumentOutOfRangeException(nameof(capacity),");
        e.WriteLine("        $\"the params region holds {capacity} bytes but this asset's Params "
                  + "needs {sizeof(Params)}; the occurrence slot was sized for a different "
                  + "behaviour (CE-331).\");");
        e.WriteLine("ref var p = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<Params>(memory);");
        e.WriteLine("p = default;");

        EmitParamsDefaultsAndOverlay(e, asset);
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();
        EmitParamJsonOptions(e);
    }

    /// <summary>
    /// ⭐ The one "declared Parameter defaults, then the JSON wrapper object by name" body, over a local
    /// <c>ref Params p</c> — shared by an Instance's <c>ParseParams</c> and a behaviour resolver asset's
    /// <c>ParseAuthored</c> (<c>CE-443</c>). An absent key keeps its default; an unknown key is ignored;
    /// malformed JSON throws. ⚠ Needs <c>__ParamJsonOptions</c> in scope (<see cref="EmitParamJsonOptions"/>).
    /// </summary>
    internal static void EmitParamsDefaultsAndOverlay(CSharpEmitter e, IrAsset asset)
    {
        // Step 1 — the declared defaults.
        foreach (var f in asset.Parameters.Where(f =>
            !Lowering.DefaultLiteral.IsSkippable(f.DefaultValueCSharp)))
        {
            e.WriteLine($"p.{f.Name} = {f.DefaultValueCSharp};");
        }
        foreach (var f in asset.Parameters.Where(f => f.Type.Capacity > 0 && f.Type.InitialLength > 0))
        {
            e.WriteLine($"p.{f.Name}.Count = {f.Type.InitialLength};");
        }

        // Step 2 — the overlay.
        e.WriteLine("if (string.IsNullOrWhiteSpace(json)) return;");
        e.WriteLine("using var __doc = global::System.Text.Json.JsonDocument.Parse(json);");
        e.WriteLine("if (__doc.RootElement.ValueKind != global::System.Text.Json.JsonValueKind.Object) return;");
        e.WriteLine("foreach (var __prop in __doc.RootElement.EnumerateObject())");
        e.WriteLine("{");
        e.Indent();
        // ⭐ CE-464: keys match CASE-INSENSITIVELY, like __ParamJsonOptions (PropertyNameCaseInsensitive) and the curated
        //   DTO parsers — a scenario authored "firingLineStart" must fill FiringLineStart. ⛔ It matched exactly, so a
        //   camelCase key was silently dropped as "unknown" and the parameter kept its default.
        e.WriteLine("switch (__prop.Name.ToLowerInvariant())");
        e.WriteLine("{");
        e.Indent();
        foreach (var f in asset.Parameters)
        {
            e.WriteLine($"case \"{f.Name.ToLowerInvariant()}\":");
            e.Indent();
            e.WriteLine($"p.{f.Name} = global::System.Text.Json.JsonSerializer.Deserialize<{CSharpType(f.Type)}>(");
            e.WriteLine("    __prop.Value.GetRawText(), __ParamJsonOptions)!;");
            e.WriteLine("break;");
            e.Outdent();
        }
        e.WriteLine("// ⭐ Unknown key: IGNORED, matching the curated path's own behaviour.");
        e.WriteLine("default: break;");
        e.Outdent();
        e.WriteLine("}");
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>The platform-canonical JSON options field the overlay body reads.</summary>
    internal static void EmitParamJsonOptions(CSharpEmitter e)
    {
        e.WriteLine("// ⭐ The platform-canonical options, so params share ONE wire format with");
        e.WriteLine("// scenario save/load and with the BTree bridge's own ParseParams.");
        e.WriteLine("private static readonly global::System.Text.Json.JsonSerializerOptions __ParamJsonOptions =");
        e.WriteLine("    global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;");
    }

    /// <summary>
    /// FC-2/LV-1 (Q#19-B, review F4) -- emits the PER-CLASS nested fixed-list wrapper structs for
    /// every list-typed field, deduped per (element, capacity): an `[InlineArray(N)]` buffer +
    /// a `{ int Count; Buffer Items; }` wrapper whose name matches the IrTypeRef's synthesized
    /// `__List_{Elem}_{N}` FullName (TypeRefToCSharp emits `_`-prefixed names bare, so the State
    /// field resolves to THIS nested type). Nested-per-class -- never a top-level shared type --
    /// because the generator emits per `.bp.json` and two blueprints sharing (Elem,N) would
    /// otherwise collide with CS0101 (review F4; the future shared-type migration is a cross-file
    /// Collect() pass that needs no asset changes -- assets never name the wrapper).
    /// </summary>
    internal static void EmitListWrappers(CSharpEmitter e, IReadOnlyList<IrField> fields)
    {
        var emitted = new HashSet<string>();
        foreach (var f in fields)
        {
            var t = f.Type;
            if (t.Capacity <= 0 || t.ElementType is null) continue;
            if (!emitted.Add(t.FullName)) continue;

            string elemCs  = StatementEmitter.TypeRefToCSharp(t.ElementType);
            string bufName = "__Buf" + t.FullName.Substring("__List".Length);
            e.WriteLine($"[global::System.Runtime.CompilerServices.InlineArray({t.Capacity})]");
            e.WriteLine($"public struct {bufName}");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"private {elemCs} _e0;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
            e.WriteLine($"public struct {t.FullName}");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine("public int Count;");
            e.WriteLine($"public {bufName} Items;");
            e.Outdent();
            e.WriteLine("}");
        }
    }

    private static void EmitVarIds(CSharpEmitter e, IrAsset asset)
    {
        e.WriteLine("public static class VarIds");
        e.WriteLine("{");
        e.Indent();
        // Batch 56 — the whole state tier, not just Variables: a name→id constant is exactly as true
        // for a WorkingState declaration on an Instance, and omitting it would be the same silent gap
        // one level up.
        foreach (var v in asset.StateDeclarations)
            e.WriteLine($"public const string {v.Name} = \"{v.Id}\";");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitInitDefault(CSharpEmitter e, IrAsset asset)
    {
        bool behaviour = IsBehavior(asset);
        e.WriteLine("public static void InitDefault(global::System.Span<byte> stateBytes)");
        e.WriteLine("{");
        e.Indent();
        // ⭐ S2 — a behaviour bakes its BLOCK (the brain state is zeroed by its own slot's attach/reset).
        e.WriteLine($"ref var {(behaviour ? "__bb" : "s")} = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, {(behaviour ? "Block" : "State")}>(");
        e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(stateBytes));");
        e.WriteLine(behaviour ? "__bb = default;" : "s = default;");
        // ⭐⭐ Batch 56 — the SILENT half of the defect lives here. An unreferenced wrong-side declaration
        // produced no Roslyn error at all: it simply had no field and no initialiser, so an authored
        // initial value was carried through the JSON, through Stage 5, and then dropped.
        // ⭐ BP-247 — the skip test is VALUE-based, not text-based. It used to be `!= "0"`, and 45
        //   shipped `float` fields carry the JSON default `0`, which now renders as `0F`; a text test
        //   would have started emitting 45 assignments writing a zero over a zero.
        foreach (var v in asset.StateDeclarations.Where(f =>
            !Lowering.DefaultLiteral.IsSkippable(f.DefaultValueCSharp)))
        {
            e.WriteLine($"{VarsContainer(asset)}.{v.Name} = {v.DefaultValueCSharp};");
        }
        // FC-2/LV-1 (Q#19-B): declared initial length seeds Count over the already-zeroed slots
        // (preallocation is free for blittable elements -- default(T) is all-zero bytes). This is
        // the PARTIAL init the whole-field DefaultValueCSharp path cannot express (review F2).
        foreach (var v in asset.StateDeclarations.Where(f => f.Type.Capacity > 0 && f.Type.InitialLength > 0))
        {
            e.WriteLine($"{VarsContainer(asset)}.{v.Name}.Count = {v.Type.InitialLength};");
        }
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitEventMethod(CSharpEmitter e, IrAsset asset, IrGraph evtGraph)
    {
        // Extra parameters come from graph Inputs (event payload fields).
        var extraParams = evtGraph.Inputs.Select(f => $"{CSharpType(f.Type)} {f.Name}");
        var extraParamStr = evtGraph.Inputs.Count > 0 ? ", " + string.Join(", ", extraParams) : "";

        e.WriteLine($"public static void Event_{evtGraph.Name}(");
        e.Indent();
        e.WriteLine(StateParamDecl(asset));
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        // ⭐ S6a — a behaviour's Event graph may be a fiber that waits: it gets the Tick's frame context (deltaTime, the run's
        //   instanceVersion for its cursor, the occurrence for a child it hosts). One shape for every behaviour Event method.
        e.WriteLine(IsBehavior(asset)
            ? $"float time, float deltaTime, uint instanceVersion, int occurrenceKey{extraParamStr})"
            : $"float time{extraParamStr})");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        LibraryEmitter.EmitGraphBody(e, asset, evtGraph);
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickMethod(CSharpEmitter e, IrAsset asset)
    {
        // ⭐ CE-446: a behaviour's Tick reports whether it finished (BehaviorDispatch).
        bool behaviour = asset.Dispatch == Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Behavior;
        // Q-18.1: includes uint instanceVersion as last parameter
        e.WriteLine(behaviour ? "public static global::Fbt.NodeStatus Tick(" : "public static void Tick(");
        e.Indent();
        e.WriteLine(StateParamDecl(asset));
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        // ⭐ S5d — a behaviour's Tick knows the occurrence it runs as, for the children it hosts (Run Behaviour).
        e.WriteLine(behaviour ? "uint instanceVersion, int occurrenceKey)" : "uint instanceVersion)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();

        var tickGraph = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function && g.Name == "Tick")
            ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);

        if (tickGraph != null)
        {
            LibraryEmitter.EmitGraphBody(e, asset, tickGraph);
        }
        else if (behaviour)
        {
            e.WriteLine(EmissionContext.ReturnRunning);   // no Tick graph: never finishes on its own
        }

        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-446</c> (<c>Q77</c> §5.6), reshaped by S2 (<c>DESIGN_Unified_Behaviour_Run</c> U-1) — the entry points a
    /// blueprint BEHAVIOUR registers with.
    /// <list type="bullet">
    /// <item><c>BehaviorParseParams</c> — bakes the BLOCK (<c>InitDefault</c>: Variable defaults), parses the JSON onto
    /// <c>In</c> (the Input prefix at 0), then runs the behaviour's own resolver over the block. ⭐ The resolver gets
    /// <c>ref Block</c> only: the brain state is out of reach by construction.</item>
    /// <item><c>BehaviorTick</c> — this frame's events, dispatched inline to the Event graphs (no handler table: two
    /// state refs cannot ride the shared <c>Span&lt;byte&gt;</c> delegate, and a per-frame dictionary walk is an
    /// enumerator allocation), then the Tick, whose status ends the behaviour.</item>
    /// </list>
    /// </summary>
    /// <summary>⭐ S5d — every Run Behaviour site in the asset (one per node), from the lowered IR.</summary>
    internal static IReadOnlyList<IrOp_RunBehavior> RunBehaviorSites(IrAsset asset)
        => asset.Graphs.SelectMany(g => g.Blocks).SelectMany(b => b.Statements)
               .Select(st => st.Operation).OfType<IrOp_RunBehavior>()
               .GroupBy(op => op.SiteId).Select(grp => grp.First()).OrderBy(op => op.SiteId).ToList();

    /// <summary>⭐ S5d — the generated field holding a Run Behaviour site's hosted-slot key.</summary>
    internal static string RunSiteField(IrOp_RunBehavior op) => $"__RunSite_{op.SiteId:N}";

    private static void EmitBehaviorEntryPoints(CSharpEmitter e, IrAsset asset)
    {
        // ⭐ S5d — each Run Behaviour site's hosted-slot key, computed ONCE (the hash is internal to the runtime): the same
        //   function a BTree host's key comes from (host asset, site node, child asset).
        foreach (var site in RunBehaviorSites(asset))
            e.WriteLine($"public static readonly int {RunSiteField(site)} = global::Fdp.Toolkit.Behavior.OccurrenceSlots.TreeStateKeyFor("
                + $"new global::System.Guid(\"{asset.AssetId}\"), new global::System.Guid(\"{site.SiteId}\"), "
                + $"global::Fdp.Toolkit.Behavior.BTreeHostedSites.AssetIdFromName(\"{site.BehaviorName}\"));");

        // ⭐ S3 (DESIGN_Unified_Behaviour_Run) — the Input manifest, exactly what a BTree/HSM registrar publishes: one
        //   entry per Parameter at its offset in the block (In sits at 0). The offsets are MEASURED on the real struct
        //   (managed layout), never baked — right under either layout regime.
        e.WriteLine("/// <summary>S3: the behaviour's Input manifest (its Parameters at their offsets in the block).</summary>");
        e.WriteLine("public static global::Fdp.Toolkit.Behavior.ManagedBlackboardVariable[] InputManifest()");
        e.WriteLine("{");
        e.Indent();
        if (asset.Parameters.Count == 0)
            e.WriteLine("return global::System.Array.Empty<global::Fdp.Toolkit.Behavior.ManagedBlackboardVariable>();");
        else
        {
            e.WriteLine("var __p = default(Params);");
            e.WriteLine("ref byte __o = ref global::System.Runtime.CompilerServices.Unsafe.As<Params, byte>(ref __p);");
            e.WriteLine("return new global::Fdp.Toolkit.Behavior.ManagedBlackboardVariable[]");
            e.WriteLine("{");
            e.Indent();
            foreach (var f in asset.Parameters)
            {
                string t = CSharpType(f.Type);
                e.WriteLine($"new(\"{f.Name}\", typeof({t}), (int)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref __o, "
                          + $"ref global::System.Runtime.CompilerServices.Unsafe.As<{t}, byte>(ref __p.{f.Name}))),");
            }
            e.Outdent();
            e.WriteLine("};");
        }
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();

        var events = asset.Graphs.Where(g => g.Kind == IrGraphKind.Event).ToList();
        var resolver = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Construction);
        if (resolver is not null)
        {
            e.WriteLine($"/// <summary>CE-446: the behaviour's own resolver — reads its Parameters, writes its Variables. "
                      + "S2: it sees the block only.</summary>");
            e.WriteLine($"private static void Resolve_{Sanitizer.SanitizeName(resolver.Name)}(ref Block __bb, "
                      + "global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self)");
            e.WriteLine("{");
            e.Indent();
            // ⚠ A resolver is latent- and When-free (BP1675), but a helper it calls is typed over (b, x): it gets a
            //   SCRATCH brain state, never the behaviour's real one.
            e.WriteLine("var __scratchExec = default(Exec); ref var __ex = ref __scratchExec;");
            LibraryEmitter.EmitGraphBody(e, asset, resolver);
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();
        }

        foreach (var g in events)
            e.WriteLine($"private static readonly int __EvtId_{g.Name} = "
                      + $"global::Fdp.Toolkit.Blueprints.BlueprintEventDispatch.ResolveTypeId(\"{g.EventTypeFqn ?? g.Name}\");");
        if (events.Count > 0) e.WriteLine();

        e.WriteLine("/// <summary>CE-446 / S2: bake the block, parse In, run the resolver over the block.</summary>");
        e.WriteLine("public static unsafe void BehaviorParseParams(string json, byte* memory, int capacity,");
        e.WriteLine("    global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("if (capacity < StateSize)");
        e.WriteLine("    throw new global::System.ArgumentOutOfRangeException(nameof(capacity),");
        e.WriteLine("        $\"the behaviour block holds {capacity} bytes but this blueprint needs {StateSize} (CE-446).\");");
        e.WriteLine("InitDefault(new global::System.Span<byte>(memory, StateSize));");
        if (asset.Parameters.Count > 0)
            e.WriteLine("ParseParams(json, memory + ParamsOffset, capacity - ParamsOffset, world, self);");
        if (resolver is not null)
            e.WriteLine($"Resolve_{Sanitizer.SanitizeName(resolver.Name)}("
                      + "ref global::System.Runtime.CompilerServices.Unsafe.AsRef<Block>(memory), world, self);");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();

        e.WriteLine("/// <summary>CE-446 / S2: this frame's events, then the Tick — its status ends the behaviour.</summary>");
        e.WriteLine("public static unsafe global::Fbt.NodeStatus BehaviorTick(ref byte block, ref byte exec,");
        e.WriteLine("    global::Fdp.Core.EntityRepository world, global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("    global::Fdp.Core.Entity self, float time, float deltaTime, uint instanceId, int occurrenceKey)");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ref var __bb = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, Block>(ref block);");
        e.WriteLine("ref var __ex = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, Exec>(ref exec);");
        // ⭐ S6a — resume every waiting Event fiber FIRST, with the inputs it started on: a fiber started below this frame
        //   is then not stepped twice in one frame, and one that finishes here is free for an event arriving this frame.
        foreach (var g in events.Where(Lowering.Fibers.IsOwnFiber))
        {
            var saved = string.Concat(g.Inputs.Select(f => $", __ex.{Lowering.Fibers.InputSlot(g, f)}"));
            e.WriteLine($"if (__ex.{Lowering.Fibers.CursorOf(g)}.ResumeAt != 0)");
            e.WriteLine($"    Event_{g.Name}(ref __bb, ref __ex, world, ecb, self, time, deltaTime, instanceId, occurrenceKey{saved});");
        }
        foreach (var g in events)
        {
            e.WriteLine($"if (world.Bus.HasEvent(__EvtId_{g.Name}))");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"var __raw = world.Bus.ReadRawByTypeId(__EvtId_{g.Name}, out int __size);");
            e.WriteLine("if (__size > 0)");
            e.WriteLine("    for (int __i = 0; __i + __size <= __raw.Length; __i += __size)");
            e.WriteLine($"        Event_{g.Name}_Thunk(ref __bb, ref __ex, world, ecb, self, time, deltaTime, instanceId, occurrenceKey, __raw.Slice(__i, __size));");
            e.Outdent();
            e.WriteLine("}");
        }
        e.WriteLine("return Tick(ref __bb, ref __ex, world, ecb, self, time, deltaTime, instanceId, occurrenceKey);");
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitTickThunk(CSharpEmitter e)
    {
        // TickDelegate signature per Q-18.1: includes uint instanceVersion
        e.WriteLine("public static void TickThunk(");
        e.Indent();
        e.WriteLine("global::System.Span<byte> bytes,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        e.WriteLine("uint instanceVersion)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("ref var s = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, State>(");
        e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(bytes));");
        e.WriteLine("Tick(ref s, view, ecb, self, time, deltaTime, instanceVersion);");
        e.Outdent();
        e.WriteLine("}");
    }

    /// <summary>
    /// Emits a private static helper method for an in-blueprint Function graph (BATCH-03A).
    /// Mirrors LibraryEmitter.EmitFunctionGraph but prepends the 7 context parameters
    /// (ref State s, ISimulationView view, IEntityCommandBuffer ecb, Entity self,
    ///  float time, float deltaTime, uint instanceVersion) so that ops like
    /// IrOp_Self/IrOp_Time/IrOp_WriteVariable etc. resolve correctly inside the body.
    /// </summary>
    private static void EmitInstanceFunctionMethod(CSharpEmitter e, IrAsset asset, IrGraph graph)
    {
        // BP-73: N outputs come back as a ValueTuple carrier; 1 output is unchanged.
        // BP-221: shared with the call site so the two cannot disagree about whether this helper
        // produces a value. An Instance function graph carries no NodeStatus terminator, so the
        // shared predicate yields exactly the old `hasStatusReturn: false` answer here.
        var retType = LibraryEmitter.HelperReturnType(graph);

        var sanitized = Sanitizer.SanitizeName(graph.Name);

        // Build the extra input parameters after the 7 context params.
        var extraParams = graph.Inputs.Count > 0
            ? ", " + string.Join(", ", graph.Inputs.Select(f => $"{CSharpType(f.Type)} {f.Name}"))
            : "";

        e.WriteLine($"private static {retType} Func_{sanitized}(");
        e.Indent();
        e.WriteLine(StateParamDecl(asset));
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        e.WriteLine($"uint instanceVersion{extraParams})");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        LibraryEmitter.EmitGraphBody(e, asset, graph);
        e.Outdent();
        e.WriteLine("}");
    }

    private static void EmitEventThunk(CSharpEmitter e, IrGraph evtGraph, bool behaviour = false)
    {
        // EventHandlerDelegate signature: (Span<byte>, ISimView, IECB, Entity, float, float, ReadOnlySpan<byte>)
        // ⭐ S2 — a behaviour's thunk takes its two state refs instead (it is called inline by BehaviorTick).
        e.WriteLine($"public static void Event_{evtGraph.Name}_Thunk(");
        e.Indent();
        e.WriteLine(behaviour ? "ref Block __bb, ref Exec __ex," : "global::System.Span<byte> bytes,");
        e.WriteLine("global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine("global::Fdp.Interfaces.IEntityCommandBuffer ecb,");
        e.WriteLine("global::Fdp.Core.Entity self,");
        e.WriteLine("float time,");
        e.WriteLine("float deltaTime,");
        if (behaviour) e.WriteLine("uint instanceVersion, int occurrenceKey,");
        e.WriteLine("global::System.ReadOnlySpan<byte> payload)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        if (!behaviour)
        {
            e.WriteLine("ref var s = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, State>(");
            e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(bytes));");
        }
        // Q#14: when the Event graph carries an event identity (EventTypeFqn) and has inputs, reinterpret the
        // dispatched payload span as that struct and pass each field to the handler. Otherwise fall back to
        // the legacy default stub (byte-identical for legacy Event graphs with no identity).
        // Q#14 (3d): the Self filter needs the reinterpreted payload even when the handler takes no inputs,
        // so reinterpret __ev whenever we have an event identity AND (payload fields OR a Self filter).
        bool hasFqn      = !string.IsNullOrEmpty(evtGraph.EventTypeFqn);
        bool selfFilter  = evtGraph.TargetFilterSelf && !string.IsNullOrEmpty(evtGraph.TargetFieldName);
        bool reinterpret = hasFqn && (evtGraph.Inputs.Count > 0 || selfFilter);

        string args;
        if (reinterpret)
        {
            e.WriteLine($"ref readonly var __ev = ref global::System.Runtime.CompilerServices.Unsafe.As<byte, global::{evtGraph.EventTypeFqn}>(");
            e.WriteLine("    ref global::System.Runtime.InteropServices.MemoryMarshal.GetReference(payload));");
            // Self/Any: skip this subscriber unless the event's target field names THIS entity.
            if (selfFilter)
                e.WriteLine($"if (__ev.{evtGraph.TargetFieldName} != self) return;");
            args = evtGraph.Inputs.Count > 0
                ? ", " + string.Join(", ", evtGraph.Inputs.Select(f => $"__ev.{f.Name}"))
                : "";
        }
        else
        {
            args = evtGraph.Inputs.Count > 0
                ? ", " + string.Join(", ", evtGraph.Inputs.Select(f => $"default({CSharpType(f.Type)})"))
                : "";
        }
        if (behaviour && Lowering.Fibers.IsOwnFiber(evtGraph))
        {
            // ⭐ S6a — Parallel(1) until S6b (U-6): an arrival while this fiber still waits is a FAULT, never a silent drop.
            //   Otherwise the inputs are saved in the fiber, so a resume next frame sees the event it started on.
            e.WriteLine($"if (__ex.{Lowering.Fibers.CursorOf(evtGraph)}.ResumeAt != 0)");
            e.WriteLine("{");
            e.WriteLine("    global::Fdp.Toolkit.Behavior.Events.BehaviorFault.Raise(view, self, "
                      + "global::Fdp.Toolkit.Behavior.Events.BehaviorFaultCode.EventOverflow,");
            e.WriteLine($"        \"event '{evtGraph.Name}' arrived while its handler was still waiting (one handler at a time).\");");
            e.WriteLine("    return;");
            e.WriteLine("}");
            if (evtGraph.Inputs.Count > 0)
            {
                foreach (var f in evtGraph.Inputs)
                    e.WriteLine($"__ex.{Lowering.Fibers.InputSlot(evtGraph, f)} = "
                              + (reinterpret ? $"__ev.{f.Name};" : $"default({CSharpType(f.Type)});"));
                args = string.Concat(evtGraph.Inputs.Select(f => $", __ex.{Lowering.Fibers.InputSlot(evtGraph, f)}"));
            }
        }
        e.WriteLine(behaviour
            ? $"Event_{evtGraph.Name}(ref __bb, ref __ex, view, ecb, self, time, deltaTime, instanceVersion, occurrenceKey{args});"
            : $"Event_{evtGraph.Name}(ref s, view, ecb, self, time{args});");
        e.Outdent();
        e.WriteLine("}");
    }

    private static string CSharpType(IrTypeRef t) => StatementEmitter.TypeRefToCSharp(t);

    /// <summary>
    /// ⭐ Every statement of every graph — INCLUDING those nested in a loop body (<see cref="IrOp_ForEach.Body"/>) or a branch
    /// (<see cref="IrOp_If.Then"/> / <see cref="IrOp_If.Else"/>). 🔴 The helper collectors used to walk only the top-level
    /// block statements, so a <c>Read EQS Result</c> inside a <c>For Each</c> body got a call site and NO helper — CS0103 in
    /// the real generator build (found by <c>PlatoonHillAttackBp</c>'s EQS migration, <c>DESIGN_Hill_Attack_Eqs_Migration.md</c> §6).
    /// </summary>
    private static IEnumerable<IrStatement> AllStatements(IrAsset asset)
    {
        foreach (var graph in asset.Graphs)
        foreach (var block in graph.Blocks)
        foreach (var stmt in Descend(block.Statements))
            yield return stmt;

        static IEnumerable<IrStatement> Descend(IReadOnlyList<IrStatement> statements)
        {
            foreach (var stmt in statements)
            {
                yield return stmt;
                var nested = stmt.Operation switch
                {
                    IrOp_ForEach fe => Descend(fe.Body),
                    IrOp_If br      => Descend(br.Then).Concat(Descend(br.Else)),
                    _               => Enumerable.Empty<IrStatement>(),
                };
                foreach (var inner in nested) yield return inner;
            }
        }
    }

    /// <summary>
    /// Collects all unique IrOp_WhenConditionMetCheck operations across all graphs.
    /// Returns list of (id8, predicateJson) pairs, deduplicated by SynthFieldName.
    /// </summary>
    private static List<(string Id8, string PredicateDtoJson)> CollectConditionMetOps(IrAsset asset)
    {
        var result = new List<(string, string)>();
        var seen   = new HashSet<string>();

        foreach (var stmt in AllStatements(asset))
        {
            if (stmt.Operation is not IrOp_WhenConditionMetCheck op) continue;
            if (!seen.Add(op.SynthFieldName)) continue;

            // Extract the 8-char hex id from "_when_{id8}_prev"
            const string prefix = "_when_";
            const string suffix = "_prev";
            string id8 = op.SynthFieldName.StartsWith(prefix) && op.SynthFieldName.EndsWith(suffix)
                ? op.SynthFieldName.Substring(prefix.Length,
                    op.SynthFieldName.Length - prefix.Length - suffix.Length)
                : op.SynthFieldName;

            result.Add((id8, op.PredicateDtoJson));
        }

        return result;
    }

    private static void EmitConditionMetFields(
        CSharpEmitter e,
        List<(string Id8, string PredicateDtoJson)> ops)
    {
        foreach (var (id8, _) in ops)
        {
            e.WriteLine($"private static global::Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? _whenCondDto_{id8};");
            e.WriteLine($"private static global::System.Func<global::Fdp.Core.EntityRepository, global::Fdp.Core.Entity, bool>? _whenCondPred_{id8};");
        }
    }

    private static void EmitInitializePredicates(
        CSharpEmitter e,
        List<(string Id8, string PredicateDtoJson)> ops)
    {
        e.WriteLine("public static void InitializePredicates(");
        e.WriteLine("    global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler predicateCompiler,");
        e.WriteLine("    global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry dtoRegistry)");
        e.WriteLine("{");
        e.Indent();

        foreach (var (id8, predicateJson) in ops)
        {
            // Escape the JSON for embedding in a C# string literal.
            string escaped = predicateJson
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");

            e.WriteLine($"// WhenNode ConditionMet {id8}:");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"const string dtoJson_{id8} = \"{escaped}\";");
            e.WriteLine("try");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"_whenCondDto_{id8} = global::System.Text.Json.JsonSerializer.Deserialize<");
            e.WriteLine($"    global::Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto>(dtoJson_{id8});");
            e.WriteLine($"_whenCondPred_{id8} = predicateCompiler.CompileComponentPredicate(_whenCondDto_{id8}!);");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine("catch (global::System.Exception)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"_whenCondPred_{id8} = null;");
            e.Outdent();
            e.WriteLine("}");
            e.Outdent();
            e.WriteLine("}");
        }

        e.Outdent();
        e.WriteLine("}");
    }

    private static List<IrOp_WhenEqsResultCheck> CollectEqsResultOps(IrAsset asset)
    {
        var result = new List<IrOp_WhenEqsResultCheck>();
        var seen   = new HashSet<string>();
        foreach (var stmt in AllStatements(asset))
        {
            if (stmt.Operation is not IrOp_WhenEqsResultCheck op) continue;
            if (!seen.Add(op.SynthFieldName)) continue;
            result.Add(op);
        }
        return result;
    }

    private static void EmitEqsResultPrevStateStructs(CSharpEmitter e, List<IrOp_WhenEqsResultCheck> ops)
    {
        foreach (var op in ops)
        {
            e.WriteLine($"[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
            e.WriteLine($"public struct {op.SynthStructTypeName}");
            e.WriteLine("{");
            e.Indent();
            switch (op.Trigger)
            {
                case "TopChanged":
                    e.WriteLine("public uint  LastEvaluatedEpoch;");
                    e.WriteLine("public long  PrevTopId;");
                    e.WriteLine("public float PrevTopScore;");
                    break;
                case "FirstReady":
                    e.WriteLine("public uint LastEvaluatedEpoch;");
                    break;
                case "ScoreCrossed":
                    e.WriteLine("public uint  LastEvaluatedEpoch;");
                    e.WriteLine("public float PrevTopScore;");
                    break;
                case "BecomesStale":
                    e.WriteLine("public float PrevStaleCheckTime;");
                    break;
            }
            e.Outdent();
            e.WriteLine("}");
        }
    }

    private static void EmitEqsConstFields(CSharpEmitter e, List<IrOp_WhenEqsResultCheck> ops)
    {
        foreach (var op in ops)
        {
            if (op.ScoreThresholdLiteral is not null)
            {
                var id8 = ExtractId8FromSynthFieldName(op.SynthFieldName);
                e.WriteLine($"private const float _whenScoreThreshold_{id8} = {op.ScoreThresholdLiteral};");
            }
            if (op.MaxAgeLiteral is not null)
            {
                var id8 = ExtractId8FromSynthFieldName(op.SynthFieldName);
                e.WriteLine($"private const float _whenMaxAge_{id8} = {op.MaxAgeLiteral};");
            }
        }
    }

    private static string ExtractId8FromSynthFieldName(string synthFieldName)
    {
        // "_when_<id8>_prev" -> "<id8>"
        const string prefix = "_when_";
        const string suffix = "_prev";
        if (synthFieldName.StartsWith(prefix) && synthFieldName.EndsWith(suffix))
            return synthFieldName.Substring(prefix.Length,
                synthFieldName.Length - prefix.Length - suffix.Length);
        return synthFieldName;
    }

    private static List<IrOp_ReadEqsResult> CollectReadEqsResultOps(IrAsset asset)
    {
        var result = new List<IrOp_ReadEqsResult>();
        var seen   = new HashSet<string>();
        foreach (var stmt in AllStatements(asset))
        {
            if (stmt.Operation is not IrOp_ReadEqsResult op) continue;
            if (!seen.Add(op.NodeId8)) continue;
            result.Add(op);
        }
        return result;
    }

    private static void EmitReadEqsResultHelpers(CSharpEmitter e, IrAsset asset, List<IrOp_ReadEqsResult> ops)
    {
        foreach (var op in ops)
        {
            // Emit the result struct
            e.WriteLine($"[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
            e.WriteLine($"private struct {op.ResultStructTypeName}");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine("public bool  IsReady;");
            e.WriteLine("public int   ResultCount;");
            e.WriteLine("public global::Fdp.Core.Entity Entity;");
            e.WriteLine("public global::System.Numerics.Vector2 Position;");
            e.WriteLine("public float Score;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();

            // Emit the helper method
            e.WriteLine($"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            e.WriteLine($"private static {op.ResultStructTypeName} ReadEqsResult_{op.NodeId8}(");
            e.Indent();
            e.WriteLine(StateParamDecl(asset));
            e.WriteLine($"global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
            e.WriteLine($"int resultIndex)");
            e.Outdent();
            e.WriteLine("{");
            e.Indent();

            e.WriteLine($"var result = default({op.ResultStructTypeName});");
            e.WriteLine();
            e.WriteLine($"ref readonly var handle = ref {VarsContainer(asset)}.{op.SensorVariableName};");
            e.WriteLine($"if (!view.IsAlive(handle.ChildId))");
            e.Indent();
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine();
            e.WriteLine($"if (!view.HasComponent<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId))");
            e.Indent();
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine();
            e.WriteLine($"ref readonly var buffer = ref view.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
            e.WriteLine($"if (!buffer.IsReady)");
            e.Indent();
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine();
            e.WriteLine("var results = buffer.GetSpanRO();");
            e.WriteLine("result.IsReady = true;");
            e.WriteLine("result.ResultCount = buffer.Count;");
            e.WriteLine();
            e.WriteLine("if (buffer.Count == 0)");
            e.Indent();
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine();
            e.WriteLine("int idx = global::System.Math.Clamp(resultIndex, 0, buffer.Count - 1);");
            e.WriteLine("var picked = results[idx];");
            e.WriteLine("result.Entity   = new global::Fdp.Core.Entity((ulong)picked.EntityId);");
            e.WriteLine("result.Position = new global::System.Numerics.Vector2(picked.PositionX, picked.PositionY);");
            e.WriteLine("result.Score    = picked.Score;");
            e.WriteLine("return result;");

            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();
        }
    }

    private static List<IrOp_ScoreDecision> CollectScoreDecisionOps(IrAsset asset)
    {
        var result = new List<IrOp_ScoreDecision>();
        var seen   = new HashSet<string>();
        foreach (var stmt in AllStatements(asset))
        {
            if (stmt.Operation is not IrOp_ScoreDecision op) continue;
            if (!seen.Add(op.NodeId8)) continue;
            result.Add(op);
        }
        return result;
    }

    private static List<IrOp_ReadRankedResult> CollectReadRankedResultOps(IrAsset asset)
    {
        var result = new List<IrOp_ReadRankedResult>();
        var seen   = new HashSet<string>();
        foreach (var stmt in AllStatements(asset))
        {
            if (stmt.Operation is not IrOp_ReadRankedResult op) continue;
            if (!seen.Add(op.NodeId8)) continue;
            result.Add(op);
        }
        return result;
    }

    private static void EmitScoreDecisionHelpers(CSharpEmitter e, List<IrOp_ScoreDecision> ops)
    {
        foreach (var op in ops)
        {
            e.WriteLine($"[global::System.Runtime.CompilerServices.MethodImpl(" +
                        $"global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            e.WriteLine($"private static byte ScoreDecision_{op.NodeId8}(");
            e.Indent();
            e.WriteLine($"global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
            e.WriteLine($"global::Fdp.Core.Entity self,");
            e.WriteLine($"float time)");
            e.Outdent();
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"uint tick = (uint)(time * 60f);");
            e.WriteLine($"return global::Fdp.Toolkit.Utility.Integration.UtilityBlueprintBridge" +
                        $".ScoreDecision(view, self, {op.DecisionIdLiteral}, tick);");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();
        }
    }

    private static void EmitReadRankedResultHelpers(CSharpEmitter e, List<IrOp_ReadRankedResult> ops)
    {
        foreach (var op in ops)
        {
            // Emit the result struct
            e.WriteLine($"[global::System.Runtime.InteropServices.StructLayout(" +
                        $"global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
            e.WriteLine($"private struct {op.ResultStructTypeName}");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine("public bool  IsValid;");
            e.WriteLine("public long  Entity;");
            e.WriteLine("public float Score;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();

            // Emit the helper method
            e.WriteLine($"[global::System.Runtime.CompilerServices.MethodImpl(" +
                        $"global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            e.WriteLine($"private static {op.ResultStructTypeName} ReadRankedResult_{op.NodeId8}(");
            e.Indent();
            e.WriteLine($"global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
            e.WriteLine($"global::Fdp.Core.Entity self)");
            e.Outdent();
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"var result = default({op.ResultStructTypeName});");
            e.WriteLine($"var (handle, score, isValid) = " +
                        $"global::Fdp.Toolkit.Utility.Integration.UtilityBlueprintBridge" +
                        $".ReadRankedResult(view, self, {op.RankLiteral});");
            e.WriteLine("result.IsValid = isValid;");
            e.WriteLine("result.Entity  = handle;");
            e.WriteLine("result.Score   = score;");
            e.WriteLine("return result;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine();
        }
    }
}
