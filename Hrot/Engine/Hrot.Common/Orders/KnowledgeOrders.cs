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
            var ticket = Fdp.Toolkit.Replication.Edits.Edits.Submit(world, Fdp.Toolkit.Replication.Edits.EditOp.Interact(perceiver,
                id => world.Bus.Publish(new SeedTargetCommand { Perceiver = perceiver, Target = target, ScoreBoost = scoreBoost, RequestId = id }), "Know of"));
            Seeds.Of(world).Record(perceiver, target, ticket.RequestId);
            return true;
        }

        /// <summary>⭐ U-24 / S5 (R-4157) — THIS station's seeds, in the order they were sent: the Unit form's <i>Knows about</i> shows them (and their outcome, by <see cref="SeededTarget.RequestId"/> in the edit journal). ⛔ Not what a unit KNOWS
        /// (that is <c>TargetMemory</c>, on the node that runs his brain — the Brain tab); UI state: per world, not recorded, not saved.</summary>
        public static Seeds SeedsOf(EntityRepository world) => Seeds.Of(world);
    }

    /// <summary>One seed this station sent: who is to know, of whom, and the id of the journalled request (its outcome is the journal's).</summary>
    public readonly record struct SeededTarget(Entity Perceiver, Entity Target, System.Guid RequestId);

    /// <summary>A small per-world ring of the seeds this station sent (newest last); a repeat of the same (unit, target) pair replaces the older entry. 0 B to read.</summary>
    public sealed class Seeds
    {
        public const int Capacity = 64;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, Seeds> PerWorld = new();
        private readonly SeededTarget[] _ring = new SeededTarget[Capacity];
        private int _count;

        public static Seeds Of(EntityRepository world) => PerWorld.GetValue(world, static _ => new Seeds());
        public int Count => _count;
        /// <summary>The <paramref name="i"/>-th seed, oldest first.</summary>
        public SeededTarget this[int i] => _ring[i];

        public void Record(Entity perceiver, Entity target, System.Guid requestId)
        {
            int at = -1;
            for (int i = 0; i < _count; i++) if (_ring[i].Perceiver == perceiver && _ring[i].Target == target) { at = i; break; }
            if (at < 0) { if (_count == Capacity) at = 0; else at = _count++; }
            for (int i = at; i < _count - 1; i++) _ring[i] = _ring[i + 1];   // newest last
            _ring[_count - 1] = new SeededTarget(perceiver, target, requestId);
        }
    }
}
