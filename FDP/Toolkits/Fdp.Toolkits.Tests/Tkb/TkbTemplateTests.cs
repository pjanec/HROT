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
        /// ⭐⭐ <b>Step 0's gate, first half — opt-in, so *"only for entities having one"* is automatic.</b>
        /// ⛔ If this ever defaulted to non-empty, every template in the system would claim a creator
        /// birthright it was never authored to have.
        /// </summary>
        [Fact]
        public void BirthCriticalComponents_IsEmpty_ByDefault()
        {
            var template = new TkbTemplate("Test", 1);
            Assert.Empty(template.BirthCriticalComponents);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Step 0's gate, second half — the list is NETWORK-FREE.</b>
        ///
        /// <para>🔒 User, <c>2026-09-01</c>: <i>"there are networkless systems as well… TKB should
        /// define what components are birth critical."</i> ⛔ The whole reason birth-criticality is a
        /// COMPONENT property and not a descriptor one is that a node with no DDS participant has no
        /// descriptor mapping — a descriptor-keyed definition would be undefined exactly where the
        /// component still exists. ⇒ this rail declares and reads the set with no database, no
        /// participant, no <c>DescriptorOwnershipMap</c> and no translator in sight, which is what
        /// "holds on a networkless node" means operationally.</para>
        /// </summary>
        [Fact]
        public void BirthCriticalComponents_AreDeclaredAndReadWithNoNetworkingInvolved()
        {
            var template = new TkbTemplate("Test", 1);

            template.AddBirthCriticalComponent<Fdp.Core.SimTransform>();

            int expected = Fdp.Core.ComponentTypeRegistry.GetOrRegisterManaged(typeof(Fdp.Core.SimTransform));
            Assert.Equal(new[] { expected }, template.BirthCriticalComponents);
        }

        /// <summary>
        /// ⭐ Idempotent, so a builder that both DEFINES a template and later DECORATES it cannot
        /// double-register — and a duplicate id would later contribute the same mask bit twice, hiding
        /// an authoring mistake behind a harmless-looking result.
        /// </summary>
        [Fact]
        public void AddBirthCriticalComponent_IsIdempotent()
        {
            var template = new TkbTemplate("Test", 1);

            template.AddBirthCriticalComponent<Fdp.Core.SimTransform>();
            template.AddBirthCriticalComponent<Fdp.Core.SimTransform>();

            Assert.Single(template.BirthCriticalComponents);
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

            template.AddBirthCriticalComponent<Fdp.Core.SimTransform>();

            Assert.Single(template.BirthCriticalComponents);
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
