using Fdp.Core;
using Hrot.Map.Common.Components;

namespace Hrot.Look;

/// <summary>⭐ CE-11006 (RR-6, R-11000) — the camera HROT would show on this node now (<see cref="CameraSuggestion"/>, written by the map each frame while its
/// 3-D view is on); false on a node with no map. Advice: a renderer may override it.</summary>
public static class CameraAdvice
{
    public static bool TryGet(EntityRepository world, out CameraSuggestion suggestion)
    {
        if (world.HasSingletonUnmanaged<CameraSuggestion>()) { suggestion = world.GetSingletonUnmanagedRO<CameraSuggestion>(); return true; }
        suggestion = default;
        return false;
    }
}
