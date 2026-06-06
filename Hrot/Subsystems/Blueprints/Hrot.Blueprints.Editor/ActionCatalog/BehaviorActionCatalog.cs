using System;
using System.Collections.Generic;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Blueprints.Editor.ActionCatalog;

/// <summary>
/// Composing implementation of <see cref="IBehaviorActionCatalog"/>.
/// </summary>
/// <remarks>
/// <para><b>Data sources:</b></para>
/// <list type="bullet">
///   <item>
///     <see cref="IChannelCommandCatalog"/> — contributes
///     <see cref="BehaviorActionSource.ChannelCommand"/> entries with
///     <see cref="BehaviorActionHosts.Blueprint"/> hosting.
///   </item>
///   <item>
///     <see cref="IActionSchemaExporter"/> — contributes
///     <see cref="BehaviorActionSource.Hardcoded"/> entries (includes post-reload
///     blueprint-authored AiPrimitive actions that appear in the same exporter after
///     compilation) with hosting derived from <see cref="ActionHosting"/> flags.
///   </item>
/// </list>
///
/// <para><b>Rebuild policy:</b>
/// The snapshot is rebuilt eagerly in the constructor (if the exporter has already been
/// populated) and whenever <see cref="IActionSchemaExporter.Changed"/> fires.
/// The channel-command catalog is re-read on every rebuild since it is a static list
/// that may be replaced by a new implementation between reloads.
/// </para>
///
/// <para><b>Thread safety:</b>
/// The snapshot field is replaced atomically (reference assignment).  Readers that call
/// <see cref="GetActions()"/> while a rebuild is in progress will see either the old or
/// the new snapshot — never a partially-constructed one.
/// </para>
/// </remarks>
public sealed class BehaviorActionCatalog : IBehaviorActionCatalog, IDisposable
{
    private readonly IChannelCommandCatalog _channelCatalog;
    private readonly IActionSchemaExporter  _schemaExporter;

    // Immutable snapshot; replaced atomically on every Rebuild.
    private volatile IReadOnlyList<BehaviorActionEntry> _snapshot
        = Array.Empty<BehaviorActionEntry>();

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>
    /// Creates a new catalog and performs an initial rebuild.
    /// Subscribes to <see cref="IActionSchemaExporter.Changed"/> to keep the snapshot fresh.
    /// </summary>
    /// <param name="channelCatalog">Source of channel-command entries.</param>
    /// <param name="schemaExporter">Source of hardcoded / AiPrimitive action entries.</param>
    public BehaviorActionCatalog(
        IChannelCommandCatalog channelCatalog,
        IActionSchemaExporter  schemaExporter)
    {
        _channelCatalog = channelCatalog ?? throw new ArgumentNullException(nameof(channelCatalog));
        _schemaExporter = schemaExporter  ?? throw new ArgumentNullException(nameof(schemaExporter));

        _schemaExporter.Changed += OnSchemaChanged;

        // Populate eagerly so the catalog is ready before any subscriber sees it.
        Rebuild();
    }

    // -------------------------------------------------------------------------
    // IBehaviorActionCatalog
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public IReadOnlyList<BehaviorActionEntry> GetActions() => _snapshot;

    /// <inheritdoc />
    public IReadOnlyList<BehaviorActionEntry> GetActions(BehaviorActionHosts host)
    {
        var all = _snapshot;
        var result = new List<BehaviorActionEntry>(all.Count);
        foreach (var entry in all)
        {
            if ((entry.ValidHosts & host) != 0)
                result.Add(entry);
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    private bool _disposed;

    /// <summary>Unsubscribes from <see cref="IActionSchemaExporter.Changed"/>.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _schemaExporter.Changed -= OnSchemaChanged;
        _disposed = true;
    }

    // -------------------------------------------------------------------------
    // Internal rebuild
    // -------------------------------------------------------------------------

    private void OnSchemaChanged() => Rebuild();

    /// <summary>
    /// Rebuilds the internal snapshot from both source catalogs and raises
    /// <see cref="Changed"/>.
    /// </summary>
    public void Rebuild()
    {
        var entries = new List<BehaviorActionEntry>();

        // ── 1. Channel-command entries ─────────────────────────────────────
        foreach (var cc in _channelCatalog.GetEntries())
        {
            // Canonical Id = "{ChannelTypeFqn}::{ActionId}"
            var id          = $"{cc.ChannelTypeFqn}::{cc.ActionId}";
            var displayName = cc.Name;
            // Category = short class name of the channel type
            var category    = ExtractShortTypeName(cc.ChannelTypeFqn);

            entries.Add(new BehaviorActionEntry(
                Id:             id,
                DisplayName:    displayName,
                Category:       category,
                ChannelTypeFqn: cc.ChannelTypeFqn,
                ActionId:       cc.ActionId,
                ParamsTypeFqn:  cc.ParamsTypeFqn,
                ValidHosts:     BehaviorActionHosts.Blueprint,
                Source:         BehaviorActionSource.ChannelCommand
            ));
        }

        // ── 2. Action-schema entries (Hardcoded + post-reload AiPrimitive) ─
        foreach (var kv in _schemaExporter.All)
        {
            var schema = kv.Value;

            var hosts = MapHosting(schema.Hosting);
            if (hosts == BehaviorActionHosts.None)
                continue; // no relevant host — skip

            // Canonical Id = FQN from the schema exporter
            var id          = schema.Fqn;
            // DisplayName = short method name portion of the FQN
            var displayName = ExtractMethodName(schema.Fqn);
            // Category = short declaring-type name portion of the FQN
            var category    = ExtractDeclaringTypeName(schema.Fqn);
            // ParamsTypeFqn = DtoType.FullName (fall back to AssemblyQualifiedName fragment)
            var paramsFqn   = schema.DtoType.FullName
                              ?? schema.DtoType.AssemblyQualifiedName
                              ?? schema.DtoType.Name;

            entries.Add(new BehaviorActionEntry(
                Id:             id,
                DisplayName:    displayName,
                Category:       category,
                ChannelTypeFqn: null,
                ActionId:       0,
                ParamsTypeFqn:  paramsFqn,
                ValidHosts:     hosts,
                Source:         BehaviorActionSource.Hardcoded
            ));
        }

        _snapshot = entries;
        Changed?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps <see cref="ActionHosting"/> flags to <see cref="BehaviorActionHosts"/>.
    /// <c>ActionHosting.BTree</c>  → <c>BehaviorActionHosts.BTree</c>;
    /// <c>ActionHosting.Hsm</c>   → <c>BehaviorActionHosts.Hsm</c>;
    /// <c>ActionHosting.Shared</c> → additionally <c>BehaviorActionHosts.Blueprint</c>
    ///   (AN7: <c>[SharedAiAction]</c> / AiPrimitive entries valid in Blueprint graphs).
    /// <c>ActionHosting.Heavy</c> is a modifier, not a host; it does not add a new host.
    /// </summary>
    private static BehaviorActionHosts MapHosting(ActionHosting hosting)
    {
        var result = BehaviorActionHosts.None;
        if ((hosting & ActionHosting.BTree) != 0) result |= BehaviorActionHosts.BTree;
        if ((hosting & ActionHosting.Hsm)   != 0) result |= BehaviorActionHosts.Hsm;
        // AN7: Shared actions (SharedAiAction / AiPrimitive with BlueprintCall hosting) are
        // also valid in Blueprint graphs — they are non-channel behavior actions that the
        // generalized ChannelCommandNode (via ActionFqn) can invoke.
        if ((hosting & ActionHosting.Shared) != 0) result |= BehaviorActionHosts.Blueprint;
        return result;
    }

    /// <summary>
    /// Extracts the short class name from a fully-qualified type name.
    /// E.g. <c>"Fdp.Toolkit.Behavior.Components.LocomotionChannel"</c> → <c>"LocomotionChannel"</c>.
    /// </summary>
    private static string ExtractShortTypeName(string fqn)
    {
        var dot = fqn.LastIndexOf('.');
        return dot >= 0 ? fqn[(dot + 1)..] : fqn;
    }

    /// <summary>
    /// Extracts the method name from a schema FQN of the form
    /// <c>"{TypeFullName}.{MethodName}"</c>.
    /// </summary>
    private static string ExtractMethodName(string fqn)
    {
        var dot = fqn.LastIndexOf('.');
        return dot >= 0 ? fqn[(dot + 1)..] : fqn;
    }

    /// <summary>
    /// Extracts the declaring-type short name from a schema FQN of the form
    /// <c>"{Namespace}.{TypeName}.{MethodName}"</c>.
    /// Returns the penultimate segment (the type name).
    /// </summary>
    private static string ExtractDeclaringTypeName(string fqn)
    {
        var last = fqn.LastIndexOf('.');
        if (last < 0) return fqn;
        var prefix     = fqn[..last];
        var penultimate = prefix.LastIndexOf('.');
        return penultimate >= 0 ? prefix[(penultimate + 1)..] : prefix;
    }
}
