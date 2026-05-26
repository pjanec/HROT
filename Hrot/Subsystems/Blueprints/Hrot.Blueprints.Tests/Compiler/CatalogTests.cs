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

    // ---- ANC-P4-03: Seven Brain-visible animation entries ------------------

    [Fact]
    public void BuiltInEngineEventCatalog_HasSevenBrainVisibleAnimationEntries()
    {
        var entries = BuiltInEngineEventCatalog.Instance.GetEntries();
        var brainAnimEntries = entries
            .Where(e => e.Category.StartsWith("Animation/") && e.PropagatesAcrossNodes)
            .ToList();

        Assert.Equal(7, brainAnimEntries.Count);

        // Verify the seven expected events are present
        var brainNames = brainAnimEntries.Select(e => e.Name).ToHashSet();
        Assert.Contains("MontageStartedEvent", brainNames);
        Assert.Contains("MontageEndedEvent", brainNames);
        Assert.Contains("MontageSectionAdvancedEvent", brainNames);
        Assert.Contains("StanceChangedEvent", brainNames);
        Assert.Contains("HitWindowOpenedEvent", brainNames);
        Assert.Contains("HitWindowClosedEvent", brainNames);
        Assert.Contains("AnimNotifyEvent", brainNames);
    }

    [Fact]
    public void BuiltInEngineEventCatalog_FootstepEvent_IsExcludedBrainSide()
    {
        var entries = BuiltInEngineEventCatalog.Instance.GetEntries();

        // FootstepEvent must be in the catalog (so BP2017 can fire), but with
        // PropagatesAcrossNodes=false, which marks it as Muscle-local.
        var footstep = entries.Single(e => e.Name == "FootstepEvent");
        Assert.False(footstep.PropagatesAcrossNodes);

        // Brain-visible entries must NOT include FootstepEvent.
        var brainVisible = entries.Where(e => e.PropagatesAcrossNodes).Select(e => e.Name);
        Assert.DoesNotContain("FootstepEvent", brainVisible);
    }

    [Fact]
    public void BuiltInEngineEventCatalog_AnimationEntries_HaveCorrectCategory()
    {
        var entries = BuiltInEngineEventCatalog.Instance.GetEntries();

        var lifecycle = new[] { "MontageStartedEvent", "MontageEndedEvent",
            "MontageSectionAdvancedEvent", "StanceChangedEvent" };
        var notify = new[] { "FootstepEvent", "HitWindowOpenedEvent",
            "HitWindowClosedEvent", "AnimNotifyEvent" };

        foreach (var name in lifecycle)
        {
            var entry = entries.Single(e => e.Name == name);
            Assert.Equal("Animation/Lifecycle", entry.Category);
        }

        foreach (var name in notify)
        {
            var entry = entries.Single(e => e.Name == name);
            Assert.Equal("Animation/Notify", entry.Category);
        }
    }

    [Fact]
    public void BuiltInEngineEventCatalog_AnimationEntries_HaveTargetFieldName()
    {
        var entries = BuiltInEngineEventCatalog.Instance.GetEntries();
        var animEntries = entries.Where(e => e.Category.StartsWith("Animation/"));

        foreach (var entry in animEntries)
            Assert.Equal("Target", entry.TargetFieldName);
    }

    [Fact]
    public void BuiltInEngineEventCatalog_AllAnimationEntries_AreReliable()
    {
        var entries = BuiltInEngineEventCatalog.Instance.GetEntries();
        var animEntries = entries.Where(e => e.Category.StartsWith("Animation/"));

        foreach (var entry in animEntries)
            Assert.Equal(EventQoS.Reliable, entry.QoS);
    }

    [Fact]
    public void BuiltInEngineEventCatalog_AnimationEntries_HaveFilterableFields()
    {
        var entries = BuiltInEngineEventCatalog.Instance.GetEntries();

        // MontageEndedEvent must have EndReason in filterable fields (DD-3 §4.1)
        var ended = entries.Single(e => e.Name == "MontageEndedEvent");
        Assert.NotNull(ended.FilterableFields);
        Assert.Contains("EndReason", ended.FilterableFields!);

        // AnimNotifyEvent must have MarkerHash in filterable fields
        var notify = entries.Single(e => e.Name == "AnimNotifyEvent");
        Assert.NotNull(notify.FilterableFields);
        Assert.Contains("MarkerHash", notify.FilterableFields!);
    }

    [Fact]
    public void BuiltInEngineEventCatalog_AnimationEntries_HaveCorrectFqns()
    {
        const string animNs = "Hrot.MuscleCharacter.Animation.Events";
        var entries = BuiltInEngineEventCatalog.Instance.GetEntries();

        var animEntries = entries.Where(e => e.Category.StartsWith("Animation/"));
        foreach (var entry in animEntries)
            Assert.StartsWith(animNs, entry.EventTypeFqn);
    }
}
