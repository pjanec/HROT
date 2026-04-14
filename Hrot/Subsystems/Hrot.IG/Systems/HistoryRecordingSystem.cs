using Hrot.IG.Components;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.IG.Systems;

/// <summary>
/// Simulation-phase system that samples an entity's world-space XY position
/// at configurable intervals and stores them in a <see cref="HistoryTrail"/>
/// circular buffer.
///
/// Sampling rules:
/// <list type="bullet">
///   <item>Only entities with <see cref="ResolvedStyle.ShowTrail"/> = <c>true</c> are sampled.</item>
///   <item>
///     A sample is only appended when <see cref="HistoryTrail.ElapsedSinceSample"/> ≥
///     <see cref="HistoryTrail.SampleInterval"/>.  Sub-frame timing is preserved by
///     subtracting (not clearing) the interval from the accumulator.
///   </item>
///   <item>
///     When <see cref="HistoryTrail.Count"/> reaches
///     <see cref="HistoryTrailConstants.MaxTrailPoints"/> the circular buffer
///     silently overwrites the oldest sample — the buffer never grows beyond its
///     compile-time limit (§CODE-STANDARDS §4, §5).
///   </item>
/// </list>
///
/// Zero allocations in <see cref="Execute"/> (§CODE-STANDARDS §4).
/// All size and default constants come from <see cref="HistoryTrailConstants"/>
/// (§CODE-STANDARDS §1).
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
public class HistoryRecordingSystem : IEcsModuleSystem
{
    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();

        var query = view.Query()
            .With<SimTransform>()
            .With<ResolvedStyle>()
            .With<HistoryTrail>()
            .Build();

        foreach (var entity in query)
        {
            ref readonly var style = ref view.GetComponentRO<ResolvedStyle>(entity);

            // Only track entities that opted into trail rendering.
            if (!style.ShowTrail)
                continue;

            ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var trail     = ref view.GetComponentRO<HistoryTrail>(entity);

            // Copy the struct so we can mutate before writing back via cmd.
            var updated = trail;
            updated.ElapsedSinceSample += deltaTime;

            if (updated.ElapsedSinceSample >= updated.SampleInterval)
            {
                // Preserve sub-frame timing by keeping the remainder.
                updated.ElapsedSinceSample -= updated.SampleInterval;
                updated.AddPoint(transform.Position.X, transform.Position.Y);
            }

            cmd.SetComponent(entity, updated);
        }
    }
}
