using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// ⭐⭐ <c>CE-11025</c> (Q113-J, R-11009; docs/designs/IG/DESIGN_Linked_Client_Director.md §2e) — the WORLD a replay plays over: the recording's
    /// terrain, then the node's 3-D map and its linked client, made resident by the load chain's SHARED parts (the same providers a live load runs,
    /// one readiness). <see cref="ReferenceReplayLoadHandler"/> drives it: <see cref="PrepareAsync"/> once the recording is open (its metadata names
    /// the terrain), <see cref="Commit"/> on the frame thread at the replay's commit, and the transition into <c>OperatingReplay</c> waits on
    /// <see cref="WhenReadyAsync"/>. The node's <c>LoadPhaseChain</c> implements it.
    /// </summary>
    public interface IReplayWorldLoad
    {
        /// <summary>The content (pure, may run off the frame thread): the terrain <paramref name="terrainName"/> with the overlay
        /// <paramref name="overlayHash"/> (null = the base terrain). A terrain already resident with that overlay prepares nothing.</summary>
        Task PrepareAsync(Guid loadId, string terrainName, string? overlayHash, CancellationToken ct);

        /// <summary>Frame thread: the prepared world goes in (the terrain), the map is asked to build and the linked client to load it.</summary>
        void Commit(Guid loadId, EntityRepository? world);

        /// <summary>Completes on the frame thread once every part reports its world ready; faults with the part's reason when one fails.</summary>
        Task WhenReadyAsync(Guid loadId);

        /// <summary>Drops what <see cref="PrepareAsync"/> staged (the replay open failed or was aborted).</summary>
        void Abort(Guid loadId);
    }
}
