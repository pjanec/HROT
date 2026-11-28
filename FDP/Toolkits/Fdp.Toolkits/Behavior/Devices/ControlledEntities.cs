using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13f D-147, refining §3.13c D-105) — THE PAIRING of every
    /// "a device commands another entity": what this device commands — a drone (the ground station, slot 0), up to four weapons (the control unit of
    /// SD-S4c). AUTHORED on the device (a scenario, or the type's preset) and never written by a behaviour; the commanded entity finds its device
    /// through <see cref="ControlledBy"/>, an index rebuilt in the brains' serial pre-pass — ⛔ never a write across entities (R-4212).
    /// <para>⚠ AS BUILT (deviation from D-147's <c>Entity[4]</c>, argued): each slot is an <see cref="EntityLink"/> — the AUTHORED reference type
    /// (DESIGN_Entity_Reference: a local <see cref="Entity"/> is runtime state only; a scenario's Entity field reaches the created world as a STAGING handle —
    /// measured, the first <c>dv-drone-fly</c> run paired the station with the wrong entity). The file holds the entity's GUID, the scenario loader fills
    /// <c>.Ref</c> with its network id (<c>RefResolve</c>, as <c>ResolutionPolicy</c>'s areas — explicit fields, not an InlineArray, so the walker sees them),
    /// and the network id is what SD-C replicates anyway.</para>
    /// Unmanaged, recorded, saved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ControlledEntities)]
    public struct ControlledEntities
    {
        public const int Count = 4;
        // explicit fields, not an InlineArray: the scenario loader's link resolution (and the EntityRef remap) walks fields
        public EntityLink Entity0, Entity1, Entity2, Entity3;

        /// <summary>Slot <paramref name="i"/> as authored (none when out of range).</summary>
        public readonly EntityLink At(int i) => i switch { 0 => Entity0, 1 => Entity1, 2 => Entity2, 3 => Entity3, _ => EntityLink.None };

        /// <summary>The entity in slot <paramref name="i"/> on this node, or <see cref="Entity.Null"/>.</summary>
        public readonly Entity Resolve(EntityRepository repo, int i) => At(i).Ref.Resolve(repo);

        /// <summary>A pairing naming <paramref name="first"/> (its network id) in slot 0 — code and rails.</summary>
        public static ControlledEntities Of(int firstNetworkId) => new() { Entity0 = new EntityLink(new EntityRef(firstNetworkId)) };
    }

    /// <summary>
    /// ⭐⭐ SD-S2c (D-147) — the ONE answer to "which device commands this entity?": built every frame in the brains' SERIAL pre-pass
    /// (<c>BrainPhase.PrepareToolkitFrame</c>, beside <c>DroneJamming</c>) from every live device's <see cref="ControlledEntities"/>, then only READ by brain
    /// code in the parallel phase. An entity two devices name answers the FIRST in query order (one controller per entity — a scenario error, not a rule).
    /// ⛔⛔ Hot path (R-2084): two flat arrays indexed by the entity index, grown and never shrunk; a steady frame allocates nothing.
    /// </summary>
    public sealed class ControlledBy
    {
        private static readonly ConditionalWeakTable<EntityRepository, ControlledBy> s_of = new();

        private EntityQuery? _devices;
        private EntityRepository? _queryFor;
        private Entity[] _controlled = new Entity[64];   // by the controlled entity's index: the entity itself (its generation)
        private Entity[] _device = new Entity[64];       // … and the device commanding it
        private int[] _set = new int[16];                // the indexes set this frame (cleared next frame)
        private int _setCount;

        /// <summary>Pairings live this frame.</summary>
        public int Count => _setCount;

        public static ControlledBy Of(EntityRepository repo) => s_of.GetValue(repo, static _ => new ControlledBy());

        /// <summary>The serial pre-pass: every live device's slots, this frame.</summary>
        public static void PrepareFrame(EntityRepository repo) => Of(repo).Rebuild(repo);

        /// <summary>The device commanding <paramref name="entity"/> this frame, or <see cref="Entity.Null"/>.</summary>
        public static Entity Of(EntityRepository repo, Entity entity)
        {
            if (entity.IsNull) return Entity.Null;
            var c = Of(repo);
            int i = entity.Index;
            return i >= 0 && i < c._controlled.Length && c._controlled[i] == entity ? c._device[i] : Entity.Null;
        }

        private void Rebuild(EntityRepository repo)
        {
            for (int k = 0; k < _setCount; k++) { int i = _set[k]; _controlled[i] = Entity.Null; _device[i] = Entity.Null; }
            _setCount = 0;
            if (!repo.IsComponentTypeRegistered<ControlledEntities>()) return;
            if (_devices == null || !ReferenceEquals(repo, _queryFor)) { _devices = repo.Query().With<ControlledEntities>().Build(); _queryFor = repo; }
            foreach (var d in _devices)
            {
                var slots = repo.GetComponentRO<ControlledEntities>(d);
                for (int s = 0; s < ControlledEntities.Count; s++)
                {
                    var e = slots.Resolve(repo, s);
                    if (e.IsNull || !repo.IsAlive(e)) continue;
                    int i = e.Index;
                    if (i >= _controlled.Length)
                    {
                        int n = Math.Max(_controlled.Length * 2, i + 1);
                        Array.Resize(ref _controlled, n); Array.Resize(ref _device, n);
                    }
                    if (!_controlled[i].IsNull) continue;   // already named by an earlier device
                    _controlled[i] = e; _device[i] = d;
                    if (_setCount == _set.Length) Array.Resize(ref _set, _set.Length * 2);
                    _set[_setCount++] = i;
                }
            }
        }
    }
}
