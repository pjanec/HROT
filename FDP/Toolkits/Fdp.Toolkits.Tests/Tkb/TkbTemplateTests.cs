using System.Linq;
using Xunit;
using Fdp.Interfaces;
using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Tkb.Tests
{
    public class TkbTemplateTests
    {
        [Fact]
        public void Ctor_ValidatesArgs()
        {
            Assert.Throws<ArgumentNullException>(() => new TkbTemplate(null!, 1));
            Assert.Throws<ArgumentNullException>(() => new TkbTemplate("", 1));
            Assert.Throws<ArgumentException>(() => new TkbTemplate("Valid", 0));
        }

        [Fact]
        public void MandatoryComponents_IsEmpty_ByDefault()
        {
            var template = new TkbTemplate("Test", 1);
            Assert.Empty(template.MandatoryComponents);
        }

        // ═══ BIRTH-CRITICAL COMPONENTS — P3 step 0 ═════════════════════════════════════════════════
        //  📄 docs/DESIGN_Role_Affinity_Ownership.md §3.1, §6 step 0. Gate: "a template that does not
        //     list it does not report it; the list is network-free".

        /// <summary>
        /// ⭐⭐⭐ <b>DERIVED, NOT AUTHORED — every template reports the same set, and that set comes from
        /// <c>[BirthCritical]</c> on the COMPONENT TYPE</b> <i>(rewritten <c>2026-09-13</c>)</i>.
        ///
        /// <para>⛔⛔ <b>This rail used to assert the opposite</b> — <c>BirthCriticalComponents_IsEmpty_ByDefault</c>,
        /// guarding an opt-in per-template list. 🔴 That list could not survive the FILE path:
        /// <c>TkbDeserializer</c> builds templates purely from descriptor keys, so a file-loaded template
        /// arrived EMPTY (<c>CE-259az</c>), while 8 authoring sites across 3 producers had drifted apart.
        /// 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a.</para>
        ///
        /// <para>⚠ <b>"Only for entities having one" is still honoured</b> — but by the CREATE LEG's
        /// intersection with the entity's live component mask, not by an empty list. A positionless entity
        /// receives no bit no matter what this reports.</para>
        /// </summary>
        [Fact]
        public void BirthCriticalComponents_AreDerivedFromTheComponentAttribute()
        {
            var template = new TkbTemplate("Test", 1);

            int simTransform = Fdp.Core.ComponentTypeRegistry.GetOrRegisterManaged(typeof(Fdp.Core.SimTransform));
            Assert.Contains(simTransform, template.BirthCriticalComponents);
            Assert.Equal(Fdp.Core.ComponentAttributeSets.BirthCritical, template.BirthCriticalComponents);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The set is NETWORK-FREE.</b>
        ///
        /// <para>🔒 User, <c>2026-09-01</c>: <i>"there are networkless systems as well… TKB should
        /// define what components are birth critical."</i> ⛔ The whole reason birth-criticality is a
        /// COMPONENT property and not a descriptor one is that a node with no DDS participant has no
        /// descriptor mapping — a descriptor-keyed definition would be undefined exactly where the
        /// component still exists. ⇒ this rail reads the set with no database, no participant, no
        /// <c>DescriptorOwnershipMap</c> and no translator in sight.</para>
        ///
        /// <para>⭐ The ruling still holds under the attribute: the TKB record still DEFINES the set, it is
        /// simply no longer hand-authored.</para>
        /// </summary>
        [Fact]
        public void BirthCriticalComponents_AreReadWithNoNetworkingInvolved()
        {
            var template = new TkbTemplate("Test", 1);

            int expected = Fdp.Core.ComponentTypeRegistry.GetOrRegisterManaged(typeof(Fdp.Core.SimTransform));
            Assert.Contains(expected, template.BirthCriticalComponents);
        }

        /// <summary>
        /// ⭐ No duplicates — a duplicate id would contribute the same mask bit twice and hide an
        /// authoring mistake behind a harmless-looking result. ⚠ Now structural rather than guarded: the
        /// resolver builds a <c>SortedSet</c>, so duplication is unrepresentable.
        /// </summary>
        [Fact]
        public void BirthCriticalComponents_ContainNoDuplicates()
        {
            var template = new TkbTemplate("Test", 1);

            Assert.Equal(
                template.BirthCriticalComponents.Count,
                template.BirthCriticalComponents.Distinct().Count());
        }

        /// <summary>
        /// ⭐⭐ <b>The two lists stay SEPARATE.</b> ⚠ §3.1 argues birth-criticality deliberately is NOT a
        /// flag on <c>MandatoryComponent</c>, because the two answer different questions — *"must be
        /// PRESENT before promotion"* versus *"the creator must OWN it at birth"*. ⛔ If someone later
        /// "simplifies" by making one delegate to the other, this reddens.
        /// </summary>
        [Fact]
        public void BirthCritical_AndMandatory_AreIndependentLists()
        {
            var template = new TkbTemplate("Test", 1);

            Assert.NotEmpty(template.BirthCriticalComponents);
            Assert.Empty(template.MandatoryComponents);
        }

        [Fact]
        public void MandatoryComponents_HardRequirement_StoredCorrectly()
        {
            var template = new TkbTemplate("Test", 1);
            template.MandatoryComponents.Add(new MandatoryComponent
            {
                ComponentTypeId = 10,
                IsHard = true,
                SoftTimeoutFrames = 0
            });

            Assert.Single(template.MandatoryComponents);
            Assert.True(template.MandatoryComponents[0].IsHard);
            Assert.Equal(10, template.MandatoryComponents[0].ComponentTypeId);
            Assert.Equal(0u, template.MandatoryComponents[0].SoftTimeoutFrames);
        }

        [Fact]
        public void MandatoryComponents_SoftRequirement_StoredCorrectly()
        {
            var template = new TkbTemplate("Test", 1);
            template.MandatoryComponents.Add(new MandatoryComponent
            {
                ComponentTypeId = 20,
                IsHard = false,
                SoftTimeoutFrames = 120
            });

            Assert.Single(template.MandatoryComponents);
            Assert.False(template.MandatoryComponents[0].IsHard);
            Assert.Equal(120u, template.MandatoryComponents[0].SoftTimeoutFrames);
        }

        [Fact]
        public void MandatoryComponents_MultipleRequirements_AllStored()
        {
            var template = new TkbTemplate("Test", 1);
            template.MandatoryComponents.Add(new MandatoryComponent { ComponentTypeId = 1, IsHard = true });
            template.MandatoryComponents.Add(new MandatoryComponent { ComponentTypeId = 2, IsHard = false, SoftTimeoutFrames = 60 });
            template.MandatoryComponents.Add(new MandatoryComponent { ComponentTypeId = 3, IsHard = true });

            Assert.Equal(3, template.MandatoryComponents.Count);
        }
    }
}
