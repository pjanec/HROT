using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.DER;

namespace Hrot.Presentation.Windows;

/// <summary>
/// ⭐⭐ <b>AQ109 §5a/§5b (X5) — the ONE generic wire inspector.</b> Every entity-keyed wire type, as received by THIS host's DDS
/// participant, shown as entities with their descriptors — with no per-descriptor code. It replaces the retired ExCon node's per-type
/// DER mirror (R-3067). 📄 docs/blueprints/Architect_Question_109_Retire_ExCon.md §5b, §6.5.
///
/// <para>⭐ <b>Opt-in:</b> it starts CLOSED. The <see cref="WireEntityMirror"/> (and its ~57 DDS readers) is created the first frame the
/// window is open and disposed the first frame it is closed — nothing is read while it is closed.</para>
/// <para>⚠ A host with no DDS participant (the editor offline, the replay browser) shows a one-line explanation, never an empty list
/// that reads like "no traffic".</para>
/// </summary>
public sealed class WireEntityInspectorWindow : ManagedWindow
{
    /// <summary>The panel kind. Single host: stays a local literal.</summary>
    internal const string Kind = "wire-entity-inspector";

    private readonly DerEntityInspectorPanel _panel = new();
    private readonly Func<DdsParticipant?> _participant;
    private readonly Func<IReadOnlyList<Type>> _types;
    private WireEntityMirror? _mirror;

    /// <param name="participant">This host's DDS participant, or null/absent when it has none.</param>
    /// <param name="types">The wire types to mirror; defaults to <see cref="WireTypeCatalog.EntityKeyedTypesInProcess"/>.</param>
    public WireEntityInspectorWindow(
        string id, string title, string owningPerspective,
        Func<DdsParticipant?>? participant, Vector4? titleBarColor = null,
        Func<IReadOnlyList<Type>>? types = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _participant = participant ?? (() => null);
        _types = types ?? WireTypeCatalog.EntityKeyedTypesInProcess;
        IsOpen = false;                       // ⭐ opt-in: the readers exist only while the user has it open
        TitleBarColor = titleBarColor;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>The live mirror — null while the window is closed or the host has no participant.</summary>
    internal WireEntityMirror? Mirror => _mirror;

    private bool EnsureMirror()
    {
        if (_mirror != null) return true;
        var p = _participant();
        if (p == null) return false;
        _mirror = new WireEntityMirror(p, _types());
        return true;
    }

    private void Release()
    {
        _mirror?.Dispose();
        _mirror = null;
    }

    /// <summary>⭐ BUILD · CAPTURE · POLL — no ImGui. Returns null when the host has no participant.</summary>
    private DerEntityInspectorPanelViewModel? BuildAndPublish()
    {
        if (!EnsureMirror()) return null;
        _mirror!.Poll();
        var vm = _panel.BuildViewModel(_mirror.Repo, Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the open-frame work (create the mirror, poll, build + capture), callable with no live ImGui context.</summary>
    internal DerEntityInspectorPanelViewModel? SimulateDrawClientArea() => BuildAndPublish();

    /// <inheritdoc/>
    protected override void OnRenderWhileClosed() => Release();

    protected override void DrawClientArea()
    {
        if (BuildAndPublish() == null)
        {
            ImGuiNET.ImGui.TextUnformatted("This host has no DDS participant (offline) - there is no wire to inspect.");
            return;
        }
        _panel.DrawContent(_mirror!.Repo);
    }
}
