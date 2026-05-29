using System.Linq;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization;
using Hrot.Common;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;
using Hrot.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>Tests for <see cref="NodeBootstrapper.RegisterMigrationServices"/>
/// and <see cref="HrotMigrationBootstrap"/> role profiles (M-2 enforcement).</summary>
public class NodeBootstrapperMigrationTests
{
    // ── T01: SimHost (MuscleGround) profile ────────────────────────────────

    [Fact]
    public void RegisterMigrationServices_MuscleGroundRole_RegistersScenarioTkbRoadNetworkOrchestratorContext()
    {
        var sut = new NodeBootstrapper();
        var ms = sut.RegisterMigrationServices(NodeRole.MuscleGround);

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,           types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition,      types);
        Assert.Contains(FdpDocumentTypes.RoadNetwork,          types);
        Assert.Contains(HrotDocumentTypes.OrchestratorContext, types);
    }

    [Fact]
    public void RegisterMigrationServices_MuscleGroundRole_DoesNotRegisterBlueprintOrMapInteractionConfig()
    {
        var sut = new NodeBootstrapper();
        var ms = sut.RegisterMigrationServices(NodeRole.MuscleGround);

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.DoesNotContain(HrotDocumentTypes.Blueprint,           types);
        Assert.DoesNotContain(HrotDocumentTypes.MapInteractionConfig, types);
    }

    // ── T02: CGF (Brain) profile ────────────────────────────────────────────

    [Fact]
    public void RegisterMigrationServices_BrainRole_RegistersSameAsSimHost()
    {
        var sut = new NodeBootstrapper();
        var ms = sut.RegisterMigrationServices(NodeRole.Brain);

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,           types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition,      types);
        Assert.Contains(FdpDocumentTypes.RoadNetwork,          types);
        Assert.Contains(HrotDocumentTypes.OrchestratorContext, types);
        Assert.DoesNotContain(HrotDocumentTypes.Blueprint, types);
    }

    // ── T03: IG profile ─────────────────────────────────────────────────────

    [Fact]
    public void BuildIg_RegistersScenarioTkbOrchestratorContextMapInteractionConfig()
    {
        var ms = HrotMigrationBootstrap.BuildIg();

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,             types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition,        types);
        Assert.Contains(HrotDocumentTypes.OrchestratorContext,   types);
        Assert.Contains(HrotDocumentTypes.MapInteractionConfig,  types);
    }

    [Fact]
    public void BuildIg_DoesNotRegisterBlueprintOrRoadNetwork()
    {
        var ms = HrotMigrationBootstrap.BuildIg();

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.DoesNotContain(HrotDocumentTypes.Blueprint,  types);
        Assert.DoesNotContain(FdpDocumentTypes.RoadNetwork, types);
    }

    // ── T04: M-2 fail-loud -- IG pipeline rejects Blueprint docType ──────────

    [Fact]
    public void BuildIg_Pipeline_ThrowsMigrationException_ForBlueprintDocType()
    {
        var ms = HrotMigrationBootstrap.BuildIg();

        var dom = System.Text.Json.Nodes.JsonNode.Parse(
            @"{""$meta"":{""docType"":""Hrot.Blueprints"",""schemaVersion"":1},""data"":""""}")!
            .AsObject();

        var ex = Assert.Throws<MigrationException>(
            () => ms.Pipeline.MigrateToCurrent(dom));

        Assert.Contains("Hrot.Blueprints", ex.Message);
    }

    // ── T05: Editor profile ─────────────────────────────────────────────────

    [Fact]
    public void BuildEditor_RegistersAllCustomerFacingAndPassthroughFormats()
    {
        var ms = HrotMigrationBootstrap.BuildEditor();

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,      types);
        Assert.Contains(HrotDocumentTypes.Blueprint,     types);
        Assert.Contains(HrotDocumentTypes.BehaviorTree,  types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition, types);
        Assert.Contains(FdpDocumentTypes.RoadNetwork,     types);
        // All passthrough formats
        Assert.Contains(HrotDocumentTypes.OrchestratorContext,   types);
        Assert.Contains(HrotDocumentTypes.MapInteractionConfig,  types);
        Assert.Contains(HrotDocumentTypes.StructEdit,            types);
        Assert.Contains(HrotDocumentTypes.TestScript,            types);
        Assert.Contains(HrotDocumentTypes.NodeConfiguration,     types);
    }

    // ── T06: ClusterRunner CI profile ───────────────────────────────────────

    [Fact]
    public void BuildClusterRunnerCi_RegistersSimHostPlusTestScriptAndNodeConfig()
    {
        var ms = HrotMigrationBootstrap.BuildClusterRunnerCi();

        var types = ms.Registry.RegisteredDocTypes.ToList();
        Assert.Contains(HrotDocumentTypes.Scenario,           types);
        Assert.Contains(HrotDocumentTypes.TkbDefinition,      types);
        Assert.Contains(FdpDocumentTypes.RoadNetwork,          types);
        Assert.Contains(HrotDocumentTypes.OrchestratorContext, types);
        Assert.Contains(HrotDocumentTypes.TestScript,          types);
        Assert.Contains(HrotDocumentTypes.NodeConfiguration,   types);
        Assert.DoesNotContain(HrotDocumentTypes.Blueprint, types);
    }

    // ── T07: MigrationServices property set on NodeBootstrapper ────────────

    [Fact]
    public void RegisterMigrationServices_SetsPropertyOnBootstrapper()
    {
        var sut = new NodeBootstrapper();
        Assert.Null(sut.MigrationServices);

        var ms = sut.RegisterMigrationServices(NodeRole.MuscleGround);

        Assert.NotNull(sut.MigrationServices);
        Assert.Same(ms, sut.MigrationServices);
    }
}
