using FDP.Toolkit.Perception.Events;
using Fdp.Kernel;

namespace FDP.Toolkit.Perception.Systems
{
    /// <summary>
    /// Main-thread system that bridges <see cref="LosCheckRequestEvent"/>s from the async
    /// <see cref="PerceptionModule"/> to the physics raycast pipeline (or, in mock mode,
    /// directly confirms visibility for all requests).
    /// <para>
    /// <b>Normal mode (future):</b> For each <see cref="LosCheckRequestEvent"/>, adds a
    /// ray entry to <c>RaycastBatchData</c>. After physics solves next frame,
    /// <c>HitResolutionSystem</c> emits <see cref="TargetVisibleEvent"/> for unobstructed rays.
    /// </para>
    /// <para>
    /// <b>Mock mode (<see cref="LOS_MOCK_MODE"/> = <c>true</c>):</b>
    /// Skips ray submission entirely and immediately emits a <see cref="TargetVisibleEvent"/>
    /// for every incoming <see cref="LosCheckRequestEvent"/>. Used in Phase 2 where no terrain
    /// geometry exists yet. The constructor parameter makes the mode testable without conditional
    /// compilation — a constructor flag was preferred over a compile-time <c>#define</c> so that
    /// tests can instantiate both modes in the same test binary.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class LosRequestBatchingSystem : ComponentSystem
    {
        /// <summary>
        /// When <c>true</c>, every <see cref="LosCheckRequestEvent"/> is immediately resolved
        /// as visible (no actual ray submission). Set to <c>true</c> for Phase 2 testing.
        /// </summary>
        private readonly bool _mockMode;

        /// <param name="mockMode">
        /// <c>true</c> to bypass ray submission and directly emit <see cref="TargetVisibleEvent"/>;
        /// <c>false</c> for production (requires physics raycast pipeline).
        /// </param>
        public LosRequestBatchingSystem(bool mockMode = false)
        {
            _mockMode = mockMode;
        }

        protected override void OnUpdate()
        {
            var requests = World.Bus.Consume<LosCheckRequestEvent>();
            if (requests.IsEmpty) return;

            if (_mockMode)
            {
                // Mock mode: treat broadphase visibility as confirmed LOS.
                // Directly publish a TargetVisibleEvent for every incoming request.
                foreach (ref readonly var req in requests)
                {
                    World.Bus.Publish(new TargetVisibleEvent
                    {
                        ObserverEntityIndex = req.ObserverEntityIndex,
                        TargetEntityIndex   = req.TargetEntityIndex,
                    });
                }
            }
            else
            {
                // Production mode (Phase 3+): batch rays into RaycastBatchData.
                // TODO: Add to RaycastBatchData.Requests when the Physics toolkit is available.
                // foreach (ref readonly var req in requests)
                // {
                //     var raycastBatch = ref World.GetSingleton<RaycastBatchData>();
                //     raycastBatch.AddRequest(req.ObserverEntityIndex, req.TargetEntityIndex);
                // }
            }
        }
    }
}
