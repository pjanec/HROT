using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>QA-008</c> — serialises every test class in this assembly that CLEARS the process-global
/// <c>ComponentTypeRegistry</c>.</b>
///
/// <para>⛔⛔ <b>This is the root cause of <c>DEBT-AIB-030</c> in this assembly</b>, measured
/// 2026-08-26. <c>ComponentTypeRegistry.Clear()</c> wipes a PROCESS-GLOBAL dictionary. This assembly
/// has no <c>xunit.runner.json</c>, so its collections run in PARALLEL — and a test class that clears
/// the registry in its constructor deletes the component-type registrations that other classes,
/// running concurrently, have already made and are about to look up by name.</para>
///
/// <para>📐 <b>The signature that made this look like flakiness:</b> whichever test is between
/// "register" and "look up" when a <c>Clear()</c> lands is the one that fails, so the failing IDENTITY
/// rotates run to run and every named victim passes under <c>--filter</c>. 📌 In one measured run the
/// log reads, in order: <c>BlueprintStateTranslatorTests</c> passes → two unrelated tests pass →
/// <c>StagingEntityExtractorTests</c> fails. Another run produced
/// <c>ScenarioSerializer: unknown component type name 'EditLoadTestPos'</c> — for a type whose own test
/// class registers it in its constructor.</para>
///
/// <para>⛔ <b>Why this is isolation and NOT an ordering hack</b> (the handoff forbids the latter, and
/// <c>R-131</c> forbids a permanent filter-around): it hides no failure. It removes a real,
/// unsynchronised mutation of shared state between concurrent tests. The victims were never wrong; the
/// registry was being deleted underneath them.</para>
///
/// <para>⚠ <b>The ledger's standing hypothesis was that ids are assigned in registration order and
/// parallelism made that order observable</b> (<c>Q52</c> §6.3, restated by <c>ST-026</c> / <c>AX-023</c>).
/// 📐 Measured: <b>false</b> — <c>ComponentTypeRegistry.GetOrRegisterManaged</c> requires an explicit
/// <c>[ComponentId]</c> and throws without one, so ids are DETERMINISTIC and order-independent. What
/// parallelism makes observable is <c>Clear()</c>, not ordering.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ComponentTypeRegistryMutatorCollection
{
    public const string Name = "ComponentTypeRegistry mutators (Hrot.SimHost)";
}

/// <summary>
/// ⭐⭐ <b><c>QA-008</c> — the gate that keeps <see cref="ComponentTypeRegistryMutatorCollection"/> from
/// being forgotten.</b> A new test class that calls <c>ComponentTypeRegistry.Clear()</c> without joining
/// the collection re-opens the defect silently, and the symptom would land on some OTHER class — which
/// is exactly why it took ~40 batches to find.
/// </summary>
public sealed class TheRegistryMutatorsAreSerialisedTests
{
    /// <summary>
    /// ⚠ A source scan, deliberately: the fact being asserted is about how the SUITE is composed, and no
    /// runtime observation can see "this class would have run in parallel". 📌 The repo already uses
    /// source-scan rails for exactly this class of claim.
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
            // ⚠ Accept EITHER spelling of the membership: the idiomatic symbol reference (what the
            //    classes actually write) or the raw literal. Matching only the literal is how the first
            //    version of this rail reddened on the very class it had just been used to fix.
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
        while (dir != null && !dir.EnumerateFiles("Hrot.SimHost.Tests.csproj").Any())
            dir = dir.Parent;

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not locate Hrot.SimHost.Tests.csproj above " + AppContext.BaseDirectory);
    }
}
