using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b>DIAGNOSTIC PROBE — is <see cref="NavigationIntentBridgeSystem"/>'s change-detection
    /// boundary OFF BY ONE when the writer runs AFTER it in the same frame?</b>
    ///
    /// <para>📌 <b>Where this came from.</b> <c>CE-103</c> (the cluster's tanks have a destination and
    /// do not move) has resisted five explanations. Its surviving signature is precise: the bridge's
    /// query matches the entity, a <c>QueryDelta</c> from an OLDER baseline matches it, driving a
    /// FRESH bridge instance by hand writes <c>NavState</c> correctly — and the SCHEDULED instance
    /// never does. <c>CE-150</c> hit the same signature on the SimHost harness for a different reason
    /// (a frozen version clock), which is what suggested looking at the boundary itself.</para>
    ///
    /// <para>🔴 <b>THE MECHANISM UNDER TEST.</b> <c>Execute</c> scans
    /// <c>QueryDelta(query, _lastScanTick)</c> — a <b>strictly-greater</b> comparison — and then ends
    /// with <c>_lastScanTick = repo.GlobalVersion</c>. So if the component write lands at version
    /// <c>V</c> <b>after</b> the bridge already recorded <c>_lastScanTick = V</c> in that same frame,
    /// <c>&gt; V</c> can never become true for that write. The intent would be <b>permanently
    /// invisible</b> to that instance, while remaining perfectly visible to a fresh one (whose
    /// <c>_lastScanTick</c> is 0) and to any query with an older baseline.</para>
    ///
    /// <para>📐 <b>Why this ordering is plausible on the cluster and not on the SimHost harness</b>
    /// (measured <c>2026-09-01</c>): in <c>SimHostCoreLogicPack</c> the bridge is simulation-list entry
    /// <b>2</b> (<c>:134</c>) — very early — and <c>LocomotionDispatcherSystem</c> is <b>not in that
    /// pack at all</b>, because on the cluster's SimHost node the intent arrives by <b>replication</b>,
    /// not from a local executor. On the SimHost integration harness the dispatcher happened to sit at
    /// index 13 and the bridge at 20, i.e. writer-before-reader, which is the benign order.</para>
    ///
    /// <para>⚠⚠ <b>This probe tests the ENGINE MECHANISM ONLY, on a bare
    /// <see cref="EntityRepository"/> — no DDS, no cluster, no harness.</b> It answers *"can this
    /// happen at all?"*, which is a precondition for <c>CE-103</c> and NOT a proof of it. ⛔ Even a
    /// positive result here does not establish that the cluster actually exhibits that ordering — that
    /// is a separate measurement, and it must be made before anything is fixed.</para>
    ///
    /// <para>⭐ It asserts only the CONTROL (writer-before-bridge must work), so a positive finding is
    /// reported in the output rather than as a red gate.</para>
    /// </summary>
    public sealed class TheBridgeDeltaBoundaryProbe
    {
        private readonly ITestOutputHelper _out;
        public TheBridgeDeltaBoundaryProbe(ITestOutputHelper output) => _out = output;

        private static (EntityRepository repo, Entity e) NewWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavigationIntent>();
            repo.RegisterComponent<NavState>();
            var e = repo.CreateEntity();
            repo.SetComponent(e, default(NavigationIntent));
            repo.SetComponent(e, default(NavState));
            return (repo, e);
        }

        private static NavigationIntent AMoveOrder() => new()
        {
            IntentId         = 1,
            Mode             = NavigationMode.DirectPoint,
            FinalDestination = new Vector3(500f, 0f, 0f),
            TargetSpeed      = 15f,
            ArrivalRadius    = 5f,
        };

        [Fact]
        [Trait("Category", "Diagnostic")]
        public void DoesAWriteLandingAfterTheScanBecomePermanentlyInvisible()
        {
            // ── ARM A — the ORDER UNDER SUSPICION: bridge scans first, writer writes after ──────
            var (repoA, eA) = NewWorld();
            var bridgeA = new NavigationIntentBridgeSystem();

            repoA.Tick();
            bridgeA.Execute(repoA, 0.016f);          // records _lastScanTick = GlobalVersion (= V)
            uint vAtScan = repoA.GlobalVersion;
            repoA.SetComponent(eA, AMoveOrder());    // ⇒ the write is stamped at V, AFTER the scan

            for (int i = 0; i < 10; i++)             // ten more frames of honest scanning
            {
                repoA.Tick();
                bridgeA.Execute(repoA, 0.016f);
            }
            var navA = repoA.GetComponent<NavState>(eA);

            // ── ARM B — the CONTROL: writer writes first, bridge scans after (the benign order) ──
            var (repoB, eB) = NewWorld();
            var bridgeB = new NavigationIntentBridgeSystem();

            repoB.Tick();
            repoB.SetComponent(eB, AMoveOrder());
            bridgeB.Execute(repoB, 0.016f);
            var navB = repoB.GetComponent<NavState>(eB);

            _out.WriteLine("── ARM A: bridge scanned at V, then the write landed at V ──");
            _out.WriteLine($"  GlobalVersion at the scan = {vAtScan}");
            _out.WriteLine($"  after 10 further frames: NavState.Mode={navA.Mode} "
                         + $"TargetSpeed={navA.TargetSpeed} "
                         + $"Dest=({navA.FinalDestination.X:F1},{navA.FinalDestination.Y:F1})");
            _out.WriteLine("── ARM B (control): the write landed BEFORE the scan ──");
            _out.WriteLine($"  NavState.Mode={navB.Mode} TargetSpeed={navB.TargetSpeed} "
                         + $"Dest=({navB.FinalDestination.X:F1},{navB.FinalDestination.Y:F1})");

            _out.WriteLine("── VERDICT ────────────────────────────────────────────────");
            if (navA.Mode == KinematicsMode.None && navB.Mode != KinematicsMode.None)
            {
                _out.WriteLine("  ⛔⛔ CONFIRMED — THE BOUNDARY IS OFF BY ONE.");
                _out.WriteLine("     A write stamped at the same version the bridge just recorded is");
                _out.WriteLine("     PERMANENTLY invisible to that instance: QueryDelta tests '> since'");
                _out.WriteLine("     and 'since' already equals the write's version.");
                _out.WriteLine("     ⇒ any system ordered BEFORE the writer of the component it watches");
                _out.WriteLine("       silently loses that write, forever. This is a class of bug, not");
                _out.WriteLine("       one system's bug.");
                _out.WriteLine("     ⚠ NEXT, and required before any fix: measure whether the CLUSTER's");
                _out.WriteLine("       SimHost node actually writes NavigationIntent after the bridge.");
            }
            else if (navA.Mode != KinematicsMode.None)
            {
                _out.WriteLine("  ⭐ REFUTED — the write WAS picked up despite landing after the scan.");
                _out.WriteLine("     The boundary is not the mechanism; CE-103 needs a different lead.");
            }
            else
            {
                _out.WriteLine("  ⚠ INCONCLUSIVE — the CONTROL arm did not write either, so this probe");
                _out.WriteLine("    is not exercising the bridge correctly. Fix the probe before");
                _out.WriteLine("    drawing anything from arm A.");
            }

            // Only the control is asserted: a positive finding must not present as a red gate.
            Assert.True(navB.Mode != KinematicsMode.None,
                "probe precondition: with the write landing BEFORE the scan, the bridge must apply it. "
              + "If this fails the probe itself is wrong and arm A proves nothing.");
        }
    }
}
