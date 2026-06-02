using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Validation;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Cross-asset diagnostics aggregation window.
/// On each render, validates all assets in the catalog via registered IAssetValidators
/// and displays a consolidated table of issues.
/// </summary>
public sealed class DiagnosticsWindow : ManagedWindow
{
    private readonly IAssetCatalog _catalog;
    private readonly IReadOnlyList<IAssetValidator> _validators;

    /// <param name="catalog">The shared asset catalog.</param>
    /// <param name="validators">Asset validators to run each frame.</param>
    /// <param name="idOverride">
    ///   Optional stable ImGui id override (e.g. <c>"ai_diagnostics_btree"</c>)
    ///   for per-perspective instances with independent dock layouts.
    /// </param>
    /// <param name="owningPerspective">
    ///   Perspective that owns this instance. Defaults to <c>"Authoring"</c>.
    /// </param>
    public DiagnosticsWindow(
        IAssetCatalog catalog,
        IReadOnlyList<IAssetValidator> validators,
        string? idOverride = null,
        string? owningPerspective = null)
        : base(idOverride ?? "ai_diagnostics", "Diagnostics",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _catalog    = catalog;
        _validators = validators;
    }

    protected override void DrawClientArea()
    {
        if (_validators.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No validators registered.");
            return;
        }

        // Collect all diagnostics from all assets
        var allDiags = new List<AssetDiagnostic>();
        foreach (var asset in _catalog.All)
        {
            foreach (var validator in _validators)
            {
                if (validator.SupportedKind != asset.Kind) continue;
                var diags = validator.Validate(asset);
                allDiags.AddRange(diags);
            }
        }

        ImGuiNET.ImGui.Text($"Total: {allDiags.Count} issue(s) across {_catalog.All.Count} asset(s).");
        ImGuiNET.ImGui.Separator();

        if (allDiags.Count == 0)
        {
            ImGuiNET.ImGui.TextColored(
                new System.Numerics.Vector4(0.4f, 0.9f, 0.4f, 1f), "No issues found.");
            return;
        }

        if (ImGuiNET.ImGui.BeginTable("##diags", 4,
            ImGuiNET.ImGuiTableFlags.RowBg | ImGuiNET.ImGuiTableFlags.BordersOuter |
            ImGuiNET.ImGuiTableFlags.BordersInnerV | ImGuiNET.ImGuiTableFlags.Resizable |
            ImGuiNET.ImGuiTableFlags.ScrollY, new System.Numerics.Vector2(0, 0)))
        {
            ImGuiNET.ImGui.TableSetupColumn("Asset",    ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
            ImGuiNET.ImGui.TableSetupColumn("Severity", ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGuiNET.ImGui.TableSetupColumn("Code",     ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 120f);
            ImGuiNET.ImGui.TableSetupColumn("Message",  ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
            ImGuiNET.ImGui.TableHeadersRow();

            foreach (var d in allDiags)
            {
                ImGuiNET.ImGui.TableNextRow();
                ImGuiNET.ImGui.TableSetColumnIndex(0);
                ImGuiNET.ImGui.Text(d.AssetName);
                ImGuiNET.ImGui.TableSetColumnIndex(1);
                // Color severity
                var sevColor = d.Severity switch
                {
                    AssetDiagnosticSeverity.Error   => new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f),
                    AssetDiagnosticSeverity.Warning => new System.Numerics.Vector4(1f, 0.85f, 0.1f, 1f),
                    _                               => new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
                };
                ImGuiNET.ImGui.TextColored(sevColor, d.Severity.ToString());
                ImGuiNET.ImGui.TableSetColumnIndex(2);
                ImGuiNET.ImGui.Text(d.Code);
                ImGuiNET.ImGui.TableSetColumnIndex(3);
                ImGuiNET.ImGui.TextWrapped(d.Message);
            }
            ImGuiNET.ImGui.EndTable();
        }
    }
}
