using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>U-3 / <c>BP-226</c> — an index that does not say WHICH LIST is a type error the compiler is not
/// making.</b>
///
/// <para>
/// ⛔ <b>The defect, in two halves that were written by different people and never compared.</b>
/// <c>Stage5.FindVariableIndex</c> searches <c>Variables</c>, then <c>WorkingState</c>, then
/// <c>Parameters</c> and returns <c>i</c> — an index <b>within whichever list matched</b>, with the
/// list itself thrown away. <c>EmissionContext.VarFieldName</c> then reads that bare <c>int</c> as a
/// <b>priority-ordered union</b>: <c>Variables</c> first, then <c>WorkingState</c>, and
/// ⛔ <b>no <c>Parameters</c> arm at all</b>.
/// </para>
///
/// <para>
/// ⇒ The two agree only while <b>at most one list is populated</b> — which is exactly what
/// <c>BP1024</c>/<c>BP1031</c> enforce for every shipped asset, and why the corpus never caught it.
/// ⭐ <b>That is also the good news:</b> no shipped asset depends on the broken behaviour, so the fix
/// cannot break one — the golden corpus is unchanged by this task.
/// </para>
///
/// <para>
/// ⚠ <b>These assets are built in memory and driven Stage 3 → 7, bypassing Stage 2 deliberately.</b>
/// Stage 2 is what refuses a mixed asset (<c>BP1024</c>/<c>BP1031</c>), and the point here is the
/// index space beneath it. Shipped tests already drive the pipeline this way.
/// </para>
/// </summary>
public sealed class VariableKindResolutionTests
{
    private static CompileOptions Options() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static Pin ExecIn()  => new() { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true };
    private static Pin ExecOut() => new() { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true };

    private static Pin DataIn(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = "In", IsExec = false,
        TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" },
    };

    private static Pin DataOut(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = "Out", IsExec = false,
        TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" },
    };

    /// <summary>
    /// Emits <c>Entry → SetVariable(<paramref name="writeTarget"/>) ← GetVariable(<paramref name="readTarget"/>)</c>.
    /// ⚠ The read needs a consumer or Stage 3 eliminates it as an orphan, which is why the write is here.
    /// </summary>
    private static string EmitReadWrite(BlueprintAsset asset, Guid readTarget, Guid writeTarget)
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecOut(); entry.Pins.Add(entryOut);

        var get = new GetVariableNode { Id = Guid.NewGuid(), VariableId = readTarget.ToString() };
        var getOut = DataOut("Value"); get.Pins.Add(getOut);

        var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = writeTarget.ToString() };
        var setIn = ExecIn(); var setOut = ExecOut(); var setVal = DataIn("Value");
        set.Pins.AddRange(new[] { setIn, setOut, setVal });

        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecIn(); ret.Pins.Add(retIn);

        // ⚠ The shipped shape: a Function graph named "Tick" (Instance) or "Main" (AiPrimitive).
        // An Event graph here is eliminated whole and TickCore emits an EMPTY body — which is how the
        // first draft of these tests "passed" before the fix: every assertion was satisfied by the
        // STRUCT DECLARATIONS, with no reference site emitted at all.
        asset.Graphs.Add(new Graph
        {
            Id = Guid.NewGuid(),
            Name = asset.Dispatch == Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.AiPrimitive ? "Main" : "Tick",
            Kind = GraphKind.Function,
            Nodes = { entry, get, set, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = set.Id, ToPinId = setIn.Id },
                new Link { FromNodeId = get.Id,   FromPinId = getOut.Id,   ToNodeId = set.Id, ToPinId = setVal.Id },
                new Link { FromNodeId = set.Id,   FromPinId = setOut.Id,   ToNodeId = ret.Id, ToPinId = retIn.Id },
            },
        });

        var opts = Options();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        // ⚠ Stage 2 skipped on purpose — see the class comment.
        var norm  = Stage3_Normalize.Run(asset, ctx);
        var typed = Stage4_TypeResolve.Run(norm, ctx);
        var ir    = Stage5_Schedule.Run(typed, ctx);
        var low   = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(low, CompilerMode.Debug, sink);
        return src;
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Pass 2 — a <c>WorkingState</c> reference must emit the WorkingState field.</b>
    ///
    /// <para>
    /// ⛔ <b>Before U-3 this emitted <c>V1</c>.</b> <c>FindVariableIndex</c> returns <c>1</c> — the
    /// position of <c>W1</c> <i>within WorkingState</i> — and <c>VarFieldName</c> sees only the
    /// integer, finds <c>Variables.Count > 1</c>, and hands back <c>Variables[1]</c>. ⚠ Not a
    /// near-miss: it is a <b>different struct at a different offset</b>.
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b>Batch 56 sharpened the assertions, and the reason matters.</b> They were file-wide
    /// <c>DoesNotContain("V1")</c> — a proxy that held only because <c>AiPrimitiveEmitter</c> dropped
    /// <c>Variables</c> entirely, so the name could not appear anywhere. ⭐ Ruling 8 makes both kinds
    /// real fields of ONE struct, so <c>V1</c> is now legitimately DECLARED. ⛔ The claim was never
    /// "the name is absent from the file" — it is <b>"the reference resolves to <c>W1</c>"</b> — so it
    /// is now stated on the reference site itself, which is strictly the tighter assertion.
    /// </para>
    /// </summary>
    [Fact]
    public void AWorkingStateReference_EmitsTheWorkingStateField_NotAVariable()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("KindMixed")
            .WithVariable("V0", typeof(int))
            .WithVariable("V1", typeof(int))
            .WithVariable("V2", typeof(int))
            .WithWorkingStateField("W0", typeof(int))
            .WithWorkingStateField("W1", typeof(int))
            .Build();

        var w1 = asset.WorkingState.Single(f => f.Name == "W1");
        var w0 = asset.WorkingState.Single(f => f.Name == "W0");

        var src = EmitReadWrite(asset, readTarget: w1.Id, writeTarget: w0.Id);

        Assert.Contains("= ws.W1;",  src);   // ⭐ the READ resolves to the field actually referenced
        Assert.Contains("ws.W0 = ",  src);   // ⭐ and the WRITE to its target
        Assert.DoesNotContain("ws.V1", src); // ⛔ the shadowing Variables entry is never touched
        Assert.DoesNotContain("ws.V0", src);
    }

    /// <summary>
    /// ⭐⭐ <b>Pass 3 — a <c>Parameters</c> reference must emit the parameter.</b>
    ///
    /// <para>
    /// ⛔ <b>Before U-3, <c>VarFieldName</c> had no <c>Parameters</c> arm at all</b>, so with the other
    /// two lists empty a parameter reference fell through to <c>__var_{index}</c> — ⚠ <b>not valid C#
    /// and with no BP diagnostic</b>: the blueprint "compiled" and the solution build broke, naming a
    /// generated file instead of the node. The same shape as <c>BP1670</c>'s <c>__var_-1</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void AParameterReference_EmitsTheParameter_NotAVarSentinel()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("KindParams")
            .WithParameter("P0", typeof(int))
            .WithParameter("P1", typeof(int))
            .WithWorkingStateField("W0", typeof(int))
            .Build();

        var p1 = asset.Parameters.Single(p => p.Name == "P1");
        var w0 = asset.WorkingState.Single(f => f.Name == "W0");

        var src = EmitReadWrite(asset, readTarget: p1.Id, writeTarget: w0.Id);

        Assert.DoesNotContain("__var_", src);   // ⛔ the invalid-C# sentinel
        Assert.Contains("P1", src);
    }

    /// <summary>
    /// ⭐ <b>And a plain <c>Variables</c> reference still resolves</b> — the case every shipped asset
    /// exercises. ⚠ Without this, "fix the other two" could be satisfied by breaking the one that works.
    /// </summary>
    [Fact]
    public void AVariablesReference_StillResolves()
    {
        var asset = BlueprintAssetBuilder
            .Instance("KindVariables")
            .WithVariable("Alpha", typeof(int))
            .WithVariable("Beta", typeof(int))
            .Build();

        var alpha = asset.Variables.Single(v => v.Name == "Alpha");
        var beta  = asset.Variables.Single(v => v.Name == "Beta");

        var src = EmitReadWrite(asset, readTarget: alpha.Id, writeTarget: beta.Id);

        Assert.DoesNotContain("__var_", src);
        Assert.Contains("Alpha", src);
        Assert.Contains("Beta", src);
    }
}

