using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Core
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for Combat component types and events (BCS-P5-T1 / BCS-P5-T2).
    /// All tests are pure reflection or struct-default checks — no ECS world required.
    /// </summary>
    public class CombatComponentTests
    {
        // ── WeaponState ───────────────────────────────────────────────────────

        /// <summary>WeaponState is an unmanaged value type.</summary>
        [Fact]
        public void WeaponState_IsUnmanagedValueType()
        {
            Assert.True(typeof(WeaponState).IsValueType);
        }

        // ── Health ────────────────────────────────────────────────────────────

        /// <summary>Health default-initialises with Current == 0.</summary>
        [Fact]
        public void Health_DefaultCurrentIsZero()
        {
            var h = new Health();
            Assert.Equal(0f, h.Current);
        }

        // ── BallisticProjectile ───────────────────────────────────────────────

        /// <summary>
        /// Shooter field must be typed as Entity, not a raw int.
        /// Guards against accidentally reverting to a raw index.
        /// </summary>
        [Fact]
        public void BallisticProjectile_ContainsEntityShooter_NotRawIndex()
        {
            var field = typeof(BallisticProjectile).GetField("Shooter");
            Assert.NotNull(field);
            Assert.Equal(typeof(Entity), field!.FieldType);
        }

        /// <summary>
        /// BallisticProjectile must have PreviousPosition (Vector3) and NO Velocity field.
        /// Velocity was removed in the Phase 0 Adaptation — movement is handled by SimVelocity.
        /// </summary>
        [Fact]
        public void BallisticProjectile_HasPreviousPosition_NotVelocity()
        {
            Assert.Null(typeof(BallisticProjectile).GetField("Velocity"));
            Assert.NotNull(typeof(BallisticProjectile).GetField("PreviousPosition"));
        }

        // ── FireRequestEvent ──────────────────────────────────────────────────

        /// <summary>FireRequestEvent carries [EventId] attribute required by the event bus.</summary>
        [Fact]
        public void FireRequestEvent_HasEventIdAttribute()
        {
            var attr = typeof(FireRequestEvent).GetCustomAttribute<EventIdAttribute>();
            Assert.NotNull(attr);
        }

        // ── HitEvent (migrated from Physics) ─────────────────────────────────

        /// <summary>
        /// CombatConstants.HitEventId must equal 5001 — the ID previously defined in
        /// PhysicsConstants. Guards against the numeric ID changing during the migration.
        /// </summary>
        [Fact]
        public void HitEvent_HasSameIdAsPhysicsToolkitHitEvent()
        {
            // The agreed ID is 5001, unchanged from PhysicsConstants.HitEventId.
            Assert.Equal(5001, CombatConstants.HitEventId);

            // The [EventId] attribute on HitEvent must carry the same value.
            var attr = typeof(HitEvent).GetCustomAttribute<EventIdAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(CombatConstants.HitEventId, attr!.Id);
        }

        // ── BS1-T001: WeaponFire pipeline ECS event structs ───────────────────

        /// <summary>
        /// BS1-T001 SC-2: WeaponFireIntent must be an unmanaged value type.
        /// PACK-P003 layout: 2×Entity(int+ushort=6 bytes under Pack=1) + sizeof(int) = 16 bytes.
        /// </summary>
        [Fact]
        public void WeaponFireIntent_IsUnmanaged_AndHasCorrectSize()
        {
            Assert.True(typeof(WeaponFireIntent).IsValueType);
            // Current actual layout: 2×Entity(8) + int(4) + bool(1) padded to 24 bytes; ⭐ CE-1032 + AtPoint (bool) and
            // TargetPoint (Vector3) = 40, ⭐ M8 + AmmoGuid (long, 8-aligned) = 48, ⭐ IF-1 (I3) + Charge, HighArc (bytes) = 50,
            // ⭐ CE-2180 + RaisedAt (double, Pack = 1) = 58, ⭐ CE-3235 + FuzeSeconds (float) = 62, ⭐ CE-3236 + BurnSeconds (float) = 66,
            // ⭐ Q94 A-1 (R-3018) + RequestId (uint) = 70.
            // A LOCAL event — the wire (WeaponFireRequest) is mapped field by field.
            Assert.Equal(70, Marshal.SizeOf<WeaponFireIntent>());
        }

        /// <summary>
        /// BS1-T001 SC-3: WeaponFireNotification must be an unmanaged value type
        /// with the same layout as WeaponFireIntent (PACK-P003: 16 bytes).
        /// </summary>
        [Fact]
        public void WeaponFireNotification_IsUnmanaged_AndHasCorrectSize()
        {
            Assert.True(typeof(WeaponFireNotification).IsValueType);
            // 2×Entity(8) + int(4) + bool (marshalled as a 4-byte BOOL) = 24, then ⭐ CE-1111 (BS-1 §5.4b F1, appended): AmmoGuid(8) +
            //   Round(8) + Origin(12) + Velocity(12) + Quantity(4) + FiredAt(8) = 52 ⇒ 76, then ⭐ Q94 A-1 (R-3018, appended):
            //   RequestId(4) + Refusal(1) ⇒ 81.
            Assert.Equal(81, Marshal.SizeOf<WeaponFireNotification>());
        }

        /// <summary>
        /// BS1-T001 SC-4: WeaponFireIntent carries an [EventId] attribute with the
        /// value defined in CombatConstants.WeaponFireIntentEventId (5003).
        /// </summary>
        [Fact]
        public void WeaponFireIntent_HasCorrectEventIdAttribute()
        {
            var attr = typeof(WeaponFireIntent).GetCustomAttribute<EventIdAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(CombatConstants.WeaponFireIntentEventId, attr!.Id);
        }

        // ── BS1-T002: Detonation / Damage pipeline ECS event structs ─────────

        /// <summary>
        /// BS1-T002 SC-2: DetonationNotification must be an unmanaged value type.
        /// PACK-P003 layout: 2×Entity(int+ushort=6 bytes under Pack=1) + 3×sizeof(float) = 24 bytes.
        /// </summary>
        [Fact]
        public void DetonationNotification_IsUnmanaged_AndHasCorrectSize()
        {
            Assert.True(typeof(DetonationNotification).IsValueType);
            // Current actual layout: 2×Entity(8) + 3×float(4) + bool (marshalled as 4) = 32, ⭐ CE-3071 + Penetration and
            // Damage (2×float) = 40, ⭐ CE-1032 + Ammo (long) = 48, ⭐ M1 + TravelX/Y/Z (3×float) = 60. A LOCAL event — the wire
            // message (MunitionDetonation) is mapped field by field, so the travel never reaches the wire. ⭐ CE-3209 + HasBodyPart,
            // BodyPart, Face (3×byte, Pack = 1) = 63. ⭐ CE-3236 + Round (Entity, local only) = 71.
            Assert.Equal(71, Marshal.SizeOf<DetonationNotification>());
        }

        /// <summary>
        /// BS1-T002 SC-3: DamageAssessedEvent must be an unmanaged value type.
        /// Layout: sizeof(long) + sizeof(float) = 12 bytes.
        /// (Pack=1 eliminates padding.)
        /// </summary>
        [Fact]
        public void DamageAssessedEvent_IsUnmanaged_AndHasCorrectSize()
        {
            Assert.True(typeof(DamageAssessedEvent).IsValueType);
            // Marshalled layout: Entity(8) + float(4) + bool(4, marshalled as a Win32 BOOL) + HasBodyPart(1) + BodyPart(1) (CE-3210)
            // + Effect(1, CE-3219) + pad(1) + EffectSeconds(4, CE-3219) = 24 bytes.
            Assert.Equal(32, Marshal.SizeOf<DamageAssessedEvent>());   // ⭐ CE-3237 (B-15) — + Source (Entity, 8)
        }
    }
}
