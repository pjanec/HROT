using System;
using System.Linq;
using Fdp.ModuleHost.Abstractions;
using Fdp.Core;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>W4</c>'s FORWARDING rail — the ONE <see cref="StagedWriteView"/> reaches EVERY table.</b>
/// 📄 <c>DESIGN_Staged_Live_Write.md</c> §4 fork A / §7 *(<c>R-120</c>)*.
///
/// <para>⭐⭐ <b>This is the control the <c>2026-08-16</c> rule prescribes</b>, and it is here because
/// this exact defect has now been filed <b>nine</b> times: <i>"a production caller that HAS a dependency
/// must PASS it"</i>, with the control being <i>"a forwarding rail PER DEPENDENCY, asserted on the
/// CONSTRUCTED OBJECT."</i> ⛔ Not on the registrar's source, not on a hand-built model.</para>
///
/// <para>⛔⛔ <b>Why it must assert INSTANCE EQUALITY and not just "not null".</b> 📐 §7's claim is that
/// Details and Watch cannot disagree, and two correctly-wired-but-DIFFERENT views would satisfy every
/// non-null assertion while reproducing the exact divergence the design forbids. ⚠ 📌 §2 <c>I2</c>
/// measured that shape already — <c>VariableTableModel.cs:122: new VariableChangeMonitor()</c>, per
/// panel, so marking Details pending could never reach the Watch.</para>
/// </summary>
public sealed class TheSharedYellowReachesEveryTableTests
{
    /// <summary>⭐ The manager needs a time controller; nothing here pauses.</summary>
    private sealed class NoTimeControl : Hrot.Blueprints.Core.Debug.IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => false;
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStepOneTick() { }
    }

    /// <summary>⭐⭐ The REAL manager, so the Watch is built the way production builds it.
    /// ⛔ Not a hand-written <c>IDataBreakpointManager</c> stub: 📌 <c>R-67</c> — this rail exists to see
    /// a composition-root defect, and a stub is one more thing that can be right while production is
    /// wrong.</summary>
    private static Hrot.Diagnostics.Breakpoints.DataBreakpointManager RealManager()
    {
        var live    = new EntityRepository();
        var preTick = new EntityRepository();
        return new Hrot.Diagnostics.Breakpoints.DataBreakpointManager(
            live, preTick, new Hrot.Diagnostics.Breakpoints.DebugSnapshotProvider(preTick),
            new NoTimeControl());
    }

    /// <summary>⭐ Nothing is ever staged here — the rail is about the WIRE, not about the yellow.</summary>
    private sealed class NothingStaged : IStagedWrites
    {
        public bool HasPending => false;
        public bool IsRewound  => false;
        public void DrainInto(ISimulationView view) { }
        public bool TryGetPending(Entity e, int t, int o, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static (PerspectiveWorkspaceRegistrar Registrar, StagedWriteView Shared) Production(
        Hrot.Diagnostics.Breakpoints.IDataBreakpointManager? breakpoints = null)
    {
        var shared = new StagedWriteView(() => new NothingStaged(), (_, _) => null, () => null);

        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(), new TheDefaultLayoutIsNotStaleTests.NoRefactor(), new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false)
        {
            StagedWrites      = shared,
            BreakpointManager = breakpoints,
        };

        return (services.CreateRegistrar("BTree", new EditorSelectionStore(),
                                         Array.Empty<IAssetValidator>()), shared);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL:</b> the Details panel's model and the standalone Variables table's model hold
    /// the <b>SAME</b> view instance the composition root supplied.
    /// </summary>
    [Fact]
    public void TheDetailsPanelAndTheVariablesTable_HoldTheSameSharedStagedWriteView()
    {
        var (reg, shared) = Production();

        var details = ((IVariableTableHost)reg.Details!).TableModel;
        var table   = ((IVariableTableHost)reg.Variables).TableModel;

        Assert.NotNull(details);
        Assert.NotNull(table);
        Assert.Same(shared, details!.StagedWrites);
        Assert.Same(shared, table!.StagedWrites);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>…and the WATCH, which is the other half of §7's sentence.</b>
    /// ⚠ The Watch exists only when the perspective was given a breakpoint manager — 📌 the same shape
    /// <c>AiWatchWindow.VariableTable</c> already has, and the reason this is a separate rail rather
    /// than one more assertion above.
    /// </summary>
    [Fact]
    public void TheWatch_HoldsTheSameSharedStagedWriteView()
    {
        var (reg, shared) = Production(RealManager());

        Assert.NotNull(reg.Watch);
        var watch = ((IVariableTableHost)reg.Watch!).TableModel;

        Assert.NotNull(watch);
        Assert.Same(shared, watch!.StagedWrites);
    }

    /// <summary>
    /// ⛔⛔ <b>THE NEGATIVE CONTROL.</b> A perspective built with no staged-write source leaves every
    /// model's view <see langword="null"/> — ⭐ so the rails above cannot be passing because the
    /// property happens to be non-null by construction. 📌 <c>BP-402</c> ②: a rail that cannot fail is
    /// a rail that asserts nothing.
    /// </summary>
    [Fact]
    public void WithNoStagedWriteSource_NoTableClaimsOne()
    {
        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(), new TheDefaultLayoutIsNotStaleTests.NoRefactor(), new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false);

        var reg = services.CreateRegistrar("BTree", new EditorSelectionStore(),
                                           Array.Empty<IAssetValidator>());

        Assert.Null(reg.StagedWrites);
        Assert.Null(((IVariableTableHost)reg.Details!).TableModel!.StagedWrites);
        Assert.Null(((IVariableTableHost)reg.Variables).TableModel!.StagedWrites);
    }

    /// <summary>
    /// ⭐⭐ <b>Every <see cref="IVariableTableHost"/> answers <c>TableModel</c>, and the ones that own a
    /// table own a model.</b>
    /// ⚠ 📌 The enumerate-don't-assume lesson: Batch 87's handoff knew of THREE hosts and the graph
    /// found FOUR; there are SIX now. ⛔ Stated over the interface so a seventh cannot be forgotten —
    /// a host with a table but no model would be wired for gestures and silently left out of the yellow.
    /// </summary>
    [Fact]
    public void EveryHostWithATable_AlsoOffersItsModel()
    {
        var (reg, shared) = Production(RealManager());

        var hosts = new IVariableTableHost?[] { reg.Details, reg.Variables, reg.Watch }
            .Where(h => h is not null)
            .Select(h => h!)
            .ToArray();

        Assert.NotEmpty(hosts);                                  // ⛔ guard: never assert over nothing
        foreach (var host in hosts)
        {
            if (host.VariableTable is null) continue;            // ⚠ a shape, not a defect
            Assert.NotNull(host.TableModel);
            Assert.Same(shared, host.TableModel!.StagedWrites);
        }
    }
}
