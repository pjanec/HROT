using Hrot.IG.Components;
using Fdp.Kernel;
using Fdp.ModuleHost.Core.Abstractions;

namespace Hrot.IG.UI;

/// <summary>
/// Captures a per-frame snapshot of ECS performance counters (IG.5.4).
///
/// Queries the ECS view to compute:
/// <list type="bullet">
///   <item>
///     <see cref="TotalEntityCount"/> — the number of entities carrying
///     <see cref="SimTransform"/> (i.e. all spatially-tracked simulation entities).
///   </item>
///   <item>
///     <see cref="VisibleEntityCount"/> — the subset of those entities whose
///     <see cref="CullingState.IsVisible"/> flag is <c>true</c> after the current
///     frame's <see cref="Hrot.IG.Systems.MapCullingSystem"/> pass.
///   </item>
/// </list>
///
/// FPS and frame-time are passed in at call-site because they come from Raylib
/// and cannot be queried from an <see cref="ISimulationView"/>.
///
/// Call <see cref="Snapshot"/> once per frame before any UI draw calls so that
/// <see cref="PerformanceOverlay"/> reads fresh values.
/// </summary>
public class PerformanceMetrics
{
    // ── Snapshot results ──────────────────────────────────────────────────────

    /// <summary>
    /// Total number of entities with a <see cref="SimTransform"/> component after the
    /// most recent <see cref="Snapshot"/> call.  Includes both visible and culled entities.
    /// </summary>
    public int TotalEntityCount { get; private set; }

    /// <summary>
    /// Number of entities with <see cref="CullingState.IsVisible"/> = <c>true</c> after
    /// the most recent <see cref="Snapshot"/> call.
    /// </summary>
    public int VisibleEntityCount { get; private set; }

    /// <summary>
    /// Frames-per-second value supplied to the most recent <see cref="Snapshot"/> call.
    /// Set by the caller from <c>Raylib.GetFPS()</c>.
    /// </summary>
    public int Fps { get; private set; }

    /// <summary>
    /// Frame time in milliseconds supplied to the most recent <see cref="Snapshot"/> call.
    /// Set by the caller from <c>Raylib.GetFrameTime() * 1000</c>.
    /// </summary>
    public float FrameTimeMs { get; private set; }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates all counters from <paramref name="view"/> for the current frame.
    ///
    /// Entity counting uses two passes over the ECS query:
    /// <list type="number">
    ///   <item>Count every entity with <see cref="SimTransform"/> → <see cref="TotalEntityCount"/>.</item>
    ///   <item>Among those, count entities where <see cref="CullingState.IsVisible"/> is <c>true</c>
    ///         → <see cref="VisibleEntityCount"/>.</item>
    /// </list>
    ///
    /// Zero heap allocations (no LINQ — plain <c>foreach</c> per §CODE-STANDARDS §4).
    /// </summary>
    /// <param name="view">Current-frame ECS view.</param>
    /// <param name="fps">Current frames per second (from <c>Raylib.GetFPS()</c>).</param>
    /// <param name="frameTimeMs">Current frame time in milliseconds (from <c>Raylib.GetFrameTime() * 1000</c>).</param>
    public void Snapshot(ISimulationView view, int fps, float frameTimeMs)
    {
        Fps         = fps;
        FrameTimeMs = frameTimeMs;

        int total   = 0;
        int visible = 0;

        var query = view.Query().With<SimTransform>().Build();

        foreach (var entity in query)
        {
            total++;

            if (view.HasComponent<CullingState>(entity))
            {
                ref readonly var cs = ref view.GetComponentRO<CullingState>(entity);
                if (cs.IsVisible)
                    visible++;
            }
        }

        TotalEntityCount   = total;
        VisibleEntityCount = visible;
    }
}
