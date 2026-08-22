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

    /// <summary>
    /// ⭐⭐⭐ <b>Diagnostics that belong to NO asset — the SCHEMA-level ones.</b>
    /// 📄 <c>docs/designs/blueprint-integ-1/DESIGN.md</c> §5.7 (<c>AIE-053</c>), verbatim: <i>"surface
    /// <c>SubElementCollision</c> diagnostics and dangling-reference classification <b>in the shared
    /// windows</b>."</i>
    ///
    /// <para>🔒 <b>User ruling, <c>2026-08-22</c>:</b> <i>"if collision strip is a warning about naming
    /// collision or something, it need to be routed to where the collision can be seen or fixed."</i>
    /// ⇒ ⭐ <b>this window is where it can be SEEN</b> — the consolidated issue table.</para>
    ///
    /// <para>⛔⛔ <b>Why a SECOND source and not an <c>IAssetValidator</c>.</b> 📐 A validator is asked
    /// <c>Validate(asset)</c> once per asset and declares a <c>SupportedKind</c>; a short-name collision
    /// between two FQNs belongs to the ACTION SCHEMA and to <b>no asset at all</b>. ⚠ Squeezing it into
    /// a validator would mean either running it N times *(N duplicate rows)* or inventing a fake asset
    /// to hang it on — ⭐ and <c>AssetDiagnostic.AssetName</c> is what the first column shows, so a fake
    /// there is a lie on screen.</para>
    ///
    /// <para>⚠ <b>Where it can be FIXED is NOT here, and the message says so.</b> 📐 The claimants are
    /// C# symbols (FQNs); the repair is a rename in source. ⇒ ⭐ the row NAMES every claiming FQN, which
    /// is the most the editor can honestly do — ⛔ it does not pretend to offer a fix.</para>
    /// </summary>
    private readonly Func<IReadOnlyList<AssetDiagnostic>>? _schemaDiagnostics;

    /// <param name="catalog">The shared asset catalog.</param>
    /// <param name="validators">Asset validators to run each frame.</param>
    /// <param name="idOverride">
    ///   Optional stable ImGui id override (e.g. <c>"ai_diagnostics_btree"</c>)
    ///   for per-perspective instances with independent dock layouts.
    /// </param>
    /// <param name="owningPerspective">
    ///   Perspective that owns this instance. Defaults to <c>"Authoring"</c>.
    /// </param>
    /// <param name="schemaDiagnostics">
    ///   ⭐⭐ Schema-level diagnostics that belong to no asset — see <see cref="_schemaDiagnostics"/>.
    ///   ⚠ <see langword="null"/> is honest for a host with no action-schema exporter; ⛔ a production
    ///   caller that HAS one must pass it *(the <c>2026-08-16</c> rule)</param>
    public DiagnosticsWindow(
        IAssetCatalog catalog,
        IReadOnlyList<IAssetValidator> validators,
        string? idOverride = null,
        string? owningPerspective = null,
        Func<IReadOnlyList<AssetDiagnostic>>? schemaDiagnostics = null)
        : base(idOverride ?? "ai_diagnostics", "Diagnostics",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _catalog            = catalog;
        _validators         = validators;
        _schemaDiagnostics  = schemaDiagnostics;
    }

    /// <summary>⭐ A rail surface — 📌 <c>R-67</c>: asked of the CONSTRUCTED window, so a composition
    /// root that holds an exporter and forgets to pass it is visible.</summary>
    public bool HasSchemaDiagnostics => _schemaDiagnostics is not null;

    /// <summary>
    /// ⭐⭐⭐ <b>Everything this window would show right now.</b> ⛔ No ImGui — 📌 <c>R-21</c>/<c>R-62</c>:
    /// the draw is unrailed by construction, so the AGGREGATION is named as a value and
    /// <see cref="DrawClientArea"/> is a thin renderer over it.
    /// <para>⭐ Schema rows come FIRST: they are global and cheap to miss at the bottom of a long
    /// per-asset list.</para>
    /// </summary>
    public IReadOnlyList<AssetDiagnostic> Collect()
    {
        var all = new List<AssetDiagnostic>();

        if (_schemaDiagnostics is { } schema) all.AddRange(schema());

        foreach (var asset in _catalog.All)
        {
            foreach (var validator in _validators)
            {
                if (validator.SupportedKind != asset.Kind) continue;
                all.AddRange(validator.Validate(asset));
            }
        }
        return all;
    }

    protected override void DrawClientArea()
    {
        // ⚠ The schema source is independent of the validators: a host can have collisions to report
        //   and no per-asset validator at all, and the old early-return would have hidden them.
        if (_validators.Count == 0 && _schemaDiagnostics is null)
        {
            ImGuiNET.ImGui.TextDisabled("No validators registered.");
            return;
        }

        var allDiags = Collect();

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
