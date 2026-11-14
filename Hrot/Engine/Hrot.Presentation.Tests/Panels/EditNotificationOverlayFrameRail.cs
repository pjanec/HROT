using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Replication.Edits;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Edits;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ Edit framework F7 (docs/UX/DESIGN_Edit_Framework.md §2.5a, B-12; the mockup <c>docs/UX/edit-notifications-visual.svg</c> is the acceptance spec, R-4143/R-4146) — the
/// notification stack in a REAL ImGui frame (Xvfb; a skip is not a pass), measured, not eyeballed: the stack sits at the main viewport's top-right, 360 px wide, three cards of
/// 62 px at a 70 px pitch (newest first), the colour bar is <see cref="StatusPalette.Error"/> (refused / lost) or <see cref="StatusPalette.Pending"/> (conflict), the glyph is drawn
/// in that colour, <b>Show</b> on a refused / lost card, <b>Reload / Overwrite</b> on a conflict (only when the host wired them), <i>+ 2 more · Dismiss all</i> under the stack;
/// clicks work, a click beside the stack reaches the window under it (non-modal), and a steady frame allocates 0 B.
/// 🔴 Red-proof (compiles): the card height 62 → 50 ⇒ the pitch/height rail goes red; <c>Collect</c> max 3 → 4 ⇒ the count rail.
/// Renders (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>edit-notifications.png</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class EditNotificationOverlayFrameRail
{
    private const int W = 900, H = 560;

    private static bool Near(Color c, Vector4 v, int tol) =>
        Math.Abs(c.R - v.X * 255f) <= tol && Math.Abs(c.G - v.Y * 255f) <= tol && Math.Abs(c.B - v.Z * 255f) <= tol;

    private static void Shoot(UiFrameSession frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    private static void Pos(ImGuiIOPtr io, float x, float y) { io.AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    /// <summary>The mockup's five notifications (two older ones fold into "+ 2 more"): Delete refused · Commit conflicts · Embark lost on top.</summary>
    private static EntityRepository World(out List<Guid> ids)
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        var list = new List<Guid>();
        EditTicket Ask(string label, EditStatus outcome, string reason, int netId)
        {
            var unit = repo.CreateEntity();                                          // the unit the edit was about: Show selects it by its network id
            repo.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = netId });
            var t = Fdp.Toolkit.Replication.Edits.Edits.Submit(repo, EditOp.Interact(unit, _ => { }, label));
            if (outcome != EditStatus.Pending) EditOutcomes.Report(repo, t.RequestId, netId, outcome, reason, 3);
            list.Add(t.RequestId);
            return t;
        }
        Ask("Rename", EditStatus.Refused, "the name is taken", 5);
        Ask("Possess", EditStatus.Refused, "someone else holds it", 6);
        Ask("Embark", EditStatus.Pending, "", 7);                                    // never answered ⇒ Lost once the 5 s pass
        Ask("Commit mission plan", EditStatus.Conflict, "the plan changed (version 4, yours 3)", 8);
        Ask("Delete", EditStatus.Refused, "Alpha 1-1 is a player's unit", 9);
        ids = list;
        return repo;
    }

    private sealed class Clock { public DateTime Now = DateTime.UtcNow; }

    private static EditNotificationOverlay Overlay(Clock clock, List<int> shown, List<string> reloaded, bool wireConflict)
        => new(show: id => shown.Add(id),
               reload: wireConflict ? e => reloaded.Add("reload " + e.Label) : null,
               overwrite: wireConflict ? e => reloaded.Add("overwrite " + e.Label) : null,
               utcNow: () => clock.Now);

    private static void Step(UiFrameSession frame, EditNotificationOverlay overlay, EntityRepository world, Action<ImGuiIOPtr>? inject = null, Action? under = null)
        => frame.Step(() =>
        {
            inject?.Invoke(ImGui.GetIO());
            if (under != null)                                                       // a window UNDER the stack, covering the viewport
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(W, H));
                ImGui.Begin("##under", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove);
                under();
                ImGui.End();
            }
            overlay.Draw(world);
        });

    /// <summary>Layout, colours, glyphs and buttons match the mockup, measured on the rendered frame.</summary>
    [SkippableFact]
    public void TheStack_SitsTopRight_360Wide_ThreeCardsAt70Pitch_WithTheMockupsBarsGlyphsAndButtons()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        new EditorFontService().Initialize(1f, 1f);
        using var world = World(out _);
        var clock = new Clock(); var shown = new List<int>(); var calls = new List<string>();
        var overlay = Overlay(clock, shown, calls, wireConflict: true);
        for (int i = 0; i < 3; i++) Step(frame, overlay, world);
        clock.Now += TimeSpan.FromSeconds(6);                                        // the Embark nobody answered
        for (int i = 0; i < 4; i++) Step(frame, overlay, world);
        Shoot(frame, "edit-notifications.png");

        Assert.Equal((3, 5), (overlay.ShownCount, overlay.TotalCount));
        var cards = overlay.Items.ToArray().Where(i => i.Kind == EditNotificationOverlay.ItemKind.Card).OrderBy(i => i.Card).ToArray();
        Assert.Equal(3, cards.Length);
        float right = W - EditNotificationOverlay.RightMargin;
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(EditNotificationOverlay.Width, cards[i].Max.X - cards[i].Min.X, 0.5f);          // 360 wide
            Assert.Equal(right, cards[i].Max.X, 0.5f);                                                    // top-right: 12 px from the right edge
            Assert.Equal(62f, cards[i].Max.Y - cards[i].Min.Y, 0.5f);
            Assert.Equal(cards[0].Min.Y + i * 70f, cards[i].Min.Y, 0.5f);                                 // 62 + 8 gap
        }
        Assert.True(cards[0].Min.Y < 40f, "the stack is at the TOP of the window");

        Item(overlay, EditNotificationOverlay.ItemKind.Show, 0); Item(overlay, EditNotificationOverlay.ItemKind.Show, 2);
        var reload = Item(overlay, EditNotificationOverlay.ItemKind.Reload, 1); var over = Item(overlay, EditNotificationOverlay.ItemKind.Overwrite, 1);
        Assert.DoesNotContain(overlay.Items.ToArray(), i => i.Kind == EditNotificationOverlay.ItemKind.Show && i.Card == 1);       // a conflict has Reload / Overwrite, not Show
        Assert.DoesNotContain(overlay.Items.ToArray(), i => i.Kind is EditNotificationOverlay.ItemKind.Reload && i.Card != 1);
        foreach (var b in new[] { Item(overlay, EditNotificationOverlay.ItemKind.Show, 0), reload, over })
            Assert.Equal(20f, b.Max.Y - b.Min.Y, 0.5f);                                                    // buttons are 20 px high
        var card1 = cards[1];
        Assert.Equal(card1.Min.X + 34f, reload.Min.X, 0.5f);                                               // the first button at x + 34
        Assert.Equal(card1.Min.Y + 41f, reload.Min.Y, 0.5f);
        Assert.Equal(reload.Max.X + 6f, over.Min.X, 0.5f);                                                 // 6 px apart
        var all = overlay.Items.ToArray().Single(i => i.Kind == EditNotificationOverlay.ItemKind.DismissAll);
        Assert.True(all.Min.Y > cards[2].Max.Y && all.Max.X <= cards[2].Max.X, "Dismiss all sits under the stack, right-aligned");

        var shot = Raylib.LoadImageFromScreen();
        try
        {
            // the left bar: Error (refused, lost) / Pending-yellow (conflict), 4 px wide, the card's full height
            var bar = new[] { StatusPalette.Error, StatusPalette.Pending, StatusPalette.Error };
            for (int i = 0; i < 3; i++)
            {
                var mid = new Vector2(cards[i].Min.X + 2f, (cards[i].Min.Y + cards[i].Max.Y) / 2f);
                Assert.True(Near(Raylib.GetImageColor(shot, (int)mid.X, (int)mid.Y), bar[i], 8), $"card {i}: the bar is {(i == 1 ? "Pending" : "Error")}");
                Assert.True(!Near(Raylib.GetImageColor(shot, (int)cards[i].Min.X + 6, (int)mid.Y), bar[i], 8), $"card {i}: the bar is 4 px wide");
            }
            // the glyph: drawn in the bar's colour, in its slot (left of the title)
            for (int i = 0; i < 3; i++)
            {
                int n = 0;
                for (int x = (int)cards[i].Min.X + 10; x < (int)cards[i].Min.X + 30; x++)
                    for (int y = (int)cards[i].Min.Y + 6; y < (int)cards[i].Min.Y + 28; y++)
                        if (Near(Raylib.GetImageColor(shot, x, y), bar[i], 40)) n++;
                Assert.True(n >= 20, $"card {i}: its glyph is drawn in its colour ({n} px)");
            }
            // the card is the mockup's #18181C
            var bg = Raylib.GetImageColor(shot, (int)cards[0].Max.X - 40, (int)cards[0].Min.Y + 30);
            Assert.True(Math.Abs(bg.R - 0x18) < 6 && Math.Abs(bg.G - 0x18) < 6 && Math.Abs(bg.B - 0x1C) < 6, $"the card background is #18181C ({bg.R:X2}{bg.G:X2}{bg.B:X2})");
        }
        finally { Raylib.UnloadImage(shot); }
    }

    /// <summary>
    /// ⭐ CE-4343 — at a UI scale above 1 (1.5: a 21 px font) the cards are laid out FROM THE FONT: 1.5× as wide, the button row under the reason (never
    /// into it), the × inside the card, the cards stacked without overlapping. 🔒 User: <i>"the title touches the icon, the reason line runs into the Show
    /// button, the cards overlap"</i>. Renders <c>edit-notifications-scale-1.5.png</c>. 🔴 Mutation: a fixed 62 px card ⇒ the pitch / button assertions fail.
    /// </summary>
    [SkippableFact]
    public void CE4343_AtUiScale1_5_TheCardsAreLaidOutFromTheFont()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        new EditorFontService().Initialize(1f, 1.5f);
        using var world = World(out _);
        var clock = new Clock(); var shown = new List<int>(); var calls = new List<string>();
        var overlay = Overlay(clock, shown, calls, wireConflict: true);
        for (int i = 0; i < 3; i++) Step(frame, overlay, world);
        clock.Now += TimeSpan.FromSeconds(6);
        for (int i = 0; i < 4; i++) Step(frame, overlay, world);
        Shoot(frame, "edit-notifications-scale-1.5.png");

        var cards = overlay.Items.ToArray().Where(i => i.Kind == EditNotificationOverlay.ItemKind.Card).OrderBy(i => i.Card).ToArray();
        Assert.Equal(3, cards.Length);
        float s = overlay.CardWidthNow / EditNotificationOverlay.Width;
        Assert.InRange(s, 1.35f, 1.65f);                                                                   // the font grew ⇒ the card grew with it
        for (int i = 0; i < 3; i++)
        {
            Assert.True(cards[i].Max.Y - cards[i].Min.Y >= 62f * s - 0.5f, $"card {i} is at least the mockup's height × scale");
            if (i > 0) Assert.True(cards[i].Min.Y >= cards[i - 1].Max.Y + 1f, $"card {i} starts below card {i - 1} (no overlap)");
            Assert.True(cards[i].Max.X <= W, $"card {i} is on screen");
        }
        var show = Item(overlay, EditNotificationOverlay.ItemKind.Show, 0);
        var reload = Item(overlay, EditNotificationOverlay.ItemKind.Reload, 1);
        foreach (var (b, c) in new[] { (show, cards[0]), (reload, cards[1]) })
        {
            Assert.True(b.Min.Y >= c.Min.Y + 41f * s - 0.5f, "the button row is under the reason line");
            Assert.True(b.Max.Y <= c.Max.Y, "the button is inside its card");
            Assert.True(b.Min.X >= c.Min.X + 34f * s - 0.5f, "the button starts in the text column");
        }
        var x0 = Item(overlay, EditNotificationOverlay.ItemKind.Close, 0);
        Assert.True(x0.Max.X <= cards[0].Max.X && x0.Max.Y <= cards[0].Max.Y, "the × is inside the card");
        var all = overlay.Items.ToArray().Single(i => i.Kind == EditNotificationOverlay.ItemKind.DismissAll);
        Assert.True(all.Min.Y >= cards[2].Max.Y, "Dismiss all sits under the stack");
    }

    private static EditNotificationOverlay.Item Item(EditNotificationOverlay overlay, EditNotificationOverlay.ItemKind kind, int card)
        => overlay.Items.ToArray().Single(i => i.Kind == kind && i.Card == card);

    /// <summary>Reload / Overwrite are not drawn when the host wired none (the plan's reload and force-commit are the host's).</summary>
    [SkippableFact]
    public void AConflictWithNoHostCallbacks_DrawsNoReloadOrOverwrite()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        new EditorFontService().Initialize(1f, 1f);
        using var world = World(out _);
        var overlay = Overlay(new Clock(), new List<int>(), new List<string>(), wireConflict: false);
        Assert.False(overlay.ConflictButtonsWired);
        for (int i = 0; i < 4; i++) Step(frame, overlay, world);
        Assert.DoesNotContain(overlay.Items.ToArray(), i => i.Kind is EditNotificationOverlay.ItemKind.Reload or EditNotificationOverlay.ItemKind.Overwrite);
        Assert.Contains(overlay.Items.ToArray(), i => i.Kind == EditNotificationOverlay.ItemKind.Card && i.Card == 1);
    }

    /// <summary>The clicks: × dismisses one (the stack moves up, "+ N more" shrinks); Show selects the entry's unit by network id; Reload / Overwrite call the host; Dismiss all clears
    /// the stack; a click BESIDE the stack reaches the window under it (non-modal). The dismissed entries stay in the journal.</summary>
    [SkippableFact]
    public void TheButtonsWork_AndTheStackIsNonModal()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        new EditorFontService().Initialize(1f, 1f);
        using var world = World(out var ids);
        var clock = new Clock(); var shown = new List<int>(); var calls = new List<string>();
        var overlay = Overlay(clock, shown, calls, wireConflict: true);
        int underClicks = 0;
        void Under() { ImGui.SetCursorScreenPos(new Vector2(20, 100)); if (ImGui.Button("under", new Vector2(120, 30))) underClicks++; }
        void Click(Vector2 at)
        {
            Step(frame, overlay, world, io => Pos(io, at.X, at.Y), Under);
            Step(frame, overlay, world, io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); }, Under);
            Step(frame, overlay, world, io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); }, Under);
            Step(frame, overlay, world, io => Pos(io, 5, 5), Under);   // the release lands one frame after it was injected
            Step(frame, overlay, world, null, Under);                   // ...and the stack redraws from the journal the frame after the click
        }
        Vector2 Centre(EditNotificationOverlay.Item i) => (i.Min + i.Max) / 2f;
        for (int i = 0; i < 4; i++) Step(frame, overlay, world, null, Under);
        clock.Now += TimeSpan.FromSeconds(6);
        for (int i = 0; i < 3; i++) Step(frame, overlay, world, null, Under);

        Click(Centre(Item(overlay, EditNotificationOverlay.ItemKind.Show, 0)));               // Delete's unit (network id 9)
        Assert.Equal(new[] { 9 }, shown);
        Click(Centre(Item(overlay, EditNotificationOverlay.ItemKind.Overwrite, 1)));
        Click(Centre(Item(overlay, EditNotificationOverlay.ItemKind.Reload, 1)));
        Assert.Equal(new[] { "overwrite Commit mission plan", "reload Commit mission plan" }, calls);

        Click(new Vector2(60, 115));                                                          // beside the stack: the window under it
        Assert.Equal(1, underClicks);

        Click(Centre(Item(overlay, EditNotificationOverlay.ItemKind.Close, 0)));              // × on the top card
        Assert.Equal((3, 4), (overlay.ShownCount, overlay.TotalCount));
        Assert.True(EditJournal.Of(world).Find(ids[4])!.Dismissed);                           // dismissed, still in the journal
        Click(Centre(overlay.Items.ToArray().Single(i => i.Kind == EditNotificationOverlay.ItemKind.DismissAll)));
        Step(frame, overlay, world, null, Under);
        Assert.Equal((0, 0), (overlay.ShownCount, overlay.TotalCount));
        Assert.Equal(5, EditJournal.Of(world).Count);
    }

    /// <summary>A steady frame allocates nothing (the card text is cached per request id and status).</summary>
    [SkippableFact]
    public void ASteadyFrame_AllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        new EditorFontService().Initialize(1f, 1f);
        using var world = World(out _);
        var clock = new Clock();
        var overlay = Overlay(clock, new List<int>(), new List<string>(), wireConflict: true);
        for (int i = 0; i < 4; i++) Step(frame, overlay, world);
        clock.Now += TimeSpan.FromSeconds(6);
        for (int i = 0; i < 4; i++) Step(frame, overlay, world);
        long bytes = -1;
        for (int i = 0; i < 4; i++)
            frame.Step(() =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                overlay.Draw(world);
                bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            });
        Assert.Equal(3, overlay.ShownCount);
        Assert.True(bytes == 0, $"a steady frame of three cards allocates nothing ({bytes} bytes)");
    }

    /// <summary>⭐ F7 (B-12, DESIGN_Edit_Framework §2.5 "the history") — the Message Log's <b>Edits</b> tab: ONE line per edit when it reaches its FINAL answer
    /// (applied ones too — the history, not the signal), none while pending, never twice; a sync with nothing moved walks nothing. No display needed.
    /// ✅ Red-proof: Sync without the IsFinal test ⇒ the pending Embark is logged ⇒ red.</summary>
    [Fact]
    public void F7_TheEditsTab_LogsEachFinalAnswerOnce_NotWhilePending()
    {
        using var world = World(out _);
        var history = new EditHistoryMessageLogSource();
        var journal = EditJournal.Of(world);
        var now = DateTime.UtcNow;
        journal.Refresh(EditOutcomeLog.Of(world), now);
        history.Sync(journal);
        var lines = new List<Fdp.Core.Logging.MessageLogEntry>();
        history.CopyMessagesTo(lines);
        Assert.Equal(4, lines.Count);                                                       // Rename, Possess, Commit, Delete — not the pending Embark
        Assert.Contains(lines, l => l.Message.StartsWith("Delete refused — Alpha 1-1 is a player's unit · #9", StringComparison.Ordinal));
        Assert.All(lines, l => Assert.Equal(Fdp.Core.Logging.LogSeverity.Warning, l.Severity));

        history.Sync(journal);                                                              // nothing moved: nothing new
        history.CopyMessagesTo(lines);
        Assert.Equal(4, lines.Count);

        journal.Refresh(EditOutcomeLog.Of(world), now + TimeSpan.FromSeconds(6));         // the Embark times out to Lost
        history.Sync(journal);
        history.CopyMessagesTo(lines);
        Assert.Equal(5, lines.Count);
        Assert.StartsWith("Embark got no answer", lines[4].Message, StringComparison.Ordinal);
        history.Sync(journal);
        history.CopyMessagesTo(lines);
        Assert.Equal(5, lines.Count);                                                       // never twice
    }
}
