using System;
using Fdp.Core;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Scenario;
using Xunit;

namespace Hrot.Common.Tests.Migrations;

/// <summary>
/// JM-P2-003: Phase 2 envelope rollout -- ScenarioSerializer integration tests.
/// Verifies that Serialize produces a $meta envelope and that Deserialize accepts
/// both Phase 2 ($meta) and legacy (Header.SubsystemType) formats.
/// </summary>
public sealed class ScenarioPhase2Tests : IDisposable
{
    private const string DocType = HrotSubsystemTypes.Scenario;

    private readonly EntityRepository _repo;

    public ScenarioPhase2Tests()
    {
        ComponentTypeRegistry.Clear();
        _repo = new EntityRepository();
    }

    public void Dispose()
    {
        _repo.Dispose();
        ComponentTypeRegistry.Clear();
    }

    private static ScenarioSerializer BuildSerializer() =>
        new ScenarioSerializerBuilder(DocType).Build();

    // ── T01 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioSerializer_Serialize_ProducesMetaEnvelope()
    {
        var serializer = BuildSerializer();
        var dom = serializer.Serialize(_repo, new ScenarioHeader(DocType));

        Assert.True(JsonEnvelope.HasEnvelope(dom));
        var meta = JsonEnvelope.Read(dom);
        Assert.Equal(DocType, meta.DocType);
        Assert.Equal(1, meta.SchemaVersion);
    }

    // ── T02 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioSerializer_Serialize_TkbName_StoredInHeader()
    {
        var serializer = BuildSerializer();
        var dom = serializer.Serialize(_repo, new ScenarioHeader(DocType, TkbName: "Alpha_v2"));

        Assert.Equal("Alpha_v2", dom["Header"]!["TkbName"]!.GetValue<string>());
    }

    // ── T03 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioSerializer_Deserialize_WithEnvelope_LoadsEntities()
    {
        var serializer = BuildSerializer();
        _repo.CreateEntity();
        _repo.CreateEntity();

        var dom = serializer.Serialize(_repo, new ScenarioHeader(DocType));
        Assert.True(JsonEnvelope.HasEnvelope(dom));

        using var freshRepo = new EntityRepository();
        serializer.Deserialize(freshRepo, dom);

        Assert.Equal(2, freshRepo.EntityCount);
    }

    // ── T04 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioSerializer_Deserialize_LegacyHeader_LoadsEntities()
    {
        const string legacyJson =
            """{"Header":{"SubsystemType":"Hrot.Scenario","SchemaVersion":1},"Entities":{}}""";

        var serializer = BuildSerializer();
        using var freshRepo = new EntityRepository();
        serializer.Deserialize(freshRepo, legacyJson);

        Assert.Equal(0, freshRepo.EntityCount);
    }
}
