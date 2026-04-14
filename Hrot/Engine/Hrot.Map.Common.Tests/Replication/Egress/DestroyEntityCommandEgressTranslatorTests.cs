using System;
using System.Collections.Generic;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Services;
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

        translator.PollIngress(null!, null!);

        Assert.Equal(1, writer.Publishes.Count);
        Assert.Equal(42, writer.Publishes[0].EntityId);
    }

    /// <summary>
    /// When a <see cref="DestroyEntityCommand"/> has <c>Reason == "EntityMaster disposed"</c>
    /// the translator must NOT forward a <see cref="DeleteEntityRequest"/> to the server.
    /// This prevents the echo-loop described in BUG2 where a remote disposal notification
    /// would be reflected back to SimHost, causing a spurious Error Code 3.
    /// </summary>
    [Fact]
    public void DestroyEntityCommand_EntityMasterDisposedReason_IsNotForwarded()
    {
        var bus        = new FdpEventBus();
        var writer     = new CapturingWriter<DeleteEntityRequest>();
        var translator = new DestroyEntityCommandEgressTranslator(writer, bus);

        bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = 99L,
            Reason    = "EntityMaster disposed",
        });
        bus.SwapBuffers();

        translator.PollIngress(null!, null!);

        Assert.Equal(0, writer.Publishes.Count);
    }
}
