using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// Destroys ghost entities that never receive their <c>EntityMaster</c> packet within the
    /// allowed time window.  This cleans up dangling ghosts caused by dropped or late UDP packets.
    ///
    /// Queries for entities in <see cref="EntityLifecycle.Ghost"/> state that carry a
    /// <see cref="GhostStateTracker"/> component.  The tracker's
    /// <see cref="GhostStateTracker.FirstSeenFrame"/> was stamped by
    /// <see cref="GhostCreationSystem"/> at ghost creation time.
    /// </summary>
    public class GhostTimeoutSystem : ComponentSystem
    {
        private const uint MAX_GHOST_AGE = 3600; // 60 seconds at 60Hz

        protected override void OnUpdate()
        {
            if (!World.HasSingletonUnmanaged<GlobalTime>()) return;
            var globalTime = World.GetSingletonUnmanaged<GlobalTime>();
            uint currentFrame = (uint)globalTime.FrameNumber;

            var query = World.Query()
                .With<GhostStateTracker>()
                .WithLifecycle(EntityLifecycle.Ghost)
                .Build();

            using (var ecb = new EntityCommandBuffer())
            {
                foreach (var entity in query)
                {
                    var tracker = World.GetComponent<GhostStateTracker>(entity);

                    uint age = currentFrame - tracker.FirstSeenFrame;
                    if (age > MAX_GHOST_AGE)
                    {
                        ecb.DestroyEntity(entity);
                    }
                }

                ecb.Playback(World);
            }
        }
    }
}

