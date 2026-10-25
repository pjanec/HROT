using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Utility;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Utility;

/// <summary>
/// ⭐⭐ <c>CE-1325</c> (docs/DESIGN_Utility_Decision_View.md) — the Decision tab's three pieces: the SHEET (the running definition's settings,
/// a unit's values from the real scorer), the BINDINGS (which decision a node binds, from its method key and the behaviour's default block),
/// and the drawing.
/// </summary>
public sealed class UtilityDecisionViewTests : IDisposable
{
    public void Dispose() { UtilityDecisionHost.Registry = null; UtilityDecisionHost.World = null; UtilityDecisionHost.FollowedUnit = null; }

    private static int Posture => UtilityDecisionCatalog.ComputeId(CombatPostureDecision.DecisionAssetId);

    /// <summary>W1 — the settings, from the C# class: option names, aggregation, inputs by name, modifiers, curves.
    /// ✅ Red-proof: the modifier flag read as false ⇒ red.</summary>
    [Fact]
    public void CE1325_TheSheet_IsTheRunningDefinitionsSettings()
    {
        var sheet = UtilityDecisionSheet.Build(Posture)!;
        Assert.Equal("Combat posture", sheet.Name);
        Assert.False(sheet.IsRanking);
        var advance = sheet.Options.First(o => o.Name == "AdvanceAndAttack");
        Assert.Equal(ScoringMode.WeightedProduct, advance.Mode);
        Assert.Equal(new[] { "HealthFraction", "AmmoFraction", "EnemyStrengthRatio", "CautionDamp", "StressDamp" },
                     advance.Considerations.Select(c => c.Input).ToArray());
        Assert.Equal(CurveKind.Threshold, advance.Considerations[1].Curve.Kind);
        Assert.Equal(0.9f, advance.Considerations[1].Weight, 3);
        Assert.Equal(new[] { false, false, false, true, true }, advance.Considerations.Select(c => c.Modifier).ToArray());
        Assert.Contains(sheet.Options, o => o.Mode == ScoringMode.WeightedSum);   // Hold is a sum
        Assert.Null(UtilityDecisionSheet.Build(12345));
    }

    /// <summary>W2 — a unit's values, from the REAL scorer: the winner, every option's score, and a traced pass's raw → curve → weight.
    /// ✅ Red-proof: the traced pass not matched to the decision ⇒ no raw values ⇒ red.</summary>
    [Fact]
    public void CE1325_WithAUnit_TheSheetCarriesWhatTheScorerDecided()
    {
        using var w = new EntityRepository();
        w.RegisterComponent<UtilityDecisionLog>();
        w.RegisterComponent<UtilityDebugFlags>();
        w.RegisterComponent<UtilityTraceWorkingMemory1024>();
        var unit = w.CreateEntity();
        w.AddComponent(unit, new UtilityDecisionLog());
        w.AddComponent(unit, new UtilityDebugFlags { TraceEnabled = 1 });
        w.AddComponent(unit, new UtilityTraceWorkingMemory1024());
        UtilityDecisionCatalog.EnsureRegistered();   // the scorer reads the registry it is built over
        UtilityScorer? cache = null;
        byte winner = UtilityDecisionCatalog.ScorerOverShared(ref cache).ChooseOption(w, unit, Posture, 0);

        var live = UtilityDecisionSheet.Build(Posture)!.WithLive(w, unit);
        Assert.True(live.Traced);
        Assert.Equal(1, live.Evaluations);
        Assert.Equal(live.Options.First(o => o.OptionId == winner).Name, live.WinnerName);
        Assert.True(live.Options[0].Winner, "the winner is drawn first");
        Assert.Contains(live.Options, o => o.Considerations.Any(c => c.Raw.HasValue));

        var other = w.CreateEntity();   // a unit with no log: the settings, "not traced"
        Assert.False(UtilityDecisionSheet.Build(Posture)!.WithLive(w, other).Traced);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Holder { public int Before; public UtilityDecisionRef Response; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Block
    {
        public ChooseOptionParams Choose;
        public UtilityChoice Choice;
        public IsOptionParams IsCover;
        public Holder Custom;
    }

    /// <summary>⭐⭐ The bindings, from the method keys and the behaviour's default block: ChooseOption CHOOSES its params' decision, an
    /// IsOption READS the decision of the ChooseOption sharing its state offset (and names its option), a custom action HOLDS every
    /// decision ref in its params. ✅ Red-proof: the IsOption match on the state offset dropped ⇒ red.</summary>
    [Fact]
    public unsafe void CE1325_ANodesDecision_ComesFromItsKeyAndTheDefaults()
    {
        int choose = (int)Marshal.OffsetOf<Block>(nameof(Block.Choose)), choice = (int)Marshal.OffsetOf<Block>(nameof(Block.Choice));
        int isCover = (int)Marshal.OffsetOf<Block>(nameof(Block.IsCover)), custom = (int)Marshal.OffsetOf<Block>(nameof(Block.Custom));
        var def = new BehaviorDefinition
        {
            Name = "TestPosture", BlackboardLayoutType = typeof(Block),
            ParseParams = (json, mem, cap, world, self) =>
            {
                ref var b = ref *(Block*)mem;
                b.Choose.Decision = new UtilityDecisionRef(Posture);
                b.IsCover.Option = (byte)Fdp.Toolkit.Utility.Posture.TakeCover;
                b.Custom.Response = new UtilityDecisionRef(Posture);
            },
        };
        const string N = "Fdp.Toolkit.Utility.UtilityNodes.";
        string chooseKey = $"{N}ChooseOption@{choose}@{choice}", readKey = $"{N}IsOption@{isCover}@{choice}", customKey = $"My.Brain.Respond@{custom}";
        var keys = new[] { chooseKey, readKey, customKey };

        var c = Assert.Single(UtilityBindings.FromKey(def, chooseKey, keys));
        Assert.Equal((Posture, DecisionRole.Chooses, "Choose"), (c.DecisionId, c.Role, c.Via));
        var r = Assert.Single(UtilityBindings.FromKey(def, readKey, keys));
        Assert.Equal((Posture, DecisionRole.Reads, (int)Fdp.Toolkit.Utility.Posture.TakeCover), (r.DecisionId, r.Role, r.Option));
        var h = Assert.Single(UtilityBindings.FromKey(def, customKey, keys));
        Assert.Equal((DecisionRole.Holds, "Custom.Response"), (h.Role, h.Via));
        Assert.Empty(UtilityBindings.FromKey(def, $"{N}IsOption@{isCover}@999", keys));   // a guard with no chooser on its state
    }

    /// <summary>📷 The Decision drawing with a unit (W2) — <c>HROT_RAIL_SHOTS</c>/<c>decision-live.png</c>; and the settings alone (W1).</summary>
    [SkippableFact]
    public void CE1325_TheDecision_Draws()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-brain");
        Directory.CreateDirectory(shots);
        using var w = new EntityRepository();
        w.RegisterComponent<UtilityDecisionLog>();
        w.RegisterComponent<UtilityDebugFlags>();
        w.RegisterComponent<UtilityTraceWorkingMemory1024>();
        var unit = w.CreateEntity();
        w.AddComponent(unit, new UtilityDecisionLog());
        w.AddComponent(unit, new UtilityDebugFlags { TraceEnabled = 1 });
        w.AddComponent(unit, new UtilityTraceWorkingMemory1024());
        UtilityDecisionCatalog.EnsureRegistered();   // the scorer reads the registry it is built over
        UtilityScorer? cache = null;
        UtilityDecisionCatalog.ScorerOverShared(ref cache).ChooseOption(w, unit, Posture, 0);
        var settings = UtilityDecisionSheet.Build(Posture)!;
        var live = settings.WithLive(w, unit);

        using var frame = UiFrameHarness.Begin(760, 900);
        void Draw(UtilityDecisionSheet s, string id)
        {
            ImGui.SetNextWindowPos(new Vector2(8, 8));
            ImGui.SetNextWindowSize(new Vector2(740, 880));
            ImGui.Begin("Decision##" + id);
            UtilityDecisionWidget.Draw(s, id, unitLabel: e => $"#{1000 + e.Index}");
            ImGui.End();
        }
        frame.StepN(4, () => Draw(settings, "w1"));
        frame.Screenshot(Path.Combine(shots, "decision-settings.png"));
        frame.StepN(4, () => Draw(live, "w2"));
        frame.Screenshot(Path.Combine(shots, "decision-live.png"));
    }
}
