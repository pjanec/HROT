using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>QA-008</c>, SECOND ASSEMBLY — serialises every test class in
/// <c>Hrot.Presentation.Tests</c> that CLEARS the process-global <c>ComponentTypeRegistry</c>.</b>
///
/// <para>⛔⛔ <b>This is the same defect <c>QA-008</c> fixed in <c>Hrot.SimHost.Tests</c>, found here on
/// `2026-09-18`. The reason it survived is the finding worth keeping:</b> the gate that enforces the
/// collection (<c>TheRegistryMutatorsAreSerialisedTests</c>) is a SOURCE SCAN rooted at its OWN
/// <c>.csproj</c>, and an xUnit collection is per-ASSEMBLY — so neither the fix nor its gate could ever
/// have reached this project. ⇒ ⭐ a per-assembly hazard needs a per-assembly gate, and
/// "we fixed <c>QA-008</c>" was true of one assembly only.</para>
///
/// <para>📐 <b>Measured here, `2026-09-18`, on the terrain batch-3 tree.</b> Four full-suite runs
/// produced <b>six distinct failing tests</b> and one clean run — <c>EntityDragGizmoTests.OnCancel…</c>,
/// <c>…OnDragUpdate_ResetsVehicleStateSpeed</c>, <c>MapInteractionPackTests.ThePack_Honours…</c>,
/// <c>…ContributeExtras_RunsBefore…</c>, <c>VertexEditGizmoTests.OnMenuAction_Delete…</c>,
/// <c>ScenarioFileServiceTests.SaveLoad_RoundTrip…</c> — while <b>every one of them passed under
/// <c>--filter</c></b>. ⭐ That rotating-identity-plus-green-in-isolation signature is
/// <c>DEBT-AIB-030</c>'s exactly, and the cause is the same: three classes here
/// (<c>ScenarioFileServiceTests</c>, <c>ScenarioFileServiceTkbTests</c>,
/// <c>HrotScenarioSaveHandlerTests</c>) call <c>ComponentTypeRegistry.Clear()</c> in a constructor while
/// other collections run concurrently, deleting registrations those collections are about to look up.</para>
///
/// <para>⚠ <b>Why it surfaced now and NOT at the base commit.</b> 📐 Measured: base
/// <c>2500ced2d</c> failed 1 of 4 runs; this tree failed 3 of 4. ⛔ The batch did not CAUSE the race —
/// adding 11 test methods to the assembly widened the concurrency window that already existed. ⭐ Stated
/// plainly so nobody reads the base's three green runs as evidence the suite was sound.</para>
///
/// <para>⭐ <b>Isolation, not an ordering hack</b> (<c>R-131</c> forbids a permanent filter-around): it
/// hides no failure. It removes a real unsynchronised mutation of shared state. The victims were never
/// wrong — the registry was being deleted underneath them.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ComponentTypeRegistryMutatorCollection
{
    public const string Name = "ComponentTypeRegistry mutators (Hrot.Presentation)";
}

/// <summary>
/// ⭐⭐ The gate that keeps <see cref="ComponentTypeRegistryMutatorCollection"/> from being forgotten in
/// THIS assembly. ⛔ Mirrors <c>Hrot.SimHost.Tests</c>'s rail deliberately rather than sharing it: the
/// scan must be rooted at this project, and the collection name must be this assembly's.
/// </summary>
public sealed class TheRegistryMutatorsAreSerialisedTests
{
    /// <summary>
    /// ⚠ A source scan, deliberately: the fact asserted is about how the SUITE is composed, and no
    /// runtime observation can see "this class would have run in parallel".
    /// </summary>
    [Fact]
    public void Every_class_that_clears_the_global_registry_joins_the_serial_collection()
    {
        var root = FindTestProjectRoot();
        var offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f) != "ComponentTypeRegistryMutatorCollection.cs")
            .Select(f => new { File = f, Text = File.ReadAllText(f) })
            .Where(x => x.Text.Contains("ComponentTypeRegistry.Clear()", StringComparison.Ordinal))
            // ⚠ Accept EITHER spelling of the membership — the symbol reference the classes actually
            //    write, or the raw literal.
            .Where(x => !x.Text.Contains("ComponentTypeRegistryMutatorCollection.Name", StringComparison.Ordinal)
                     && !x.Text.Contains(ComponentTypeRegistryMutatorCollection.Name, StringComparison.Ordinal))
            .Select(x => Path.GetFileName(x.File))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These files call ComponentTypeRegistry.Clear() but do not carry "
            + $"[Collection(ComponentTypeRegistryMutatorCollection.Name)]:\n  {string.Join("\n  ", offenders)}\n"
            + "Clearing the process-global registry while other collections run concurrently deletes their "
            + "component registrations mid-test — the DEBT-AIB-030 flake. Join the collection, or stop clearing.");
    }

    /// <summary>Walks up from the test binary to the directory holding the .csproj.</summary>
    private static string FindTestProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("Hrot.Presentation.Tests.csproj").Any())
            dir = dir.Parent;

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not locate Hrot.Presentation.Tests.csproj above " + AppContext.BaseDirectory);
    }
}
