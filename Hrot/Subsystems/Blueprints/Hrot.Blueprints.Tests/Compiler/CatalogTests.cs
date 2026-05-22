using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class CatalogTests
{
    // ---- T5a: BuiltInEngineEventCatalog has expected entries -----------------

    [Fact]
    public void BuiltInEngineEventCatalog_HasExpectedEntries()
    {
        var entries = BuiltInEngineEventCatalog.Instance.GetEntries();
        Assert.True(entries.Count >= 2);
        Assert.Contains(entries, e => e.Name == "HitEvent");
        Assert.Contains(entries, e => e.Name == "BehaviorFinishedEvent");
    }

    // ---- T5b: BuiltInChannelCommandCatalog has loco and weapon entries -------

    [Fact]
    public void BuiltInChannelCommandCatalog_HasLocoAndWeaponEntries()
    {
        var entries = BuiltInChannelCommandCatalog.Instance.GetEntries();
        Assert.Contains(entries, e => e.Name == "MoveTo");
        Assert.Contains(entries, e => e.Name == "AimAndFire");
    }

    // ---- T5c: BuiltInWaitPrimitiveCatalog has channel and event entries ------

    [Fact]
    public void BuiltInWaitPrimitiveCatalog_HasChannelAndEventEntries()
    {
        var entries = BuiltInWaitPrimitiveCatalog.Instance.GetEntries();
        Assert.Contains(entries, e => e.Name == "WaitForChannel:Locomotion");
        Assert.Contains(entries, e => e.Name == "WaitForEvent:BehaviorFinishedEvent");
    }

    // ---- T5d: Stage2 validates channel command when catalog is populated -----

    [Fact]
    [CoversDiagnosticCode("BP1401")]
    public void Stage2_ValidatesChannelCommand_WhenCatalogIsPopulated()
    {
        // Build a graph with a ChannelCommandNode referencing an UNKNOWN command.
        // Stage 2 should reject it now that the catalog is non-empty.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("TestAsset")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().ChannelCommand("NonExistent", "UnknownAction").Return())
            .Build();

        var options = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var result = new BlueprintCompiler().Compile(asset, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.BP1401);
    }
}
