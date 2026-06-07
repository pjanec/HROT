using System.Linq;
using Fdp.Toolkit.Behavior.Demo;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.ActionCatalog;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared.Blackboard;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// DEMO-ACTIONS batch — headless verification that the demo <c>[SharedAiAction]</c>
/// <see cref="DemoSharedActions.AlertNearbyUnits"/> is:
/// <list type="bullet">
///   <item>Discovered by the real <see cref="ActionSchemaExporter"/> (reflection over loaded assemblies).</item>
///   <item>Surfaced by <see cref="BehaviorActionCatalog"/> as a
///     <see cref="BehaviorActionHosts.Blueprint"/>-valid non-channel action.</item>
///   <item>Registered in the palette registry as <c>"Action:{FQN}"</c> by
///     <see cref="BlueprintEditorBootstrap.CreatePaletteRegistry"/>.</item>
///   <item>Projected by <see cref="NodePinSchema"/> with the expected data-IN pins
///     (AlertRadius, PostureHint, MaxUnits), where the enum field PostureHint carries
///     a TypeId prefixed with <c>"global::"</c> per AN6.</item>
/// </list>
/// <para>
/// None of these tests compile a blueprint asset — the action will emit <c>#error</c> in the
/// MSBuild generator until AN8b implements the non-channel AiPrimitive lowering path.
/// </para>
/// </summary>
public sealed class AN8b_DemoSharedActionTests
{
    // ── Expected identity ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FQN the ActionSchemaExporter builds for the demo method:
    /// "{DeclaringType.FullName}.{MethodName}".
    /// </summary>
    private static readonly string DemoFqn =
        $"{typeof(DemoSharedActions).FullName}.{nameof(DemoSharedActions.AlertNearbyUnits)}";

    // ── 1. ActionSchemaExporter discovers the demo method ────────────────────────────────────

    [Fact]
    public void ActionSchemaExporter_Rebuild_FindsDemoSharedAction_AN8b()
    {
        // Arrange: real exporter; Fdp.Toolkits is loaded (transitively via Hrot.Blueprints.Editor).
        var exporter = new ActionSchemaExporter();

        // Act
        exporter.Rebuild();

        // Assert: the demo method appears in All
        Assert.True(exporter.All.ContainsKey(DemoFqn),
            $"Expected '{DemoFqn}' in ActionSchemaExporter.All after Rebuild.");
    }

    [Fact]
    public void ActionSchemaExporter_DemoEntry_HasSharedHosting_AN8b()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[DemoFqn];

        // SharedAiAction sets BTree | Hsm | Shared flags.
        Assert.True(entry.Hosting.HasFlag(ActionHosting.BTree),  "Expected BTree flag.");
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Hsm),    "Expected Hsm flag.");
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Shared), "Expected Shared flag.");
    }

    [Fact]
    public void ActionSchemaExporter_DemoEntry_DtoTypeIsDemoSharedActionParams_AN8b()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[DemoFqn];

        // The first ref param of AlertNearbyUnits is ref DemoSharedActionParams,
        // so ExtractFirstRefParamType returns typeof(DemoSharedActionParams).
        Assert.Equal(typeof(DemoSharedActionParams), entry.DtoType);
    }

    // ── 2. BehaviorActionCatalog surfaces it as Blueprint-valid ──────────────────────────────

    [Fact]
    public void BehaviorActionCatalog_WithRealExporter_ContainsDemoAction_AsBlueprint_AN8b()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        using var catalog = new BehaviorActionCatalog(new FakeChannelCommandCatalog(), exporter);

        var blueprintActions = catalog.GetActions(BehaviorActionHosts.Blueprint);

        Assert.Contains(blueprintActions, e =>
            e.Source != BehaviorActionSource.ChannelCommand
            && e.Id == DemoFqn);
    }

    [Fact]
    public void BehaviorActionCatalog_DemoEntry_ParamsTypeFqn_IsDemoSharedActionParams_AN8b()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        using var catalog = new BehaviorActionCatalog(new FakeChannelCommandCatalog(), exporter);

        var entry = catalog.GetActions(BehaviorActionHosts.Blueprint)
            .FirstOrDefault(e =>
                e.Source != BehaviorActionSource.ChannelCommand
                && e.Id == DemoFqn);

        Assert.NotNull(entry);
        Assert.Equal(typeof(DemoSharedActionParams).FullName, entry!.ParamsTypeFqn);
    }

    // ── 3. Palette registry contains the "Action:{FQN}" kind ─────────────────────────────────

    [Fact]
    public void PaletteRegistry_WithRealCatalog_ContainsDemoActionKind_AN8b()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        using var catalog = new BehaviorActionCatalog(new FakeChannelCommandCatalog(), exporter);

        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry(
            channelCatalog:        null,
            behaviorActionCatalog: catalog);

        var expectedKind = $"Action:{DemoFqn}";
        var descriptor = registry.TryGet(expectedKind);

        Assert.NotNull(descriptor);
    }

    [Fact]
    public void PaletteRegistry_DemoDescriptor_CreateInstance_BakesActionFqn_AN8b()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        using var catalog = new BehaviorActionCatalog(new FakeChannelCommandCatalog(), exporter);

        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry(
            channelCatalog:        null,
            behaviorActionCatalog: catalog);

        var expectedKind = $"Action:{DemoFqn}";
        var descriptor   = registry.TryGet(expectedKind)!;
        var node         = descriptor.CreateInstance() as ChannelCommandNode;

        Assert.NotNull(node);
        Assert.Equal(DemoFqn, node!.ActionFqn);
        // Non-channel path: ChannelType and ActionId stay empty.
        Assert.True(string.IsNullOrEmpty(node.ChannelType));
        Assert.True(string.IsNullOrEmpty(node.ActionId));
    }

    // ── 4. NodePinSchema projects DemoSharedActionParams fields as data-IN pins ──────────────

    [Fact]
    public void NodePinSchema_DemoSharedNode_ProjectsThreeDataInPins_AN8b()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        using var catalog = new BehaviorActionCatalog(new FakeChannelCommandCatalog(), exporter);

        var node = new ChannelCommandNode
        {
            Id        = System.Guid.NewGuid(),
            ActionFqn = DemoFqn,
        };

        var pins = NodePinSchema.GetCanonicalPins(
            node,
            behaviorActions: catalog);

        var dataIn = pins
            .Where(p => !p.IsExec && p.Direction == "In")
            .ToList();

        // DemoSharedActionParams has three fields: AlertRadius, PostureHint, MaxUnits.
        Assert.Equal(3, dataIn.Count);
        Assert.Contains(dataIn, p => p.Name == nameof(DemoSharedActionParams.AlertRadius));
        Assert.Contains(dataIn, p => p.Name == nameof(DemoSharedActionParams.PostureHint));
        Assert.Contains(dataIn, p => p.Name == nameof(DemoSharedActionParams.MaxUnits));
    }

    [Fact]
    public void NodePinSchema_DemoSharedNode_PostureHintPin_HasGlobalColonColonTypeId_AN8b()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        using var catalog = new BehaviorActionCatalog(new FakeChannelCommandCatalog(), exporter);

        var node = new ChannelCommandNode
        {
            Id        = System.Guid.NewGuid(),
            ActionFqn = DemoFqn,
        };

        var pins = NodePinSchema.GetCanonicalPins(
            node,
            behaviorActions: catalog);

        var posturePin = pins
            .FirstOrDefault(p =>
                !p.IsExec
                && p.Direction == "In"
                && p.Name == nameof(DemoSharedActionParams.PostureHint));

        Assert.NotNull(posturePin);

        // AN6: enum fields are stamped "global::" + FullName by ReflectDataMembers.
        var expectedTypeId = "global::" + typeof(DemoStance).FullName;
        Assert.Equal(expectedTypeId, posturePin!.TypeRef?.TypeId);
    }

    [Fact]
    public void NodePinSchema_DemoSharedNode_HasExecInAndExecOut_AN8b()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        using var catalog = new BehaviorActionCatalog(new FakeChannelCommandCatalog(), exporter);

        var node = new ChannelCommandNode
        {
            Id        = System.Guid.NewGuid(),
            ActionFqn = DemoFqn,
        };

        var pins = NodePinSchema.GetCanonicalPins(
            node,
            behaviorActions: catalog);

        Assert.Contains(pins, p => p.IsExec && p.Direction == "In");
        Assert.Contains(pins, p => p.IsExec && p.Direction == "Out");
    }
}
