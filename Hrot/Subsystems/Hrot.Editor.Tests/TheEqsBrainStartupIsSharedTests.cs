using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiComposition;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ The EQS Brain part — and its startup — is ONE implementation on the editor AND CGF.
/// 🔒 User, <c>2026-09-30</c>: <i>"the EQS brain part must be a shared code including the startup code for
/// editor and CGF alike"</i> · <i>"CGF == editor in most features, unification and sharing desired"</i>.
/// 📄 <c>docs/designs/eqs-2/EQS_Design_v1.3_final.md</c> §17.8. The constructed-host half of this rail is
/// <c>EqsAuthoringOnBothHostsTests</c> (ClusterRunner integration).
/// </summary>
public sealed class TheEqsBrainStartupIsSharedTests
{
    // ══ ① both hosts call the SAME binder, and neither builds the pieces itself ══

    [Theory]
    [InlineData("Hrot.CGF",    "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void AHostBuildsBlueprintNodeAuthoringOnlyThroughTheSharedBinder(string project, string file)
    {
        var text = HostSource.Read(project, file);

        Assert.Contains("AiBlueprintNodeAuthoringBinder.CreateDrawers(", text);
        Assert.Contains("AiBlueprintNodeAuthoringBinder.InstallDetails(", text);
        Assert.DoesNotContain("BlueprintEditorBootstrap.CreateNodeDrawerRegistry(", text);
        Assert.DoesNotContain("BlueprintDetailsContribution.InstallInto(", text);
        Assert.DoesNotContain("EqsTemplateRegistry.Discover(", text);

        // ⭐ The canvas pills: built by the binder, handed to the shared document binder. 🔴 The editor
        //   used to build them itself into a local nobody read, so no pill rendered on either host.
        Assert.Contains("BlueprintNodeAuthoring = _blueprintNodeAuthoring", text);
        Assert.DoesNotContain("BlueprintEditorBootstrap.CreateAttachmentProviders(", text);
        Assert.DoesNotContain("BlueprintEditorBootstrap.CreateCanvasRenderers(", text);
    }

    [Theory]
    [InlineData("Hrot", "Subsystems", "Hrot.SimHost", "SimHostCapabilities.cs")]
    [InlineData("Hrot", "Subsystems", "Hrot.Editor", "EditorCapabilities.cs")]
    [InlineData("Stride", "HrotStrideApp.Game", "StrideCapabilities.cs")]
    public void EveryEqsSolverHostStartsItThroughTheSharedStartup(params string[] path)
    {
        var text = HostSource.ReadRelative(path);

        Assert.Contains("EqsSolverStartup.Register(context)", text);
        Assert.DoesNotContain("new EqsModule()", text);
        Assert.DoesNotContain("EqsTemplateRegistry.InstallDefault(", text);
    }

    // ══ ② what the binder builds ══

    [Fact]
    public void TheCrossAssetPill_ResolvesPeerNamesFromACachedScan_NotAScanPerFrame()
    {
        var peer = System.Guid.NewGuid();
        var peers = new CountingPeers(new Hrot.Blueprints.Editor.NodeDrawers.BlueprintPeerInfo(
            peer, "SquadState", System.Array.Empty<string>()));
        var now = new System.DateTime(2026, 9, 30, 12, 0, 0, System.DateTimeKind.Utc);
        var cache = new AiBlueprintNodeAuthoringBinder.PeerNameCache(peers, () => now);

        Assert.Equal("SquadState", cache.Resolve(peer));
        for (int frame = 0; frame < 100; frame++) cache.Resolve(peer);
        Assert.Equal(1, peers.Scans);                            // a hit never rescans

        var created = System.Guid.NewGuid();                    // a peer the last scan did not see
        Assert.Null(cache.Resolve(created));
        Assert.Null(cache.Resolve(created));
        Assert.Equal(1, peers.Scans);                            // a miss rescans at most once per interval

        now += AiBlueprintNodeAuthoringBinder.PeerNameCache.RescanInterval;
        cache.Resolve(created);
        Assert.Equal(2, peers.Scans);
    }

    private sealed class CountingPeers(params Hrot.Blueprints.Editor.NodeDrawers.BlueprintPeerInfo[] peers)
        : Hrot.Blueprints.Editor.NodeDrawers.IBlueprintPeerProvider
    {
        public int Scans { get; private set; }
        public System.Collections.Generic.IReadOnlyList<Hrot.Blueprints.Editor.NodeDrawers.BlueprintPeerInfo> GetPeers()
        {
            Scans++;
            return peers;
        }
    }

    [Fact]
    public void TheSharedPickerListsEveryRuntimeTemplate()
    {
        // Discovery scans LOADED assemblies. ⚠ `_ = typeof(X);` does NOT load one — the discard is dropped
        // (measured: this rail was red with it). Touch the assembly for real.
        Assert.Equal("Hrot.SimHost", typeof(Hrot.SimHost.Systems.EntitiesOfForceInArea).Assembly.GetName().Name);

        var picker = AiBlueprintNodeAuthoringBinder.CreateEqsTemplates();

        Assert.NotNull(picker.TryGet(new System.Guid(Hrot.SimHost.Systems.EntitiesOfForceInArea.AssetId)));
        Assert.NotNull(picker.TryGet(new System.Guid("f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d"))); // FindCoverFromTarget
    }
}
