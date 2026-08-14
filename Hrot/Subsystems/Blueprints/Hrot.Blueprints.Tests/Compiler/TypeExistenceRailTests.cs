using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>U-7 / <c>BP-228</c> — a declared field type that does not exist compiled clean.</b>
///
/// <para>
/// ⛔ <b>The defect:</b> <c>Stage4_TypeResolve.TryResolveFieldType</c>'s AN2 fallback accepts any
/// <b>dotted</b> type id verbatim — <i>"looks like a project FQN"</i> — because the reflection-less
/// compiler cannot verify it. ⇒ <c>Totally.Made.Up.Type</c> yields <b>Succeeded = true, zero
/// diagnostics</b>, emitting <c>public global::Totally.Made.Up.Type Threat;</c> and a field
/// descriptor over a type that does not exist. ⭐ <b>The dot was doing the work of a type check</b>,
/// and only Roslyn caught it — as a <c>CS0246</c> naming a generated file, not the variable.
/// </para>
///
/// <para>
/// ⚠ <b>The fallback contract is as load-bearing as the rail.</b> Almost every caller passes
/// <c>ClrSignatureResolver: null</c> — measured: <b>exactly one production site supplies one</b>,
/// <c>BlueprintIncrementalGenerator</c>. A rail that fired without an oracle would redden the suite
/// for the wrong reason and break every in-memory <c>.Succeeded</c> check.
/// </para>
/// </summary>
public sealed class TypeExistenceRailTests
{
    /// <summary>An oracle that knows exactly one type — the shape the plan specifies.</summary>
    private sealed class OnlyKnowsOne : IClrSignatureResolver
    {
        private readonly string _known;
        public OnlyKnowsOne(string known) => _known = known;

        public bool TryResolve(string targetTypeId, string methodName, out ClrMethodSig? sig)
        {
            sig = null;
            return false;
        }

        public bool TypeExists(string typeId)
            => string.Equals(Strip(typeId), _known, StringComparison.Ordinal);

        private static string Strip(string id)
            => id.StartsWith("global::", StringComparison.Ordinal) ? id.Substring("global::".Length) : id;
    }

    private static CompileOptions Options(IClrSignatureResolver? oracle) => new(
        Mode:                 CompilerMode.Release,
        NodeRegistry:         BuiltInNodeRegistry.Instance,
        TypeRegistry:         StaticTypeRegistry.Instance,
        EngineEvents:         BuiltInEngineEventCatalog.Instance,
        ChannelCommands:      BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:       BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures:    Array.Empty<BlueprintSignature>(),
        ClrSignatureResolver: oracle);

    /// <summary>An Instance asset with one struct-typed variable and a trivial body.</summary>
    private static BlueprintAsset AssetTypedAs(string typeId, string variableName = "Threat")
    {
        var asset = BlueprintAssetBuilder.Instance("TypeRailHost").Build();
        asset.Variables.Add(new VariableDecl
        {
            Id = Guid.NewGuid(), Name = variableName,
            Type = new BlueprintTypeRef { TypeId = typeId }, DefaultValueJson = "",
        });

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true };
        entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true };
        ret.Pins.Add(retIn);

        asset.Graphs.Add(new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, ret },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = ret.Id, ToPinId = retIn.Id } },
        });
        return asset;
    }

    private const string Known = "Hrot.AI.Behaviors.StructDemoData";

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Pass 1 — a fabricated type is REFUSED, and the diagnostic names the variable AND the
    /// type.</b> ⛔ Compiled clean before this rail.
    /// </summary>
    [Fact]
    public void WithAnOracle_AFabricatedTypeIsRefused_NamingTheVariableAndTheType()
    {
        var result = new BlueprintCompiler().Compile(
            AssetTypedAs("Totally.Made.Up.Type"), Options(new OnlyKnowsOne(Known)));

        Assert.False(result.Succeeded);
        var d = Assert.Single(result.Diagnostics.Where(x => x.IsError));
        Assert.Contains("Threat", d.Message);                 // the variable
        Assert.Contains("Totally.Made.Up.Type", d.Message);   // and the type
    }

    /// <summary>⭐ A type the oracle DOES know still compiles — the rail refuses absence, not novelty.</summary>
    [Fact]
    public void WithAnOracle_AKnownProjectTypeStillCompiles()
    {
        var result = new BlueprintCompiler().Compile(
            AssetTypedAs(Known), Options(new OnlyKnowsOne(Known)));

        Assert.True(result.Succeeded,
            string.Join(",", result.Diagnostics.Where(x => x.IsError).Select(x => x.Code + ":" + x.Message)));
    }

    /// <summary>
    /// ⭐⭐ <b>Pass 2 — the fallback contract. With NO oracle the same asset compiles exactly as
    /// before.</b>
    ///
    /// <para>
    /// ⚠ <b>This is not a nicety.</b> Every in-memory caller — unit tests, editor <c>.Succeeded</c>
    /// checks, the golden harness — passes <c>null</c>. A rail that fired without an oracle would
    /// redden the suite for a reason that has nothing to do with the asset.
    /// </para>
    /// </summary>
    [Fact]
    public void WithNoOracle_TheFabricatedTypeStillCompiles_UnchangedBehaviour()
    {
        var result = new BlueprintCompiler().Compile(
            AssetTypedAs("Totally.Made.Up.Type"), Options(null));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(result.Diagnostics, d => d.IsError);
    }

    /// <summary>
    /// ⚠ <b>A PRIMITIVE is never asked about.</b> The registry resolves it outright, so the oracle is
    /// consulted only on the AN2 "trust the dotted string" path. ⛔ Asking about primitives would make
    /// the rail depend on an oracle knowing <c>System.Int32</c>, which the Roslyn one does but a
    /// narrow test double need not.
    /// </summary>
    [Fact]
    public void APrimitiveIsNeverPutToTheOracle()
    {
        var result = new BlueprintCompiler().Compile(
            AssetTypedAs("System.Int32"), Options(new OnlyKnowsOne(Known)));

        Assert.True(result.Succeeded,
            string.Join(",", result.Diagnostics.Where(x => x.IsError).Select(x => x.Code)));
    }

    /// <summary>
    /// ⭐ The rail covers <b>every</b> declaration list, not just <c>Variables</c> — a fabricated type
    /// on a graph LOCAL (BP-57) is refused the same way. ⚠ Locals share the type-resolution pass but
    /// nothing else, so it is worth asserting rather than assuming.
    /// </summary>
    [Fact]
    public void ALocalVariablesFabricatedTypeIsAlsoRefused()
    {
        var asset = AssetTypedAs(Known);
        asset.Graphs[0].LocalVariables.Add(new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "Scratch",
            Type = new BlueprintTypeRef { TypeId = "Also.Made.Up" }, DefaultValueJson = "",
        });

        var result = new BlueprintCompiler().Compile(asset, Options(new OnlyKnowsOne(Known)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Message.Contains("Scratch"));
    }
}
