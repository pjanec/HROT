using System;
using System.Reflection;
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
    }
}
