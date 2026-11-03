using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Infrastructure;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐ P-7 / F-28′ (📄 docs/DESIGN_Node_Features.md §11) — hands the loaded scenario's header <c>features</c> to THIS node's <see cref="NodeFeatures"/>, on EVERY ECS node.
/// ⛔ Not part of <see cref="ScenarioLoadStep"/>: that step is composed only where a scenario extractor, source and id allocator exist (the node that LOADS the entities), so the preset
/// reached the CGF node alone — measured live, SimHost and IG kept their role defaults. The HUD draws on the nodes that get their entities by replication.
/// <para>Prepare reads the header from the node's OWN staged copy of the scenario (the load is dispatched only after every node acknowledged its files): the first staged file that carries
/// <c>features</c> — a node holds the files of several subsystems and the author wrote the field once. No file, a new scenario or an unreadable one ⇒ no preset (never a throw: a node may
/// legitimately hold none). Commit calls <see cref="NodeFeatures.SetScenarioPreset"/>, so every load replaces the last scenario's preset and an abort leaves it.</para>
/// </summary>
public sealed class ScenarioFeaturePresetStep : ILoadPartProvider
{
    private readonly IScenarioStorageProvider _storage;
    private readonly Func<NodeFeatures?> _features;
    private System.Collections.Generic.IReadOnlyDictionary<string, string>? _pending;
    private Guid? _pendingTransactionId;
    private bool _prepared;

    /// <param name="features">The node's feature reader (<c>HrotNodeContext.Features</c> — built after this step, hence a delegate). ⛔ A caller that HAS a context passes it.</param>
    public ScenarioFeaturePresetStep(IScenarioStorageProvider storage, Func<NodeFeatures?> features)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _features = features ?? throw new ArgumentNullException(nameof(features));
    }

    /// <inheritdoc/>
    public LoadPart Part => LoadPart.ScenarioFeatures;

    /// <inheritdoc/>
    public bool PreparesOffFrameThread => true;   // reads files into a private field; the shared write is Commit's

    /// <inheritdoc/>
    public Task PrepareAsync(LoadPhaseContext context, CancellationToken ct)
    {
        _pending = null;
        _pendingTransactionId = context.TransactionId;
        _prepared = true;
        if (context.IsNewScenario || string.IsNullOrWhiteSpace(context.ScenarioId)) return Task.CompletedTask;
        try
        {
            foreach (var file in _storage.EnumerateScenarioFiles(context.ScenarioId))
            {
                using var stream = _storage.OpenScenarioFile(context.ScenarioId, Path.GetFileName(file));
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                var features = ScenarioHeaderJson.Read(JsonNode.Parse(reader.ReadToEnd()) as JsonObject)?.Features;
                if (features is { Count: > 0 }) { _pending = features; break; }
            }
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _pending = null;   // an unreadable copy is "no preset", not a failed load
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Commit(LoadPhaseContext context, EntityRepository? world)
    {
        if (!_prepared || (_pendingTransactionId != null && _pendingTransactionId != context.TransactionId)) return;   // a stale batch of another transaction is not ours to apply
        _features()?.SetScenarioPreset(_pending);
    }

    /// <inheritdoc/>
    public void Abort(LoadPhaseContext context)
    {
        _pending = null;
        _pendingTransactionId = null;
        _prepared = false;
    }
}
