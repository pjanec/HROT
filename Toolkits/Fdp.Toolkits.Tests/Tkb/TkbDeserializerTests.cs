using System;
using System.Text;
using System.Text.Json;
using Fdp.Core.Serialization;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Vfs;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests
{
    [CollectionDefinition("TkbDeserializerTests")]
    public class TkbDeserializerCollection { }

    public class TkbDeserializerFixture : IDisposable
    {
        public TkbDeserializerFixture()
        {
            TkbDescriptorRegistry.Clear();
            TkbDescriptorRegistry.RegisterParser("TkbMaster", (template, partId, elem) =>
            {
                var dto = elem.Deserialize<TkbMasterDto>(FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });
            TkbDescriptorRegistry.RegisterParser("Gen.VehicleParameters", (template, partId, elem) =>
            {
                var dto = elem.Deserialize<VehicleParametersDto>(FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });
            TkbDescriptorRegistry.RegisterParser("Gen.WeaponCapabilities", (template, partId, elem) =>
            {
                var dto = elem.Deserialize<WeaponCapabilitiesDto>(FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });
            TkbDescriptorRegistry.RegisterParser("Gen.AmmoWeaponBallistics", (template, partId, elem) =>
            {
                var dto = elem.Deserialize<AmmoWeaponBallisticsDto>(FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });
        }

        public void Dispose()
        {
            TkbDescriptorRegistry.Clear();
        }
    }

    [Collection("TkbDeserializerTests")]
    public class TkbDeserializerTests : IClassFixture<TkbDeserializerFixture>
    {
        private readonly TkbDeserializer _deserializer = new();
        private readonly TkbDatabase _db = new();

        // ---- Inline JSON fixtures ----

        private const string AbramsJson = """
            {
              "$guid": 100,
              "TkbMaster": {
                "CustomName": "M1 Abrams"
              },
              "Gen.VehicleParameters": {
                "Mass": 61000.0,
                "Length": 7.93,
                "Width": 3.66,
                "MaxSpeedFwd": 20.0,
                "MaxSpeedRev": 12.0,
                "MaxAccel": 2.5
              },
              "Gen.WeaponCapabilities": {
                "EffectiveRange": 3000.0,
                "RateOfFire": 6.0,
                "MagazineCapacity": 42
              },
              "_EditorMetadata": { "Author": "test" }
            }
            """;

        private const string MissingGuidJson = """
            {
              "Gen.VehicleParameters": {
                "Mass": 1000.0
              }
            }
            """;

        private const string UnknownDescJson = """
            {
              "$guid": 200,
              "CGFX.ABSTRACT_ENTITY": { "foo": "bar" },
              "Future.NotYetRegistered": { "x": 1 }
            }
            """;

        private const string AmmoJson = """
            {
              "$guid": 300,
              "Gen.AmmoWeaponBallistics#1": {
                "WeaponGuid": 10,
                "MuzzleSpeed": 1700.0,
                "Damage": 500.0
              },
              "Gen.AmmoWeaponBallistics#2": {
                "WeaponGuid": 11,
                "MuzzleSpeed": 1600.0,
                "Damage": 450.0
              }
            }
            """;

        public TkbDeserializerTests(TkbDeserializerFixture _) { }

        private static TkbEntityFile MakeFile(string fileName, string json,
            string categoryPath = "Test/Category")
        {
            var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json));
            return new TkbEntityFile(categoryPath, fileName, stream);
        }

        private static string SmallEntityJson(int i) =>
            $"{{\"$guid\":{1000 + i},\"TkbMaster\":{{\"CustomName\":\"Entity{i}\"}}}}";

        // ---- Registration / parsing ----

        [Fact]
        public void ParseAndRegister_ValidEntity_TemplateHasCorrectTkbType()
        {
            _deserializer.ParseAndRegister(MakeFile("M1_Abrams", AbramsJson), _db);

            Assert.Equal(100L, _db.GetByType(100).TkbType);
        }

        [Fact]
        public void ParseAndRegister_ValidEntity_TemplateHasCorrectCategoryPath()
        {
            _deserializer.ParseAndRegister(MakeFile("M1_Abrams", AbramsJson, "Platform/Vehicle"), _db);

            Assert.Equal("Platform/Vehicle", _db.GetByType(100).CategoryPath);
        }

        [Fact]
        public void ParseAndRegister_ValidEntity_HasVehicleParametersDto()
        {
            _deserializer.ParseAndRegister(MakeFile("M1_Abrams", AbramsJson), _db);
            var template = _db.GetByType(100);

            Assert.True(template.HasDescriptor<VehicleParametersDto>());
            Assert.Equal(61000f, template.GetDescriptor<VehicleParametersDto>()!.Mass);
        }

        [Fact]
        public void ParseAndRegister_ValidEntity_HasTkbMasterDto()
        {
            _deserializer.ParseAndRegister(MakeFile("M1_Abrams", AbramsJson), _db);

            Assert.Equal("M1 Abrams", _db.GetByType(100).GetDescriptor<TkbMasterDto>()!.CustomName);
        }

        [Fact]
        public void ParseAndRegister_ValidEntity_HasWeaponCapabilitiesDto()
        {
            _deserializer.ParseAndRegister(MakeFile("M1_Abrams", AbramsJson), _db);
            var dto = _db.GetByType(100).GetDescriptor<WeaponCapabilitiesDto>()!;

            Assert.Equal(3000f, dto.EffectiveRange);
            Assert.Equal(42, dto.MagazineCapacity);
        }

        // ---- Error handling ----

        [Fact]
        public void ParseAndRegister_MissingGuid_ThrowsTkbFormatException()
        {
            Assert.Throws<TkbFormatException>(() =>
                _deserializer.ParseAndRegister(MakeFile("Missing", MissingGuidJson), _db));
        }

        // ---- Skip logic ----

        [Fact]
        public void ParseAndRegister_UnknownDescriptors_ParsesWithoutThrowing()
        {
            _deserializer.ParseAndRegister(MakeFile("Unknown", UnknownDescJson), _db);
            var template = _db.GetByType(200);

            Assert.Equal(200L, template.TkbType);
            Assert.False(template.HasDescriptor<VehicleParametersDto>());
            Assert.False(template.HasDescriptor<WeaponCapabilitiesDto>());
        }

        [Fact]
        public void ParseAndRegister_MetadataKey_IsSkipped()
        {
            _deserializer.ParseAndRegister(MakeFile("M1_Abrams", AbramsJson), _db);
            var template = _db.GetByType(100);

            // Verify standard descriptors are present; the _EditorMetadata field was silently skipped.
            Assert.True(template.HasDescriptor<TkbMasterDto>());
            Assert.True(template.HasDescriptor<VehicleParametersDto>());
        }

        // ---- Multi-part ----

        [Fact]
        public void ParseAndRegister_MultiplePartIds_BothAmmoBallisticsStored()
        {
            _deserializer.ParseAndRegister(MakeFile("120mm_APFSDS", AmmoJson), _db);
            var template = _db.GetByType(300);

            Assert.True(template.HasDescriptor<AmmoWeaponBallisticsDto>(partId: 1));
            Assert.True(template.HasDescriptor<AmmoWeaponBallisticsDto>(partId: 2));
            Assert.Equal(10L, template.GetDescriptor<AmmoWeaponBallisticsDto>(partId: 1)!.WeaponGuid);
            Assert.Equal(11L, template.GetDescriptor<AmmoWeaponBallisticsDto>(partId: 2)!.WeaponGuid);
        }

        // ---- LOH / hot-path ----

        [Fact]
        public void ParseAndRegister_LargeVolume_DoesNotAllocateOnLargeObjectHeap()
        {
            const int count = 10_000;
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < count; i++)
                {
                    var file = MakeFile($"Entity_{i}", SmallEntityJson(i), "Platform");
                    _deserializer.ParseAndRegister(file, _db);
                }
                long after = GC.GetAllocatedBytesForCurrentThread();
                long perEntity = (after - before) / count;

                // Heuristic: no individual entity parse should cause LOH allocations (>= 85,000 bytes).
                // GC behavior can vary; this is a best-effort regression guard, not a strict guarantee.
                Assert.True(perEntity < 85_000,
                    $"Average allocation per entity ({perEntity} bytes) must be below LOH threshold (85,000 bytes).");
            }
            finally
            {
                _db.Clear();
            }
        }
    }
}
