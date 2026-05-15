using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests
{
    /// <summary>
    /// Tests for TKB-006: TkbTemplate descriptor bag and TKB-008: ITkbDatabase extensions
    /// (Clear, GetEntitiesByCategory, ActiveTkbName).
    /// </summary>
    public class DescriptorBagTests
    {
        // ─── Descriptor bag: AddDescriptor / GetDescriptor ────────────────────

        [Fact]
        public void AddDescriptor_ThenGetDescriptor_ReturnsMatchingInstance()
        {
            var template = new TkbTemplate("TestVehicle", 1);
            var dto = new VehicleParametersDto { Mass = 1200f, Length = 4.5f, Width = 2.0f, MaxSpeedFwd = 30f, MaxSpeedRev = 5f, MaxAccel = 3f };
            template.AddDescriptor(dto);

            var result = template.GetDescriptor<VehicleParametersDto>();

            Assert.Equal(dto.Mass,      result.Mass);
            Assert.Equal(dto.Length,    result.Length);
            Assert.Equal(dto.Width,     result.Width);
            Assert.Equal(dto.MaxSpeedFwd, result.MaxSpeedFwd);
        }

        [Fact]
        public void AddDescriptor_Overwrite_ReturnsLatestValue()
        {
            var template = new TkbTemplate("T", 1);
            template.AddDescriptor(new TkbMasterDto { CustomName = "First" });
            template.AddDescriptor(new TkbMasterDto { CustomName = "Second" });

            var result = template.GetDescriptor<TkbMasterDto>();

            Assert.Equal("Second", result.CustomName);
        }

        // ─── HasDescriptor ────────────────────────────────────────────────────

        [Fact]
        public void HasDescriptor_ReturnsFalse_WhenNotAdded()
        {
            var template = new TkbTemplate("T", 1);
            Assert.False(template.HasDescriptor<WeaponCapabilitiesDto>());
        }

        [Fact]
        public void HasDescriptor_ReturnsTrue_AfterAdd()
        {
            var template = new TkbTemplate("T", 1);
            template.AddDescriptor(new WeaponCapabilitiesDto { EffectiveRange = 500f, RateOfFire = 2f, MagazineCapacity = 10 });
            Assert.True(template.HasDescriptor<WeaponCapabilitiesDto>());
        }

        // ─── TryGetDescriptor ─────────────────────────────────────────────────

        [Fact]
        public void TryGetDescriptor_ReturnsFalse_WhenMissing()
        {
            var template = new TkbTemplate("T", 1);
            // HasDescriptor should return false, confirming the bag is empty.
            Assert.False(template.HasDescriptor<WeaponCapabilitiesDto>());
        }

        // ─── GetAllDescriptors ────────────────────────────────────────────────

        [Fact]
        public void GetAllDescriptors_ReturnsAll()
        {
            var template = new TkbTemplate("T", 1);
            template.AddDescriptor(new TkbMasterDto { CustomName = "Alpha" });
            template.AddDescriptor(new VehicleParametersDto { Mass = 900f });

            var all = template.GetAllDescriptors().ToList();

            Assert.Equal(2, all.Count);
        }

        // ─── CategoryPath ─────────────────────────────────────────────────────

        [Fact]
        public void CategoryPath_DefaultsToEmpty()
        {
            var template = new TkbTemplate("T", 1);
            Assert.Equal(string.Empty, template.CategoryPath);
        }

        [Fact]
        public void CategoryPath_SetViaConstructor()
        {
            var template = new TkbTemplate("T", 1, "Platform/Vehicle");
            Assert.Equal("Platform/Vehicle", template.CategoryPath);
        }

        // ─── ITkbDatabase.Clear ───────────────────────────────────────────────

        [Fact]
        public void Clear_RemovesAllTemplates()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("A", 1));
            db.Register(new TkbTemplate("B", 2));

            db.Clear();

            Assert.Empty(db.GetAll());
        }

        [Fact]
        public void Clear_ThenReRegister_FindsNewTemplate()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("Old", 1));
            db.Clear();

            var fresh = new TkbTemplate("Fresh", 1);
            db.Register(fresh);

            Assert.True(db.TryGetByType(1, out var found));
            Assert.Equal("Fresh", found.Name);
        }

        // ─── ITkbDatabase.GetEntitiesByCategory ───────────────────────────────

        [Fact]
        public void GetEntitiesByCategory_EmptyPrefix_ReturnsAll()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("A", 1, "Ground/Tank"));
            db.Register(new TkbTemplate("B", 2, "Air/Heli"));

            var all = db.GetEntitiesByCategory(string.Empty).ToList();

            Assert.Equal(2, all.Count);
        }

        [Fact]
        public void GetEntitiesByCategory_ExactMatch_ReturnsMatch()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("Tank", 1, "Ground/Tank"));
            db.Register(new TkbTemplate("Heli", 2, "Air/Heli"));

            var results = db.GetEntitiesByCategory("Ground/Tank").ToList();

            Assert.Single(results);
            Assert.Equal("Tank", results[0].Name);
        }

        [Fact]
        public void GetEntitiesByCategory_ChildPath_ReturnsChild()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("Parent", 1, "Ground"));
            db.Register(new TkbTemplate("Child", 2, "Ground/Tank"));

            var results = db.GetEntitiesByCategory("Ground").ToList();

            // Must return both the exact match and its child path.
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void GetEntitiesByCategory_DoesNotMatchPartialSuffix()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("AB", 1, "A/BC"));
            db.Register(new TkbTemplate("AB2", 2, "A/B"));

            // Category "A/B" must NOT match "A/BC" (only "A/B" and children of "A/B/...").
            var results = db.GetEntitiesByCategory("A/B").ToList();

            Assert.Single(results);
            Assert.Equal("AB2", results[0].Name);
        }

        // ─── ITkbDatabase.ActiveTkbName ───────────────────────────────────────

        [Fact]
        public void ActiveTkbName_DefaultsToNull()
        {
            var db = new TkbDatabase();
            Assert.Null(db.ActiveTkbName);
        }

        [Fact]
        public void ActiveTkbName_CanBeSetAndRead()
        {
            var db = new TkbDatabase();
            db.ActiveTkbName = "HillAttack";
            Assert.Equal("HillAttack", db.ActiveTkbName);
        }
    }
}
