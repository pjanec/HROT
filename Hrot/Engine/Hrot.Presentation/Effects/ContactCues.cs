using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐⭐ <c>CE-4600</c> (B-3b; 📄 docs/DESIGN_Vehicle_Breaching.md §7.2 V-17, §7.15; mockup <c>docs/UX/vehicle-breaching.svg</c> A) — what each vehicle's contact with a barrier did lately,
/// for the driver's HUD cue under the crosshair: <i>BREAKING — hold throttle</i> with the push bar while a push is in progress, <i>TOO STRONG for this vehicle</i> at a wall it cannot
/// break, <i>BREACHED — a way in for infantry</i> after a break. Written by the map host's <c>EffectSpawnSystem</c> from every <see cref="ContactBreachEvent"/> (the same reader that
/// spawns the dust and queues the crash); read by the possession station. ⚠ Presentation memory only — no wire; held beside the world, as <see cref="HitFeedback"/>.
/// No allocation after the first contact of a vehicle (a fixed slot per vehicle, R-2084).
/// </summary>
public sealed class ContactCues
{
    public const string BreakingText = "BREAKING — hold throttle", TooStrongText = "TOO STRONG for this vehicle", BreachedText = "BREACHED — a way in for infantry";
    /// <summary>A push shows while its events keep coming (s) — the hull stops pushing ⇒ the cue goes.</summary>
    public const double PushHoldSeconds = 0.3;
    /// <summary>How long <i>TOO STRONG</i> and <i>BREACHED</i> stay (s).</summary>
    public const double ShowSeconds = 2.0;

    /// <summary>What the cue says: its text, its kind (the colour), the push's progress (0…1; only for <see cref="ContactBreachKind.Pushing"/>).</summary>
    public readonly record struct Cue(string Text, ContactBreachKind Kind, float Progress);

    private readonly Dictionary<Entity, (ContactBreachKind Kind, float Progress, double Time)> _last = new();

    /// <summary>A contact of <paramref name="vehicle"/> at sim time <paramref name="now"/>.</summary>
    public void Record(Entity vehicle, ContactBreachKind kind, float progress, double now) => _last[vehicle] = (kind, progress, now);

    /// <summary>The cue for <paramref name="vehicle"/> at <paramref name="now"/>, or null (nothing lately, or it ran out).</summary>
    public Cue? For(Entity vehicle, double now)
    {
        if (!_last.TryGetValue(vehicle, out var c) || now < c.Time) return null;
        double age = now - c.Time;
        return c.Kind switch
        {
            ContactBreachKind.Pushing when age <= PushHoldSeconds => new Cue(BreakingText, c.Kind, Math.Clamp(c.Progress, 0f, 1f)),
            ContactBreachKind.TooStrong when age <= ShowSeconds => new Cue(TooStrongText, c.Kind, 0f),
            ContactBreachKind.Broke or ContactBreachKind.Bumped when age <= ShowSeconds => new Cue(BreachedText, c.Kind, 0f),
            _ => null,
        };
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, ContactCues> ByWorld = new();

    /// <summary>The world's cues (null when no contact has been seen since the world was made).</summary>
    public static ContactCues? Of(EntityRepository world) => ByWorld.TryGetValue(world, out var c) ? c : null;

    /// <summary>The world's cues, made on first use.</summary>
    public static ContactCues In(EntityRepository world) => ByWorld.GetValue(world, _ => new ContactCues());
}
