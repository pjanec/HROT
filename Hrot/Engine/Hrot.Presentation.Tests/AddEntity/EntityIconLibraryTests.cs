using System.Numerics;
using Fdp.Toolkit.Tkb;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Tkb;
using Hrot.UI.Common.AddEntity;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ <c>CE-1017</c> S5 — the entity icon library serves every icon key the Add Entity catalog asks for.
/// 📄 docs/DESIGN_Add_Entity_Picker.md D3c, S5.
/// </summary>
public sealed class EntityIconLibraryTests
{
    /// <summary>A fake GPU: every upload succeeds with a fresh id and records which bytes it got.</summary>
    private sealed class FakeGpu
    {
        public readonly List<byte[]> Uploads = new();
        public (nint, int, int)? Upload(byte[] png) { Uploads.Add(png); return (Uploads.Count, 64, 64); }
    }

    private sealed class Table : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle)
        {
            handle = new IconHandle(999, 16, 16, Vector2.Zero, Vector2.One);
            return key == "folder";
        }
    }

    private static string EmptyDir() => Directory.CreateTempSubdirectory("entity-icons").FullName;

    [Fact]
    public void CE1017_EveryIconKeyTheBuiltInCatalogAsksFor_HasEmbeddedArt()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        UrbanCombatTkbCatalog.RegisterAll(db);

        foreach (var e in EntityTypeCatalog.Build(db))
        {
            var name = EntityTypeCatalog.IconKeyOf(e)[EntityIconLibrary.Prefix.Length..];
            Assert.True(EntityIconLibrary.EmbeddedNames.Contains(name), $"'{e.Name}' asks for '{name}', which has no PNG");
        }
    }

    [Fact]
    public void CE1017_ATypeWithNoTkbIcon_GetsAGlyphFromWhatItIs()
    {
        var db = new TkbDatabase();
        UrbanCombatTkbCatalog.RegisterAll(db);
        var c = EntityTypeCatalog.Build(db);

        Assert.Equal("_person",  c.Single(e => e.Name == "InfantrySoldier").IconName);
        Assert.Equal("_wheeled", c.Single(e => e.Name == "CivilianCar").IconName);
        Assert.Equal("_afv",     c.Single(e => e.Name == "MilitaryAPC").IconName);
    }

    [Fact]
    public void CE1017_AnEntityKey_IsUploadedOnce_AndCached()
    {
        var gpu = new FakeGpu();
        var lib = new EntityIconLibrary(overrideDirectory: EmptyDir(), upload: gpu.Upload);

        Assert.True(lib.TryGet("entity/t72", out var a));
        Assert.True(lib.TryGet("entity/t72", out var b));

        Assert.Single(gpu.Uploads);
        Assert.Equal(a.TextureId, b.TextureId);
        Assert.Equal(Vector2.One, a.Uv1);
    }

    [Fact]
    public void CE1017_ANameWithNoArt_FallsBackToTheMarker()
    {
        var gpu = new FakeGpu();
        var lib = new EntityIconLibrary(overrideDirectory: EmptyDir(), upload: gpu.Upload);

        Assert.True(lib.TryGet("entity/no_such_icon", out _));
        Assert.Single(gpu.Uploads);   // the _point marker
    }

    [Fact]
    public void CE1017_AFileInTheOverrideFolder_WinsOverTheEmbeddedArt()
    {
        var dir = EmptyDir();
        var mine = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(Path.Combine(dir, "t72.png"), mine);
        var gpu = new FakeGpu();

        new EntityIconLibrary(overrideDirectory: dir, upload: gpu.Upload).TryGet("entity/t72", out _);

        Assert.Equal(mine, Assert.Single(gpu.Uploads));
    }

    [Fact]
    public void CE1017_OtherKeys_GoToTheHostProvider_AndAFailedUploadIsNoIcon()
    {
        var lib = new EntityIconLibrary(new Table(), EmptyDir(), upload: _ => null);

        Assert.True(lib.TryGet("folder", out var folder));
        Assert.Equal(999, (int)folder.TextureId);
        Assert.False(lib.TryGet("entity/t72", out _));   // headless GL: id 0 ⇒ nothing drawn, no crash
    }
}
