using System;
using System.IO;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Runtime;
using Hrot.ClusterRunner.Integration.Tests;

namespace Hrot.Smoke.Tests;

/// <summary>
/// ⭐⭐⭐ <b>ONE SCENARIO: one entity, one behaviour, run it, look at the panels.</b>
///
/// <para>🔴 <b>User, <c>2026-08-20</c>:</b> <i>"running a set of simple scenarios with single entity
/// carrying simple behavior (like the Count4 blueprint) and running it, watching if it does what it
/// usually does, checking the panels if they show what they usually do… giving many times better and
/// faster indication of 'something is wrong' than running thousands of little unit tests that never
/// 'see' the stuff what the user sees."</i></para>
///
/// <para>📄 <c>DESIGN_Smoke_Suite.md</c> §2–§3. ⭐ <b>Two of the three tiers are built here</b> —
/// <b>T1</b> the blackboard after <c>PumpFrames(n)</c>, <b>T2</b> the row TEXT the Details table and
/// the Watch would render. ⛔ <b>T3</b> (a drawn frame) is deliberately not in this batch.</para>
///
/// <para>⭐⭐ <b>NOTHING IS HAND-BUILT.</b> The asset is <c>Count4.bp.json</c> read from the repository
/// — <i>"the asset the user actually opens"</i> — and its runtime definition is the SOURCE-GENERATED
/// one, registered by scanning <c>Hrot.AI.Behaviors</c> exactly as the host does. ⛔ A code-defined
/// stand-in would prove the harness works, not that the shipped asset does.</para>
///
/// <para>⭐⭐ <b>Add a scenario by copying this fixture</b> — 📌 the design: <i>"do not build a
/// DSL."</i></para>
/// </summary>
public sealed class SmokeFixture : IDisposable
{
    /// <summary>⭐ The authored asset's name, and the file's stem.</summary>
    public const string ScenarioName = "Count4";

    /// <summary>
    /// ⭐⭐ <b>What one tick of <c>Count4</c> does, measured from the GENERATED code</b>
    /// *(<c>Count4_F44891A7_Bp.Tick</c>)*: <c>Count += 11</c>, then <c>Delay(1s)</c>.
    /// ⇒ ⭐ the count is <b>11 for the first second of sim time</b>, ⛔ not 11 per frame.
    /// 📌 The design's own sequence diagram says <c>Count == 11</c> — this is why.
    /// </summary>
    public const int CountPerPass = 11;

    /// <summary>⭐ The real headless editor + sim. ⛔ REUSED, never copied — 📌 ruling 9.</summary>
    public EditorHarness Harness { get; }

    /// <summary>⭐⭐ The panel graph — what closes <c>G-c</c> and unlocks <b>T2</b>.</summary>
    public EditorPanels Panels { get; }

    /// <summary>⭐ The entity the scenario runs on. One, as the user described.</summary>
    public Entity Entity { get; }

    /// <summary>⭐ The authored asset, loaded from disk exactly as the editor opens it.</summary>
    public BlueprintAsset Asset { get; }

    public SmokeFixture()
    {
        Harness = new EditorHarness();

        // ⭐⭐ The PRODUCTION registration path: every [BlueprintRegistrar] in the behaviours assembly,
        //    which is where the generator emits Count4's definition, its Tick thunk and its
        //    StateFields metadata. ⛔ Not a hand-written BlueprintDefinition.
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(
            typeof(Hrot.AI.Behaviors.Generated.Count4_F44891A7_Bp).Assembly,
            staging, new BehaviorRegistry(), skipOnUnknownParam: true);
        Harness.BlueprintRegistry.CommitStaging(staging);

        Asset  = LoadAsset(ScenarioName);
        Entity = Harness.Repo.CreateEntity();

        var attach = BlueprintAttachService.AttachToEntity(
            Harness.Repo, Harness.BlueprintRegistry, Asset, Entity);
        if (attach.Status != BlueprintAttachStatus.Attached)
            throw new InvalidOperationException(
                $"The smoke scenario could not attach '{ScenarioName}': {attach.Status}. "
              + "Nothing below this point would mean anything.");

        Panels = new EditorPanels(Harness, Asset, Entity);
    }

    /// <summary>⭐ Deterministic frames. 📌 <c>102c</c>: the harness is already in Stepping, so frame
    /// #1 carries a real <c>dt</c> — before that fix every scenario silently lost its first tick.</summary>
    public void PumpFrames(int frames) => Harness.PumpFrames(frames);

    /// <summary>
    /// ⭐⭐ <b>T1 — the blackboard's OWN answer</b>, read through the production debug session rather
    /// than by re-deriving the slot offset here. ⛔ A smoke test that computed the address itself would
    /// agree with a broken resolver.
    /// </summary>
    public int Count => Panels.LiveCount();

    /// <summary>
    /// ⭐ Reads the shipped asset from the repository's own asset root. ⚠ Located by walking up from
    /// the test binary, because the smoke project deliberately does not COPY the asset — ⛔ a copy
    /// would let the shipped one rot while the suite stayed green.
    /// </summary>
    private static BlueprintAsset LoadAsset(string name)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(
                dir, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "Blueprints", $"{name}.bp.json");
            if (File.Exists(candidate))
                return Hrot.Blueprints.Core.BlueprintJsonServices.Deserialize(
                           File.ReadAllText(candidate))
                       ?? throw new InvalidDataException($"'{candidate}' deserialized to null.");
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException(
            $"Could not find the shipped '{name}.bp.json' above {AppContext.BaseDirectory}. "
          + "The smoke suite reads the asset the user opens, not a copy.");
    }

    public void Dispose()
    {
        Panels.Dispose();
        Harness.Dispose();
    }
}
