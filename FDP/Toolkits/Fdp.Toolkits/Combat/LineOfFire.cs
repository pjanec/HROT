using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-321</c> — <b>a unit never fires through a friendly.</b> 🔒 User, <c>2026-10-04</c>: "A+B approved" (A = this hold-fire
    /// rule). A friendly is any OTHER entity of the shooter's force (<see cref="EntityInfo.ForceId"/>) with a
    /// <see cref="PhysicsCollider"/> — alive or wrecked (a dead hull still stops a round). The test is the same flat
    /// segment-against-circle sweep the bullet's own raycast resolves against.
    /// <para>📌 Found by <c>UrbanCombatNew</c>: dismounted soldiers emptied their rifles into their own APC (hp 475 → 0).</para>
    /// </summary>
    public static class LineOfFire
    {
        /// <summary>True when a friendly collider lies on the line from <paramref name="shooter"/> to <paramref name="target"/>.
        /// False when either end has no position or the shooter has no force (nothing to be friendly with).</summary>
        public static bool BlockedByFriendly(EntityRepository world, Entity shooter, Entity target)
        {
            if (!world.HasComponent<SimTransform>(target)) return false;
            return Blocked(world, shooter, target, world.GetComponentRO<SimTransform>(target).Position);
        }

        /// <summary>
        /// ⭐ <c>CE-3158</c> G3 (📄 docs/DESIGN_Peek_And_Fire.md §10.5) — the same test to a POINT: a burst at a remembered spot, a
        /// round at a ground point. ⚠ XY only, like the entity form (a friend below a shot on a slope counts as blocking —
        /// conservative); both move behind <c>IWorldQuery.Trace(Fire)</c> with WQ-F (§10.8).
        /// </summary>
        public static bool BlockedByFriendly(EntityRepository world, Entity shooter, Vector3 point)
            => Blocked(world, shooter, Entity.Null, point);

        /// <summary>
        /// ⭐ <c>AW-20</c> (R-2026; docs/DESIGN_Civilians_And_Crowd_Control.md §5h) — <b>a neutral in the line of fire or within the weapon's
        /// spread holds the shot.</b> A neutral is a living PERSON (<see cref="PhysicsColliderReaders.IsPerson"/>) of
        /// <see cref="ForceId.Neutral"/> other than the target, when the shooter is not neutral himself (then he is a friend, and
        /// <see cref="BlockedByFriendly(EntityRepository, Entity, Entity)"/> holds). His circle is widened by the spread at his range:
        /// <paramref name="sigma"/> (<see cref="HitModel.Sigma"/>, the half-width of the uniform cone) × his distance along the line.
        /// <para>⭐ AW-24: the one new rule a soldier obeys, and it costs him one pass over the colliders — empty of neutrals in a
        /// military fight. ⛔ A wall, a wreck, a dropped weapon (default <see cref="ForceId.Neutral"/>) never holds it: people only.</para>
        /// </summary>
        public static bool BlockedByNeutral(EntityRepository world, Entity shooter, Entity target, float sigma)
        {
            if (!world.HasComponent<SimTransform>(target)) return false;
            return NeutralOnLine(world, shooter, target, world.GetComponentRO<SimTransform>(target).Position, sigma);
        }

        /// <summary>⭐ <c>AW-20</c> — the same hold toward a POINT (a burst at a remembered spot).</summary>
        public static bool BlockedByNeutral(EntityRepository world, Entity shooter, Vector3 point, float sigma)
            => NeutralOnLine(world, shooter, Entity.Null, point, sigma);

        /// <summary>A round that is not a warhead is held when the terrain stops it this close to the muzzle — the frame he leans out of.</summary>
        public const float MuzzleClearanceM = 2f;

        /// <summary>
        /// ⭐ <c>CE-2218</c> — <b>a shooter never fires into the wall in front of his own muzzle.</b> True when the terrain (a wall, a window
        /// frame, a floor) STOPS the round — by the one penetration rule (<see cref="TerrainPenetration.Carry"/>) — within its danger
        /// distance of where it leaves (<see cref="ShotOrigin"/>): <see cref="MuzzleClearanceM"/>, or a warhead's blast-injury radius (it would
        /// burst on him). The eye's line passing the window does not make the barrel's.
        /// <para>📐 Live <c>ua-rpg-window</c>: an RPG man at an upper window aimed steeply down at an IFV 10 m off; the rocket stopped 5 cm
        /// from the muzzle, burst, and killed him. ⛔ Not for an ARC (the throw already lobs over what stops it — <c>ThrowArc</c>) and not for
        /// a bomb; with no terrain, never.</para>
        /// </summary>
        public static bool MuzzleMasked(EntityRepository world, Entity shooter, int weaponIndex, long loadedAmmo, Vector3 aim)
        {
            if (Fdp.Toolkit.World.WorldQuery.Of(world) is not { } terrain || !world.HasComponent<SimTransform>(shooter)) return false;
            var mount = CombatTkb.MountOf(world, shooter, weaponIndex);
            long ammo = loadedAmmo != 0 ? loadedAmmo : mount != null ? unchecked((long)mount.AmmoGuid) : 0L;
            var warhead = CombatTkb.WarheadOf(world, ammo);
            if (warhead is { Indirect: true } || mount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb) return false;

            var eye = ShotOrigin.Eye(world, shooter, out _);
            var origin = ShotOrigin.From(world, shooter, weaponIndex, eye, aim, out _);
            return Stops(world, terrain, mount, ammo, warhead, origin, aim);
        }

        /// <summary>
        /// ⭐ <c>CE-2327</c> (R-2064) — the same hold for a round that WOULD leave from <paramref name="origin"/> (an eye at a spot he is not
        /// at yet: the firing-position search marks the spots a round clears from). ⛔ The barrel is not placed — a spot has no pose yet; a
        /// rifleman's round leaves from his eye anyway (<see cref="ShotOrigin"/>).
        /// </summary>
        public static bool MaskedFrom(EntityRepository world, Fdp.Toolkit.World.IWorldQuery terrain, Entity shooter, int weaponIndex,
                                      Vector3 origin, Vector3 aim)
        {
            var mount = CombatTkb.MountOf(world, shooter, weaponIndex);
            long ammo = mount != null ? unchecked((long)mount.AmmoGuid) : 0L;
            var warhead = CombatTkb.WarheadOf(world, ammo);
            if (warhead is { Indirect: true } || mount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb) return false;
            return Stops(world, terrain, mount, ammo, warhead, origin, aim);
        }

        /// <summary>The terrain stops the round within its danger distance of <paramref name="origin"/> on its way to <paramref name="aim"/>.</summary>
        private static bool Stops(EntityRepository world, Fdp.Toolkit.World.IWorldQuery terrain, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount, long ammo,
                                  Fdp.Toolkit.Tkb.Domain.WarheadDto? warhead, Vector3 origin, Vector3 aim)
        {
            var line = aim - origin;
            float length = line.Length();
            if (length < 1e-3f) return false;
            float danger = MathF.Max(MuzzleClearanceM, warhead is { HasAreaEffect: true } ? warhead.BlastInjuryRadiusM : 0f);
            var end = origin + line * (MathF.Min(danger, length) / length);

            var (penetration, _) = CombatTkb.RoundOf(world, mount, ammo);
            float damage = 1f;
            return TerrainPenetration.Carry(terrain, origin, end, ref damage, ref penetration, out _);
        }

        /// <summary>
        /// ⭐ CE-2379 — the horizontal radius within which a round can strike <paramref name="e"/>: his collider radius, and for a PERSON also
        /// the reach of his CURRENT pose's body-part boxes (scaled by his height as <c>BodyPartRay.Person</c> scales) plus his lean shift —
        /// a round meets a man by his parts, so an arm reaches past the collider (Bridgehead Red-on-Red, backend's report).
        /// </summary>
        public static float StrikeRadius(EntityRepository world, Entity e)
        {
            var collider = world.GetComponentRO<PhysicsCollider>(e);
            float r = collider.Radius;
            if (!PhysicsColliderReaders.IsPerson(world, e)) return r;
            var pose = Fdp.Toolkit.Physics.BodyParts.PersonBody.PoseOf(world, e);
            float reach = Fdp.Toolkit.Physics.BodyParts.PersonBody.HorizontalReach(pose) * Fdp.Toolkit.Physics.BodyParts.PersonBody.ScaleOf(collider.Height);
            if (pose != Fdp.Toolkit.Physics.BodyParts.PersonPose.Downed)
            {
                var stance = Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(world, e);
                if (Leans.SignOf(world, e, stance) != 0) reach += Leans.ShiftOf(world, e);
            }
            return MathF.Max(r, reach);
        }

        private static bool Blocked(EntityRepository world, Entity shooter, Entity target, Vector3 b3)
        {
            if (!world.IsComponentTypeRegistered<PhysicsCollider>() || !world.IsComponentTypeRegistered<EntityInfo>()) return false;
            if (!world.HasComponent<SimTransform>(shooter)) return false;
            if (!world.HasComponent<EntityInfo>(shooter)) return false;
            var force = world.GetComponentRO<EntityInfo>(shooter).ForceId;
            var a3 = world.GetComponentRO<SimTransform>(shooter).Position;
            var a = new Vector2(a3.X, a3.Y);
            var ab = new Vector2(b3.X, b3.Y) - a;
            float len2 = ab.LengthSquared();
            if (len2 < 1e-6f) return false;

            // ⭐ CE-4516 (X-1) — the lock covers only the frame's build; the walk writes nothing to the grid (AnySegmentCandidate), so the weapon
            //   phase's shooters ask in parallel. ⛔ SUPERSEDED: the lock held across Refresh + Near + the walk — every shooter of the phase queued on it.
            var bodies = Bodies.Of(world);
            lock (bodies) bodies.Refresh(world);
            var friend = new FriendOnLine { World = world, Shooter = shooter, Target = target, Force = force, A = a, AB = ab, Len2 = len2 };
            return bodies.Grid.AnySegmentCandidate(a, a + ab, bodies.Pad(0f), ref friend, Bodies.EveryBodyForRails);
        }

        /// <summary>The friendly-fire rule, applied LIVE to each body the frame grid gives (the same rule the walk applied under the lock).</summary>
        private struct FriendOnLine : Fdp.Toolkit.Spatial.FrameEntityGrid.ICandidateTest
        {
            public EntityRepository World; public Entity Shooter, Target; public ForceId Force; public Vector2 A, AB; public float Len2;
            public bool Test(Entity e)
            {
                var world = World;
                if (!world.IsAlive(e) || !world.HasComponent<PhysicsCollider>(e)) return false;
                if (e == Shooter || e == Target || RidesWith(world, Shooter, e)) return false;
                if (world.GetComponentRO<EntityInfo>(e).ForceId != Force) return false;
                // ⭐ CE-2338 — a friendly PERSON down (dead or incapacitated) lies on the floor: the round flies over him (CE-2318 — he is
                //   Prone for the round). A hull, alive or wrecked, still holds it. 📌 bc-room-clear: two men down in room A's doorway
                //   held every man behind them while the defenders shot them (entry men 5 rounds, defenders 20).
                if (PhysicsColliderReaders.IsPerson(world, e) && !Injury.IsInTheFight(world, e)) return false;
                float r = world.GetComponentRO<PhysicsCollider>(e).Radius;
                if (r <= 0f) return false;
                r = StrikeRadius(world, e);
                var p3 = world.GetComponentRO<SimTransform>(e).Position;
                var ap = new Vector2(p3.X, p3.Y) - A;
                float t = Vector2.Dot(ap, AB) / Len2;
                if (t <= 0f || t >= 1f) return false;                 // behind the shooter or beyond the target
                return Vector2.DistanceSquared(AB * t, ap) < r * r;
            }
        }

        private static bool NeutralOnLine(EntityRepository world, Entity shooter, Entity target, Vector3 b3, float sigma)
        {
            if (!world.IsComponentTypeRegistered<PhysicsCollider>() || !world.IsComponentTypeRegistered<EntityInfo>()) return false;
            if (!world.HasComponent<SimTransform>(shooter) || !world.HasComponent<EntityInfo>(shooter)) return false;
            if (world.GetComponentRO<EntityInfo>(shooter).ForceId == ForceId.Neutral) return false;   // a friend to them: BlockedByFriendly
            var a3 = world.GetComponentRO<SimTransform>(shooter).Position;
            var a = new Vector2(a3.X, a3.Y);
            var ab = new Vector2(b3.X, b3.Y) - a;
            float len2 = ab.LengthSquared();
            if (len2 < 1e-6f) return false;
            float len = MathF.Sqrt(len2);
            bool health = world.IsComponentTypeRegistered<Health>();

            var bodies = Bodies.Of(world);
            lock (bodies) bodies.Refresh(world);   // ⭐ CE-4516 — the lock covers only the build (as BlockedByFriendly)
            var neutral = new NeutralOnLineTest { World = world, Shooter = shooter, Target = target, Health = health, A = a, AB = ab, Len2 = len2, Len = len, Sigma = MathF.Max(0f, sigma) };
            return bodies.Grid.AnySegmentCandidate(a, a + ab, bodies.Pad(MathF.Max(0f, sigma) * len), ref neutral, Bodies.EveryBodyForRails);
        }

        /// <summary>The neutral-on-the-line rule (AW-20), applied LIVE to each body the frame grid gives.</summary>
        private struct NeutralOnLineTest : Fdp.Toolkit.Spatial.FrameEntityGrid.ICandidateTest
        {
            public EntityRepository World; public Entity Shooter, Target; public bool Health; public Vector2 A, AB; public float Len2, Len, Sigma;
            public bool Test(Entity e)
            {
                var world = World;
                if (!world.IsAlive(e) || !world.HasComponent<PhysicsCollider>(e)) return false;
                if (e == Shooter || e == Target) return false;
                if (!Fdp.Toolkit.Perception.ThreatDanger.IsNeutral(world, e) || !PhysicsColliderReaders.IsPerson(world, e)) return false;
                if (Health && world.HasComponent<Health>(e) && world.GetComponentRO<Health>(e).Current <= 0f) return false;
                var p3 = world.GetComponentRO<SimTransform>(e).Position;
                var ap = new Vector2(p3.X, p3.Y) - A;
                float t = Vector2.Dot(ap, AB) / Len2;
                if (t <= 0f || t >= 1f) return false;                 // behind the shooter or beyond the target
                float r = StrikeRadius(world, e) + Sigma * t * Len;
                return Vector2.DistanceSquared(AB * t, ap) < r * r;
            }
        }

        /// <summary>⭐ CE-2411 — how far a body may have moved within the frame and still be found where the frame's grid placed it.</summary>
        public const float MovedSlackMetres = 4f;

        /// <summary>
        /// ⭐ CE-2411 — the bodies a round can meet (PhysicsCollider + SimTransform + EntityInfo — the set the full scan walked), in the
        /// shared per-frame <see cref="Fdp.Toolkit.Spatial.FrameEntityGrid"/>; the walk's pad is the largest strike radius the build saw plus
        /// <see cref="MovedSlackMetres"/>. 📐 The scan per shot was 18 % of the CGF node's thread on the CE-2408 stress world.
        /// ⚠ One shared buffer per world: callers hold the lock across <see cref="Refresh"/>, <see cref="Near"/> and the walk of its result.
        /// </summary>
        internal sealed class Bodies
        {
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, Bodies> s_of = new();
            public readonly Fdp.Toolkit.Spatial.FrameEntityGrid Grid = new();
            private EntityQuery? _query;
            private float _maxStrike;

            /// <summary>A rail sets it to walk every body (the old full scan) through the same rule, to compare the answers.</summary>
            internal static bool EveryBodyForRails;

            private static readonly Func<EntityRepository, Entity, bool> Member =
                static (w, e) => w.HasComponent<PhysicsCollider>(e) && w.HasComponent<EntityInfo>(e);
            public static Bodies Of(EntityRepository world) => s_of.GetValue(world, static _ => new Bodies());

            public void Refresh(EntityRepository world)
            {
                var b = this;
                b._query ??= world.Query().With<PhysicsCollider>().With<SimTransform>().With<EntityInfo>().Build();
                if (b.Grid.EnsureFresh(world, b._query, Member))
                {
                    float max = 0f;
                    for (int i = 0; i < b.Grid.Count; i++)
                    {
                        var e = b.Grid.EntityAt(i);
                        if (world.GetComponentRO<PhysicsCollider>(e).Radius > 0f) max = MathF.Max(max, StrikeRadius(world, e));
                    }
                    b._maxStrike = max;
                }
            }

            /// <summary>The walk's pad around a→b: the largest strike radius the build saw + <paramref name="extra"/> + the slack.</summary>
            public float Pad(float extra) => _maxStrike + extra + MovedSlackMetres;
        }

        /// <summary>
        /// ⭐ Weapon systems WS-3 — <paramref name="e"/> rides with <paramref name="shooter"/>: the vehicle it is aboard, a passenger of it, or
        /// a fellow passenger of its vehicle (the pickup, its gunner, the M2 in its bed). They move as one body and are never "on the line":
        /// a station weapon stands off its host's reference point, where the crew rides.
        /// </summary>
        /// <summary>⭐ Also the impact rule's (<c>DroneImpactSystem</c>, CE-2173 slice 6d): a carrier and its riders never meet each other.</summary>
        internal static bool RidesWith(EntityRepository world, Entity shooter, Entity e)
        {
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()) return false;
            Entity shooterHost = world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(shooter)
                ? world.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(shooter).VehicleEntity : Entity.Null;
            if (!shooterHost.IsNull && e == shooterHost) return true;
            if (!world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) return false;
            var host = world.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e).VehicleEntity;
            return host == shooter || (!shooterHost.IsNull && host == shooterHost);
        }
    }
}
