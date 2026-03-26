using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Kernel
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;
using Xunit;

namespace FDP.Toolkit.Combat.Tests
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
        /// BS1-T001 SC-2: WeaponFireIntent must be an unmanaged value type whose
        /// sequential layout equals 2×sizeof(long) + sizeof(int) = 20 bytes.
        /// (Pack=1 eliminates alignment padding.)
        /// </summary>
        [Fact]
        public void WeaponFireIntent_IsUnmanaged_AndHasCorrectSize()
        {
            Assert.True(typeof(WeaponFireIntent).IsValueType);
            int expected = sizeof(long) + sizeof(long) + sizeof(int); // 20
            Assert.Equal(expected, Marshal.SizeOf<WeaponFireIntent>());
        }

        /// <summary>
        /// BS1-T001 SC-3: WeaponFireNotification must be an unmanaged value type
        /// with the same 20-byte layout as WeaponFireIntent.
        /// </summary>
        [Fact]
        public void WeaponFireNotification_IsUnmanaged_AndHasCorrectSize()
        {
            Assert.True(typeof(WeaponFireNotification).IsValueType);
            int expected = sizeof(long) + sizeof(long) + sizeof(int); // 20
            Assert.Equal(expected, Marshal.SizeOf<WeaponFireNotification>());
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
        /// Layout: 2×sizeof(long) + 3×sizeof(float) = 16 + 12 = 28 bytes.
        /// (Pack=1 eliminates padding.)
        /// </summary>
        [Fact]
        public void DetonationNotification_IsUnmanaged_AndHasCorrectSize()
        {
            Assert.True(typeof(DetonationNotification).IsValueType);
            int expected = sizeof(long) + sizeof(long) + sizeof(float) * 3; // 28
            Assert.Equal(expected, Marshal.SizeOf<DetonationNotification>());
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
            int expected = sizeof(long) + sizeof(float); // 12
            Assert.Equal(expected, Marshal.SizeOf<DamageAssessedEvent>());
        }
    }
}
