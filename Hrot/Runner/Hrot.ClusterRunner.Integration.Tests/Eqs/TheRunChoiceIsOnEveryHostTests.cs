using System.Linq;
using System.Threading;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Runner;
using Hrot.CGF;
using Hrot.Common;
using Hrot.Editor;
using Hrot.Map.Common;
using Hrot.Network.NED.Factory;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.RunMode;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// ⭐⭐⭐ <c>CE-4097</c> (AQ108 §2.8, D12) — the run choice is composed ONCE per host, on the CONSTRUCTED hosts (the silent-default rule: a caller that HAS
/// the dependency must pass it). The editor arms Preview, CGF arms Live, both over the scenario session their File menu uses (so ▶ is enabled), and both
/// hand the choice to the shared toolbar registrar.
/// 🔴 Red-proofs: the editor's / CGF's <c>AttachRunChoice</c> default flipped ⇒ the armed assertions; the CGF line passing <c>null</c> for the session ⇒
/// <c>PlayDisabledReason</c> is the "no scenario session" text; the registrar call dropping the choice ⇒ the toolbar entry still exists but the choice is unwired (covered by the
/// presentation rails — here only that the entry exists on both).
/// </summary>
[Collection("EqsIntegrationTests")]   // the same one-DDS-participant-at-a-time collection the other constructed-host rails use
public sealed class TheRunChoiceIsOnEveryHostTests
{
    private static WindowManager MakeWindowManager() => new(new IconAtlas(System.IntPtr.Zero, 512, 512));

    [Fact(Timeout = 60_000)]
    public void TheEditorArmsPreview_OverItsSession()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var windows = MakeWindowManager();
            editor.RegisterWindows(windows);

            var choice = editor.RunChoiceForTest;
            Assert.NotNull(choice);
            Assert.Equal(RunArmed.Preview, choice!.HostDefault);   // D12: the editor starts in Preview
            Assert.Equal(RunArmed.Preview, choice.Armed);
            Assert.Null(choice.PlayDisabledReason);                // a session is wired: ▶ is not disabled for want of one
            choice.Choose(RunArmed.Live);
            Assert.Null(choice.PlayDisabledReason);                // …and Live-armed ▶ neither (it needs the session's LoadForLive)
            // (the editor registers its toolbar only when windowed; the headless editor has none to assert — CGF's windowed build below does)
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>⭐ CGF's windows are composed only when it is NOT headless (<c>RegisterWindows</c> returns early otherwise), so this is the one rail that builds it windowed.</summary>
    [Fact(Timeout = 60_000)]
    public void CgfArmsLive_OverItsSession_AndTheToolbarCarriesTheTransport()
    {
        var factory = new NedNetworkFactory(
            participant:  null,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.Brain);
        var cgf = new CgfSubsystem(factory);
        try
        {
            cgf.Initialize(new SubsystemConfig { DomainId = 230 + Interlocked.Increment(ref _domain), Headless = false });
            var windows = MakeWindowManager();
            cgf.RegisterWindows(windows);

            var choice = cgf.RunChoiceForTest;
            Assert.NotNull(choice);
            Assert.Equal(RunArmed.Live, choice!.HostDefault);      // D12: CGF / the all-in-one / the server start in Live
            Assert.Equal(RunArmed.Live, choice.Armed);
            Assert.Null(choice.PlayDisabledReason);                // the session is passed: Live-armed ▶ is enabled
            AssertTheTransportIsOnTheToolbarAndNotTheStatusBar(windows, "Scenario");   // ⭐ CE-4083 (AQ108 D13) — success conditions 1 + 2
        }
        finally
        {
            cgf.Shutdown();
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4083</c> (AQ108 D13: "the picker exists once") — the WINDOWED editor: its toolbar carries the transport group and its status bar carries NO time control.
    /// 🔴 Red-proof: re-register a <c>"editor_time_controls"</c> status-bar section in <c>EditorSubsystem.RegisterWindows</c> ⇒ the status-bar assertion reddens.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void TheWindowedEditor_HasTheTransportOnTheToolbar_AndNoTimeControlInTheStatusBar()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = false });
            var windows = MakeWindowManager();
            editor.RegisterWindows(windows);
            AssertTheTransportIsOnTheToolbarAndNotTheStatusBar(windows, "Scenario");
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4083</c> — SimHost's ONLY time control used to be the status bar's; it now has the shared toolbar group. SimHost's windows need a real visualization, so this
    /// composes the host's own <c>RegisterTimeTransport</c> (the one method <c>RegisterWindows</c> calls) over the facade the host builds.
    /// 🔴 Red-proof: make <c>RegisterTimeTransport</c> a no-op (or re-register a status-bar section in it) ⇒ this reddens.
    /// </summary>
    [Fact]
    public void SimHost_HasTheTransportOnTheToolbar_AndNoTimeControlInTheStatusBar()
    {
        var windows = MakeWindowManager();
        var adapter = new Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter(new FdpEventBus(), () => 0.0);
        adapter.AttachRunChoice(RunArmed.Live, control: null);

        Hrot.SimHost.SimHostSubsystem.RegisterTimeTransport(windows, adapter);

        AssertTheTransportIsOnTheToolbarAndNotTheStatusBar(windows, "SimHost");
    }

    /// <summary>
    /// ⭐⭐⭐ The two CE-4083 conditions on ONE composed window manager: (1) no status-bar section is a time control, (2) the host's toolbar carries <c>TimeControlGroup</c>.
    /// ⚠ A time control is recognised by id (<c>*time*</c> / <c>*transport*</c>) — the bar has no richer seam than <c>SectionIds</c>.
    /// </summary>
    private static void AssertTheTransportIsOnTheToolbarAndNotTheStatusBar(WindowManager windows, string perspective)
    {
        Assert.Contains(windows.MainToolbar.BuildViewModel(currentPerspective: perspective).Entries, e => e.Id == ShellTimeControlToolbar.EntryId);

        var ids = windows.StatusBar.SectionIds;
        Assert.DoesNotContain(ids, id => id.Contains("time", System.StringComparison.OrdinalIgnoreCase) || id.Contains("transport", System.StringComparison.OrdinalIgnoreCase));
    }

    private static int _domain;
}
