using Fdp.Core;
using Fdp.Toolkit.Combat.Components;

namespace Hrot.Look.People;

/// <summary>
/// ⭐ CE-11002 (RR-2) — moved from the map (<c>Hrot.UI.Common.Map3D.PostureBlend</c>) so a renderer eases a body exactly as the map does; each consumer
/// owns its own instance (its state is per entity, per consumer). ⭐ WL-6b (docs/DESIGN_Weapon_Layers.md §5h) — the picture's own timing of a weapon-posture change, on SIMULATION time (a pause
/// freezes it, every host shows the same): the rifle moves from the old posture to the new one over <see cref="Seconds"/>, and an aimed
/// upper body turns and pitches toward its aim with a <see cref="AimSeconds"/> time constant instead of jumping. The posture itself is
/// the weapon brain's (R-272), replicated; nothing here is sent — like <c>DeathFall</c>, a unit first seen (a load, a late host,
/// a replay jump) is simply in its posture, and a clock that goes back snaps.
/// </summary>
public sealed class PostureBlend
{
    /// <summary>How long the rifle takes to move between two postures (s, sim time).</summary>
    public const float Seconds = 0.3f;

    /// <summary>The aim angles' time constant (s, sim time): about 95 % of a turn is done in three of these.</summary>
    public const float AimSeconds = 0.12f;

    /// <summary>⭐ CE-1204 (§8l LK-5) — the drawn head's time constant (s, sim time): a glance turns the head, never snaps it.</summary>
    public const float HeadSeconds = 0.15f;

    private struct State
    {
        public WeaponPostureKind From, To;
        public double ChangedAt;
        public float Twist, Pitch;
        public double AimAt;
        public bool HasAim;
        public float Head;
        public double HeadAt;
        public bool HasHead;
    }

    private readonly Dictionary<Entity, State> _states = new();
    private readonly List<Entity> _gone = new();

    /// <summary>Where <paramref name="e"/>'s rifle is this frame: between <c>From</c> and <c>To</c>, <c>T</c> of the way (1 = there).</summary>
    public (WeaponPostureKind From, WeaponPostureKind To, float T) Posture(Entity e, WeaponPostureKind now, double simTime)
    {
        if (!_states.TryGetValue(e, out var st))
        {
            st = new State { From = now, To = now, ChangedAt = double.NegativeInfinity };
            _states[e] = st;
            return (now, now, 1f);
        }
        if (now != st.To)
        {
            // a change mid-blend starts from whichever end the rifle was nearer
            float done = Progress(st, simTime);
            st.From = done < 0.5f ? st.From : st.To;
            st.To = now;
            st.ChangedAt = simTime;
            _states[e] = st;
        }
        return (st.From, st.To, Progress(st, simTime));
    }

    private static float Progress(in State st, double simTime)
        => double.IsNegativeInfinity(st.ChangedAt) || simTime < st.ChangedAt ? 1f
         : (float)Math.Clamp((simTime - st.ChangedAt) / Seconds, 0.0, 1.0);

    /// <summary>The aim angles this frame, eased toward <paramref name="twist"/> / <paramref name="pitch"/> (0 when not aimed, so the
    /// body turns back). Must follow <see cref="Posture"/> for the same entity in the frame.</summary>
    public (float Twist, float Pitch) Aim(Entity e, float twist, float pitch, double simTime)
    {
        if (!_states.TryGetValue(e, out var st)) st = new State { From = WeaponPostureKind.AtEase, To = WeaponPostureKind.AtEase, ChangedAt = double.NegativeInfinity };
        double dt = simTime - st.AimAt;
        if (!st.HasAim || dt < 0 || dt > 1.0)
        {
            st.Twist = twist; st.Pitch = pitch;   // first seen, a clock jump, or a long gap: no easing
        }
        else if (dt > 0)
        {
            float k = 1f - MathF.Exp(-(float)dt / AimSeconds);
            st.Twist += (twist - st.Twist) * k;
            st.Pitch += (pitch - st.Pitch) * k;
        }
        st.AimAt = simTime;
        st.HasAim = true;
        _states[e] = st;
        return (st.Twist, st.Pitch);
    }

    /// <summary>⭐ CE-1204 (§8l LK-5) — the head's turn off the torso this frame, eased toward <paramref name="turn"/> like the aim.</summary>
    public float Head(Entity e, float turn, double simTime)
    {
        if (!_states.TryGetValue(e, out var st)) st = new State { From = WeaponPostureKind.AtEase, To = WeaponPostureKind.AtEase, ChangedAt = double.NegativeInfinity };
        double dt = simTime - st.HeadAt;
        if (!st.HasHead || dt < 0 || dt > 1.0) st.Head = turn;   // first seen, a clock jump, or a long gap: no easing
        else if (dt > 0) st.Head += (turn - st.Head) * (1f - MathF.Exp(-(float)dt / HeadSeconds));
        st.HeadAt = simTime;
        st.HasHead = true;
        _states[e] = st;
        return st.Head;
    }

    /// <summary>Forgets everyone not drawn this frame.</summary>
    public void EndFrame(HashSet<Entity> drawn)
    {
        _gone.Clear();
        foreach (var e in _states.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _gone) _states.Remove(e);
    }
}
