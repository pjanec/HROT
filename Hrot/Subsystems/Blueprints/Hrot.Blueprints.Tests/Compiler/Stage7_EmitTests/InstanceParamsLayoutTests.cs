using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b>The Instance params LAYOUT — <c>DESIGN_Parameter_Model.md</c> §3.3.</b>
///
/// <para>
/// 🔴🔴 <b>What was wrong.</b> <c>FieldLayout</c> laid parameters at <c>startOffset: 0</c> for BOTH
/// dispatch kinds, and an Instance payload <b>opens with the 16-byte <c>BlueprintLatentCursor</c></b>.
/// 📐 Safe on <c>HEAD</c> only because <b>296 shipped Instance assets carry ZERO parameters</b> — the
/// first one that did would have resolved its params straight over the latent scheduler's cursor.
/// ⚠ And a second, quieter half: <c>IrOp_ReadParam</c> emitted a hardcoded <c>p.</c>, an identifier no
/// Instance declares ⇒ <b>CS0103 in generated code</b>.
/// </para>
///
/// <para>
/// ⭐ <b>The AiPrimitive side must not move</b>, and that is asserted here rather than assumed: its
/// params keep base <b>0</b> and its state keeps base <b>8</b>, both of which are hashed into
/// <c>StructureHash</c> for 32 shipped assets.
/// </para>
/// </summary>
public sealed class InstanceParamsLayoutTests
{
    private static CompileOptions DefaultOptions() =>
        new(Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static CompileResult Compile(BlueprintAsset asset)
        => new BlueprintCompiler().Compile(asset, DefaultOptions());

    private static ParameterDecl Param(string name, string typeId)
        => new() { Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = typeId } };

    private static VariableDecl Var(string name, string typeId)
        => new() { Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = typeId } };

    /// <summary>An Instance carrying two parameters and one state variable.</summary>
    private static BlueprintAsset InstanceWithParams()
    {
        var asset = BlueprintAssetBuilder
            .Instance("ParamCarrier")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        asset.Parameters = new List<ParameterDecl> { Param("Speed", "System.Single"), Param("Count", "System.Int32") };
        asset.Variables  = new List<VariableDecl>  { Var("Health", "System.Single") };
        return asset;
    }

    private static string CompileSource(BlueprintAsset asset)
    {
        var result = Compile(asset);
        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code + ": " + d.Message))}");
        return result.GeneratedSource!;
    }

    // ── the layout ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The params region begins at 16, not 0</b> — the <c>startOffset: 0</c> trap, caught in
    /// the emitted declaration itself rather than only at runtime.
    /// </summary>
    [Fact]
    public void AnInstancesParams_AreDeclaredAfterTheCursor_NotAtZero()
    {
        var src = CompileSource(InstanceWithParams());

        Assert.Contains("public const int ParamsOffset = 16;", src);
        Assert.Contains("public Params Params;", src);
        // ⛔ The cursor still owns offset 0.
        Assert.Contains("public global::Fdp.Toolkit.Blueprints.BlueprintLatentCursor Cursor;", src);
    }

    /// <summary>
    /// ⭐⭐ <b>The state fields shift by N.</b> With 8 bytes of params, <c>Health</c> lands at 24 rather
    /// than 16 — and the descriptor the debugger reads must say so, or it reads plausible bytes from
    /// the wrong place.
    /// </summary>
    [Fact]
    public void AnInstancesState_StartsAfterTheParamsRegion()
    {
        var src = CompileSource(InstanceWithParams());

        // float Speed @16 + int Count @20 ⇒ params occupy [16,24) ⇒ state opens at 24.
        Assert.Contains("BlueprintFieldDescriptor(\"Health\", typeof(float), 24, 4", src);
    }

    /// <summary>
    /// ⭐ <b>Without parameters, nothing moves</b> — no <c>Params</c> field, and the state still opens
    /// at 16. 📐 This is 296 of 296 shipped Instance assets, so it is the case whose byte-identity the
    /// unchanged <c>StructureHash</c> rests on.
    /// </summary>
    [Fact]
    public void AnInstanceWithNoParams_KeepsItsStateAtSixteen()
    {
        var asset = BlueprintAssetBuilder
            .Instance("NoParams")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        asset.Variables = new List<VariableDecl> { Var("Health", "System.Single") };

        var src = CompileSource(asset);

        Assert.Contains("BlueprintFieldDescriptor(\"Health\", typeof(float), 16, 4", src);
        Assert.DoesNotContain("public Params Params;", src);
        Assert.Contains("public static int ParamsSize => 0;", src);
    }

    /// <summary>
    /// ⭐⭐ <b>The AiPrimitive side is UNCHANGED</b>: params at 0 in their own struct, state at 8.
    /// ⛔ Both numbers are hashed into <c>StructureHash</c> for 32 shipped assets, so this is the rail
    /// that says the Instance fix did not reach across.
    /// </summary>
    [Fact]
    public void AnAiPrimitivesParamsAndState_KeepTheirOwnBases()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("PrimitiveWithParams")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Parameters  = new List<ParameterDecl> { Param("Speed", "System.Single") };
        asset.WorkingState = new List<VariableDecl> { Var("Ticks", "System.Int32") };

        var src = CompileSource(asset);

        Assert.Contains("public struct Params", src);
        // ⭐ The descriptor is STRUCT-relative and the AiPrimitive rebase (-8) still applies ⇒ 0.
        Assert.Contains("BlueprintFieldDescriptor(\"Ticks\", typeof(int), 0, 4", src);
        Assert.DoesNotContain("public const int ParamsOffset", src);
    }

    // ── the emitted supply path ─────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>An Instance emits a <c>ParseParams</c> with the SAME delegate shape a behaviour uses.</b>
    /// ⛔ Not a second delegate type (ruling 9) — the runtime assigns this straight onto
    /// <c>BlueprintDefinition.ParseParams</c>, whose type is
    /// <c>Fdp.Toolkit.Behavior.ParseParamsDelegate</c>.
    /// </summary>
    [Fact]
    public void AnInstanceWithParams_EmitsAParseParamsAndWiresItOntoTheDefinition()
    {
        var src = CompileSource(InstanceWithParams());

        Assert.Contains("public static unsafe void ParseParams(", src);
        Assert.Contains("global::Fdp.Toolkit.Behavior.IHostVariableAccess? host)", src);
        Assert.Contains("ParseParams = ParamCarrier_", src);
        Assert.Contains("ParamsOffset = ParamCarrier_", src);
        Assert.Contains("ParamsSize = ParamCarrier_", src);
    }

    /// <summary>
    /// ⭐ <b>Every parameter gets its own overlay arm</b>, and an unknown key falls through to
    /// <c>default: break;</c> rather than throwing — <c>DEBT-AIB-021</c>'s decided behaviour, reused
    /// here rather than redecided.
    /// </summary>
    [Fact]
    public void TheEmittedParseParams_OverlaysEveryParameterAndIgnoresUnknownKeys()
    {
        var src = CompileSource(InstanceWithParams());

        Assert.Contains("case \"Speed\":", src);
        Assert.Contains("case \"Count\":", src);
        Assert.Contains("default: break;", src);
        // ⭐ Bake first, overlay second -- the ORDER is the ruling.
        Assert.True(src.IndexOf("p = default;", StringComparison.Ordinal)
                    < src.IndexOf("__doc.RootElement.EnumerateObject()", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⭐⭐ <b>A blueprint with no parameters emits NO <c>ParseParams</c> at all</b> — nothing to parse,
    /// so nothing to call. ⚠ Stated as a decision, because the alternative (an always-emitted empty
    /// method) would have changed the generated text of all 296 shipped Instance assets.
    /// </summary>
    [Fact]
    public void AnInstanceWithNoParams_EmitsNoParseParams()
    {
        var asset = BlueprintAssetBuilder
            .Instance("NoParamsEither")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var src = CompileSource(asset);

        Assert.DoesNotContain("public static unsafe void ParseParams(", src);
        Assert.DoesNotContain("ParseParams = ", src);
    }
}
