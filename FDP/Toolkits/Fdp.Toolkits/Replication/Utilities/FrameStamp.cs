using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Utilities
{
    /// <summary>
    /// ⭐ <c>CE-2414</c> — "is this still the world, frame and state I built my table from?" for a per-thread table that is otherwise rebuilt on
    /// every ask (<c>DoorStates</c>, <c>CloudTable</c> — each sight / fire query asked, ~15 % of the SimHost thread on the CE-2408 stress world).
    /// <para>⭐ Valid only while: the same repository, the same <see cref="EntityRepository.GlobalVersion"/>, the same world epoch and restore
    /// count (<see cref="WorldEpoch"/> — a load, a rewind, a checkpoint), and NO chunk of the source component stamped at or after the frame it
    /// was taken in (<see cref="IComponentTable.HasChanges"/>: a value written within the frame forces a rebuild per ask for the rest of that
    /// frame — ⛔ R-220's hazard, "a door written within the tick served stale", cannot happen). A destroy stamps no component chunk: the
    /// caller also checks the entities it read are still alive.</para>
    /// </summary>
    internal struct FrameStamp
    {
        private EntityRepository? _repo;
        private uint _version;
        private int _epoch, _restores;

        public void Take(EntityRepository repo)
        {
            _repo = repo;
            _version = repo.GlobalVersion;
            _epoch = WorldEpoch.Of(repo);
            _restores = WorldEpoch.RestoresOf(repo);
        }

        public void Clear() => _repo = null;

        /// <summary>Same world, frame and epoch (a load, a rewind, a checkpoint all break it).</summary>
        public readonly bool HoldsFrame(EntityRepository repo)
            => ReferenceEquals(repo, _repo) && repo.GlobalVersion == _version
               && WorldEpoch.Of(repo) == _epoch && WorldEpoch.RestoresOf(repo) == _restores;

        /// <summary>Same world, frame and epoch, and nothing of <paramref name="source"/> written since the frame began.</summary>
        public readonly bool Holds(EntityRepository repo, Type source)
        {
            if (!ReferenceEquals(repo, _repo) || repo.GlobalVersion != _version) return false;
            if (WorldEpoch.Of(repo) != _epoch || WorldEpoch.RestoresOf(repo) != _restores) return false;
            return !(repo.TryGetTable(source, out var table) && table.HasChanges(_version == 0 ? 0 : _version - 1));
        }
    }
}
