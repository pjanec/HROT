using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Hsm.Editor.Blackboard;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Blackboard;

public sealed class HsmBlackboardAggregatorTests
{
    // ---- dto stub ----

    private struct SomeDto { }

    // ---- schema stub ----

    private sealed class StubSchemaExporter : IActionSchemaExporter
    {
        private readonly Dictionary<string, ActionSchemaEntry> _entries = new();

        public void Add(string fqn, Type dtoType) =>
            _entries[fqn] = new ActionSchemaEntry(
                fqn, dtoType, ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);

        public IReadOnlyDictionary<string, ActionSchemaEntry> All => _entries;
        public ActionSchemaEntry? Lookup(string fqn) => _entries.TryGetValue(fqn, out var e) ? e : null;
        public void Rebuild() { }
        public event Action? Changed { add { } remove { } }
    }

    // ---- catalog stub ----

    private sealed class StubCatalog : IAssetCatalog
    {
        public IReadOnlyList<IEditableAsset> All => Array.Empty<IEditableAsset>();
        public IEditableAsset? FindByAssetId(Guid id) => null;
        public IEditableAsset? FindByName(string name) => null;
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
        public event Action? Changed { add { } remove { } }
    }

    // ---- helpers ----

    private static HsmAsset BuildAndProject(HsmBuilder builder, string name = "TestMachine")
    {
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flat     = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flat);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(blob, metadata, null, Guid.NewGuid(), name, "", false, "");
    }

    private static (BlackboardAggregatorService service, HsmBlackboardAggregatorStrategy strategy)
        MakeServiceAndStrategy(StubSchemaExporter schema, StubCatalog catalog)
    {
        var service  = new BlackboardAggregatorService(
            Enumerable.Empty<IBlackboardAggregatorStrategy>(), schema, catalog);
        var strategy = new HsmBlackboardAggregatorStrategy(service);
        service.Register(strategy);
        return (service, strategy);
    }

    // ---- tests ----

    [Fact]
    public void Aggregate_state_OnEntry_action_emits_requirement()
    {
        const string fqn = "Ai.States.OnActivate";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.State("Active").Initial();
        var asset = BuildAndProject(builder);
        asset.AllStates.First(s => s.Name == "Active").OnEntryAction = fqn;

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_state_OnExit_action_emits_requirement()
    {
        const string fqn = "Ai.States.OnDeactivate";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.State("Active").Initial();
        var asset = BuildAndProject(builder);
        asset.AllStates.First(s => s.Name == "Active").OnExitAction = fqn;

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_state_Activity_action_emits_requirement()
    {
        const string fqn = "Ai.States.Patrol";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.State("Patrolling").Initial();
        var asset = BuildAndProject(builder);
        asset.AllStates.First(s => s.Name == "Patrolling").ActivityAction = fqn;

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_state_Timer_action_emits_requirement()
    {
        const string fqn = "Ai.States.OnTimer";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.State("Idle").Initial();
        var asset = BuildAndProject(builder);
        asset.AllStates.First(s => s.Name == "Idle").TimerAction = fqn;

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_transition_guard_emits_requirement()
    {
        const string fqn = "Ai.Guards.IsEnemyVisible";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.Event("E", 1);
        builder.State("Alert");
        builder.State("Idle").Initial().On("E").GoTo("Alert");
        var asset = BuildAndProject(builder);
        asset.AllTransitions[0].GuardFunction = fqn;

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_transition_action_emits_requirement()
    {
        const string fqn = "Ai.Actions.PlayAlertSound";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.Event("E", 1);
        builder.State("Alert");
        builder.State("Idle").Initial().On("E").GoTo("Alert");
        var asset = BuildAndProject(builder);
        asset.AllTransitions[0].ActionFunction = fqn;

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_global_transition_guard_emits_requirement()
    {
        const string fqn = "Ai.Guards.IsDead";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.Event("OnDeath", 99);
        builder.State("Alive").Initial();
        builder.State("Dead").Final();
        builder.GlobalTransition("OnDeath", "Dead");
        var asset = BuildAndProject(builder);
        asset.AllGlobalTransitions[0].GuardFunction = fqn;

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_null_fqn_not_emitted()
    {
        var schema  = new StubSchemaExporter();
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.State("Idle").Initial();
        var asset = BuildAndProject(builder);
        // All action fields remain null (default)

        var result = service.Aggregate(asset);

        result.Requirements.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_unknown_fqn_emits_schema_not_found_warning()
    {
        const string fqn = "Unknown.Method.NotInSchema";
        var schema  = new StubSchemaExporter();   // fqn not registered
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.State("Idle").Initial();
        var asset = BuildAndProject(builder);
        asset.AllStates.First(s => s.Name == "Idle").OnEntryAction = fqn;

        var result = service.Aggregate(asset);

        result.Requirements.Should().BeEmpty();
        result.Warnings.Should().HaveCount(1);
        result.Warnings[0].Kind.Should().Be(AggregationWarningKind.SchemaEntryNotFound);
    }

    [Fact]
    public void Aggregate_cycle_guard_returns_empty_on_second_visit()
    {
        var schema  = new StubSchemaExporter();
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var builder = new HsmBuilder("M");
        builder.State("Idle").Initial();
        var asset = BuildAndProject(builder);

        // Simulate a second visit by pre-populating visited with the asset's id
        var visited = new HashSet<Guid> { asset.AssetId };
        var result  = service.AggregateInternal(asset, visited);

        result.Requirements.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }
}
