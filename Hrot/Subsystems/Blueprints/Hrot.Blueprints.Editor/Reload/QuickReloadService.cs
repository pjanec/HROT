using System.Diagnostics;
using System.Runtime.Loader;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Reload;

public sealed class QuickReloadService
{
    private readonly IAssetCatalog _catalog;
    private readonly EditorState _editorState;
    private readonly IBlueprintDebugSession? _session;
    private readonly IOutputConsole _outputConsole;
    private readonly IBlueprintCompiler _compiler;
    private readonly AiHotReloadCoordinator _coordinator;

    // Internal test accessor: signatures built for the last reload.
    public IReadOnlyList<BlueprintSignature>? LastSignaturesUsedForTesting { get; private set; }

    public QuickReloadService(
        IAssetCatalog catalog,
        EditorState editorState,
        IOutputConsole outputConsole,
        IBlueprintCompiler compiler,
        AiHotReloadCoordinator coordinator,
        IBlueprintDebugSession? session = null)
    {
        _catalog       = catalog       ?? throw new ArgumentNullException(nameof(catalog));
        _editorState   = editorState   ?? throw new ArgumentNullException(nameof(editorState));
        _outputConsole = outputConsole ?? throw new ArgumentNullException(nameof(outputConsole));
        _compiler      = compiler      ?? throw new ArgumentNullException(nameof(compiler));
        _coordinator   = coordinator   ?? throw new ArgumentNullException(nameof(coordinator));
        _session       = session;
    }

    /// <summary>
    /// Triggers an in-memory quick reload for <paramref name="asset"/>.
    /// Compiles the asset, loads the result into a collectible ALC, registers
    /// the debug map, then hands off to the coordinator which handles staging
    /// and ALC swap atomically.
    /// </summary>
    public Task<QuickReloadResult> TriggerAsync(BlueprintAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        var sw = Stopwatch.StartNew();
        _outputConsole.LogInfo($"Quick Reload starting for '{asset.Name}'...");

        try
        {
            // Step 1: Build sibling signatures from catalog + in-memory overrides.
            var siblings = BuildSiblingSignatures(asset);

            // Step 2: Compile in memory with embedded PDB for debugger support.
            var options = new CompileOptions(
                Mode:                      CompilerMode.Debug,
                NodeRegistry:              BuiltInNodeRegistry.Instance,
                TypeRegistry:              StaticTypeRegistry.Instance,
                EngineEvents:              BuiltInEngineEventCatalog.Instance,
                ChannelCommands:           BuiltInChannelCommandCatalog.Instance,
                WaitPrimitives:            BuiltInWaitPrimitiveCatalog.Instance,
                SiblingSignatures:         siblings,
                EmitPdbWithEmbeddedSource: true);

            var result = _compiler.Compile(asset, options);
            if (!result.Succeeded)
            {
                foreach (var d in result.Diagnostics)
                    _outputConsole.LogError($"[{d.Code}] {d.Message}");
                sw.Stop();
                return Task.FromResult(new QuickReloadResult(false, "Compilation failed.", sw.ElapsedMilliseconds));
            }

            // Step 3: Load compiled PE + PDB into a new collectible ALC.
            var alc = new AssemblyLoadContext(
                $"BlueprintPatch_{result.BlueprintId:X8}_{Guid.NewGuid():N}",
                isCollectible: true);

            System.Reflection.Assembly assembly;
            using (var peStream  = new MemoryStream(result.PortablePe!))
            using (var pdbStream = new MemoryStream(result.PortablePdb ?? Array.Empty<byte>()))
            {
                assembly = result.PortablePdb != null
                    ? alc.LoadFromStream(peStream, pdbStream)
                    : alc.LoadFromStream(peStream);
            }

            // Step 4: Register debug map BEFORE coordinator handoff so that
            // OnReloadCompleted subscribers see a consistent state.
            if (result.DebugMap != null)
                _session?.RegisterDebugMap(result.DebugMap);

            // Step 5: Hand off to the coordinator. It handles HsmActionDispatcher.ClearAll(),
            // registrar scanning, staging, and ALC swap atomically.
            try
            {
                _coordinator.ApplyQuickReload(alc, assembly);
            }
            catch
            {
                // Rollback debug map on coordinator failure to avoid stale state.
                if (result.DebugMap != null)
                    _session?.UnregisterDebugMap(asset.AssetId);
                throw;
            }

            sw.Stop();
            _outputConsole.LogInfo($"Quick Reload completed in {sw.ElapsedMilliseconds}ms.");
            return Task.FromResult(new QuickReloadResult(true, null, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            _outputConsole.LogError($"Quick Reload failed: {ex.Message}");
            return Task.FromResult(new QuickReloadResult(false, ex.Message, sw.ElapsedMilliseconds));
        }
    }

    private IReadOnlyList<BlueprintSignature> BuildSiblingSignatures(BlueprintAsset editedAsset)
    {
        var signatures   = new List<BlueprintSignature>();
        bool editedIncluded = false;

        foreach (var entry in _catalog.EnumerateAll())
        {
            if (entry.AssetId == editedAsset.AssetId)
            {
                // Use the dirty in-memory version for the asset being reloaded.
                signatures.Add(BlueprintSignatureBuilder.FromInMemoryAsset(editedAsset));
                editedIncluded = true;
                continue;
            }

            // Use in-memory override if the asset has unsaved changes, else parse from disk.
            var inMemory = _editorState.GetInMemoryAsset(entry.AssetId);
            if (inMemory != null)
            {
                signatures.Add(BlueprintSignatureBuilder.FromInMemoryAsset(inMemory));
            }
            else if (File.Exists(entry.Path))
            {
                signatures.Add(BlueprintSignatureParser.Parse(entry.Path, File.ReadAllText(entry.Path)));
            }
        }

        // Include edited asset even when not in catalog (new unsaved assets).
        if (!editedIncluded)
            signatures.Add(BlueprintSignatureBuilder.FromInMemoryAsset(editedAsset));

        LastSignaturesUsedForTesting = signatures;
        return signatures;
    }
}

