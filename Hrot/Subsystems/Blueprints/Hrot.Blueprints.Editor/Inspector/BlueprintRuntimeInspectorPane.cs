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
/// Shows the selected entity's attached-blueprint live working-state (field values + latent cursor)
/// by calling <see cref="IBlueprintDebugSession.CaptureLiveState"/> on every draw — no pause required.
///
/// The pane is registered next to BTree/HSM panes and the window selects it when the active
/// asset kind is <see cref="AssetKind.Blueprint"/>.
///
/// Field projection is ImGui-free (done in <see cref="ProjectFields"/>); only the draw method
/// calls ImGui so the projection is independently testable.
/// </summary>
public sealed class BlueprintRuntimeInspectorPane : IRuntimeInspectorPane
{
    private IBlueprintDebugSession? _session;

    /// <summary>Resolves the currently selected entity for the Blueprint perspective.</summary>
    private Func<Entity?>? _selectedEntityResolver;

    /// <summary>Resolves the active blueprint asset id from the active canvas context.</summary>
    private Func<Guid?>? _activeAssetIdResolver;

    public AssetKind TargetKind => AssetKind.Blueprint;

    /// <summary>Sets the debug session used for live state reads.</summary>
    public void SetSession(IBlueprintDebugSession? session) => _session = session;

    /// <summary>Sets the delegates that resolve the selected entity and active asset id at draw time.</summary>
    public void SetResolvers(Func<Entity?> selectedEntityResolver, Func<Guid?> activeAssetIdResolver)
    {
        _selectedEntityResolver = selectedEntityResolver ?? throw new ArgumentNullException(nameof(selectedEntityResolver));
        _activeAssetIdResolver  = activeAssetIdResolver  ?? throw new ArgumentNullException(nameof(activeAssetIdResolver));
    }

    // ---- IRuntimeInspectorPane ---------------------------------------------

    public void Draw()
    {
        // Guard: skip entirely when ImGui context is not available (headless / unit tests).
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return;

        var entity  = _selectedEntityResolver?.Invoke();
        var assetId = _activeAssetIdResolver?.Invoke();

        if (entity is null || assetId is null)
        {
            ImGuiNET.ImGui.TextDisabled("No entity or blueprint selected.");
            return;
        }

        var snapshot = _session?.CaptureLiveState(entity.Value, assetId.Value);
        if (snapshot is null)
        {
            ImGuiNET.ImGui.TextDisabled("No live Blueprint state (DebugMap not registered?).");
            return;
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
}
