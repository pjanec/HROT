using System;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>U-obs-1</c> / <c>U1c</c> — <see cref="AiVariablesWindow"/> converted, mirroring
/// <c>ThePilotPanelDumpsWhatItDrawsTests</c> (<c>EntityBlueprintsPanel</c>, the pilot) end to end.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example · §Invariant.
///
/// <para>⭐⭐ <b>Why these rails run HEADLESS, with no ImGui context at all.</b> ⛔ Unlike the pilot,
/// <see cref="AiVariablesWindow"/> is a <c>ManagedWindow</c>, whose public <c>Render</c> already calls
/// <c>Gui.Begin</c> before <c>DrawClientArea</c> is reached — so a headless test cannot go through
/// <c>Render</c> at all. ⭐ <c>SimulateDrawClientArea</c> is the window's own headless entry point (the
/// BUILD + CAPTURE half of <c>DrawClientArea</c>), 📌 mirroring <c>AiGraphCanvasWindow</c>'s own test
/// hook of the same name.</para>
///
/// <para>⚠ <b>ONE class</b>: <c>PanelSnapshot</c> is process-global static state and xunit parallelises
/// across CLASSES. Every case opens by resetting it.</para>
/// </summary>
public sealed class TheVariablesPanelDumpsItsTableTests : IDisposable
{
    private static readonly Guid AssetA = new("aaaaaaaa-0000-0000-0000-000000000001");

    public TheVariablesPanelDumpsItsTableTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static AiVariablesWindow MakeWindow(string id = "ai_variables_btree")
        => new(id, "BTree", new Hrot.Editor.AiShared.Variables.VariableValueFormatter(
            Hrot.Editor.AiShared.Variables.RawValueDecoder.Instance));

    private static Hrot.Editor.AiShared.Variables.VariableRow Row(string name, byte[]? bytes = null)
        => new(
            Origin:    new Hrot.Editor.AiShared.Variables.VariableRowOrigin(AssetA, new Entity(1, 0), "Variables", name, "Alpha"),
            ShortName: name,
            TypeText:  typeof(int).Name,
            ClrType:   typeof(int),
            ReadValue: () => bytes ?? new byte[4],
            AssetTick: null,
            RowKind:   Hrot.Editor.AiShared.Variables.VariableRowKind.Normal,
            IsStale:   false,
            HasEverBeenWritten: true);

    // ── U1b, on the PRODUCTION object ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The window is instrumented the moment it is CONSTRUCTED — before it has ever drawn.</b>
    /// ⛔ This is the rail that would go red if <c>DeclareInstrumented</c> drifted into the draw: a
    /// window nobody opened would then look exactly like a window nobody converted, and the reader
    /// could not tell <i>"showed nothing"</i> from <i>"not instrumented"</i>.
    /// 📌 Asserted on the CONSTRUCTED object, not on the source — <c>R-67</c>.
    /// </summary>
    [Fact]
    public void ThePanelIsInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "ai_variables_btree_construct";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        _ = MakeWindow(id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
    }

    // ── U1c — the dump carries what the designer sees ──────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Draw a frame, read the model over the snapshot, assert FIELDS.</b> ⭐ <c>panelKind</c> is
    /// <see cref="PanelIds.Variables"/> — identical across hosts, per <c>PanelIds.cs</c> — while
    /// <c>panelId</c> is this window's own ADDRESS. The dump's <c>rows</c> match what the model was fed.
    /// </summary>
    [Fact]
    public void AfterADraw_TheDumpCarriesThePanelKindAndTheFedRows()
    {
        const string id = "ai_variables_btree_dump";
        PanelSnapshot.CaptureEnabled = true;
        var window = MakeWindow(id);

        window.ShowSection("Working State", new Hrot.Editor.AiShared.Variables.FixedVariableRowSource(
            new[] { Row("Health"), Row("Speed") }));

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id,                  vm!.PanelId);
        Assert.Equal(PanelIds.Variables,  vm.PanelKind);

        var dump = vm.Dump();
        Assert.Equal(id,                 dump["panelId"]!.GetValue<string>());
        Assert.Equal(PanelIds.Variables, dump["panelKind"]!.GetValue<string>());

        var rows = dump["rows"]!.AsArray();
        Assert.Equal(2, rows.Count);
        var names = new[] { rows[0]!["name"]!.GetValue<string>(), rows[1]!["name"]!.GetValue<string>() };
        Assert.Contains("Health", names);
        Assert.Contains("Speed",  names);
    }

    // ── The flag gates the DUMP, not the BUILD ─────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Production default: capture OFF ⇒ nothing is published, ⛔ but the window is still known to be
    /// instrumented.
    /// </summary>
    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        const string id = "ai_variables_btree_off";
        var window = MakeWindow(id);        // CaptureEnabled stays false

        window.ShowSection("Working State", new Hrot.Editor.AiShared.Variables.FixedVariableRowSource(
            new[] { Row("Health") }));
        window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
    }
}
