using System;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>
    /// ⭐ CE-2380 part 1 (📄 docs/DESIGN_Remote_Edit_Outcome.md §2, lean D) — the last 64 edit outcomes this node knows (its own and those that arrived), so a UI that polls
    /// once a frame cannot miss one. A MANAGED SERVICE SINGLETON (<c>repo.SetSingletonManaged</c>, like <c>ICoverProvider</c>): no component id, never recorded or saved
    /// (<see cref="DataPolicy.NoScenario"/> | <see cref="DataPolicy.NoReplay"/>). <see cref="TryGet"/> answers the LATEST outcome of a request (Pending then Refused ⇒ Refused).
    /// </summary>
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    public sealed class EditOutcomeLog
    {
        /// <summary>The ring's size.</summary>
        public const int Capacity = 64;

        private static readonly ConditionalWeakTable<EntityRepository, EditOutcomeLog> PerWorld = new();

        private readonly EditOutcome[] _ring = new EditOutcome[Capacity];
        private int _next;
        private int _count;

        /// <summary>How many outcomes are held (≤ <see cref="Capacity"/>).</summary>
        public int Count => _count;

        /// <summary>The log of <paramref name="repo"/>; created and set on first use.</summary>
        public static EditOutcomeLog Of(EntityRepository repo)
        {
            ArgumentNullException.ThrowIfNull(repo);
            return PerWorld.GetValue(repo, static _ => new EditOutcomeLog());
        }

        /// <summary>Appends <paramref name="outcome"/>; the oldest is overwritten when full.</summary>
        public void Add(EditOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            _ring[_next] = outcome;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>True when the log already holds an outcome with this request id, status AND responder (the dedupe the wire ingress uses).</summary>
        public bool Holds(Guid requestId, EditStatus status, int respondingNode)
        {
            for (int i = 1; i <= _count; i++)
            {
                var o = _ring[(_next - i + Capacity) % Capacity];
                if (o.RequestId == requestId && o.Status == status && o.RespondingNode == respondingNode) return true;
            }
            return false;
        }

        /// <summary>The LATEST outcome of <paramref name="requestId"/>.</summary>
        public bool TryGet(Guid requestId, out EditOutcome outcome)
        {
            for (int i = 1; i <= _count; i++)
            {
                var o = _ring[(_next - i + Capacity) % Capacity];
                if (o.RequestId == requestId) { outcome = o; return true; }
            }
            outcome = null!;
            return false;
        }
    }
}
