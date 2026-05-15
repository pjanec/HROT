using System;
using System.Reflection;
using Fdp.Toolkit.Tkb.Attributes;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests.Attributes
{
    public class TkbDescriptorAttributeTests
    {
        // ---- TkbDescriptorAttribute construction validation ----

        [Fact]
        public void Constructor_EmptyString_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => new TkbDescriptorAttribute(""));
            Assert.NotNull(ex);
        }

        [Fact]
        public void Constructor_Null_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => new TkbDescriptorAttribute(null!));
            Assert.NotNull(ex);
        }

        [Fact]
        public void Constructor_WhitespaceOnly_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => new TkbDescriptorAttribute("   "));
            Assert.NotNull(ex);
        }

        [Fact]
        public void Constructor_NameContainsHash_ThrowsArgumentException()
        {
            // '#' is the runtime PartId delimiter and must not appear in schema-level names
            var ex = Assert.Throws<ArgumentException>(() => new TkbDescriptorAttribute("Platform#1"));
            Assert.Contains("#", ex.Message);
        }

        [Fact]
        public void Constructor_ValidName_SetsHierarchicalName()
        {
            var attr = new TkbDescriptorAttribute("Gen.VehicleParameters");
            Assert.Equal("Gen.VehicleParameters", attr.HierarchicalName);
        }

        [Fact]
        public void Constructor_TkbMasterName_SetsHierarchicalName()
        {
            var attr = new TkbDescriptorAttribute("TkbMaster");
            Assert.Equal("TkbMaster", attr.HierarchicalName);
        }

        // ---- Field-level attribute smoke tests via reflection ----

        private class DummyTarget
        {
            [WeaponRef]
            public long WeaponId { get; set; }

            [AmmoRef]
            public long AmmoId { get; set; }

            [ModelRef]
            public string? ModelPath { get; set; }
        }

        [Fact]
        public void WeaponRefAttribute_CanBeAppliedToProperty_ReflectedSuccessfully()
        {
            var prop = typeof(DummyTarget).GetProperty(nameof(DummyTarget.WeaponId))!;
            var attr = prop.GetCustomAttribute<WeaponRefAttribute>();
            Assert.NotNull(attr);
        }

        [Fact]
        public void AmmoRefAttribute_CanBeAppliedToProperty_ReflectedSuccessfully()
        {
            var prop = typeof(DummyTarget).GetProperty(nameof(DummyTarget.AmmoId))!;
            var attr = prop.GetCustomAttribute<AmmoRefAttribute>();
            Assert.NotNull(attr);
        }

        [Fact]
        public void ModelRefAttribute_CanBeAppliedToProperty_ReflectedSuccessfully()
        {
            var prop = typeof(DummyTarget).GetProperty(nameof(DummyTarget.ModelPath))!;
            var attr = prop.GetCustomAttribute<ModelRefAttribute>();
            Assert.NotNull(attr);
        }
    }
}
