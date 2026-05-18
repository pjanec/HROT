using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.WindowManager;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

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
        IReadOnlyDictionary<Type, IImGuiFieldDrawer>? customDrawers = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _session       = session;
        _targetEntity  = targetEntity;
        _componentType = componentType;
        _sessionGetter = sessionGetter;
        _drawer        = new ComponentEditDrawer(session, pickerCtx, customDrawers);

        IsVolatile = true;
        ShowInMenu = false;
        IsOpen     = true;
    }

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
