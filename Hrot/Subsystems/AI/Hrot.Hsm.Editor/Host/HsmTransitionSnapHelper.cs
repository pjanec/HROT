using System.Numerics;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Helper for snap-to-state behavior during drag-to-create-transition gestures.
/// When the user drags from a state's output pin and hovers near another state,
/// this helper finds the nearest valid snap target so the wire snaps into place.
/// </summary>
public static class HsmTransitionSnapHelper
{
    // Default canvas-space node size used when SizeOverride is not set.
    private static readonly Vector2 DefaultNodeSize = new(120f, 40f);

    /// <summary>
    /// Finds the nearest state whose input pin area is within
    /// <paramref name="snapRadiusCanvas"/> of <paramref name="canvasPos"/>.
    /// Returns null if no state is within snap range.
    /// </summary>
    /// <param name="canvasPos">The cursor position in canvas (graph) space.</param>
    /// <param name="asset">The HSM asset being edited.</param>
    /// <param name="excludeSource">
    /// Optional state to exclude (the source of the transition being created).
    /// History and final pseudo-states are always excluded as targets.
    /// </param>
    /// <param name="snapRadiusCanvas">Snap radius in canvas units (default 24).</param>
    public static StateNode? FindNearestSnapTarget(
        Vector2 canvasPos,
        HsmAsset asset,
        StateNode? excludeSource = null,
        float snapRadiusCanvas = 24f)
    {
        StateNode? nearest = null;
        float nearestDist = snapRadiusCanvas;

        foreach (var state in asset.AllStates)
        {
            // Skip the synthetic root (never a valid transition target).
            if (state == asset.RootState) continue;
            // Exclude the source state.
            if (state == excludeSource) continue;
            // History and final pseudo-states can only be targets in specific cases;
            // exclude them from general snap targets.
            if (state.IsHistory || state.IsDeepHistory) continue;

            // The snap target is the center of the state's input edge (left-center).
            var size = state.SizeOverride ?? DefaultNodeSize;
            var inputPinPos = state.Position + new Vector2(0f, size.Y * 0.5f);

            float dist = Vector2.Distance(canvasPos, inputPinPos);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = state;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Returns true if the given <paramref name="state"/> is a valid target
    /// for a new transition (not a history, not the root, not a final state
    /// when <paramref name="allowFinalTarget"/> is false).
    /// </summary>
    public static bool IsValidTransitionTarget(
        StateNode state,
        HsmAsset asset,
        bool allowFinalTarget = true)
    {
        if (state == asset.RootState) return false;
        if (state.IsHistory || state.IsDeepHistory) return false;
        if (state.IsFinal && !allowFinalTarget) return false;
        return true;
    }
}
