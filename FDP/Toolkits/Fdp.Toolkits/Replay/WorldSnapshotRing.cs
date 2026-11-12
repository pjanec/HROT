using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration.Preview;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐⭐ Edit-mode undo B-13 (📄 docs/UX/DESIGN_Edit_Mode_Undo.md §2.1, §2.3, R-4133) — the world states an undo goes back to: a ring of
    /// AUTHORED-mask keyframes (<see cref="CaptureOptions.Undo"/>, measured in §1.2), bounded by a memory budget and a depth, each with the
    /// non-ECS state beside the world (the id allocator, the network-id map, the lifecycle participants — the SAME
    /// <see cref="IPreviewRewindable"/> set the dry run brackets, captured per slot through a <see cref="PreviewStateBracket"/>).
    /// <para>⭐ A restore is the dry run's exit, unchanged (§2.3, R-174 — no second restore path): <see cref="WorldKeyframes.Apply"/> (versions only
    /// grow) → the bracket's restore → <see cref="AfterRestore.Run"/> (map rebuild, <c>WorldEpoch.Restored</c> — every epoch-keyed cache forgets,
    /// U3 — and the republish).</para>
    /// <para>⚠ A slot is an OPAQUE handle: <c>EditHistory</c>'s world step (B-14) keeps it. A slot the budget evicted answers
    /// <c>false</c> from <see cref="Restore"/> — the history then greys that step (never a silent partial undo).</para>
    /// </summary>
    public sealed class WorldSnapshotRing
    {
        /// <summary>The design's budget (Q5, R-4133): 256 MB.</summary>
        public const long DefaultBudgetBytes = 256L * 1024 * 1024;

        /// <summary>The design's depth cap (Q5): 100 steps.</summary>
        public const int DefaultMaxDepth = 100;

        private sealed record Slot(long Id, WorldKeyframe Keyframe, PreviewStateBracket Bracket);

        private readonly IReadOnlyList<IPreviewRewindable> _participants;
        private readonly LinkedList<Slot> _slots = new();   // oldest first
        private readonly Dictionary<long, LinkedListNode<Slot>> _byId = new();
        private long _next = 1;

        public WorldSnapshotRing(IEnumerable<IPreviewRewindable>? participants = null, long budgetBytes = DefaultBudgetBytes, int maxDepth = DefaultMaxDepth)
        {
            if (budgetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            if (maxDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            _participants = new List<IPreviewRewindable>(participants ?? Array.Empty<IPreviewRewindable>());
            BudgetBytes = budgetBytes;
            MaxDepth = maxDepth;
            Fdp.Core.MemoryCensus.RegisterOwner("WorldSnapshotRing (undo)", this, o => ((WorldSnapshotRing)o).UsedBytes);   // ⭐ CE-4456 N-1
        }

        public long BudgetBytes { get; }
        public int MaxDepth { get; }

        /// <summary>How many states are held.</summary>
        public int Count => _slots.Count;

        /// <summary>Their bytes.</summary>
        public long UsedBytes { get; private set; }

        /// <summary>Captures <paramref name="world"/> as it is now; returns the slot's handle. The oldest states go first when the budget or the depth is passed
        /// (the newest is always kept, even alone over the budget).</summary>
        public long Capture(EntityRepository world)
        {
            if (world is null) throw new ArgumentNullException(nameof(world));
            var keyframe = WorldKeyframes.Capture(world, CaptureOptions.Undo);
            var bracket = new PreviewStateBracket(_participants);
            bracket.Capture();
            var slot = new Slot(_next++, keyframe, bracket);
            _byId[slot.Id] = _slots.AddLast(slot);
            UsedBytes += keyframe.SizeBytes;
            while (_slots.Count > 1 && (UsedBytes > BudgetBytes || _slots.Count > MaxDepth)) Evict(_slots.First!);
            return slot.Id;
        }

        /// <summary>Is <paramref name="slot"/> still held?</summary>
        public bool Holds(long slot) => _byId.ContainsKey(slot);

        /// <summary>Puts <paramref name="world"/> back to <paramref name="slot"/>'s state, in place (the dry run's exit sequence). The slot stays held
        /// (redo goes forward again to another slot). <c>false</c> — nothing changed — when the slot was evicted or never existed.</summary>
        public bool Restore(EntityRepository world, long slot, EventAccumulator? accumulator = null)
        {
            if (world is null) throw new ArgumentNullException(nameof(world));
            if (!_byId.TryGetValue(slot, out var node)) return false;
            WorldKeyframes.Apply(world, node.Value.Keyframe, RestoreOptions.DryRunExit);
            node.Value.Bracket.Restore();
            AfterRestore.Run(world, accumulator);
            return true;
        }

        /// <summary>Forgets <paramref name="slot"/> and every state after it (a new edit after an undo drops the redo branch).</summary>
        public void DropFrom(long slot)
        {
            if (!_byId.TryGetValue(slot, out var node)) return;
            while (node != null) { var next = node.Next; Evict(node); node = next; }
        }

        /// <summary>Forgets everything (a scenario opened or created, the station gone Live — §2.2 "lifetime").</summary>
        public void Clear()
        {
            if (Evicted != null) foreach (var slot in _slots) Evicted(slot.Id);
            _slots.Clear();
            _byId.Clear();
            UsedBytes = 0;
        }

        /// <summary>⭐ N-7 (CE-4469) — a slot is no longer held (aged out, dropped with a redo branch, or cleared). A threaded node's undo sink turns it
        /// into the UI history's "this step is gone" — the UI never asks the ring (<see cref="Holds"/> reads a dictionary the sim thread writes).</summary>
        public event Action<long>? Evicted;

        private void Evict(LinkedListNode<Slot> node)
        {
            Evicted?.Invoke(node.Value.Id);
            _slots.Remove(node);
            _byId.Remove(node.Value.Id);
            UsedBytes -= node.Value.Keyframe.SizeBytes;
            node.Value.Bracket.Discard();
        }
    }
}
