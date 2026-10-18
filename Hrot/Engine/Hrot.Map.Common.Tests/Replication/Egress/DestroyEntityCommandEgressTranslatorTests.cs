using System;
using System.Collections.Generic;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Messages;

namespace Hrot.Map.Common.Tests.Replication.Egress;

/// <summary>
/// Standalone unit tests for <see cref="DestroyEntityCommandEgressTranslator"/> (DEBT-05).
/// Uses the internal testable constructor (accessible via InternalsVisibleTo from
/// <c>Hrot.Map.Common.csproj</c>).
/// </summary>
public class DestroyEntityCommandEgressTranslatorTests
{
    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class CapturingWriter<T> : IDdsWriter<T>
    {
        public List<T> Publishes { get; } = new();
        public void Write(T sample) => Publishes.Add(sample);
        public void DisposeInstance(T key) { }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Publishing a <see cref="DestroyEntityCommand"/> results in exactly one
    /// <see cref="DeleteEntityRequest"/> whose <c>EntityId</c> matches the command's
    /// <c>NetworkId</c>.
    /// </summary>
    [Fact]
    public void DestroyEntityCommand_IsConsumed_WritesOneDeleteEntityRequest_WithMatchingEntityId()
    {
        var bus        = new FdpEventBus();
        var writer     = new CapturingWriter<DeleteEntityRequest>();
        var translator = new DestroyEntityCommandEgressTranslator(writer, bus);

        bus.PublishManaged(new DestroyEntityCommand { NetworkId = 42L, Reason = "test" });
        bus.SwapBuffers();

        translator.ScanAndPublish(null!);

        Assert.Equal(1, writer.Publishes.Count);
        Assert.Equal(42, writer.Publishes[0].EntityId);
    }

    /// <summary>
    /// When a <see cref="DestroyEntityCommand"/> has <c>IsRemote == true</c>
    /// the translator must NOT forward a <see cref="DeleteEntityRequest"/> to the server.
    /// This prevents the echo-loop described in BUG2 where a remote disposal notification
    /// would be reflected back to SimHost, causing a spurious Error Code 3.
    /// </summary>
    [Fact]
    public void DestroyEntityCommand_Remote_IsNotForwarded()
    {
        var bus        = new FdpEventBus();
        var writer     = new CapturingWriter<DeleteEntityRequest>();
        var translator = new DestroyEntityCommandEgressTranslator(writer, bus);

        bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = 99L,
            Reason    = "EntityMaster disposed",
            IsRemote  = true,
        });
        bus.SwapBuffers();

        translator.ScanAndPublish(null!);

        Assert.Equal(0, writer.Publishes.Count);
    }

    /// <summary>
    /// ⭐ <c>CE-2247</c> — a destroy on a node that does NOT own the entity's master goes to the owner as a request (its own
    /// teardown cannot dispose the master, so the replicas elsewhere would stay — the spent vest on CGF and the IG); a destroy on
    /// the master's OWNER is not forwarded (its teardown disposes it everywhere).
    /// ✅ Red-proof: forwarding regardless of the owner ⇒ the owner case writes a request ⇒ red.
    /// </summary>
    [Fact]
    public void CE2247_ANonOwnersDestroy_GoesToTheOwner_TheOwnersOwnDoesNot()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkAuthority>();
        var map = new NetworkEntityMap();
        var mine   = repo.CreateEntity(); repo.AddComponent(mine,   new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1)); map.Register(10L, mine);
        var theirs = repo.CreateEntity(); repo.AddComponent(theirs, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1)); map.Register(20L, theirs);

        var bus        = new FdpEventBus();
        var writer     = new CapturingWriter<DeleteEntityRequest>();
        var translator = new DestroyEntityCommandEgressTranslator(writer, bus, localNodeId: 1, entityMap: map);

        bus.PublishManaged(new DestroyEntityCommand { NetworkId = 10L, Reason = "mine" });
        bus.PublishManaged(new DestroyEntityCommand { NetworkId = 20L, Reason = "theirs" });
        bus.SwapBuffers();
        translator.ScanAndPublish(repo);

        Assert.Equal(20, Assert.Single(writer.Publishes).EntityId);
    }
}
