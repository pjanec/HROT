using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// ⭐ G-7 / CE-4354 (docs/UX/DESIGN_Review_Perspective.md §6.4 I2, §6.12) — the ONE persistent world the binary merge writes and what it remembers about it:
    /// <b>network id → target entity</b> (a unit keeps its target entity for as long as ANY node holds it — across steps and across an owner change, M-8) and
    /// <b>(node, local index, generation) → target entity</b> for the local (non-networked) entities of the provider node. An entity is CREATED the first time it
    /// is seen and DESTROYED when it is gone from every node. The map outlives a <c>Merge</c> call; only <see cref="Reset"/> (a world boundary) forgets it.
    /// </summary>
    public sealed class MergeTarget
    {
        private struct Slot { public Entity Entity; public int Pass; }

        private readonly Dictionary<long, Slot> _byNet = new();
        private readonly Dictionary<(int node, int index, ushort generation), Slot> _local = new();
        private readonly List<long> _goneNet = new();
        private readonly List<(int, int, ushort)> _goneLocal = new();
        private int _pass;

        /// <summary>The repository the map belongs to (null before the first merge).</summary>
        public EntityRepository? Repo { get; private set; }

        /// <summary>Networked entities in the target.</summary>
        public int NetworkedCount => _byNet.Count;

        /// <summary>Local entities in the target.</summary>
        public int LocalCount => _local.Count;

        /// <summary>Born in the last pass.</summary>
        public int LastCreated { get; private set; }

        /// <summary>Destroyed in the last pass.</summary>
        public int LastDestroyed { get; private set; }

        /// <summary>Starts a pass over <paramref name="repo"/> (a different repo than the last one forgets the map).</summary>
        public void Begin(EntityRepository repo)
        {
            if (!ReferenceEquals(Repo, repo)) { Reset(); Repo = repo; }
            _pass++;
            LastCreated = LastDestroyed = 0;
        }

        /// <summary>Forgets every mapping (the caller cleared the world, or is about to bind a new one). Destroys nothing.</summary>
        public void Reset()
        {
            _byNet.Clear(); _local.Clear();
            Repo = null;
        }

        /// <summary>The target entity of network id <paramref name="netId"/>: the mapped one while alive, else a new one.</summary>
        public Entity EnsureNetworked(long netId)
        {
            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_byNet, netId, out bool existed);
            if (!existed || !Repo!.IsAlive(slot.Entity)) { slot.Entity = Repo!.CreateEntity(); LastCreated++; }
            slot.Pass = _pass;
            return slot.Entity;
        }

        /// <summary>The target entity of a local entity of <paramref name="node"/>'s sandbox.</summary>
        public Entity EnsureLocal(int node, Entity source)
        {
            var key = (node, source.Index, source.Generation);
            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_local, key, out bool existed);
            if (!existed || !Repo!.IsAlive(slot.Entity)) { slot.Entity = Repo!.CreateEntity(); LastCreated++; }
            slot.Pass = _pass;
            return slot.Entity;
        }

        /// <summary>The target entity of network id <paramref name="netId"/> if the map has one.</summary>
        public bool TryGet(long netId, out Entity entity)
        {
            if (_byNet.TryGetValue(netId, out var s) && Repo != null && Repo.IsAlive(s.Entity)) { entity = s.Entity; return true; }
            entity = Entity.Null;
            return false;
        }

        /// <summary>Destroys the entities this pass did not see (gone from every node).</summary>
        public void Sweep()
        {
            _goneNet.Clear(); _goneLocal.Clear();
            foreach (var kv in _byNet) if (kv.Value.Pass != _pass) _goneNet.Add(kv.Key);
            foreach (var kv in _local) if (kv.Value.Pass != _pass) _goneLocal.Add(kv.Key);
            foreach (var k in _goneNet) { Destroy(_byNet[k].Entity); _byNet.Remove(k); }
            foreach (var k in _goneLocal) { Destroy(_local[k].Entity); _local.Remove(k); }
        }

        private void Destroy(Entity e)
        {
            if (Repo!.IsAlive(e)) { Repo.DestroyEntity(e); LastDestroyed++; }
        }
    }
}
