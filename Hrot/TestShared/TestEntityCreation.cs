using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Replication.Services;
using Hrot.Core.Network;
using Hrot.Common.EntityCreation;

namespace Hrot.TestShared;

/// <summary>
/// ⭐ <c>CE-259ax</c> — the ONE test-side way to get a real <see cref="EntityCreation"/> over a world, for rails that exercise an AUTHOR
/// (the shared spawn adapter, the SimHost scenario manager). ⛔ A fake would let the author's call pass while the production affordance
/// (<c>RequestEntityCreation</c>) drifted; this is the PRODUCTION pack built the way <c>EntityCreationPackRails</c> builds it.
/// Linked into the test projects that need it (<c>Hrot/TestShared</c>).
/// </summary>
internal static class TestEntityCreation
{
    /// <param name="world">The world whose bus the request lands on.</param>
    /// <param name="tkb">The TKB the pack is built over.</param>
    /// <param name="nodeId">The node the pack acts as (<c>creation.NodeId</c>).</param>
    public static EntityCreation For(EntityRepository world, ITkbDatabase tkb, int nodeId = 7)
        => EntityCreationPack.Build(new EntityCreationContext
        {
            World       = world,
            EntityMap   = new NetworkEntityMap(),
            TkbDb       = tkb,
            IdAllocator = new SequentialIdAllocator(),
            Elm         = new EntityLifecycleModule(tkb, Array.Empty<int>()),
            NodeId      = (Fdp.Core.NodeId)(nodeId),
        });
}
