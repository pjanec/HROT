using System.Text.Json;

namespace Hrot.Blueprints.Editor;

public sealed class FileSystemAssetCatalog : IAssetCatalog
{
    private readonly string _rootDirectory;

    public FileSystemAssetCatalog(string rootDirectory)
    {
        _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
    }

    public IEnumerable<AssetCatalogEntry> EnumerateAll()
    {
        if (!Directory.Exists(_rootDirectory))
            yield break;

        foreach (var filePath in Directory.EnumerateFiles(
            _rootDirectory, "*.bp.json", SearchOption.AllDirectories))
        {
            Guid assetId;
            try
            {
                // Attempt to read AssetId from the JSON file header.
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("AssetId", out var idEl) ||
                    !idEl.TryGetGuid(out assetId))
                    continue;
            }
            catch
            {
                continue;  // Skip unreadable/malformed files.
            }

            yield return new AssetCatalogEntry(assetId, filePath);
        }
    }
}
