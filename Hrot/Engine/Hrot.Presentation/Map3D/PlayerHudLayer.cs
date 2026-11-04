using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.Combat.Components;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Rules;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>What the player's HUD shows this frame, as data (the draw and the rails read the same thing).</summary>
public sealed record PlayerHudFrame(PlayerView View, TrainingHudModel? Training, GameHudModel? Game, IReadOnlyList<KillFeed.Entry> Kills, double Now)
{
    /// <summary>⭐ G1 — the scoreboard, only while the key is held and <c>game.scoreboard</c> is on.</summary>
    public ScoreboardModel? Board { get; init; }
    /// <summary>⭐ G1 — the live "+100" pop-ups of the possessed man.</summary>
    public IReadOnlyList<ScorePopup> Popups { get; init; } = Array.Empty<ScorePopup>();
    /// <summary>⭐ CE-4246 — the coaching hint (<c>hud.coaching</c>), or null.</summary>
    public string? Coaching { get; init; }
}

/// <summary>
/// ⭐⭐ H1 + H2 (CE-4226 … CE-4228; docs/DESIGN_Player_HUD_And_Debrief.md PH-3 … PH-5) — the player's HUD: <see cref="Compose"/> turns the ONE reader's rows
/// (<see cref="RulesReader"/>), the kill feed and the viewer into models, each element behind its own Node Feature; <see cref="Draw"/> paints them through the ONE
/// <see cref="HudText"/> in <c>PossessionStation.DrawOverlay</c>. An element with no data draws nothing, so a training scenario shows no game bar and a game
/// scenario no mission list even with every feature on.
/// </summary>
public static class PlayerHudLayer
{
    /// <summary>The models for <paramref name="unit"/>, each only while its feature is on (<paramref name="on"/>). Null models ⇒ nothing to draw.</summary>
    public static PlayerHudFrame Compose(EntityRepository w, Entity unit, double now, Func<string, bool> on, bool scoreboardHeld = false)
    {
        var view = PlayerView.Of(w, unit);
        if (view.IsNone) return new PlayerHudFrame(view, null, null, Array.Empty<KillFeed.Entry>(), now);

        bool training = on(HudFeatures.Objectives) || on(HudFeatures.OwnEvaluation);
        bool game = on(HudFeatures.Tickets) || on(HudFeatures.CapturePoints) || on(HudFeatures.RoundTimer) || on(HudFeatures.ScorePopups) || on(HudFeatures.BountyMarker);
        TrainingHudModel? t = null; GameHudModel? g = null; ScoreboardModel? board = null; IReadOnlyList<ScorePopup> popups = Array.Empty<ScorePopup>();
        bool wantBoard = scoreboardHeld && on(HudFeatures.Scoreboard), wantPopups = on(HudFeatures.ScorePopups), wantCoach = on(HudFeatures.Coaching);
        string? coaching = null;
        if ((training || game || wantBoard || wantPopups || wantCoach) && w.IsComponentTypeRegistered<ObjectiveStatus>())
        {
            var entries = RulesReader.Read(w, includeAuthored: false);
            if (training) t = TrainingHud.Build(entries, view, now, e => MissionSummaryModel.SideOfRule(w, e));
            if (game) g = GameHud.Build(entries, view, now, w.HasComponent<SimTransform>(unit) ? new Vector2(w.GetComponentRO<SimTransform>(unit).Position.X, w.GetComponentRO<SimTransform>(unit).Position.Y) : null, e => MissionSummaryModel.SideOfRule(w, e));
            if (wantCoach) coaching = CoachingHint.Build(entries, view, e => LiveLeafOverlay.Read(w, e.Entity));
            if (wantPopups) popups = ScorePopups.In(w).Update(entries, view.UnitNetId, now);
            if (wantBoard) board = Scoreboard.Build(entries, view.UnitNetId, e => MissionSummaryModel.SideOfRule(w, e), net => RulesModel.NameOf(w, NetworkIdResolver.ResolveNetworkId(w, net), net));
        }
        var kills = on(HudFeatures.KillFeed) && KillFeed.Of(w) is { } feed ? feed.Recent(now) : Array.Empty<KillFeed.Entry>();
        return new PlayerHudFrame(view, t, g, kills, now) { Board = board, Popups = popups, Coaching = coaching };
    }

    private static Color Rgb(uint rgb, int a = 255) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)a);
    private static Color SideText(ForceId side) => side switch { ForceId.Friend => new Color(127, 182, 255, 255), ForceId.Hostile => new Color(255, 138, 115, 255), _ => new Color(235, 238, 242, 255) };

    private const int Small = 16, Row = 21, Pad = 8;
    /// <summary>The HUD starts this far below the window's top: the compass strip (<c>DrawCompass</c>) owns the first 50 px, its heading read-out the next 10.</summary>
    public const int Top = 62;

    /// <summary>Paints the HUD: the game bar top-centre, the kill feed, the mission list and my standards in the top-right column, the streak under the crosshair, bounty marks over men.</summary>
    public static void Draw(EntityRepository w, Entity unit, MapCamera3D camera, Vector2 size, Font? font, Func<string, bool> on, bool scoreboardHeld = false)
    {
        var f = Compose(w, unit, Fdp.Toolkit.Time.SimClock.Of(w).TotalTime, on, scoreboardHeld);
        if (f.View.IsNone) return;
        if (f.Game is { } g) DrawBar(g, f.View, size, font, on);
        DrawRightColumn(f, size, font, on);
        if (f.Game is { Streak: >= GameHud.StreakShownFrom } s && on(HudFeatures.ScorePopups)) DrawStreak(s.Streak, camera, font);
        DrawPopups(f, camera, font);
        if (f.Board is { IsGame: true } board) DrawBoard(board, size, font);
        if (f.Game is { BountyOn.Count: > 0 } b && on(HudFeatures.BountyMarker)) DrawBounties(w, b.BountyOn, camera, font);
    }

    // ── the top bar (PH-4)

    private static void DrawBar(GameHudModel g, PlayerView view, Vector2 size, Font? font, Func<string, bool> on)
    {
        bool tickets = on(HudFeatures.Tickets) && g.Tickets.Count > 0, points = on(HudFeatures.CapturePoints) && g.Points.Count > 0, clock = on(HudFeatures.RoundTimer) && g.Clock.Length > 0;
        if (!tickets && !points && !clock) return;
        const int box = 28, gap = 6, big = 24;
        var blue = tickets ? g.Tickets.FirstOrDefault(t => t.Side == ForceId.Friend) : null;
        var red = tickets ? g.Tickets.FirstOrDefault(t => t.Side == ForceId.Hostile) : null;
        string bt = blue != null ? ((int)Math.Round(blue.Value)).ToString() : "", rt = red != null ? ((int)Math.Round(red.Value)).ToString() : "";
        float width = Pad * 2;
        if (bt.Length > 0) width += HudText.Measure(font, bt, big) + 14;
        if (points) width += g.Points.Count * (box + gap) - gap + 14;
        if (rt.Length > 0) width += HudText.Measure(font, rt, big) + 14;
        if (clock) width += HudText.Measure(font, g.Clock, 20) + 8;
        var centre = Fdp.Presentation.WindowManager.OpenMapArea.Centre(size);
        float x = centre.X - width / 2, top = Fdp.Presentation.WindowManager.OpenMapArea.Corner(size, true, true, 0, 0, 12f).Y + Top - 8;
        Raylib.DrawRectangleRounded(new Rectangle(x, top, width, box + Pad * 2), 0.25f, 6, new Color(8, 10, 12, 170));
        x += Pad; float ty = top + Pad + (box - big) / 2f - 2;
        if (bt.Length > 0) { HudText.Draw(font, bt, x, ty, big, Rgb(0x7FB6FF)); x += HudText.Measure(font, bt, big) + 14; }
        if (points)
        {
            foreach (var p in g.Points)
            {
                var r = new Rectangle(x, top + Pad, box, box);
                Raylib.DrawRectangleRec(r, Rgb(GameHud.RgbOf(p.Owner)));
                if (p.Contested) Raylib.DrawRectangleRec(new Rectangle(x, top + Pad, box * p.Fraction, box), Rgb(GameHud.RgbOf(p.Challenger)));
                if (p.Contested) for (int sx = (int)x + 4; sx < x + box; sx += 8) Raylib.DrawLine(sx, (int)(top + Pad), sx - 4, (int)(top + Pad + box), new Color(255, 255, 255, 70));   // the stripes
                bool here = g.Strip != null && g.Strip.Text.Contains(" " + p.Letter + " ", StringComparison.Ordinal);
                if (here) Raylib.DrawRectangleLinesEx(r, 2f, Color.White);
                float lw = HudText.Measure(font, p.Letter, 18);
                HudText.Draw(font, p.Letter, x + (box - lw) / 2, top + Pad + 4, 18, Color.White);
                x += box + gap;
            }
            x += 14 - gap;
        }
        if (rt.Length > 0) { HudText.Draw(font, rt, x, ty, big, Rgb(0xFF8A73)); x += HudText.Measure(font, rt, big) + 14; }
        if (clock) HudText.Draw(font, g.Clock, x, ty + 2, 20, g.ClockIsCountdown ? Rgb(0xFFD27A) : Rgb(0xD9DDE2));
        if (g.Strip is { } strip && points)
        {
            string shown = strip.Text.ToUpperInvariant();
            float sw = HudText.Measure(font, shown, 16) + 20;
            float sx = centre.X - sw / 2, sy = top + box + Pad * 2 + 6;
            Raylib.DrawRectangleRounded(new Rectangle(sx, sy, sw, 24), 0.3f, 6, Rgb(GameHud.RgbOf(strip.Side), 200));
            HudText.Draw(font, shown, sx + 10, sy + 3, 16, Color.White);
        }
    }

    // ── the right column: kill feed, mission, my standards

    private static float DrawRightColumn(PlayerHudFrame f, Vector2 size, Font? font, Func<string, bool> on)
    {
        float right = Fdp.Presentation.WindowManager.OpenMapArea.Corner(size, false, true, 0, 0, 16f).X;
        float y = Fdp.Presentation.WindowManager.OpenMapArea.Corner(size, false, true, 0, 0, 16f).Y + Top - 16;
        // the bar is centred and narrow; a column starts below nothing — only the kill feed may sit near the bar's height
        foreach (var e in f.Kills)
        {
            var parts = KillFeed.Line(e, f.View.UnitNetId);
            float w = parts.Sum(p => HudText.Measure(font, p.Text, Small)) + 14;
            float x = right - w;
            float age = (float)Math.Clamp((f.Now - e.At) / KillFeed.ShowSeconds, 0, 1);
            Raylib.DrawRectangle((int)x, (int)y, (int)w, Row, new Color(8, 10, 12, (int)(170 - 80 * age)));
            float cx = x + 7;
            foreach (var p in parts) { HudText.Draw(font, p.Text, cx, y + 2, Small, SideText(p.Side)); cx += HudText.Measure(font, p.Text, Small); }
            y += Row + 2;
        }
        if (f.Kills.Count > 0) y += 6;
        if (f.Training is { } t)
        {
            if (on(HudFeatures.Objectives) && t.Objectives.Count > 0) y = DrawObjectives(t.Objectives, right, y, font);
            if (on(HudFeatures.OwnEvaluation) && t.Standards.Count > 0) y = DrawStandards(t, right, y, font);
        }
        if (f.Coaching is { Length: > 0 } hint && on(HudFeatures.Coaching))
        {
            float w = HudText.Measure(font, hint, Small) + 14, x = right - w;
            Raylib.DrawRectangle((int)x, (int)y, (int)w, Row, new Color(8, 10, 12, 170));
            HudText.Draw(font, hint, x + 7, y + 2, Small, Rgb(0xFFD27A));
            y += Row + 2;
        }
        return y;
    }

    private static float DrawObjectives(IReadOnlyList<ObjectiveLine> lines, float right, float y, Font? font)
    {
        float inner = Math.Max(240f, lines.Max(l => HudText.Measure(font, l.Title, Small) + l.Depth * 14 + 60 + (l.Detail.Length > 0 ? HudText.Measure(font, l.Detail, 13) : 0)));
        float w = inner + Pad * 2, x = right - w;
        int bars = lines.Count(l => l.Fraction >= 0f);
        float h = Pad * 2 + Row + lines.Count * Row + bars * 6;
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), 0.08f, 6, new Color(8, 10, 12, 150));
        HudText.Draw(font, "MISSION", x + Pad, y + Pad - 2, 13, new Color(160, 166, 175, 255));
        float cy = y + Pad + Row - 2;
        foreach (var l in lines)
        {
            float lx = x + Pad + l.Depth * 14;
            DrawMark(l.Mark, lx + 6, cy + 9);
            var col = l.State == ObjectiveState.Active ? Color.White : l.Mark == HudMark.Done ? new Color(150, 215, 175, 255) : l.Mark == HudMark.Failed ? new Color(240, 130, 120, 255) : new Color(170, 176, 185, 255);
            HudText.Draw(font, l.Title, lx + 18, cy + 1, Small, col);
            if (l.Detail.Length > 0) HudText.Draw(font, l.Detail, x + w - Pad - HudText.Measure(font, l.Detail, 13), cy + 3, 13, new Color(200, 205, 212, 255));
            cy += Row;
            if (l.Fraction >= 0f)
            {
                float bw = w - Pad * 2 - l.Depth * 14 - 18;
                Raylib.DrawRectangle((int)(lx + 18), (int)cy - 3, (int)bw, 4, new Color(255, 255, 255, 40));
                Raylib.DrawRectangle((int)(lx + 18), (int)cy - 3, (int)(bw * l.Fraction), 4, Rgb(0x5AA8FF));
                cy += 6;
            }
        }
        return y + h + 6;
    }

    private static void DrawMark(HudMark mark, float cx, float cy)
    {
        switch (mark)
        {
            case HudMark.Done:
                Raylib.DrawLineEx(new Vector2(cx - 5, cy), new Vector2(cx - 1, cy + 4), 2.2f, Rgb(0x4FBF85));
                Raylib.DrawLineEx(new Vector2(cx - 1, cy + 4), new Vector2(cx + 6, cy - 5), 2.2f, Rgb(0x4FBF85));
                break;
            case HudMark.Active: Raylib.DrawCircle((int)cx, (int)cy, 5f, Rgb(0x5AA8FF)); break;
            case HudMark.Failed:
                Raylib.DrawLineEx(new Vector2(cx - 4, cy - 4), new Vector2(cx + 4, cy + 4), 2.2f, Rgb(0xE5645A));
                Raylib.DrawLineEx(new Vector2(cx - 4, cy + 4), new Vector2(cx + 4, cy - 4), 2.2f, Rgb(0xE5645A));
                break;
            default: Raylib.DrawCircleLines((int)cx, (int)cy, 5f, new Color(170, 176, 185, 255)); break;
        }
    }

    private static float DrawStandards(TrainingHudModel t, float right, float y, Font? font)
    {
        var lines = t.Standards;
        float inner = Math.Max(240f, lines.Max(l => HudText.Measure(font, l.Title, Small) + HudText.Measure(font, l.Text, Small) + 24));
        float w = inner + Pad * 2, x = right - w, h = Pad * 2 + Row + lines.Count * Row;
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), 0.08f, 6, new Color(8, 10, 12, 150));
        HudText.Draw(font, t.StandardsAreTeams ? "TEAM STANDARDS" : "MY STANDARDS", x + Pad, y + Pad - 2, 13, new Color(160, 166, 175, 255));
        float cy = y + Pad + Row - 2;
        foreach (var l in lines)
        {
            HudText.Draw(font, l.Title, x + Pad, cy + 1, Small, new Color(225, 228, 232, 255));
            HudText.Draw(font, l.Text, x + w - Pad - HudText.Measure(font, l.Text, Small), cy + 1, Small, Rgb(l.Rgb));
            cy += Row;
        }
        return y + h + 6;
    }

    // ── the streak and the bounty

    private static void DrawStreak(int streak, MapCamera3D camera, Font? font)
    {
        string text = $"STREAK x{streak}";
        var vc = camera.ViewCentre;
        float tw = HudText.Measure(font, text, 22);
        HudText.Draw(font, text, vc.X - tw / 2, vc.Y + 120, 22, new Color(255, 210, 122, 255));
    }

    private static void DrawBounties(EntityRepository w, IReadOnlySet<int> subjects, MapCamera3D camera, Font? font)
    {
        foreach (int net in subjects)
        {
            var e = NetworkIdResolver.ResolveNetworkId(w, net);
            if (e.IsNull || !w.IsAlive(e) || !w.HasComponent<SimTransform>(e)) continue;
            var at = w.GetComponentRO<SimTransform>(e).Position + new Vector3(0f, 0f, 2.5f);
            if (Vector3.Dot(at - camera.Eye, camera.Forward) <= 0.1f) continue;
            var s = camera.WorldToScreen(at);
            if (!float.IsFinite(s.X) || !float.IsFinite(s.Y)) continue;
            Raylib.DrawPoly(new Vector2(s.X, s.Y + 16), 4, 7f, 45f, new Color(255, 210, 122, 255));
            float tw = HudText.Measure(font, "BOUNTY", 14);
            HudText.Draw(font, "BOUNTY", s.X - tw / 2, s.Y - 6, 14, new Color(255, 210, 122, 255));
        }
    }

    // ── G1: the "+100" pop-ups and the scoreboard (hold K)

    private static void DrawPopups(PlayerHudFrame f, MapCamera3D camera, Font? font)
    {
        var vc = camera.ViewCentre;
        int i = 0;
        foreach (var p in f.Popups.OrderByDescending(p => p.At).Take(4))
        {
            float age = (float)Math.Clamp((f.Now - p.At) / ScorePopups.ShowSeconds, 0, 1);
            string text = $"+{p.Points:0.##} {p.Title}";
            float tw = HudText.Measure(font, text, 22);
            HudText.Draw(font, text, vc.X - tw / 2, vc.Y + 70 - age * 30 - i * 24, 22, new Color(255, 210, 122, (int)(255 * (1 - age * age))));
            i++;
        }
    }

    private static void DrawBoard(ScoreboardModel board, Vector2 size, Font? font)
    {
        string[] heads = { "K", "D", "A", "CAPT", "TK", "STREAK", "SCORE" };
        const int rowH = 22, nameW = 190, colW = 62;
        int Rows(ScoreTeam t) => Math.Min(8, t.Rows.Count) + (t.Rows.Skip(8).Any(r => r.Me) ? 1 : 0);
        int lines = board.Teams.Sum(t => Rows(t) + 1);
        float w = 40 + nameW + heads.Length * colW, h = 24 + lines * rowH + 12;
        var centre = Fdp.Presentation.WindowManager.OpenMapArea.Centre(size);
        float x = centre.X - w / 2, y = Math.Max(Top + 60, size.Y / 2 - h / 2);
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), 0.03f, 6, new Color(8, 10, 12, 215));
        float cy = y + 12;
        foreach (var t in board.Teams)
        {
            var head = t.Side == ForceId.Friend ? new Color(127, 182, 255, 255) : new Color(255, 138, 115, 255);
            HudText.Draw(font, $"{t.Name.ToUpperInvariant()}   {t.Total:0.##}", x + 16, cy, 18, head);
            for (int c = 0; c < heads.Length; c++) HudText.Draw(font, heads[c], x + 40 + nameW + c * colW, cy + 3, 13, new Color(160, 166, 175, 255));
            cy += rowH + 2;
            int n = 0;
            foreach (var r in t.Rows.Where((r, idx) => idx < 8 || r.Me))
            {
                if (r.Me) Raylib.DrawRectangle((int)x + 8, (int)cy - 1, (int)w - 16, rowH, new Color(255, 255, 255, 30));
                string name = (r.SubjectNet == t.MvpSubject ? "* " : "") + (r.Me ? "You - " : "") + r.Name;
                while (name.Length > 6 && HudText.Measure(font, name, 15) > nameW - 8) name = name[..^2] + "…";
                HudText.Draw(font, name, x + 24, cy + 2, 15, Color.White);
                string[] vals = { r.Kills.ToString("0"), r.Deaths.ToString("0"), r.Assists.ToString("0"), r.Captures.ToString("0"), r.TeamKills.ToString("0"), $"{r.Streak:0}/{r.BestStreak:0}", r.Score.ToString("0.##") };
                for (int c = 0; c < vals.Length; c++) HudText.Draw(font, vals[c], x + 40 + nameW + c * colW, cy + 2, 15, c == vals.Length - 1 ? new Color(255, 210, 122, 255) : new Color(225, 228, 232, 255));
                cy += rowH; n++;
            }
            cy += rowH * 0.0f + 4;
        }
    }
}
