using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐ Airborne insertion (docs/designs/airborne/DESIGN_Airborne_Insertion.md §5) — what both descent models share: the surface under a
    /// man coming down (the ground or a roof), where his carrier's rope or door is, and the phase clock.
    /// </summary>
    public static class DescentRules
    {
        public const float Gravity = 9.81f;

        /// <summary>A rope at each side door when the carrier states none (its frame: left / right of the reference point, 0.5 m up).</summary>
        public static Vector3 DefaultAnchor(int index) => new(0f, index % 2 == 0 ? 1.2f : -1.2f, 0.5f);

        /// <summary>The sim time (s).</summary>
        public static double Now(EntityRepository world) => HitModel.Now(world);

        /// <summary>The highest surface (ground, roof, floor) at (x, y) at or below <paramref name="z"/> — where a man coming down lands.</summary>
        public static float SurfaceBelow(in MotionFrame f, float x, float y, float z)
        {
            if (f.Terrain is not { } t) return 0f;
            var surfaces = t.SurfacesAt(x, y, out int ground);
            float best = surfaces.Count > 0 && ground >= 0 && ground < surfaces.Count ? surfaces[ground] : t.GroundHeightAt(x, y);
            foreach (var s in surfaces) if (s <= z + 0.05f && s > best) best = s;
            return best;
        }

        /// <summary>Enters <paramref name="phase"/> now.</summary>
        public static void SetPhase(ref Descent d, DescentPhase phase, double now)
        {
            if (d.Phase == phase) return;
            d.Phase = phase;
            d.PhaseSince = now;
        }

        /// <summary>The world point of <paramref name="d"/>'s rope or door, and the carrier's velocity; false when the carrier is gone or dead.</summary>
        public static bool TryAnchor(EntityRepository world, in Descent d, out Vector3 point, out Vector3 carrierVelocity, out Vector3 carrierAt)
        {
            point = carrierVelocity = carrierAt = default;
            var c = d.Anchor;
            if (c.IsNull || !world.IsAlive(c) || !world.HasComponent<SimTransform>(c)) return false;
            if (world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && world.HasComponent<Fdp.Toolkit.Combat.Components.Health>(c)
                && world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(c).Current <= 0f) return false;
            ref readonly var tf = ref world.GetComponentRO<SimTransform>(c);
            var place = DefaultAnchor(d.AnchorIndex);
            if (CombatTkb.TryGetTemplate(world, c, out var t) && t.GetDescriptor<CarrierDefDto>() is { RopeAnchors.Count: > 0 } carrier)
            {
                var a = carrier.RopeAnchors[d.AnchorIndex % carrier.RopeAnchors.Count];
                place = new Vector3(a.X, a.Y, a.Z);
            }
            carrierAt = tf.Position;
            point = tf.Position + Vector3.Transform(place, tf.Rotation);
            if (world.HasComponent<SimVelocity>(c)) carrierVelocity = world.GetComponentRO<SimVelocity>(c).Linear;
            return true;
        }

        /// <summary>A carrier's rope when its type states none (m) — 60 ft, the shortest standard fast rope (design §2).</summary>
        public const float DefaultRopeLengthM = 18f;

        /// <summary>The carrier's rope length (m; <see cref="DefaultRopeLengthM"/> when its type states none).</summary>
        public static float RopeLength(EntityRepository world, Entity carrier)
            => CombatTkb.TryGetTemplate(world, carrier, out var t) && t.GetDescriptor<CarrierDefDto>() is { RopeLengthM: > 0f } c
                ? c.RopeLengthM : DefaultRopeLengthM;

        /// <summary>
        /// ⭐ B-6 (§5.3) — a man hits the ground at <paramref name="speed"/>: above <see cref="FallKillMps"/> all of his health, above
        /// <see cref="FallSafeMps"/> <see cref="FallIncapacitateShare"/> of it, through the existing chain — a whole-body
        /// <c>DamageAssessedEvent</c> (no body PART: a zone's multiplier would bend the thresholds), applied by his Health owner (on the
        /// wire when that is another node). A canopy's landing (≤ 5.5 m/s) never hurts.
        /// </summary>
        public static void Impact(EntityRepository world, Entity man, float speed)
        {
            if (speed <= FallSafeMps || !world.Bus.IsRegistered<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>()) return;
            float max = world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && world.HasComponent<Fdp.Toolkit.Combat.Components.Health>(man)
                ? world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(man).Max : 0f;
            if (max <= 0f) return;
            world.Bus.Publish(new Fdp.Toolkit.Combat.Events.DamageAssessedEvent
                { HitEntity = man, TotalDamage = speed > FallKillMps ? max : FallIncapacitateShare * max });
        }

        /// <summary>⭐ B-4 — the world point of <paramref name="carrier"/>'s rope or door <paramref name="index"/> (its <c>RopeAnchors</c>,
        /// else <see cref="DefaultAnchor"/>); false when it has no transform.</summary>
        public static bool DoorPoint(EntityRepository world, Entity carrier, int index, out Vector3 point)
            => TryAnchor(world, new Descent { Anchor = carrier, AnchorIndex = (byte)index }, out point, out _, out _)
               || TryPlace(world, carrier, index, out point);

        private static bool TryPlace(EntityRepository world, Entity carrier, int index, out Vector3 point)
        {
            point = default;
            if (carrier.IsNull || !world.IsAlive(carrier) || !world.HasComponent<SimTransform>(carrier)) return false;
            ref readonly var tf = ref world.GetComponentRO<SimTransform>(carrier);
            point = tf.Position + Vector3.Transform(DefaultAnchor(index), tf.Rotation);
            return true;
        }

        /// <summary>The highest surface at (x, y) — the roof a rope hold hovers over (the ground where nothing stands).</summary>
        public static float HighestSurface(Fdp.Toolkit.World.IWorldQuery t, float x, float y)
        {
            float best = t.GroundHeightAt(x, y);
            foreach (var s in t.SurfacesAt(x, y, out _)) if (s > best) best = s;
            return best;
        }

        /// <summary>⭐ B-6 (§5.3) — a fall's landing speed (m/s) up to which he walks away; above <see cref="FallKillMps"/> it kills; between,
        /// it incapacitates him (<see cref="FallIncapacitateShare"/> of his health — from full, under the injury's 0.3).</summary>
        public const float FallSafeMps = 6f, FallKillMps = 10f, FallIncapacitateShare = 0.75f;

        /// <summary>A man who lost his anchor (or never opened): gravity; true once he is down on <paramref name="surface"/> — and then
        /// ⭐ B-6 (§5.3) he is hurt by the speed he hit at (<see cref="Impact"/>).</summary>
        public static bool Fall(ref MotionFrame f, float surface, float dt)
        {
            var v = f.Velocity.Linear;
            v = new Vector3(v.X * MathF.Max(0f, 1f - 0.3f * dt), v.Y * MathF.Max(0f, 1f - 0.3f * dt), v.Z - Gravity * dt);
            var p = f.Transform.Position + v * dt;
            if (p.Z <= surface)
            {
                Impact(f.World, f.Self, v.Length());
                p.Z = surface; f.Velocity.Linear = Vector3.Zero; f.Transform.Position = p; return true;
            }
            f.Velocity.Linear = v;
            f.Transform.Position = p;
            return false;
        }
    }

    /// <summary>
    /// ⭐ Airborne insertion B-5 (design §6, the "release point") — where a jumper must LEAVE the aircraft for his canopy to come down on
    /// <c>point</c>: upwind of it by the drift during his descent, and short of it by the throw from the door (the aircraft's speed decaying
    /// to the wind's over the opening — <see cref="ParachuteModel"/>'s Deploying phase, a time constant of a third of <c>OpenSeconds</c>).
    /// A round canopy (static line) drifts the whole way down, so this IS its aim; a ram-air canopy flies to the point from anywhere within
    /// its glide, so for it this is only where the drift of the fall before the pull is undone. Pure arithmetic: the Brain's drills call it
    /// (the jumpmaster, H-1).
    /// </summary>
    public static class ReleasePoint
    {
        /// <summary>The release point (x, y) for a jump <paramref name="heightAgl"/> above <paramref name="point"/>'s ground.</summary>
        public static Vector2 For(Vector2 point, float heightAgl, ParachuteDto chute, Vector2 wind, Vector2 carrierVelocity)
        {
            float tau = MathF.Max(chute.OpenSeconds, 0.5f) / 3f;
            var throw_ = (carrierVelocity - wind) * tau;
            if (chute.Canopy == CanopyKind.RamAir)
            {
                // the free fall to the pull height drifts with the wind (he tracks toward the point on top of that); the canopy does the rest
                float fall = MathF.Max(0f, heightAgl - chute.PullHeightM) / ParachuteModel.FreeFallMps;
                return point - wind * fall - throw_;
            }
            float underCanopy = heightAgl / MathF.Max(chute.SinkMps, 0.5f);
            return point - wind * underCanopy - throw_;
        }
    }

    /// <summary>
    /// ⭐ Airborne insertion B-4 (§1) — a descent BEGINS: <paramref name="man"/> has left <paramref name="carrier"/> by its rope or door.
    /// Adds his <see cref="Descent"/> in phase <see cref="DescentPhase.Exit"/> (the model puts him at the door); a jump is a free fall under
    /// a ram-air canopy, a static line under a round one — by his type's <c>Gen.Parachute</c>. ⛔ Only on the node owning his position
    /// (the caller's gate): the Descent is Muscle state, published from there.
    /// </summary>
    public static class Descents
    {
        public static bool Start(EntityRepository world, Entity man, Entity carrier, ExitKind exit, byte anchorIndex)
        {
            if (exit == ExitKind.StepOut || !world.IsAlive(man) || !world.IsComponentTypeRegistered<Descent>()) return false;
            var d = new Descent { Anchor = carrier, AnchorIndex = anchorIndex, Phase = DescentPhase.Exit, PhaseSince = DescentRules.Now(world) };
            if (exit == ExitKind.RopeDown) d.Kind = DescentKind.FastRope;
            else
            {
                d.Canopy = ParachuteModel.Of(world, man, CanopyKind.NoCanopy).Canopy;
                d.Kind = d.Canopy == CanopyKind.RamAir ? DescentKind.FreeFall : DescentKind.StaticLine;
            }
            if (world.HasComponent<Descent>(man)) world.GetComponentRW<Descent>(man) = d;
            else world.AddComponent(man, d);
            if (DescentRules.DoorPoint(world, carrier, anchorIndex, out var door) && world.HasComponent<SimTransform>(man))
                world.GetComponentRW<SimTransform>(man).Position = door;
            return true;
        }
    }

    /// <summary>
    /// ⭐⭐ Airborne insertion B-2 (§5.1) — a man down a FAST ROPE: under his carrier's rope anchor (x, y follow the helicopter), sliding at
    /// <see cref="SlideMps"/> (eased in over <see cref="EaseSeconds"/>), braking to <see cref="BrakeMps"/> below <see cref="BrakeHeightM"/>,
    /// landed on the surface under the anchor — the ground or a roof. The anchor dead ⇒ he falls. He cannot steer: the manual axes are ignored.
    /// </summary>
    public sealed class RopeSlideModel : IMotionModel
    {
        /// <summary>The slide (design §2: 3–5 m/s), the touch-down speed the brake ends at, and the height the brake begins over the surface:
        /// the speed ramps down linearly with the height left, so a 12 m rope takes ~3.5 s (B-2's acceptance).</summary>
        public const float SlideMps = 5f, BrakeMps = 1f, BrakeHeightM = 3f, EaseSeconds = 0.3f, GripSeconds = 0.3f;

        public LocomotionClass Class => LocomotionClass.Rope;

        public void Manual(ref MotionFrame f, in ManualDrive drive, float dt) => Autonomous(ref f, default, dt);

        public void Autonomous(ref MotionFrame f, in NavigationIntent intent, float dt)
        {
            var world = f.World;
            ref var d = ref world.GetComponentRW<Descent>(f.Self);
            double now = DescentRules.Now(world);
            bool anchored = DescentRules.TryAnchor(world, in d, out var anchor, out _, out var carrierAt);
            var pos = f.Transform.Position;

            if (d.Phase is DescentPhase.Landed) { f.Velocity.Linear = Vector3.Zero; return; }
            if (d.Phase is DescentPhase.Falling || !anchored)
            {
                DescentRules.SetPhase(ref d, DescentPhase.Falling, now);
                if (DescentRules.Fall(ref f, DescentRules.SurfaceBelow(in f, pos.X, pos.Y, pos.Z), dt)) DescentRules.SetPhase(ref d, DescentPhase.Landed, now);
                return;
            }

            // facing the helicopter, at the rope
            var toCarrier = new Vector2(carrierAt.X - anchor.X, carrierAt.Y - anchor.Y);
            float yaw = toCarrier.LengthSquared() > 1e-4f ? MathF.Atan2(toCarrier.Y, toCarrier.X) : RotorcraftModel.YawOf(f.Transform.Rotation);
            f.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);

            switch (d.Phase)
            {
                case DescentPhase.Exit:
                    f.Transform.Position = anchor;
                    f.Velocity.Linear = Vector3.Zero;
                    DescentRules.SetPhase(ref d, DescentPhase.Grip, now);
                    return;
                case DescentPhase.Grip:
                    f.Transform.Position = anchor;
                    f.Velocity.Linear = Vector3.Zero;
                    if (now - d.PhaseSince >= GripSeconds) DescentRules.SetPhase(ref d, DescentPhase.Slide, now);
                    return;
            }

            float surface = DescentRules.SurfaceBelow(in f, anchor.X, anchor.Y, MathF.Min(pos.Z, anchor.Z));
            float rope = DescentRules.RopeLength(world, d.Anchor);
            if (rope > 0f && pos.Z < anchor.Z - rope - 0.05f && pos.Z - surface > 0.5f)
            {
                DescentRules.SetPhase(ref d, DescentPhase.Falling, now);   // off the rope's end
                return;
            }
            if (pos.Z - surface <= BrakeHeightM) DescentRules.SetPhase(ref d, DescentPhase.Brake, now);
            float v = d.Phase == DescentPhase.Brake
                ? BrakeMps + (SlideMps - BrakeMps) * Math.Clamp((pos.Z - surface) / BrakeHeightM, 0f, 1f)
                : SlideMps * MathF.Min(1f, (float)(now - d.PhaseSince) / EaseSeconds + 0.1f);
            float z = MathF.Max(surface, pos.Z - v * dt);
            var next = new Vector3(anchor.X, anchor.Y, z);
            f.Velocity.Linear = (next - pos) / MathF.Max(dt, 1e-4f);
            f.Transform.Position = next;
            if (z <= surface + 1e-3f) { f.Velocity.Linear = Vector3.Zero; DescentRules.SetPhase(ref d, DescentPhase.Landed, now); }
        }
    }

    /// <summary>
    /// ⭐⭐ Airborne insertion B-2 (§5.2) — a man under a PARACHUTE: out of the door (the carrier's speed), a free fall to his pull height
    /// (free fall) or straight into the opening (static line), then the canopy. A ram-air canopy is FLOWN to his landing point (the
    /// <see cref="Descent.Target"/>, else his intent's): a spiral while too high, then a final into the point trimmed with the brakes, a
    /// flare. A round canopy drifts with the wind, turned into it and slipping toward the point. The bank IS the body's roll (D-5).
    /// </summary>
    public sealed class ParachuteModel : IMotionModel
    {
        /// <summary>A free fall's terminal speed (belly), m/s; how fast he tracks across while falling.</summary>
        public const float FreeFallMps = 55f, TrackMps = 15f;
        /// <summary>The spiral: the turn it is flown at, and how far from the point it may drift before turning back.</summary>
        public const float SpiralTurn = 0.7f, SpiralRadiusM = 60f;
        /// <summary>A round canopy's slip toward the point (m/s); the height it starts its landing roll (m).</summary>
        public const float RoundSlipMps = 1.5f, RoundLandingM = 1f;
        public const float BankLagSeconds = 0.5f, FlareSeconds = 1f;

        public LocomotionClass Class => LocomotionClass.Parachute;

        /// <summary>The figures a canopy flies by when the jumper's <c>Gen.Parachute</c> leaves a field 0 (§2, public figures, rounded).</summary>
        public static ParachuteDto Defaults(CanopyKind canopy) => canopy == CanopyKind.RamAir
            ? new ParachuteDto { Canopy = CanopyKind.RamAir, SinkMps = 4.5f, ForwardMps = 11f, TurnRateDeg = 60f, MaxBankDeg = 45f, BrakeSinkFactor = 0.5f,
                                 FlareHeightM = 4f, OpenSeconds = 3f, PullHeightM = 1100f, LandingSeconds = 10f }
            : new ParachuteDto { Canopy = CanopyKind.Round, SinkMps = 5.5f, ForwardMps = 0f, TurnRateDeg = 30f, MaxBankDeg = 0f, BrakeSinkFactor = 1f,
                                 FlareHeightM = 0f, OpenSeconds = 4f, PullHeightM = 0f, LandingSeconds = 40f };

        /// <summary>The jumper's parachute: his type's <c>Gen.Parachute</c>, its unstated fields from <see cref="Defaults"/>.</summary>
        public static ParachuteDto Of(EntityRepository world, Entity jumper, CanopyKind canopy)
        {
            var own = CombatTkb.TryGetTemplate(world, jumper, out var t) ? t.GetDescriptor<ParachuteDto>() : null;
            var kind = canopy != CanopyKind.NoCanopy ? canopy : own?.Canopy ?? CanopyKind.Round;
            var d = Defaults(kind);
            if (own == null) return d;
            static float Or(float v, float dflt) => v > 0f ? v : dflt;
            return d with
            {
                SinkMps = Or(own.SinkMps, d.SinkMps), ForwardMps = Or(own.ForwardMps, d.ForwardMps), TurnRateDeg = Or(own.TurnRateDeg, d.TurnRateDeg),
                MaxBankDeg = Or(own.MaxBankDeg, d.MaxBankDeg), BrakeSinkFactor = Or(own.BrakeSinkFactor, d.BrakeSinkFactor),
                FlareHeightM = Or(own.FlareHeightM, d.FlareHeightM), OpenSeconds = Or(own.OpenSeconds, d.OpenSeconds),
                PullHeightM = Or(own.PullHeightM, d.PullHeightM), LandingSeconds = Or(own.LandingSeconds, d.LandingSeconds),
            };
        }

        public void Manual(ref MotionFrame f, in ManualDrive drive, float dt) => Autonomous(ref f, default, dt);

        public void Autonomous(ref MotionFrame f, in NavigationIntent intent, float dt)
        {
            var world = f.World;
            ref var d = ref world.GetComponentRW<Descent>(f.Self);
            double now = DescentRules.Now(world);
            var p = Of(world, f.Self, d.Canopy);
            var wind = Wind.Of(world);
            var pos = f.Transform.Position;
            float surface = DescentRules.SurfaceBelow(in f, pos.X, pos.Y, pos.Z);
            var target = d.Target != Vector3.Zero ? d.Target : intent.Mode != NavigationMode.None ? intent.FinalDestination : Vector3.Zero;
            bool hasTarget = d.Target != Vector3.Zero || intent.Mode != NavigationMode.None;

            switch (d.Phase)
            {
                case DescentPhase.Landed:
                    f.Velocity.Linear = Vector3.Zero;
                    return;
                case DescentPhase.Falling:
                    if (DescentRules.Fall(ref f, surface, dt)) DescentRules.SetPhase(ref d, DescentPhase.Landed, now);
                    return;
                case DescentPhase.Exit:
                {
                    // out of the door at the carrier's speed; the free faller falls, the static line opens at once
                    if (DescentRules.TryAnchor(world, in d, out var door, out var carrierV, out _))
                    {
                        f.Transform.Position = door;
                        f.Velocity.Linear = carrierV;
                    }
                    DescentRules.SetPhase(ref d, d.Kind == DescentKind.FreeFall ? DescentPhase.FreeFalling : DescentPhase.Deploying, now);
                    return;
                }
                case DescentPhase.FreeFalling:
                {
                    var v = f.Velocity.Linear;
                    float vz = v.Z - Gravity(v.Z) * dt;
                    var track = Vector2.Zero;
                    if (hasTarget)
                    {
                        var to = new Vector2(target.X - pos.X, target.Y - pos.Y);
                        if (to.LengthSquared() > 1f) track = Vector2.Normalize(to) * MathF.Min(TrackMps, to.Length() / 4f);
                    }
                    var h = Approach(new Vector2(v.X, v.Y), track + wind, 3f * dt);
                    Step(ref f, new Vector3(h, vz), dt, surface, ref d, now);
                    if (pos.Z - surface <= p.PullHeightM) DescentRules.SetPhase(ref d, DescentPhase.Deploying, now);
                    return;
                }
                case DescentPhase.Deploying:
                {
                    // the canopy opens over OpenSeconds: the fall slows to the canopy's sink, the throw from the door decays
                    var v = f.Velocity.Linear;
                    float rate = 1f / MathF.Max(p.OpenSeconds, 0.5f);
                    float k = MathF.Min(1f, 3f * rate * dt);
                    float vz = v.Z < -p.SinkMps ? v.Z + (-p.SinkMps - v.Z) * k : v.Z - Gravity(v.Z) * dt;
                    float yaw = RotorcraftModel.YawOf(f.Transform.Rotation);
                    var air = p.Canopy == CanopyKind.RamAir ? new Vector2(MathF.Cos(yaw), MathF.Sin(yaw)) * p.ForwardMps : Vector2.Zero;
                    var h = new Vector2(v.X, v.Y) + (air + wind - new Vector2(v.X, v.Y)) * k;
                    Step(ref f, new Vector3(h, vz), dt, surface, ref d, now);
                    if (now - d.PhaseSince >= p.OpenSeconds && d.Phase == DescentPhase.Deploying) DescentRules.SetPhase(ref d, DescentPhase.Canopy, now);
                    return;
                }
            }

            // ── under the canopy (Canopy · Flare · Landing) ──
            float height = pos.Z - surface;
            float psi = RotorcraftModel.YawOf(f.Transform.Rotation);
            var (_, rollNow) = RotorcraftModel.TiltOf(f.Transform.Rotation);
            float u, b;
            Vector2 slip = Vector2.Zero;
            if (p.Canopy == CanopyKind.RamAir)
            {
                if (height <= p.FlareHeightM) DescentRules.SetPhase(ref d, DescentPhase.Flare, now);
                (u, b) = d.Phase == DescentPhase.Flare
                    ? (0f, MathF.Min(1f, (float)(now - d.PhaseSince) / FlareSeconds))
                    : Guide(pos, psi, target, hasTarget, height, wind, p);
            }
            else
            {
                if (height <= RoundLandingM) DescentRules.SetPhase(ref d, DescentPhase.Landing, now);
                // a round canopy: face into the wind, slip toward the point
                float into = wind.LengthSquared() > 0.01f ? MathF.Atan2(-wind.Y, -wind.X) : psi;
                u = Math.Clamp(MathF.IEEERemainder(into - psi, 2f * MathF.PI) * 1.5f, -1f, 1f);
                b = 0f;
                if (hasTarget)
                {
                    var to = new Vector2(target.X - pos.X, target.Y - pos.Y);
                    if (to.LengthSquared() > 0.25f) slip = Vector2.Normalize(to) * MathF.Min(RoundSlipMps, to.Length());
                }
            }

            float turnRate = p.TurnRateDeg * MathF.PI / 180f;
            float psiDot = u * turnRate;
            psi += psiDot * dt;
            bool flaring = d.Phase == DescentPhase.Flare;
            float speed = p.ForwardMps * (1f - (flaring ? 0.8f : 0.6f) * b);
            float maxBank = p.MaxBankDeg * MathF.PI / 180f;
            float rollTarget = Math.Clamp(-MathF.Atan(speed * psiDot / DescentRules.Gravity), -maxBank, maxBank);
            float roll = rollNow + (rollTarget - rollNow) * MathF.Min(1f, dt / BankLagSeconds);
            float cos = MathF.Max(0.2f, MathF.Cos(roll));
            float sink = p.SinkMps / MathF.Pow(cos, 1.5f) * (1f - (1f - p.BrakeSinkFactor) * b);
            if (flaring) sink = sink + (1f - sink) * b;   // the flare: ≈ 1 m/s at full brakes
            var ground = new Vector2(MathF.Cos(psi), MathF.Sin(psi)) * speed + wind + slip;
            f.Transform.Rotation = BodyGeometry.Rotation(psi, new RestingPose(0f, 0f, roll));
            Step(ref f, new Vector3(ground, -sink), dt, surface, ref d, now);
        }

        /// <summary>The ram-air guidance (§5.2): the turn <c>u</c> ∈ [−1, 1] and the brakes <c>b</c> ∈ [0, 1] toward <paramref name="target"/>.</summary>
        internal static (float U, float B) Guide(Vector3 pos, float psi, Vector3 target, bool hasTarget, float height, Vector2 wind, ParachuteDto p)
        {
            if (!hasTarget)
            {
                float into = wind.LengthSquared() > 0.01f ? MathF.Atan2(-wind.Y, -wind.X) : psi;
                return (Steer(into, psi), 0f);
            }
            var to = new Vector2(target.X - pos.X, target.Y - pos.Y);
            float dist = to.Length();
            float toward = dist > 0.1f ? MathF.Atan2(to.Y, to.X) : psi;
            float hP = MathF.Max(0f, height);
            // the glide each brake setting gives along the track to the point (crabbed against the wind)
            float Reach(float b, out float heading)
            {
                float v = p.ForwardMps * (1f - 0.6f * b), w = p.SinkMps * (1f - (1f - p.BrakeSinkFactor) * b);
                heading = toward;
                if (dist < 0.1f) return 0f;
                var unit = to / dist;
                var perp = new Vector2(-unit.Y, unit.X);
                float wa = Vector2.Dot(wind, unit), wc = Vector2.Dot(wind, perp);
                if (MathF.Abs(wc) >= v) return MathF.Max(0f, wa) * hP / w;
                float along = MathF.Sqrt(v * v - wc * wc);
                var air = unit * along - perp * wc;
                heading = MathF.Atan2(air.Y, air.X);
                return MathF.Max(0f, along + wa) * hP / w;
            }
            float far = Reach(0f, out float crab0), near = Reach(1f, out _);
            if (near > dist + 10f)
            {
                // too high: get over the point, then spiral down over it
                return dist > SpiralRadiusM ? (Steer(crab0, psi), 0f) : (SpiralTurn, 0f);
            }
            if (far <= dist) return (Steer(crab0, psi), 0f);   // too low: straight in, no brakes
            // the final: trim the brakes so the glide ends on the point
            float lo = 0f, hi = 1f, heading = crab0;
            for (int i = 0; i < 12; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Reach(mid, out heading) > dist) lo = mid; else hi = mid;
            }
            Reach(0.5f * (lo + hi), out heading);
            return (Steer(heading, psi), 0.5f * (lo + hi));
        }

        private static float Steer(float want, float psi) => Math.Clamp(MathF.IEEERemainder(want - psi, 2f * MathF.PI) * 1.5f, -1f, 1f);

        /// <summary>The fall's net downward acceleration at vertical speed <paramref name="vz"/>: gravity, less drag reaching the terminal speed.</summary>
        private static float Gravity(float vz) => DescentRules.Gravity * (1f - MathF.Min(1f, vz * vz / (FreeFallMps * FreeFallMps)));

        private static Vector2 Approach(Vector2 from, Vector2 to, float step)
        {
            var d = to - from;
            return d.Length() <= step ? to : from + Vector2.Normalize(d) * step;
        }

        /// <summary>Moves him by <paramref name="v"/>; on the surface he has landed.</summary>
        private static void Step(ref MotionFrame f, Vector3 v, float dt, float surface, ref Descent d, double now)
        {
            var p = f.Transform.Position + v * dt;
            if (p.Z <= surface)
            {
                p.Z = surface;
                f.Velocity.Linear = Vector3.Zero;
                f.Transform.Position = p;
                f.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, RotorcraftModel.YawOf(f.Transform.Rotation));
                DescentRules.SetPhase(ref d, DescentPhase.Landed, now);
                return;
            }
            f.Velocity.Linear = v;
            f.Transform.Position = p;
        }
    }

    /// <summary>
    /// ⭐ Airborne insertion (§1 module graph, §5.3) — a man who has <see cref="DescentPhase.Landed"/> stays down for his landing time (the
    /// rope's grip off, the harness off — he cannot move or fire), then his <see cref="Descent"/> is removed: a Pedestrian again on the
    /// surface he landed on. Runs after <see cref="AirMotionSystem"/>, on the node that owns his position.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class DescentLandingSystem : IEcsModuleSystem
    {
        /// <summary>How long a roped man takes to clear the rope (s).</summary>
        public const float RopeClearSeconds = 1f;

        private readonly List<Entity> _done = new();

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Descent>()) return;
            double now = DescentRules.Now(repo);
            _done.Clear();
            foreach (var e in repo.Query().With<Descent>().With<SimTransform>().WithOwned<SimTransform>().Build())
            {
                ref readonly var d = ref repo.GetComponentRO<Descent>(e);
                if (d.Phase != DescentPhase.Landed) continue;
                float wait = d.Kind == DescentKind.FastRope ? RopeClearSeconds : ParachuteModel.Of(repo, e, d.Canopy).LandingSeconds;
                if (now - d.PhaseSince >= wait) _done.Add(e);
            }
            foreach (var e in _done) repo.RemoveComponent<Descent>(e);
        }
    }
}
