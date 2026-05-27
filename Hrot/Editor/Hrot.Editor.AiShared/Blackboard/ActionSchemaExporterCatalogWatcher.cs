using System;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Subscribes to <see cref="IAssetCatalog.Changed"/> and triggers
/// <see cref="IActionSchemaExporter.Rebuild"/> whenever the catalog changes.
/// Dispose to unsubscribe and prevent event-handler leaks.
/// </summary>
public sealed class ActionSchemaExporterCatalogWatcher : IDisposable
{
    private readonly IActionSchemaExporter _exporter;
    private readonly IAssetCatalog _catalog;
    private bool _disposed;

    public ActionSchemaExporterCatalogWatcher(
        IActionSchemaExporter exporter,
        IAssetCatalog catalog)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _catalog  = catalog  ?? throw new ArgumentNullException(nameof(catalog));

        _catalog.Changed += OnCatalogChanged;
    }

    private void OnCatalogChanged()
    {
        _exporter.Rebuild();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _catalog.Changed -= OnCatalogChanged;
        _disposed = true;
    }
}
