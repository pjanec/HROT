using System;
using System.Collections.Generic;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Ir;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler.Stage6_LoweringTests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-388</c> / <c>Q74 D-A2</c> — the compiler derives which actuator channels a
/// blueprint drives, with NO authoring.</b>
/// 📄 <c>Architect_Question_74_Blueprint_Channel_Lifecycle.md</c> §4 <c>D-A</c>.
///
/// <para>🔒 <b>The user's constraint:</b> <i>"the most common use case is a default so the user does
/// not need to author unless he needs something extra."</i> ⇒ these rails pin that the common case
/// needs no declaration, and that the cases the compiler CANNOT see are reported rather than
/// silently treated as "writes nothing".</para>
///
/// <para>⚠ <b>The user's own objection is what shaped this</b> — <i>"it can call macros and functons
/// and even hardcoded c#"</i>. Each arm below is one of those.</para>
/// </summary>
public sealed class BlueprintChannelDerivationTests
{
    private const string Loco   = "Fdp.Toolkit.Behavior.Components.LocomotionChannel";
    private const string Weapon = "Fdp.Toolkit.Behavior.Components.WeaponChannel";

    // ── fixture helpers ──────────────────────────────────────────────────────

    private static IrGraph Graph(string name, IrGraphKind kind, params IrOperation[] ops)
        => new()
        {
            Id     = Guid.NewGuid(),
            Name   = name,
            Kind   = kind,
            Blocks = new[]
            {
                new IrBlock
                {
                    Id         = new IrBlockId(0),
                    Label      = "entry",
                    Statements = Array.ConvertAll(ops, o => new IrStatement { Operation = o }),
                    Terminator = new IrTerm_FallThrough(),
                },
            },
        };

    private static IrAsset Asset(params IrGraph[] graphs)
        => new() { AssetId = Guid.NewGuid(), Name = "ChannelDemo", Graphs = graphs };

    private static IrOp_ChannelCommand Command(string channelFqn)
        => new(channelFqn, "MoveTo", "Fdp.Toolkit.Navigation.MoveToParams",
               Array.Empty<(string, IrValue)>());

    // ── ① the common case: a direct channel-command node, nothing authored ───

    /// <summary>
    /// ⭐⭐⭐ <b>THE HEADLINE — a graph that issues channel commands needs NO declaration.</b>
    /// Every entry in <c>BuiltInChannelCommandCatalog</c> already names its channel type, so the
    /// compiler already holds the fact the author would otherwise have to repeat.
    /// </summary>
    [Fact]
    public void CE388_R1_DirectChannelCommands_AreDerivedWithNoAuthoring()
    {
        var result = BlueprintChannelDerivation.Derive(
            Asset(Graph("Tick", IrGraphKind.AiPrimitiveMain, Command(Loco), Command(Weapon))));

        Assert.True(result.IsComplete);
        Assert.Equal(new[] { Loco, Weapon }, result.ChannelComponentFqns);
    }

    /// <summary>⛔ The no-churn half: a blueprint that commands nothing derives an EMPTY set and is
    /// still COMPLETE — so nothing binds a cleanup and no existing asset moves.</summary>
    [Fact]
    public void CE388_R2_ABlueprintThatCommandsNothing_IsEmptyAndComplete()
    {
        var result = BlueprintChannelDerivation.Derive(
            Asset(Graph("Tick", IrGraphKind.AiPrimitiveMain)));

        Assert.True(result.IsComplete);
        Assert.Empty(result.ChannelComponentFqns);
    }

    // ── ② the user's three escape hatches ────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>A FUNCTION graph in the same asset is followed.</b> 🔒 The user: <i>"it can call …
    /// functons …"</i>. Its IR is right here, so this is exact, not an approximation.
    /// </summary>
    [Fact]
    public void CE388_R3_AChannelCommandInsideACalledFunctionGraph_IsFollowed()
    {
        var fn   = Graph("Helper", IrGraphKind.Function, Command(Loco));
        var tick = Graph("Tick", IrGraphKind.AiPrimitiveMain,
                         new IrOp_GraphCall(fn.Id, Array.Empty<IrValue>(), null!));

        var result = BlueprintChannelDerivation.Derive(Asset(tick, fn));

        Assert.True(result.IsComplete);
        Assert.Equal(new[] { Loco }, result.ChannelComponentFqns);
    }

    /// <summary>
    /// ⛔⛔ <b>REACHABILITY, not "every graph in the asset".</b> An UNCALLED Function graph must NOT
    /// contribute — binding a cleanup for a channel this blueprint never drives is exactly the
    /// over-clean that can wipe a channel a PARALLEL REGION is currently driving.
    /// </summary>
    [Fact]
    public void CE388_R4_AnUncalledFunctionGraph_DoesNotContribute()
    {
        var orphan = Graph("NeverCalled", IrGraphKind.Function, Command(Weapon));
        var tick   = Graph("Tick", IrGraphKind.AiPrimitiveMain, Command(Loco));

        var result = BlueprintChannelDerivation.Derive(Asset(tick, orphan));

        Assert.True(result.IsComplete);
        Assert.Equal(new[] { Loco }, result.ChannelComponentFqns);
    }

    /// <summary>
    /// 🔴🔴 <b>HARDCODED C# MAKES THE RESULT INCOMPLETE — it does not make it EMPTY.</b>
    /// 🔒 The user: <i>"… and even hardcoded c#"</i>. 📐 The compiler holds a string FQN and nothing
    /// else, and the netstandard2.0 generator host cannot load the assemblies it is compiling.
    ///
    /// <para>⛔ The caller must report this, never silently bind no cleanup. ⚠ And the tempting
    /// alternative — "assume it writes all three" — is worse, not safer: see <c>CE388_R4</c>.</para>
    /// </summary>
    [Fact]
    public void CE388_R5_ACallIntoHardcodedCSharp_MakesTheSetINCOMPLETE_NotEmpty()
    {
        var result = BlueprintChannelDerivation.Derive(
            Asset(Graph("Tick", IrGraphKind.AiPrimitiveMain,
                        Command(Loco),
                        new IrOp_InlineActionCall("Some.Nodes.Action_DoesWhoKnowsWhat",
                                                  "Some.Params",
                                                  Array.Empty<(string, IrValue)>(),
                                                  IsAiPrimitive: false))));

        // the compiler cannot see inside a C# method body
        Assert.False(result.IsComplete);
        Assert.Contains("Some.Nodes.Action_DoesWhoKnowsWhat", result.OpaqueCalls);

        // ⭐ and what it COULD see is still reported — the diagnostic names the gap, it does not
        //   discard the derivation.
        Assert.Equal(new[] { Loco }, result.ChannelComponentFqns);
    }

    /// <summary>
    /// ⚠ <b>A call into another ASSET is opaque too</b> — its IR is not given to this pass.
    /// ⭐ Named separately from the C# case so the diagnostic can say which kind it hit.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossAssetCalls))]
    public void CE388_R6_ACallIntoAnotherAsset_MakesTheSetIncomplete(IrOperation call, string expected)
    {
        var result = BlueprintChannelDerivation.Derive(
            Asset(Graph("Tick", IrGraphKind.AiPrimitiveMain, call)));

        Assert.False(result.IsComplete);
        Assert.Contains(expected, result.OpaqueCalls);
    }

    public static IEnumerable<object[]> CrossAssetCalls() => new[]
    {
        new object[] { new IrOp_LibraryCall(7, "Helper", Array.Empty<IrValue>(), null!), "library 7.Helper" },
        new object[] { new IrOp_AiPrimitiveCall(9, Array.Empty<IrValue>(), null!),       "aiprimitive 9"    },
        new object[] { new IrOp_PeerCall(11, "Ping", Array.Empty<IrValue>(), null!),     "peer 11.Ping"     },
    };

    // ── ③ macros: the one escape hatch that is a NON-ISSUE, pinned so nobody re-derives it ──

    /// <summary>
    /// ⭐⭐ <b>A MACRO needs no handling at all, and this rail is why.</b>
    /// 🔒 The user asked about macros; 📐 <c>Stage2_5_ExpandMacros</c> is a compile-time fixpoint that
    /// runs BEFORE IR and removes the <c>MacroCallNode</c> (<c>MacroExpander.cs:16</c>,
    /// <c>BlueprintCompiler.cs:100</c>) ⇒ by the time this pass runs, a macro's channel commands are
    /// ORDINARY INLINED OPS in the host graph and are derived like any other.
    ///
    /// <para>⛔ <c>IrGraphKind</c> has no <c>Macro</c> member — that absence IS the mechanism, and
    /// this rail records it so a later reader does not add speculative macro handling.</para>
    /// </summary>
    [Fact]
    public void CE388_R7_MacrosNeedNoHandling_BecauseTheyAreGoneBeforeIr()
    {
        // macros are expanded away by Stage2_5 before IR exists — if this ever changes,
        // BlueprintChannelDerivation must learn about them.
        Assert.DoesNotContain("Macro", Enum.GetNames(typeof(IrGraphKind)));

        // The post-expansion shape: the macro's command is simply inline in the host graph.
        var result = BlueprintChannelDerivation.Derive(
            Asset(Graph("Tick", IrGraphKind.AiPrimitiveMain, Command(Weapon))));

        Assert.True(result.IsComplete);
        Assert.Equal(new[] { Weapon }, result.ChannelComponentFqns);
    }
}
