using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3117</c> — the <b>hearing</b> debug layer (📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a). Two halves, both from recorded
/// components, for EVERY entity (one global gizmo walks both halves; it needs the emitters and the listeners together):
/// <list type="bullet">
///   <item>every <see cref="AcousticEmitter"/>: a shot or a detonation sends ONE ring expanding from where it happened to its audible
///     range over the <see cref="SoundEmissionSystem.SoundLingerSeconds"/> it lasts; a moving entity sends a ring every
///     <see cref="MovingRingPeriod"/> s sized to its current (speed-scaled) range. ⚠ The growth is a picture, not the speed of
///     sound — a sound is heard in the tick it is made.</item>
///   <item>every listener's <see cref="HeardTraces"/> younger than <see cref="Shown"/> s: a dashed line to the estimate and its
///     uncertainty circle, coloured by what it sounded like. ⛔ No "true source" line: the answer carries no source (R-207) — the
///     rings show where the sound really was.</item>
/// </list>
/// Toggled by the <c>Hearing</c> bit of the layer control.
/// </summary>
[GizmoProjector]
public sealed class HearingGizmo : IGlobalStatelessGizmo
{
    public const double Shown = 1.0;
    public const double MovingRingPeriod = 1.0;

    private static readonly Rgba32 MovingColor     = new(90, 160, 255, 200);
    private static readonly Rgba32 ShotColor       = new(255, 200, 40, 230);
    private static readonly Rgba32 DetonationColor = new(255, 80, 40, 230);

    private EntityRepository? _queryRepo;
    private EntityQuery? _emitters;
    private EntityQuery? _listeners;

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo) return;
        bool emitters = repo.IsComponentTypeRegistered<AcousticEmitter>();
        bool listeners = repo.IsComponentTypeRegistered<HeardTraces>();
        if (!emitters && !listeners) return;
        if (!ReferenceEquals(repo, _queryRepo))
        {
            _queryRepo = repo;
            _emitters = emitters ? repo.Query().With<AcousticEmitter>().With<SimTransform>().Build() : null;
            _listeners = listeners ? repo.Query().With<HeardTraces>().With<SimTransform>().Build() : null;
        }
        double now = DebugTraceClock.Now(repo);

        if (_emitters != null)
            foreach (var e in _emitters)
            {
                ref readonly var a = ref repo.GetComponentRO<AcousticEmitter>(e);
                if (a.CurrentMovingRange > 0f)
                {
                    float phase = (float)((now / MovingRingPeriod + e.Index * 0.37) % 1.0);
                    var p = repo.GetComponentRO<SimTransform>(e).Position;
                    Ring(draw, p, a.CurrentMovingRange * phase, MovingColor, 1f - phase);
                }
                if (a.ShotTimeLeft > 0f)
                {
                    float f = 1f - a.ShotTimeLeft / SoundEmissionSystem.SoundLingerSeconds;
                    Ring(draw, new Vector3(a.ShotX, a.ShotY, a.ShotZ), a.FiringAudibleRange * f, ShotColor, 1f - f * 0.7f);
                }
                if (a.DetonationTimeLeft > 0f)
                {
                    float f = 1f - a.DetonationTimeLeft / SoundEmissionSystem.SoundLingerSeconds;
                    Ring(draw, new Vector3(a.DetonationX, a.DetonationY, a.DetonationZ), a.DetonationAudibleRange * f, DetonationColor,
                        1f - f * 0.7f);
                }
            }

        if (_listeners != null)
            foreach (var e in _listeners)
            {
                var ear = repo.GetComponentRO<SimTransform>(e).Position;
                var traces = repo.GetComponentRO<HeardTraces>(e);
                var slots = traces.SlotsRO();
                for (int i = 0; i < traces.Count; i++)
                {
                    ref readonly var h = ref slots[i];
                    float fade = DebugTraceClock.Fade(now - h.Time, Shown);
                    if (fade < 0f) continue;
                    var c = DetonationGizmo.WithAlpha(ClassColor(h.SourceClass), fade);
                    draw.DrawLine(ear, h.At, c, 1.2f, layer: DebugTraceLayers.Hearing, style: LineStyle.Dashed);
                    draw.DrawSphere(h.At, MathF.Max(h.Radius, 0.5f), c, thickness: 1.2f, layer: DebugTraceLayers.Hearing);
                }
            }
    }

    private static void Ring(IDebugDrawBuilder draw, Vector3 at, float radius, Rgba32 color, float alpha)
    {
        if (radius <= 0.05f) return;
        draw.DrawSphere(at, radius, DetonationGizmo.WithAlpha(color, alpha), thickness: 1.5f, layer: DebugTraceLayers.Hearing);
    }

    /// <summary>The colour of a heard <see cref="SoundSourceClass"/>.</summary>
    public static Rgba32 ClassColor(byte sourceClass) => (SoundSourceClass)sourceClass switch
    {
        SoundSourceClass.Footsteps     => new Rgba32(90, 160, 255, 255),
        SoundSourceClass.WheeledEngine => new Rgba32(60, 200, 200, 255),
        SoundSourceClass.TrackedEngine => new Rgba32(150, 90, 220, 255),
        SoundSourceClass.SmallArms     => new Rgba32(255, 200, 40, 255),
        SoundSourceClass.HeavyWeapon   => new Rgba32(255, 120, 20, 255),
        SoundSourceClass.Explosion     => new Rgba32(255, 60, 40, 255),
        _                              => new Rgba32(180, 180, 180, 255),
    };
}
