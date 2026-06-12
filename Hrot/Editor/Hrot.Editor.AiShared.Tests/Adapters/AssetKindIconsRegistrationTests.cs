using Fdp.Presentation.Icons;
using Hrot.Editor.AiShared.Adapters;

namespace Hrot.Editor.AiShared.Tests.Adapters;

/// <summary>
/// Verifies that the 6 asset-kind icon keys + folder/folder_open resolve
/// to pairwise-distinct atlas cells (DBT-1 testable part).
/// </summary>
public sealed class AssetKindIconsRegistrationTests
{
    private static IconAtlas MakeAtlas(nint textureId = 99)
        => new IconAtlas(textureId, atlasWidth: 256f, atlasHeight: 256f, iconSize: 16f);

    // ── DBT-1a: asset-kind icons ──────────────────────────────────────

    [Fact]
    public void EachAssetKind_ResolvesToDistinctIcon_NoSharedCell()
    {
        var provider = new SilkIconProvider(MakeAtlas());
        var kinds = Enum.GetValues<AssetKind>();

        var cells = new Dictionary<AssetKind, string>();

        foreach (var kind in kinds)
        {
            var key = AssetKindIcons.GetIconKey(kind);

            // Must resolve via TryGet.
            Assert.True(
                provider.TryGet(key, out _),
                $"Icon key '{key}' for AssetKind.{kind} must resolve via TryGet.");

            // Must be present in the cell map.
            Assert.True(
                provider.KeyToCellMap.ContainsKey(key),
                $"Icon key '{key}' for AssetKind.{kind} must be in KeyToCellMap.");

            cells[kind] = provider.KeyToCellMap[key];
        }

        // All 6 cells must be pairwise distinct.
        Assert.Equal(6, cells.Count);
        var distinctCells = new HashSet<string>(cells.Values);
        Assert.True(
            distinctCells.Count == cells.Count,
            $"Expected {cells.Count} distinct atlas cells, but found only " +
            $"{distinctCells.Count}. Duplicates: " +
            string.Join(", ", cells.GroupBy(kv => kv.Value)
                .Where(g => g.Count() > 1)
                .Select(g => $"cell {g.Key} used by [{string.Join(", ", g.Select(x => x.Key))}]")));
    }

    // ── DBT-1b: folder icons ──────────────────────────────────────────

    [Fact]
    public void FolderIcons_ResolveAndAreDistinct()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        // Both folder keys must resolve.
        Assert.True(provider.TryGet("folder", out _),
            "Key 'folder' must resolve via TryGet.");
        Assert.True(provider.TryGet("folder_open", out _),
            "Key 'folder_open' must resolve via TryGet.");

        Assert.True(provider.KeyToCellMap.ContainsKey("folder"),
            "Key 'folder' must be in KeyToCellMap.");
        Assert.True(provider.KeyToCellMap.ContainsKey("folder_open"),
            "Key 'folder_open' must be in KeyToCellMap.");

        var folderCell = provider.KeyToCellMap["folder"];
        var folderOpenCell = provider.KeyToCellMap["folder_open"];

        // Folder cells must differ from each other.
        Assert.NotEqual(folderCell, folderOpenCell);

        // Folder cells must also be distinct from all 6 asset-kind cells
        // (the full 8-element set must be pairwise distinct — DBT-1).
        var assetKindCells = new HashSet<string>();
        foreach (var kind in Enum.GetValues<AssetKind>())
        {
            var key = AssetKindIcons.GetIconKey(kind);
            assetKindCells.Add(provider.KeyToCellMap[key]);
        }

        Assert.DoesNotContain(folderCell, assetKindCells);
        Assert.DoesNotContain(folderOpenCell, assetKindCells);
    }

    // ── BATCH-31 (MTB2-T2): shell/save icon ────────────────────────────

    [Fact]
    public void ShellSave_Icon_Resolves_DistinctCell()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        // shell/save must resolve via TryGet.
        Assert.True(provider.TryGet("shell/save", out _),
            "Key 'shell/save' must resolve via TryGet.");

        // shell/save must be in the cell map.
        Assert.True(provider.KeyToCellMap.ContainsKey("shell/save"),
            "Key 'shell/save' must be in KeyToCellMap.");

        var saveCell = provider.KeyToCellMap["shell/save"];

        // Collect all asset-kind cells.
        var assetKindCells = new HashSet<string>();
        foreach (var kind in Enum.GetValues<AssetKind>())
        {
            var key = AssetKindIcons.GetIconKey(kind);
            assetKindCells.Add(provider.KeyToCellMap[key]);
        }

        // shell/save cell must be distinct from every asset-kind cell.
        Assert.DoesNotContain(saveCell, assetKindCells);

        // shell/save cell must be distinct from folder and folder_open.
        Assert.NotEqual(saveCell, provider.KeyToCellMap["folder"]);
        Assert.NotEqual(saveCell, provider.KeyToCellMap["folder_open"]);
    }
}
