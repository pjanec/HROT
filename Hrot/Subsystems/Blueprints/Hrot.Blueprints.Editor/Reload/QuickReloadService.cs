using System.Diagnostics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Reload;

public sealed class QuickReloadService
{
    private readonly IAssetCatalog _catalog;
    private readonly EditorState _editorState;
    private readonly IBlueprintDebugSession? _session;
    private readonly IOutputConsole _outputConsole;

    // Internal test accessor: signatures built for the last reload.
    public IReadOnlyList<BlueprintSignature>? LastSignaturesUsedForTesting { get; private set; }

    public QuickReloadService(
        IAssetCatalog catalog,
        EditorState editorState,
        IOutputConsole outputConsole,
        IBlueprintDebugSession? session = null)
    {
        _catalog       = catalog       ?? throw new ArgumentNullException(nameof(catalog));
        _editorState   = editorState   ?? throw new ArgumentNullException(nameof(editorState));
        _outputConsole = outputConsole ?? throw new ArgumentNullException(nameof(outputConsole));
        _session       = session;
    }

    /// <summary>
    /// Triggers an in-memory quick reload for <paramref name="asset"/>.
    /// Slice 1: logs the intent and returns a stub result.
    /// </summary>
    public Task<QuickReloadResult> TriggerAsync(BlueprintAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        var sw = Stopwatch.StartNew();
        _outputConsole.LogInfo($"Quick reload requested for asset {asset.AssetId}.");
        sw.Stop();
        return Task.FromResult(new QuickReloadResult(
            Succeeded:    false,
            ErrorMessage: "QuickReload pipeline not yet wired (Slice 1 stub).",
            DurationMs:   sw.ElapsedMilliseconds));
    }
}
