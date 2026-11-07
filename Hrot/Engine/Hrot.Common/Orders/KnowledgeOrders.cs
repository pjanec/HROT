using Fdp.Core;
using Fdp.Toolkit.Perception.Events;

namespace Hrot.Common.Orders
{
    /// <summary>
    /// ⭐ B-5 S3 (R-4137; 📄 docs/UX/DESIGN_Initial_State.md §3a) — THE ONE way a person makes a unit KNOW of another (the Unit form's "Knows about",
    /// the map menu's "Mark Target…"): one journalled interaction (edit framework F4) whose <see cref="SeedTargetCommand"/> goes to the node that
    /// owns the perceiver's <c>TargetMemory</c> and is applied there (<c>SeedTargetSystem</c>), answered Applied or Refused + why.
    /// </summary>
    public static class KnowledgeOrders
    {
        /// <summary><paramref name="perceiver"/> comes to know <paramref name="target"/>. False when either is gone or they are one.</summary>
        public static bool SeedTarget(EntityRepository world, Entity perceiver, Entity target, float scoreBoost = 1.0f)
        {
            if (perceiver == target || !world.IsAlive(perceiver) || !world.IsAlive(target)) return false;
            Fdp.Toolkit.Replication.Edits.Edits.Submit(world, Fdp.Toolkit.Replication.Edits.EditOp.Interact(perceiver,
                id => world.Bus.Publish(new SeedTargetCommand { Perceiver = perceiver, Target = target, ScoreBoost = scoreBoost, RequestId = id }), "Know of"));
            return true;
        }
    }
}
