using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-3257</c> (BR-5, Building Clearing CB-21) — which way a man leans. ⚠ Member names unique (one IDL scope).</summary>
    public enum LeanSide : byte
    {
        LeanNone = 0,
        /// <summary>Round a corner on his LEFT (his body frame's +y).</summary>
        LeanLeft = 1,
        /// <summary>Round a corner on his RIGHT.</summary>
        LeanRight = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-3257</c> (BR-5, CB-21, R-2032; docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md §13) — a man LEANING round a
    /// corner: written by his brain (<see cref="Leans.Set"/>), Brain group, replicated as <c>dtLean</c> (149). ⛔ Not a stance — he leans
    /// crouched too. Absent ⇒ <see cref="LeanSide.LeanNone"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Lean)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Lean
    {
        public LeanSide Side;
    }

    /// <summary>
    /// ⭐ <c>CE-3257</c> (§13 LN-2) — the one writer, the one reader, and the ONE geometric rule of a lean: a SHEAR about his feet across
    /// his own left axis — a point <c>h</c> above his feet moves <c>Shift · h / eye</c> sideways, so his eye moves exactly <see cref="Shift"/>
    /// and his feet stay at the corner. The eye (<c>TerrainWorldLosStrategy.EyeOf</c>), the points he is seen at (<c>BodyProfile</c>) and the
    /// boxes a round meets (<c>BodyPartRay.Person</c>) all read it, so "he sees round it", "he is seen round it" and "he is hit round it" agree.
    /// A prone man does not lean.
    /// </summary>
    public static class Leans
    {
        /// <summary>How far his eye moves sideways (m) — the frame's ≈ 0.4 m.</summary>
        public const float Shift = 0.4f;

        /// <summary>LN-6 — how far into the exposed half a shooter aims (m): about the middle of a half of a man's width.</summary>
        public const float ExposedHalf = 0.1f;

        /// <summary>Sets <paramref name="unit"/>'s lean; call it on the node that owns his brain (and so <c>dtLean</c>). LeanNone on a man who
        /// never leaned adds nothing. False when the type is not registered or the unit is gone.</summary>
        public static bool Set(EntityRepository repo, Entity unit, LeanSide side)
        {
            if (!repo.IsAlive(unit) || !repo.IsComponentTypeRegistered<Lean>()) return false;
            if (!repo.HasComponent<Lean>(unit))
            {
                if (side == LeanSide.LeanNone) return true;
                repo.AddComponent(unit, new Lean { Side = side });
                return true;
            }
            repo.GetComponentRW<Lean>(unit).Side = side;
            return true;
        }

        /// <summary>The unit's lean on this node (the owner's or the replicated one); LeanNone when it has none.</summary>
        public static LeanSide Of(ISimulationView view, Entity unit)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Lean>()) return LeanSide.LeanNone;
            return view.IsAlive(unit) && view.HasComponent<Lean>(unit) ? view.GetComponentRO<Lean>(unit).Side : LeanSide.LeanNone;
        }

        /// <summary>+1 leaning left, −1 right, 0 not leaning — or prone (a prone man does not lean).</summary>
        public static int SignOf(ISimulationView view, Entity unit, StanceId stance)
        {
            if (stance == StanceId.Prone) return 0;
            return Of(view, unit) switch { LeanSide.LeanLeft => 1, LeanSide.LeanRight => -1, _ => 0 };
        }

        /// <summary>
        /// LN-2 — how far (world) a point <paramref name="height"/> above <paramref name="unit"/>'s feet moves when he leans, his eye being
        /// <paramref name="eyeHeight"/> above them; zero when he does not lean.
        /// </summary>
        public static Vector3 Offset(ISimulationView view, Entity unit, StanceId stance, float height, float eyeHeight)
        {
            int sign = SignOf(view, unit, stance);
            if (sign == 0 || eyeHeight <= 0f || !view.HasComponent<SimTransform>(unit)) return Vector3.Zero;
            var left = Vector3.Transform(Vector3.UnitY, view.GetComponentRO<SimTransform>(unit).Rotation);
            return left * (sign * Shift * height / eyeHeight);
        }

        /// <summary>LN-6 — where a shooter aims on a leaning man at <paramref name="height"/>: the sheared centreline plus
        /// <see cref="ExposedHalf"/> into the side he leans to (the half he shows); zero when he does not lean.</summary>
        public static Vector3 AimOffset(ISimulationView view, Entity unit, StanceId stance, float height, float eyeHeight)
        {
            int sign = SignOf(view, unit, stance);
            if (sign == 0 || eyeHeight <= 0f || !view.HasComponent<SimTransform>(unit)) return Vector3.Zero;
            var left = Vector3.Transform(Vector3.UnitY, view.GetComponentRO<SimTransform>(unit).Rotation);
            return left * (sign * (Shift * height / eyeHeight + ExposedHalf));
        }

        /// <summary>
        /// LN-5 — the segment <paramref name="a"/>→<paramref name="b"/> as it meets a man leaning <paramref name="sign"/> (+1 left, −1 right):
        /// taken into his UPRIGHT frame (the shear undone, so his upright boxes can be tested) and clipped to the half he shows (body-frame
        /// y · sign ≥ 0). <paramref name="t0"/>/<paramref name="t1"/> are where the clipped segment lies on the original (a hit's t maps back as
        /// <c>t0 + t·(t1 − t0)</c>). False when the segment never enters that half.
        /// </summary>
        public static bool Upright(in SimTransform tf, int sign, float eyeHeight, ref Vector3 a, ref Vector3 b, out float t0, out float t1)
        {
            t0 = 0f; t1 = 1f;
            var toBody = Quaternion.Inverse(tf.Rotation);
            var la = Vector3.Transform(a - tf.Position, toBody);
            var lb = Vector3.Transform(b - tf.Position, toBody);
            float k = sign * Shift / eyeHeight;
            la.Y -= k * la.Z;
            lb.Y -= k * lb.Z;
            float ya = sign * la.Y, yb = sign * lb.Y;
            if (ya < 0f && yb < 0f) return false;
            if (ya < 0f) t0 = ya / (ya - yb);
            else if (yb < 0f) t1 = ya / (ya - yb);
            a = tf.Position + Vector3.Transform(la + (lb - la) * t0, tf.Rotation);
            b = tf.Position + Vector3.Transform(la + (lb - la) * t1, tf.Rotation);
            return t1 > t0;
        }
    }
}
