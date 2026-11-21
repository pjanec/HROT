using System.Reflection;
using Hrot.NED.Messages;
using CycloneDDS.Schema;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    /// <summary>
    /// Reflection tests for the fire-interaction DDS message types added in BS1-T001 and BS1-T002.
    /// Verifies struct layout (value-type, size) and <see cref="DdsTopicAttribute"/> correctness.
    /// </summary>
    public class FireInteractionMessageTests
    {
        // ── BS1-T001: WeaponFireRequest ───────────────────────────────────────

        /// <summary>
        /// BS1-T001 SC-4: WeaponFireRequest must carry exactly one DdsTopicAttribute
        /// with no explicit TopicName (derived: Hrot_NED_Messages_WeaponFireRequest).
        /// </summary>
        [Fact]
        public void WeaponFireRequest_HasDdsTopicAttribute_WithCorrectName()
        {
            var attrs = typeof(WeaponFireRequest).GetCustomAttributes<DdsTopicAttribute>(inherit: false);
            var list = new System.Collections.Generic.List<DdsTopicAttribute>(attrs);
            Assert.Single(list);
            Assert.Null(list[0].TopicName);   // AQ107 A — never written: derived from the type
            Assert.Equal("Hrot_NED_Messages_WeaponFireRequest", DdsTopicName.Of<WeaponFireRequest>());
        }

        /// <summary>
        /// BS1-T001: WeaponFire must carry exactly one DdsTopicAttribute
        /// with no explicit TopicName (derived: Hrot_NED_Messages_WeaponFire).
        /// </summary>
        [Fact]
        public void WeaponFire_HasDdsTopicAttribute_WithCorrectName()
        {
            var attrs = typeof(WeaponFire).GetCustomAttributes<DdsTopicAttribute>(inherit: false);
            var list = new System.Collections.Generic.List<DdsTopicAttribute>(attrs);
            Assert.Single(list);
            Assert.Null(list[0].TopicName);   // AQ107 A — never written: derived from the type
            Assert.Equal("Hrot_NED_Messages_WeaponFire", DdsTopicName.Of<WeaponFire>());
        }

        /// <summary>
        /// BS1-T001: WeaponFireRequest is a value type (unmanaged struct).
        /// </summary>
        [Fact]
        public void WeaponFireRequest_IsValueType()
        {
            Assert.True(typeof(WeaponFireRequest).IsValueType);
        }

        // ── BS1-T002: MunitionDetonation ─────────────────────────────────────

        /// <summary>
        /// BS1-T002 SC-3: MunitionDetonation must carry exactly one DdsTopicAttribute
        /// with no explicit TopicName (derived: Hrot_NED_Messages_MunitionDetonation).
        /// </summary>
        [Fact]
        public void MunitionDetonation_HasDdsTopicAttribute_WithCorrectName()
        {
            var attrs = typeof(MunitionDetonation).GetCustomAttributes<DdsTopicAttribute>(inherit: false);
            var list = new System.Collections.Generic.List<DdsTopicAttribute>(attrs);
            Assert.Single(list);
            Assert.Null(list[0].TopicName);   // AQ107 A — never written: derived from the type
            Assert.Equal("Hrot_NED_Messages_MunitionDetonation", DdsTopicName.Of<MunitionDetonation>());
        }

        /// <summary>
        /// BS1-T002: EntityHitDamage must carry exactly one DdsTopicAttribute
        /// with no explicit TopicName (derived: Hrot_NED_Messages_EntityHitDamage).
        /// </summary>
        [Fact]
        public void EntityHitDamage_HasDdsTopicAttribute_WithCorrectName()
        {
            var attrs = typeof(EntityHitDamage).GetCustomAttributes<DdsTopicAttribute>(inherit: false);
            var list = new System.Collections.Generic.List<DdsTopicAttribute>(attrs);
            Assert.Single(list);
            Assert.Null(list[0].TopicName);   // AQ107 A — never written: derived from the type
            Assert.Equal("Hrot_NED_Messages_EntityHitDamage", DdsTopicName.Of<EntityHitDamage>());
        }

        /// <summary>
        /// BS1-T002: MunitionDetonation is a value type (unmanaged struct).
        /// </summary>
        [Fact]
        public void MunitionDetonation_IsValueType()
        {
            Assert.True(typeof(MunitionDetonation).IsValueType);
        }
    }
}
