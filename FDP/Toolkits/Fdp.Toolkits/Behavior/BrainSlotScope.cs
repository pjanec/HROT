using System;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-3035</c> (R-189) — THE SLOT IS A VIEW. While the SOP slot of a unit runs (its start or its tick), this scope
    /// makes every ENTITY-KEYED storage read of that one unit — root params, root tree state, root HSM instance — resolve
    /// the SOP's behaviour instead of the one in <see cref="Components.BehaviorState"/>, and makes parts it spawns belong
    /// to the SOP run. ⇒ the three runners, the generated node code and the storage layer stay unchanged: they already key
    /// storage by behaviour hash, and the SOP is a different behaviour (the same behaviour in both slots is refused).
    /// 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §6 ("the slot is a VIEW over one of two components").
    /// <para>⚠ Thread-static and nest-safe; it affects ONE entity only, so a node reading another unit is untouched.</para>
    /// </summary>
    public static class BrainSlotScope
    {
        [ThreadStatic] private static Entity _entity;
        [ThreadStatic] private static int _hash;
        [ThreadStatic] private static uint _instanceId;
        [ThreadStatic] private static bool _active;

        /// <summary>Enter the SOP slot view of <paramref name="entity"/>; dispose to leave (restores any outer scope).</summary>
        public static Scope Enter(Entity entity, int behaviourHash, uint instanceId)
        {
            var outer = new Scope(_active, _entity, _hash, _instanceId);
            _active = true; _entity = entity; _hash = behaviourHash; _instanceId = instanceId;
            return outer;
        }

        /// <summary>The behaviour hash storage should resolve for <paramref name="entity"/>, when a slot view is active for it.</summary>
        public static bool TryGetHash(Entity entity, out int behaviourHash)
        {
            behaviourHash = _hash;
            return _active && _entity.Equals(entity);
        }

        /// <summary>The run token parts spawned under <paramref name="entity"/> belong to, when a slot view is active for it.</summary>
        public static bool TryGetInstanceId(Entity entity, out uint instanceId)
        {
            instanceId = _instanceId;
            return _active && _entity.Equals(entity);
        }

        /// <summary>Restores the outer view on dispose.</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly bool _wasActive;
            private readonly Entity _wasEntity;
            private readonly int _wasHash;
            private readonly uint _wasInstanceId;

            internal Scope(bool wasActive, Entity wasEntity, int wasHash, uint wasInstanceId)
            {
                _wasActive = wasActive; _wasEntity = wasEntity; _wasHash = wasHash; _wasInstanceId = wasInstanceId;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                _active = _wasActive; _entity = _wasEntity; _hash = _wasHash; _instanceId = _wasInstanceId;
            }
        }
    }
}
