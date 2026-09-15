using Fdp.Core.Logging;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using NLog;
using NLog.Config;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// <b>Batch 27, matrix axis 1 — RUN the result and assert a VALUE.</b>
///
/// <para>
/// ⭐ <b>The gap this closes.</b> The Batch-25 matrix compiles an editor-authored blueprint and asserts
/// zero diagnostics. It never ticks one, so it can only ever prove *"this graph is valid C#"* — and the
/// user's report was a graph that <b>compiled perfectly and printed the wrong number</b>: *"every second
/// I got `[AI.Behavior.Blueprint] 0` — the value NOT following the Count variable."* No number of
/// compile cells can see that.
/// </para>
///
/// <para>
/// ⚠ <b>And the first thing this axis did was contradict the diagnosis it was built to confirm.</b>
/// Batch 27's handoff attributed the printed <c>0</c> to the missing <c>ArgTypes</c> entry leaving the
/// pin <c>System.Object</c> and formatting a default. Measured here, that is <b>not</b> what happens —
/// see <see cref="AnUntypedArgumentPin_StillFormatsTheWiredValueCorrectly"/> and
/// <see cref="AnUnwiredUntypedArgumentPin_PrintsNothingAndWarns"/>. The three shapes below are what the
/// runtime actually does, and they are pinned precisely so the next session reasons from measurements
/// rather than from a plausible story. ⭐ <b>An axis whose first result is "the stated cause is wrong"
/// is the axis earning its cost.</b>
/// </para>
///
/// <para>
/// ⚠ <b>The composition must stay on the authoring path.</b> <c>BP124_PrintStringReachesTheLogTests</c>
/// already ticks a Print String and asserts the substituted value — but it builds the asset's JSON by
/// hand, including the pin types, so it proves the <i>compiler</i> substitutes correctly while saying
/// nothing about whether the <i>editor</i> ever recorded a type to substitute. That distinction is the
/// entire content of BP-201, and it is why these tests compose through <see cref="AuthoringPath"/> and
/// then round-trip through the editor's own save.
/// </para>
///
/// <para>
/// ⚠ <see cref="AiBehaviorLogTarget.SharedInstance"/> and <see cref="LogManager.Configuration"/> are
/// process-wide singletons; both are restored in a <c>finally</c> so nothing leaks into another test.
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class AuthoringPathRunValueTests
{
    /// <summary>
    /// ⭐ <b>The user's defect, as a value assertion.</b> Author a Print String through the editor, wire
    /// an int literal into its argument pin through the editor, tick it, and assert the <b>number</b>
    /// reaches the log — not merely that a line did.
    ///
    /// <para>
    /// ⚠ <b>What makes this non-vacuous:</b> it also asserts the raw <c>{Count}</c> placeholder is
    /// absent. A message containing it would mean interpolation never ran, which is a different (and
    /// louder) failure than the silent wrong-value one being tested.
    /// </para>
    /// </summary>
    [Fact]
    public void AnEditorAuthoredPrintString_Ticked_LogsTheWiredValue_NotADefault()
    {
        var messages = AuthorWirePrintAndTick("Run_Value", "count={Count}", "Count", literalValue: "42");

        Assert.Contains(messages, m => m.Contains("count=42"));
        Assert.DoesNotContain(messages, m => m.Contains("{Count}"));
    }

    /// <summary>
    /// ⚠ <b>The measurement that refuted the handoff's diagnosis.</b> An argument pin with <b>no</b>
    /// <c>ArgTypes</c> entry — i.e. exactly the shipped shape, <c>System.Object</c> — still formats the
    /// wired value correctly, because <c>Stage5.ResolveDataPin</c> resolves the <i>source</i> value and
    /// the pin's declared type never enters the interpolation.
    ///
    /// <para>
    /// ⭐ So the missing <c>ArgTypes</c> is a real gap (an untyped pin is a wildcard that accepts any
    /// wire, and renders unnamed) but it is <b>not</b> what printed the user's <c>0</c>. That cause is
    /// still open — see the tracker.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUntypedArgumentPin_StillFormatsTheWiredValueCorrectly()
    {
        var previousConfig = LogManager.Configuration;
        AiBehaviorLogTarget.SharedInstance.Clear();
        try
        {
            CaptureAiBehaviorLog();

            var doc = Compose("Run_Untyped", "count={Count}", "Count", "42", out var print, out _);

            // Strip the type the wire-time bake recorded, leaving the pre-BP-201 shape verbatim.
            ((PrintStringNode)print).ArgTypes.Clear();

            var messages = Tick(doc.Asset);

            Assert.Contains(messages, m => m.Contains("count=42"));
        }
        finally
        {
            AiBehaviorLogTarget.SharedInstance.Clear();
            LogManager.Configuration = previousConfig;
        }
    }

    /// <summary>
    /// An <b>unwired</b> argument pin prints <b>nothing</b> for that placeholder (<c>count=</c>) and
    /// emits <c>BP4001</c>. ⚠ Not a zero — which is why the user's <c>0</c> needs a different
    /// explanation.
    ///
    /// <para>
    /// ⭐ <b>The warning is the story here.</b> <c>BP4001</c> was emitted all along and the generator
    /// discarded it on the success path until BP-121, so a designer whose pin was silently unwired saw
    /// a blank value and no diagnostic whatsoever.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnwiredUntypedArgumentPin_PrintsNothingAndWarns()
    {
        var previousConfig = LogManager.Configuration;
        AiBehaviorLogTarget.SharedInstance.Clear();
        try
        {
            CaptureAiBehaviorLog();

            var doc   = AuthoringPath.Open(
                AuthoringPath.NewAsset("Run_Unwired", BlueprintDispatchKind.Instance));
            var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");
            var s     = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
            s.SetFormatForTest("count={Count}");
            doc.Model.RebuildAndNotify();

            var entry = doc.Graph.Nodes.First(n => n is EventEntryNode);
            var ret   = doc.Graph.Nodes.First(n => n is ReturnNode);
            AuthoringPath.Link(doc, entry, "Out", print, "In");
            AuthoringPath.Link(doc, print, "Out", ret,   "In");
            // {Count} deliberately left unwired.

            var generated = AuthoringPath.Generate(doc.Asset);
            Assert.Contains(generated.GeneratorDiagnostics,
                d => d.Id == "BP4001"
                     && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);

            var messages = Tick(doc.Asset);

            Assert.Contains(messages, m => m.Contains("count="));
            Assert.DoesNotContain(messages, m => m.Contains("count=0"));
        }
        finally
        {
            AiBehaviorLogTarget.SharedInstance.Clear();
            LogManager.Configuration = previousConfig;
        }
    }

    /// <summary>
    /// The same run, one edit later: renaming the placeholder and re-wiring must still print the value.
    /// ⚠ Axis 1 crossed with axis 3 — BP-202's prune removes a wire, and a fix that left the node
    /// unreachable would still compile clean and simply print nothing.
    /// </summary>
    [Fact]
    public void AfterRenamingThePlaceholderAndRewiring_TheValueStillReachesTheLog()
    {
        var previousConfig = LogManager.Configuration;
        AiBehaviorLogTarget.SharedInstance.Clear();
        try
        {
            CaptureAiBehaviorLog();

            var doc     = Compose("Run_Rename", "count={Count}", "Count", "7", out var print, out var literal);
            var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);

            session.SetFormatForTest("total={Sum}");
            doc.Model.RebuildAndNotify();
            AuthoringPath.Link(doc, literal, "Value", print, "Sum");

            var messages = Tick(doc.Asset);

            Assert.Contains(messages, m => m.Contains("total=7"));
        }
        finally
        {
            AiBehaviorLogTarget.SharedInstance.Clear();
            LogManager.Configuration = previousConfig;
        }
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> AuthorWirePrintAndTick(
        string assetName, string format, string argName, string literalValue)
    {
        var previousConfig = LogManager.Configuration;
        AiBehaviorLogTarget.SharedInstance.Clear();
        try
        {
            CaptureAiBehaviorLog();
            var doc = Compose(assetName, format, argName, literalValue, out _, out _);
            return Tick(doc.Asset);
        }
        finally
        {
            AiBehaviorLogTarget.SharedInstance.Clear();
            LogManager.Configuration = previousConfig;
        }
    }

    /// <summary>
    /// Entry → Print String → Return, with an int literal wired into the derived argument pin — every
    /// step through the editor's own commands and Details sessions.
    /// </summary>
    private static AuthoringPath.Document Compose(
        string assetName, string format, string argName, string literalValue,
        out Node print, out Node literal)
    {
        var doc = AuthoringPath.Open(AuthoringPath.NewAsset(assetName, BlueprintDispatchKind.Instance));

        print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");
        var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        session.SetFormatForTest(format);
        doc.Model.RebuildAndNotify();

        var entry = doc.Graph.Nodes.First(n => n is EventEntryNode);
        var ret   = doc.Graph.Nodes.First(n => n is ReturnNode);
        AuthoringPath.Link(doc, entry, "Out", print, "In");
        AuthoringPath.Link(doc, print, "Out", ret,   "In");

        literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralInt");
        ((LiteralNode)literal).ValueJson = literalValue;
        AuthoringPath.Link(doc, literal, "Value", print, argName);

        return doc;
    }

    /// <summary>
    /// Saves and reloads exactly as the editor does, compiles through real Roslyn, attaches to an
    /// entity and ticks one frame — then returns everything the AI-Behaviors sink captured.
    /// </summary>
    private static IReadOnlyList<string> Tick(BlueprintAsset authored)
    {
        var asset = AuthoringPath.SaveAndReload(authored);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);

        return AiBehaviorLogTarget.SharedInstance.GetMessages().Select(m => m.Message).ToList();
    }

    /// <summary>
    /// Registers the <c>"AI.Behavior*"</c> NLog rule that <c>Hrot.ClusterRunner/Program.cs</c> sets up
    /// at startup and which never runs headless. ⚠ Without it every assertion here would pass or fail
    /// on an empty list rather than on what the blueprint actually logged.
    /// </summary>
    private static void CaptureAiBehaviorLog()
    {
        var config = new LoggingConfiguration();
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, AiBehaviorLogTarget.SharedInstance, "AI.Behavior*");
        LogManager.Configuration = config;
    }
}
