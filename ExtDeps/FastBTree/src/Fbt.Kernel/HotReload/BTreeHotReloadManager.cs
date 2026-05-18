using System;
using System.Collections.Generic;

namespace Fbt.HotReload
{
    /// <summary>
    /// Delegate for resetting an element in a span by index.
    /// Used in <see cref="BTreeHotReloadManager.TryReload{TState}"/> for hard resets.
    /// Span is passed as first argument so callers can write: (span, i) => span[i] = default
    /// </summary>
    public delegate void SpanResetAction<TState>(Span<TState> span, int index);

    /// <summary>
    /// Manages hot reload of behavior tree blobs.
    /// Tracks registered blobs and computes reload results by comparing structure/param hashes.
    /// BehaviorRegistry patching is the caller's responsibility.
    /// </summary>
    public class BTreeHotReloadManager
    {
        private readonly Dictionary<string, BehaviorTreeBlob> _knownBlobs
            = new Dictionary<string, BehaviorTreeBlob>(StringComparer.Ordinal);

        /// <summary>
        /// Determines the reload result for a new blob and updates the internal registry.
        /// Calls <paramref name="hardResetAction"/> on each span element when a HardReset occurs.
        /// Never throws; guards against null newBlob.
        /// </summary>
        public ReloadResult TryReload<TState>(
            string treeName,
            BehaviorTreeBlob? newBlob,
            Span<TState> liveInstances,
            SpanResetAction<TState>? hardResetAction)
            where TState : unmanaged
        {
            if (newBlob == null) return ReloadResult.NoChange;

            if (!_knownBlobs.TryGetValue(treeName, out var oldBlob))
            {
                _knownBlobs[treeName] = newBlob;
                return ReloadResult.NewTree;
            }

            if (oldBlob.StructureHash == newBlob.StructureHash &&
                oldBlob.ParamHash == newBlob.ParamHash)
            {
                return ReloadResult.NoChange;
            }

            _knownBlobs[treeName] = newBlob;

            if (oldBlob.StructureHash != newBlob.StructureHash)
            {
                // Structure changed -- reset all live instances
                for (int i = 0; i < liveInstances.Length; i++)
                {
                    hardResetAction?.Invoke(liveInstances, i);
                }
                return ReloadResult.HardReset;
            }

            // Only params changed
            return ReloadResult.SoftReload;
        }

        /// <summary>
        /// Returns the currently registered blob for the given tree name, or null if not known.
        /// </summary>
        public BehaviorTreeBlob? GetKnownBlob(string treeName)
        {
            _knownBlobs.TryGetValue(treeName, out var blob);
            return blob;
        }
    }
}
