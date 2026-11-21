using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Debug;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Blueprints.Editor.Inspector;

/// <summary>
/// Blueprint-specific pane that plugs into the shared Runtime Inspector window.
/// Shows the selected entity's attached-blueprint working-state (field values + latent cursor).
///
/// While the session is paused and the virtual node pointer is active, the pane shows the
/// pointer's per-node snapshot (via <see cref="ResolveInspectorSnapshot"/>). When not paused,
/// or when <see cref="IBlueprintDebugSession.GetCurrentStateSnapshot"/> returns null (e.g. the
/// selected entity differs from the paused entity), it falls back to live state via
/// <see cref="IBlueprintDebugSession.CaptureLiveState"/>.
///
/// The pane is registered next to BTree/HSM panes and the window selects it when the active
/// asset kind is <see cref="AssetKind.Blueprint"/>.
///
/// Field projection (<see cref="ProjectFields"/>) and snapshot resolution
/// (<see cref="ResolveInspectorSnapshot"/>) are ImGui-free and independently testable.
/// </summary>
public sealed class BlueprintRuntimeInspectorPane : IRuntimeInspectorPane
{
    private IBlueprintDebugSession? _session;

    /// <summary>Resolves the currently selected entity for the Blueprint perspective.</summary>

    /// <summary>Resolves the active blueprint asset id from the active canvas context.</summary>
    private Func<Guid?>? _activeAssetIdResolver;

    public AssetKind TargetKind => AssetKind.Blueprint;

    /// <summary>Sets the debug session used for live state reads.</summary>
    public void SetSession(IBlueprintDebugSession? session) => _session = session;

    /// <summary>
    /// ⭐ Sets the delegate that resolves the ACTIVE ASSET id at draw time.
    ///
    /// <para>⚠⚠ <b><c>CE-303</c> REMOVED the <c>selectedEntityResolver</c> half.</b> 🔴 It read
    /// <c>EditorSelectionStore.SelectedEntity</c> — a GLOBAL — so a PINNED copy of this view rendered
    /// whatever was selected NOW instead of the entity it was pinned to. ⭐ The entity now arrives with
    /// the <see cref="Hrot.Editor.AiShared.Shell.DetailsContext"/>, which is LIVE when docked and
    /// FROZEN when pinned. 📄 <c>DESIGN_Editor_Entity_Selection_Source.md</c> §11.</para>
    ///
    /// <para>⚠ The ASSET id stays a resolver: it follows the active DOCUMENT, not the selection, and
    /// <c>DetailsContext.Asset</c> is the shell's <c>IEditableAsset</c> rather than the
    /// <c>Guid</c> this pane resolves snapshots by.</para>
    /// </summary>
    public void SetResolvers(Func<Guid?> activeAssetIdResolver)
    {
        _activeAssetIdResolver = activeAssetIdResolver ?? throw new ArgumentNullException(nameof(activeAssetIdResolver));
    }

    // ---- IRuntimeInspectorPane ---------------------------------------------

    /// <summary>
    /// ⭐ The entity this pane draws about — the context's primary, or <c>null</c> when it names none.
    /// ⛔ Exposed as a method rather than inlined so a rail can assert the rule without an ImGui context,
    /// which <see cref="Draw"/> returns early without.
    /// </summary>
    internal static Entity? ResolveEntity(Hrot.Editor.AiShared.Shell.DetailsContext context)
        => context.Entities is { Count: > 0 } ? context.Entities[0] : null;

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-303</c> — the entity comes from the CONTEXT, so this pane can be PINNED.</b>
    /// 📄 <c>DESIGN_Editor_Entity_Selection_Source.md</c> §11.
    /// </summary>
    public void Draw(Hrot.Editor.AiShared.Shell.DetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Guard: skip entirely when ImGui context is not available (headless / unit tests).
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return;

        // ⭐ LIVE when docked, FROZEN when pinned — the view hands over whichever its source holds.
        // ⚠ The PRIMARY, as everywhere else: this pane is single-entity by construction.
        var entity  = ResolveEntity(context);
        var assetId = _activeAssetIdResolver?.Invoke();

        if (entity is null || assetId is null)
        {
            ImGuiNET.ImGui.TextDisabled("No entity or blueprint selected.");
            return;
        }

        // NGS-2.4a: while paused, prefer the per-node pointer snapshot over live state.
        var snapshot = _session is null
            ? null
            : ResolveInspectorSnapshot(_session, entity.Value, assetId.Value);

        if (snapshot is null)
        {
            ImGuiNET.ImGui.TextDisabled("No live Blueprint state (DebugMap not registered?).");
            return;
        }

        // Optionally show a paused/node-position hint in the header area.
        if (_session is { IsPaused: true, RecordedNodeCount: > 0 })
        {
            var hint = FormatPausedHint(_session);
            if (!string.IsNullOrEmpty(hint))
            {
                ImGuiNET.ImGui.TextDisabled(hint);
                ImGuiNET.ImGui.SameLine();
            }
        }

        DrawHeader(snapshot);
        ImGuiNET.ImGui.Spacing();
        DrawFieldsTable(snapshot.FieldValues, snapshot.Cursor);
    }

    // ---- ImGui-gated drawing helpers ----------------------------------------

    private static void DrawHeader(BlueprintStateSnapshot snap)
    {
        ImGuiNET.ImGui.TextUnformatted($"Blueprint: {snap.AssetName}");
        ImGuiNET.ImGui.Separator();
        ImGuiNET.ImGui.Text($"Entity:    {snap.Self.Index} (gen {snap.Self.Generation})");
        ImGuiNET.ImGui.Text($"Dispatch:  {snap.Dispatch}");
    }

    private static void DrawFieldsTable(
        IReadOnlyDictionary<string, object> fields,
        BlueprintLatentCursor? cursor)
    {
        if (cursor.HasValue)
        {
            ImGuiNET.ImGui.TextDisabled("Latent cursor:");
            ImGuiNET.ImGui.Text($"  ResumeAt={cursor.Value.ResumeAt}  WaitUntil={cursor.Value.WaitUntilTime:F3}  InstanceVer={cursor.Value.InstanceVersion}");
            ImGuiNET.ImGui.Spacing();
        }

        if (fields.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("(no state fields — 07-D deferred)");
            return;
        }

        ImGuiNET.ImGui.TextDisabled("Field values:");
        if (ImGuiNET.ImGui.BeginTable("bp_fields", 2,
            ImGuiNET.ImGuiTableFlags.BordersInnerV | ImGuiNET.ImGuiTableFlags.SizingStretchProp))
        {
            ImGuiNET.ImGui.TableSetupColumn("Field");
            ImGuiNET.ImGui.TableSetupColumn("Value");
            ImGuiNET.ImGui.TableHeadersRow();

            foreach (var (name, value) in fields)
            {
                ImGuiNET.ImGui.TableNextRow();
                ImGuiNET.ImGui.TableSetColumnIndex(0);
                ImGuiNET.ImGui.TextUnformatted(name);
                ImGuiNET.ImGui.TableSetColumnIndex(1);
                ImGuiNET.ImGui.TextUnformatted(value?.ToString() ?? "(null)");
            }

            ImGuiNET.ImGui.EndTable();
        }
    }

    // ---- ImGui-free projection (testable) -----------------------------------

    /// <summary>
    /// Projects the snapshot into a flat list of display rows without touching ImGui.
    /// Safe to call from unit tests.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> ProjectFields(
        BlueprintStateSnapshot snapshot)
    {
        var rows = new List<(string, string)>();
        if (snapshot.Cursor.HasValue)
        {
            var c = snapshot.Cursor.Value;
            rows.Add(("_cursor.ResumeAt",       c.ResumeAt.ToString()));
            rows.Add(("_cursor.WaitUntilTime",   c.WaitUntilTime.ToString("F3")));
            rows.Add(("_cursor.InstanceVersion", c.InstanceVersion.ToString()));
        }
        foreach (var (name, value) in snapshot.FieldValues)
            rows.Add((name, value?.ToString() ?? "(null)"));
        return rows;
    }

    // ---- NGS-2.4a: snapshot resolution (testable, no ImGui) -----------------

    /// <summary>
    /// Resolves which snapshot the inspector should display.
    ///
    /// Logic:
    /// <list type="bullet">
    ///   <item>When <paramref name="session"/> is paused, try
    ///         <see cref="IBlueprintDebugSession.GetCurrentStateSnapshot"/>. That method returns
    ///         the virtual pointer's per-node restored state (or null when the paused entity
    ///         differs from the selected entity). If non-null, return it.</item>
    ///   <item>Otherwise fall back to <see cref="IBlueprintDebugSession.CaptureLiveState"/> for
    ///         the supplied <paramref name="entity"/> and <paramref name="assetId"/>.</item>
    /// </list>
    ///
    /// The fall-back-to-live rule keeps the inspector useful when the user has a different entity
    /// selected than the one currently paused at a breakpoint.
    /// </summary>
    /// <param name="session">The active debug session.</param>
    /// <param name="entity">The entity currently selected in the inspector.</param>
    /// <param name="assetId">The blueprint asset id associated with the active canvas.</param>
    /// <returns>
    /// A <see cref="BlueprintStateSnapshot"/> representing either the per-node pointer state (while
    /// paused and the pointer entity matches) or the current live state. Null when no state is
    /// available (no DebugMap registered, entity has no blackboard, etc.).
    /// </returns>
    public static BlueprintStateSnapshot? ResolveInspectorSnapshot(
        IBlueprintDebugSession session,
        Entity entity,
        Guid assetId)
    {
        if (session.IsPaused)
        {
            var pointerSnapshot = session.GetCurrentStateSnapshot();
            if (pointerSnapshot is not null)
                return pointerSnapshot;
        }

        return session.CaptureLiveState(entity, assetId);
    }

    /// <summary>
    /// Formats the paused node-position hint shown in the inspector header.
    /// Returns an empty string when not paused or when no recordings exist.
    /// This is ImGui-free and independently testable.
    /// </summary>
    private static string FormatPausedHint(IBlueprintDebugSession session)
    {
        int count = session.RecordedNodeCount;
        if (!session.IsPaused || count <= 0) return string.Empty;
        int pointer = session.CurrentNodePointer;
        if (pointer < 0) return "(paused)";
        return $"(paused — node {pointer + 1} / {count})";
    }
}
