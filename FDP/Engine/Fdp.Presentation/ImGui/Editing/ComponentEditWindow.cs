using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Diagnostics.Gizmos;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="ComponentEditWindow"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example · the queue's caller-registers rule
/// (mirrors <c>NodeEdit</c>'s inversion): <c>ComponentEditDrawer</c>/<c>StructEdit.Core</c>'s edit-node
/// tree is a generic third-party document, not addressable on its own — this window is the caller that
/// knows WHICH entity/component it edits.
///
/// <para>⚠ <b>Deliberately does not walk <c>Document.Root</c> field-by-field.</b> That tree is the
/// generic <c>StructEdit.Core</c> edit-node structure — reflecting it recursively into a dump shape
/// would be reimplementing a third-party editor's own model, out of scope here. ⇒ this VM captures the
/// addressable/assertable state around the edit session: which entity/component, dirty/rebuild state,
/// and the current validation error — the same shape of deviation as
/// <c>EntityInspectorPanelViewModel</c>'s "types, not every reflected field".</para>
/// </summary>
internal sealed record ComponentEditWindowViewModel(
    string PanelId,
    string PanelKind,
    int TargetEntityIndex,
    int TargetEntityGeneration,
    string ComponentTypeName,
    bool IsDirty,
    string RebuildState,
    string? ErrorMessage) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Volatile floating window that hosts the <see cref="ComponentEditDrawer"/> for a single
/// ECS component on a single entity. Opened by <c>ComponentReflector</c> on double-click;
/// self-terminates when the target entity is destroyed.
/// </summary>
internal sealed class ComponentEditWindow : ManagedWindow
{
    private readonly IEditSession _session;
    private readonly Entity _targetEntity;
    private readonly Type _componentType;
    private readonly Func<IInspectableSession?> _sessionGetter;
    private readonly ComponentEditDrawer _drawer;
    private readonly IMutationInterceptor? _interceptor;
    /// <summary>
    /// ⭐⭐ Ruling 14 — the component value the edit session was SEEDED with.
    ///
    /// <para>
    /// 🔴 Without it a staged edit is a WHOLE-COMPONENT write built from what the paused editor saw,
    /// i.e. the PRE-tick snapshot — and the drain lands it after the post-tick restore, so every field
    /// the designer did not touch reverts by a tick. ⛔ Nothing at the drain can separate the
    /// designer's change from the simulation's; only this baseline can.
    /// </para>
    /// </summary>
    private readonly object? _baseline;
    private string? _errorMessage;

    internal ComponentEditWindow(
        string id,
        string title,
        string owningPerspective,
        IEditSession session,
        Entity targetEntity,
        Type componentType,
        Func<IInspectableSession?> sessionGetter,
        IComponentPickerContext? pickerCtx = null,
        IReadOnlyDictionary<Type, IImGuiFieldDrawer>? customDrawers = null,
        IMutationInterceptor? interceptor = null,
        object? baseline = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _baseline      = baseline;
        _session       = session;
        _targetEntity  = targetEntity;
        _componentType = componentType;
        _sessionGetter = sessionGetter;
        _interceptor   = interceptor;
        _drawer        = new ComponentEditDrawer(session, pickerCtx, customDrawers);

        IsVolatile = true;
        ShowInMenu = false;
        IsOpen     = true;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. Single host: stays a local literal.</summary>
    internal const string Kind = "component-edit";

    // ── U-obs-5: BUILD · CAPTURE ─────────────────────────────────────────────

    /// <summary>⭐⭐⭐ BUILD — a pure projection of the edit session's addressable state. No ImGui.</summary>
    internal ComponentEditWindowViewModel BuildViewModel() => new(
        Id, Kind, _targetEntity.Index, _targetEntity.Generation, _componentType.Name,
        _session.IsDirty, _session.RebuildState.ToString(), _errorMessage);

    /// <summary>⭐⭐⭐ U-obs-5: CAPTURE. No ImGui here.</summary>
    private ComponentEditWindowViewModel BuildAndPublish()
    {
        var vm = BuildViewModel();
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal ComponentEditWindowViewModel SimulateDrawClientArea() => BuildAndPublish();

    // ── Internal test accessors ───────────────────────────────────────────────

    /// <summary>The current validation error message; <c>null</c> when none.</summary>
    internal string? ErrorMessage => _errorMessage;

    /// <summary>
    /// Executes the liveness guard and rebuild logic from <see cref="DrawClientArea"/>
    /// without issuing any ImGui table or button calls.
    /// Used by unit tests (T-CE08b, T-CE08c, T-CE08d, T-CE08e) to verify state
    /// transitions without requiring an ImGui context.
    /// </summary>
    internal void ExecuteDrawLogic()
    {
        var liveCheck = _sessionGetter();
        if (liveCheck == null || !liveCheck.IsAlive(_targetEntity))
        {
            CloseAndCleanup();
            return;
        }

        if (_session.RebuildState == EditRebuildState.RebuildRequired)
            _session.RebuildDocument();
    }

    /// <summary>
    /// Executes the OK-button commit path from <see cref="DrawClientArea"/> without
    /// requiring an ImGui context.
    /// Used by unit tests (T-CE08f, T-CE08g) to verify validation error handling and
    /// mid-frame session disposal.
    /// </summary>
    internal void ExecuteOkLogic()
    {
        try
        {
            object newState = _session.Commit();

            if (_interceptor != null && _interceptor.IsPaused)
            {
                // ⭐ Ruling 14 — hand over the baseline so only the bytes the designer changed are
                //   written. A null baseline degrades to the old whole-component write by contract.
                _interceptor.StageMutation(_targetEntity, _componentType, newState, _baseline);
                CloseAndCleanup();
                return;
            }

            var ls = _sessionGetter();
            if (ls != null && ls.IsAlive(_targetEntity))
                ls.SetComponent(_targetEntity, _componentType, newState);
            CloseAndCleanup();
        }
        catch (EditValidationException ex)
        {
            _errorMessage = ex.Result.Errors.Count > 0
                ? ex.Result.Errors[0].Message
                : "Validation failed.";
            // Do NOT close on validation failure so the user can correct the value.
        }
    }

    // ── ManagedWindow rendering ───────────────────────────────────────────────

    protected override void DrawClientArea()
    {
        // 1. Liveness guard.
        var liveCheck = _sessionGetter();
        if (liveCheck == null || !liveCheck.IsAlive(_targetEntity))
        {
            CloseAndCleanup();
            return;
        }

        // 2. Rebuild if the document tree is stale.
        if (_session.RebuildState == EditRebuildState.RebuildRequired)
            _session.RebuildDocument();

        // ⭐⭐⭐ U-obs-5 — BUILD · CAPTURE, after the rebuild (so RebuildState/dirty reflect this
        // frame's truth) and before any ImGui call.
        BuildAndPublish();

        // 3. Two-column property table (mirrors ImGuiPropertyTree style).
        if (ImGuiApi.BeginTable("##cedit", 2,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
        {
            ImGuiApi.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
            ImGuiApi.TableSetupColumn("Value",    ImGuiTableColumnFlags.WidthStretch);

            // 4. Render the full node tree.
            _drawer.DrawEditNode(_session.Document.Root);

            // 5. Close table.
            ImGuiApi.EndTable();
        }

        // 6. Validation error banner.
        if (_errorMessage != null)
            ImGuiApi.TextColored(new Vector4(1f, 0.2f, 0.2f, 1f), _errorMessage);

        // 7. Separator before buttons.
        ImGuiApi.Separator();

        // 8. OK — commit and apply.
        if (ImGuiApi.Button("OK") || ImGuiApi.IsKeyPressed(ImGuiKey.Enter))
        {
            try
            {
                object newState = _session.Commit();
                // Re-evaluate session: entity may have been destroyed between frame start
                // and the commit (mid-frame disposal guard).
                var ls = _sessionGetter();
                if (ls != null && ls.IsAlive(_targetEntity))
                    ls.SetComponent(_targetEntity, _componentType, newState);
                CloseAndCleanup();
            }
            catch (EditValidationException ex)
            {
                _errorMessage = ex.Result.Errors.Count > 0
                    ? ex.Result.Errors[0].Message
                    : "Validation failed.";
                // Do NOT close on validation failure so the user can correct the value.
            }
        }

        // 9. Cancel — discard edits and close.
        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Cancel") || ImGuiApi.IsKeyPressed(ImGuiKey.Escape))
            CloseAndCleanup();
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void CloseAndCleanup()
    {
        _session.Dispose();
        IsOpen = false;
    }
}
