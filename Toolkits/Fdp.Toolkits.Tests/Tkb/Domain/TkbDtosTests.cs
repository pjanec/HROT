using System.Reflection;
using System.Text.Json;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Tkb.Attributes;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests.Domain
{
    public class TkbDtosTests
    {
        // ---- TkbMasterDto deserialization ----

        [Fact]
        public void TkbMasterDto_DeserializesFromJson()
        {
            const string json = """{ "CustomName": "M1 Abrams", "DisType": "1.1.225.1.1.1.0" }""";

            var dto = JsonSerializer.Deserialize<TkbMasterDto>(json, FdpJsonOptionsRegistry.DefaultRelaxed);

            Assert.NotNull(dto);
            Assert.Equal("M1 Abrams", dto!.CustomName);
            Assert.Equal("1.1.225.1.1.1.0", dto.DisType);
        }

        // ---- VehicleParametersDto deserialization ----

        [Fact]
        public void VehicleParametersDto_DeserializesFromJson()
        {
            const string json = """
                {
                    "Mass": 61000.0, "Length": 7.93, "Width": 3.66,
                    "MaxSpeedFwd": 20.0, "MaxSpeedRev": 12.0, "MaxAccel": 2.5
                }
                """;

            var dto = JsonSerializer.Deserialize<VehicleParametersDto>(json, FdpJsonOptionsRegistry.DefaultRelaxed);

            Assert.NotNull(dto);
            Assert.Equal(61000.0f, dto!.Mass);
            Assert.Equal(7.93f, dto.Length);
            Assert.Equal(3.66f, dto.Width);
            Assert.Equal(20.0f, dto.MaxSpeedFwd);
            Assert.Equal(12.0f, dto.MaxSpeedRev);
            Assert.Equal(2.5f, dto.MaxAccel);
        }

        // ---- WeaponCapabilitiesDto deserialization ----

        [Fact]
        public void WeaponCapabilitiesDto_DeserializesFromJson()
        {
            const string json = """{ "EffectiveRange": 3000.0, "RateOfFire": 6.0, "MagazineCapacity": 40 }""";

            var dto = JsonSerializer.Deserialize<WeaponCapabilitiesDto>(json, FdpJsonOptionsRegistry.DefaultRelaxed);

            Assert.NotNull(dto);
            Assert.Equal(3000.0f, dto!.EffectiveRange);
            Assert.Equal(6.0f, dto.RateOfFire);
            Assert.Equal(40, dto.MagazineCapacity);
        }

        // ---- AmmoWeaponBallisticsDto deserialization ----

        [Fact]
        public void AmmoWeaponBallisticsDto_DeserializesFromJson()
        {
            const string json = """{ "WeaponGuid": 2001, "MuzzleSpeed": 1500.0, "Damage": 600.0 }""";

            var dto = JsonSerializer.Deserialize<AmmoWeaponBallisticsDto>(json, FdpJsonOptionsRegistry.DefaultRelaxed);

            Assert.NotNull(dto);
            Assert.Equal(2001L, dto!.WeaponGuid);
            Assert.Equal(1500.0f, dto.MuzzleSpeed);
            Assert.Equal(600.0f, dto.Damage);
        }

        // ---- Attribute presence checks via reflection ----

        [Fact]
        public void TkbMasterDto_CarriesTkbDescriptorAttribute()
        {
            var attr = typeof(TkbMasterDto).GetCustomAttribute<TkbDescriptorAttribute>();
            Assert.NotNull(attr);
            Assert.Equal("TkbMaster", attr!.HierarchicalName);
        }

        [Fact]
        public void VehicleParametersDto_CarriesTkbDescriptorAttribute()
        {
            var attr = typeof(VehicleParametersDto).GetCustomAttribute<TkbDescriptorAttribute>();
            Assert.NotNull(attr);
            Assert.Equal("Gen.VehicleParameters", attr!.HierarchicalName);
        }

        [Fact]
        public void WeaponCapabilitiesDto_CarriesTkbDescriptorAttribute()
        {
            var attr = typeof(WeaponCapabilitiesDto).GetCustomAttribute<TkbDescriptorAttribute>();
            Assert.NotNull(attr);
            Assert.Equal("Gen.WeaponCapabilities", attr!.HierarchicalName);
        }

        [Fact]
        public void AmmoWeaponBallisticsDto_CarriesTkbDescriptorAttribute()
        {
            var attr = typeof(AmmoWeaponBallisticsDto).GetCustomAttribute<TkbDescriptorAttribute>();
            Assert.NotNull(attr);
            Assert.Equal("Gen.AmmoWeaponBallistics", attr!.HierarchicalName);
        }

        [Fact]
        public void AmmoWeaponBallisticsDto_WeaponGuidProperty_CarriesWeaponRefAttribute()
        {
            var prop = typeof(AmmoWeaponBallisticsDto).GetProperty(nameof(AmmoWeaponBallisticsDto.WeaponGuid))!;
            var attr = prop.GetCustomAttribute<WeaponRefAttribute>();
            Assert.NotNull(attr);
        }

        // ---- Negative: no ECS base class or MessagePackObject ----

        [Fact]
        public void NoDtoType_InheritsFromEcsOrUsesMessagePackObject()
        {
            var dtoTypes = new[]
            {
                typeof(TkbMasterDto),
                typeof(VehicleParametersDto),
                typeof(WeaponCapabilitiesDto),
                typeof(AmmoWeaponBallisticsDto),
            };

            foreach (var t in dtoTypes)
            {
                // Must not inherit from anything other than object/record base
                Assert.True(
                    t.BaseType == typeof(object) || (t.BaseType != null && t.BaseType.Name == "TkbMasterDto"),
                    $"{t.Name} must not inherit from an ECS base class.");

                // Must not have MessagePackObjectAttribute
                var allAttrs = t.GetCustomAttributes(inherit: false);
                foreach (var a in allAttrs)
                    Assert.False(
                        a.GetType().Name == "MessagePackObjectAttribute",
                        $"{t.Name} must not carry [MessagePackObject].");
            }
        }
    }
}
