using Fdp.Core;
using System.Numerics;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Per-agent state stored in the fake crowd simulation.
    /// ECS component; ComponentId = <see cref="NavFakeIds.FakeCrowdAgentState"/>.
    /// </summary>
    [ComponentId(NavFakeIds.FakeCrowdAgentState)]
    public struct FakeCrowdAgentState
    {
        public Vector3 Target;
        public Vector3 Velocity;
        public bool    HasTarget;
        public bool    ReachedTarget;
    }

    /// <summary>
    /// Global crowd simulation state (one per crowd provider instance).
    /// ECS component; ComponentId = <see cref="NavFakeIds.FakeCrowdGlobalState"/>.
    /// </summary>
    [ComponentId(NavFakeIds.FakeCrowdGlobalState)]
    public struct FakeCrowdGlobalState
    {
        public int AgentCount;
        public int UpdateCount;
    }
}

