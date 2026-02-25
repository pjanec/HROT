using Bagira.BDC.SSTD;
using Bagira.IG.Translators;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.IG.Tests;

/// <summary>
/// Unit tests for <see cref="EntityMasterTranslator"/> logic paths exercised
/// without a live DDS participant.
///
/// Passing <c>null</c> as the participant parameter activates test mode: the
/// translator's DDS reader is not constructed and <see cref="EntityMasterTranslator.PollIngress"/>
/// becomes a no-op. The business logic methods
/// <see cref="EntityMasterTranslator.ProcessSample"/> and
/// <see cref="EntityMasterTranslator.ProcessDispose"/> are called directly
/// via the <c>InternalsVisibleTo</c> attribute declared in Bagira.IG.csproj.
///
/// Full DDS-level integration tests (DdsParticipant → DdsReader.Take() →
/// PollIngress result) require a live CycloneDDS domain and are deferred to the
/// integration test suite that runs with native libraries present.
/// </summary>
public class EntityMasterTranslatorTests
{
    // ── Test constants (§CODE-STANDARDS §1 — no magic numbers) ───────────────
    private const long  TestNetworkId = 77L;
    private const long  TestTkbType   = 42L;
    private const ulong TestDisType   = 100UL;

    // ── Fixture factory ──────────────────────────────────────────────────────

    private static (EntityRepository repo, NetworkEntityMap entityMap, FdpEventBus eventBus, EntityMasterTranslator translator)
        CreateFixture()
    {
        var repo       = new EntityRepository();
        repo.RegisterComponent<EntityMaster>(); // unmanaged struct — must be registered
        var entityMap  = new NetworkEntityMap();
        var eventBus   = new FdpEventBus();
        // null participant = test mode (no DDS reader created)
        var translator = new EntityMasterTranslator(null, entityMap, eventBus);
        return (repo, entityMap, eventBus, translator);
    }

    // ── SpawnEntityCommand path (new entity) ─────────────────────────────────

    /// <summary>
    /// When a sample arrives for a network ID not yet in the entity map,
    /// ProcessSample must publish exactly one <see cref="SpawnEntityCommand"/>
    /// carrying the correct fields.
    /// </summary>
    [Fact]
    public void ProcessSample_NewEntity_PublishesSpawnEntityCommandWithCorrectFields()
    {
        var (repo, _, eventBus, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType, DisType = TestDisType };

        ISimulationView      view = repo;
        IEntityCommandBuffer cmd  = view.GetCommandBuffer();

        translator.ProcessSample(in master, cmd, view);

        eventBus.SwapBuffers();
        var commands = eventBus.ConsumeManaged<SpawnEntityCommand>();

        Assert.Single(commands);
        Assert.Equal(TestNetworkId,                    commands[0].NetworkId);
        Assert.Equal(TestTkbType,                      commands[0].TkbType);
        Assert.Equal(IgNetworkConstants.LocalNodeId,   commands[0].OwnerNodeId);
        Assert.Equal(ReliableInitType.None,            commands[0].InitType);
    }

    /// <summary>
    /// The <see cref="SpawnEntityCommand.InitialComponents"/> list must contain
    /// the <see cref="EntityMaster"/> sample so the spawning system can apply it
    /// on entity construction.
    /// </summary>
    [Fact]
    public void ProcessSample_NewEntity_InitialComponentsContainsEntityMaster()
    {
        var (repo, _, eventBus, translator) = CreateFixture();
        var master = new EntityMaster { EntityId = (int)TestNetworkId, TkbType = TestTkbType };

        ISimulationView      view = repo;
        IEntityCommandBuffer cmd  = view.GetCommandBuffer();

        translator.ProcessSample(in master, cmd, view);

        eventBus.SwapBuffers();
        var commands = eventBus.ConsumeManaged<SpawnEntityCommand>();

        Assert.Single(commands[0].InitialComponents);
        Assert.IsType<EntityMaster>(commands[0].InitialComponents[0]);
        var embeddedMaster = (EntityMaster)commands[0].InitialComponents[0];
        Assert.Equal(TestTkbType, embeddedMaster.TkbType);
    }

    // ── Component-update path (known entity) ─────────────────────────────────

    /// <summary>
    /// When the entity is already known, ProcessSample must update the
    /// <see cref="EntityMaster"/> component on the existing entity and must NOT
    /// emit a new <see cref="SpawnEntityCommand"/>.
    /// </summary>
    [Fact]
    public void ProcessSample_KnownEntity_UpdatesComponentAndEmitsNoSpawnCommand()
    {
        var (repo, entityMap, eventBus, translator) = CreateFixture();

        // Pre-register a known entity in the world and map
        var entity = repo.CreateEntity();
        repo.SetComponent(entity, new EntityMaster { EntityId = (int)TestNetworkId, TkbType = 1L });
        entityMap.Register(TestNetworkId, entity);

        var updatedMaster = new EntityMaster
        {
            EntityId = (int)TestNetworkId,
            TkbType  = TestTkbType,   // changed
            DisType  = TestDisType,   // changed
        };

        ISimulationView view = repo;
        var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
        translator.ProcessSample(in updatedMaster, cmd, view);
        cmd.Playback(repo);

        // No spawn command must have been published
        eventBus.SwapBuffers();
        Assert.Empty(eventBus.ConsumeManaged<SpawnEntityCommand>());

        // The component on the entity must reflect the updated values
        var stored = repo.GetComponent<EntityMaster>(entity);
        Assert.Equal(TestTkbType, stored.TkbType);
        Assert.Equal(TestDisType, stored.DisType);
    }

    // ── DestroyEntityCommand path (disposed instance) ────────────────────────

    /// <summary>
    /// ProcessDispose must publish a <see cref="DestroyEntityCommand"/> with the
    /// supplied network ID and a non-empty reason string.
    /// </summary>
    [Fact]
    public void ProcessDispose_PublishesDestroyEntityCommandWithMatchingNetworkId()
    {
        var (_, _, eventBus, translator) = CreateFixture();

        translator.ProcessDispose(TestNetworkId);

        eventBus.SwapBuffers();
        var commands = eventBus.ConsumeManaged<DestroyEntityCommand>();

        Assert.Single(commands);
        Assert.Equal(TestNetworkId, commands[0].NetworkId);
        Assert.False(string.IsNullOrEmpty(commands[0].Reason),
            "DestroyEntityCommand.Reason must carry a non-empty disposal message for diagnostics");
    }

    // ── Test-mode safety ─────────────────────────────────────────────────────

    /// <summary>
    /// PollIngress with a null participant must return without publishing any
    /// events. This verifies the test-mode guard added to support unit testing.
    /// </summary>
    [Fact]
    public void PollIngress_WithNullParticipant_IsNoOpAndEmitsNoEvents()
    {
        var (repo, _, eventBus, translator) = CreateFixture();

        ISimulationView      view = repo;
        IEntityCommandBuffer cmd  = view.GetCommandBuffer();
        translator.PollIngress(cmd, view);

        eventBus.SwapBuffers();
        Assert.Empty(eventBus.ConsumeManaged<SpawnEntityCommand>());
        Assert.Empty(eventBus.ConsumeManaged<DestroyEntityCommand>());
    }
}
