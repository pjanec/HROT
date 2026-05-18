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
