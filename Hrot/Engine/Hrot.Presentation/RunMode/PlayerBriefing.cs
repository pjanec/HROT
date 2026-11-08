using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.UI.Common.Rules;
using ImGuiNET;

namespace Hrot.UI.Common.RunMode;

/// <summary>What the briefing screen says, as text (the draw and the rails read the same thing). Built once per change, never per frame.</summary>
/// <param name="Title"><c>"&lt;scenario&gt; — briefing"</c>.</param>
/// <param name="YouAre"><c>"You are: &lt;side&gt; · &lt;unit&gt; · &lt;role&gt;"</c>, or the waiting line while nothing is assigned to this seat yet.</param>
/// <param name="Mission">The scenario header's description (empty = none).</param>
/// <param name="Objectives">My side's objectives, required first (<see cref="TrainingHud"/>'s list — the ONE reader).</param>
public sealed record BriefingContent(string Title, string YouAre, string Mission, IReadOnlyList<string> Objectives)
{
    public static readonly BriefingContent Empty = new("", "", "", Array.Empty<string>());
}

/// <summary>
/// ⭐⭐ R-7 / CE-3416 (AQ108 §0 D32, R2″; story panel ⑨) — <b>the Player's briefing screen</b>: while Live waits at 0:00 (<see cref="RunModeChoice.LiveBriefing"/>) a Player seat shows the scenario's
/// description (<i>Mission</i>), who I am (side · unit · role) and MY side's objectives, with a RESERVED, framed area for the scenario's picture book (CE-3417 designs it later —
/// ⛔ nothing is built behind the frame) and <i>waiting for the instructor to start…</i>. It hides the moment ▶ is pressed (LiveBriefing is false then).
/// <para>⭐ Reuses, never re-queries: <see cref="RulesReader"/> → <see cref="TrainingHud.Build"/> for the objectives (R-174), <see cref="PlayerView"/> for who I am, <see cref="ScenarioDescription"/> for the text.
/// ⭐ 0 B steady: the model is rebuilt only when its inputs change (the world, who I hold, how many objective statuses there are, the resident description object); the draw reads cached strings.</para>
/// </summary>
public sealed class PlayerBriefing
{
    public const string Footer = "waiting for the instructor to start…";
    public const string PictureBookTitle = "picture book — page 1 / 4";
    public const string PictureBookNote = "(the scenario's briefing section, to be designed)";
    /// <summary>The Mission text is capped so one ImGui call never marshals more than its stack buffer (0 B per frame).</summary>
    public const int MissionCap = 1400;

    private EntityRepository? _world;
    private EntityQuery? _holds, _statuses;
    private Entity _unit; private int _statusCount = -1; private object? _resident;
    private BriefingContent _content = BriefingContent.Empty;
    private bool _wasShown;

    /// <summary>How many times the model was rebuilt (a rail reads it: a steady frame rebuilds nothing).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>What is on the screen (empty until <see cref="Refresh"/> ran).</summary>
    public BriefingContent Content => _content;

    /// <summary>Whether the screen shows in this run state (null choice = no run controls: never).</summary>
    public static bool Shows(RunModeChoice? choice) => choice is { LiveBriefing: true };

    // ── pure ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The content for the given facts. Pure — the rails drive it directly.</summary>
    public static BriefingContent Build(string scenario, string? description, PlayerView view, string unitName, TacticalDesignation role, IReadOnlyList<ObjectiveLine> objectives)
    {
        string who = view.IsNone ? "You are: not assigned to a unit yet"
            : $"You are: {RulesModel.SideName(view.Side)} · {unitName}" + (role == TacticalDesignation.Undefined ? "" : " · " + RoleWords(role));
        string mission = description ?? "";
        if (mission.Length > MissionCap) mission = mission.Substring(0, MissionCap - 1) + "…";
        var lines = new string[objectives.Count];
        for (int i = 0; i < lines.Length; i++) lines[i] = objectives[i].Title + (objectives[i].Required ? " (required)" : "");
        return new BriefingContent(scenario.Length == 0 ? "Briefing" : scenario + " — briefing", who, mission, lines);
    }

    /// <summary>A role in words (<c>AutomaticRifleman</c> → <c>automatic rifleman</c>).</summary>
    public static string RoleWords(TacticalDesignation role)
    {
        var s = role.ToString(); var sb = new System.Text.StringBuilder(s.Length + 2);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
            sb.Append(char.ToLowerInvariant(s[i]));
        }
        return sb.ToString();
    }

    // ── the model, rebuilt only on change ─────────────────────────────────────────────────

    /// <summary>Call once per frame while the screen shows: rebuilds the content only when the world, the held unit, the number of objective statuses or the resident header changed. 0 B on a steady frame.</summary>
    public void Refresh(EntityRepository? world)
    {
        if (world == null) { if (_world != null) { _world = null; _content = BriefingContent.Empty; } return; }
        if (!ReferenceEquals(world, _world))
        {
            _world = world; _holds = null; _statuses = null; _statusCount = -1; _unit = Entity.Null; _resident = null;
            if (world.IsComponentTypeRegistered<PossessedBy>()) _holds = world.Query().With<PossessedBy>().Build();
            if (world.IsComponentTypeRegistered<ObjectiveStatus>()) _statuses = world.Query().With<ObjectiveStatus>().Build();
        }
        var unit = Entity.Null;
        if (_holds != null) foreach (var e in _holds) { unit = e; break; }
        int count = 0;
        if (_statuses != null) foreach (var _ in _statuses) count++;
        object? resident = world.HasSingletonManaged<ScenarioDescriptionResident>() ? world.GetSingletonManaged<ScenarioDescriptionResident>() : null;
        if (unit == _unit && count == _statusCount && ReferenceEquals(resident, _resident) && Rebuilds > 0) return;
        _unit = unit; _statusCount = count; _resident = resident;
        Rebuilds++;
        Rebuild(world, unit, resident);
    }

    /// <summary>⚠ A separate method: the lambda below captures <c>world</c>, and a captured parameter makes the compiler allocate its closure on ENTRY of the method that holds it — in <see cref="Refresh"/> that was 24 B on every steady frame.</summary>
    private void Rebuild(EntityRepository world, Entity unit, object? resident)
    {
        var view = unit.IsNull ? default : PlayerView.Of(world, unit);
        var resid = resident as ScenarioDescriptionResident;
        IReadOnlyList<ObjectiveLine> objectives = Array.Empty<ObjectiveLine>();
        if (!view.IsNone && _statuses != null)
            objectives = TrainingHud.Build(RulesReader.Read(world, includeAuthored: false), view, Fdp.Toolkit.Time.SimClock.Of(world).TotalTime, e => MissionSummaryModel.SideOfRule(world, e)).Objectives;
        var role = !view.IsNone && world.HasComponent<UnitSubordinate>(unit) ? world.GetComponentRO<UnitSubordinate>(unit).Designation : TacticalDesignation.Undefined;
        string unitName = view.IsNone ? "" : RulesModel.NameOf(world, unit, view.UnitNetId);
        _content = Build(resid?.Name ?? "", string.IsNullOrEmpty(resid?.Text) ? null : resid!.Text, view, unitName, role, objectives);
    }

    // ── the draw ──────────────────────────────────────────────────────────────────────────

    /// <summary>Draws the screen when <see cref="Shows"/> (call once per frame inside the ImGui frame, after <see cref="Refresh"/>). Cached strings only.</summary>
    public void Draw(RunModeChoice? choice, EntityRepository? world)
    {
        bool shows = Shows(choice);
        if (!shows) { _wasShown = false; return; }
        _wasShown = true;
        Refresh(world);
        DrawContent(_content);
    }

    /// <summary>Whether the last <see cref="Draw"/> drew the screen.</summary>
    public bool WasShown => _wasShown;

    private static readonly Vector4 Panel = new(0.07f, 0.09f, 0.12f, 0.96f), Frame = new(0.45f, 0.50f, 0.58f, 0.85f), Dim = new(0.62f, 0.67f, 0.74f, 1f), Text = new(0.92f, 0.94f, 0.97f, 1f);

    /// <summary>The picture: a card over the middle of the window — left the briefing, right the reserved picture-book frame, the footer under both.</summary>
    public static void DrawContent(BriefingContent c)
    {
        var vp = ImGui.GetMainViewport();
        var size = new Vector2(MathF.Min(1000f, vp.Size.X - 64f), MathF.Min(560f, vp.Size.Y - 64f));
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + (vp.Size.X - size.X) / 2f, vp.Pos.Y + (vp.Size.Y - size.Y) / 2f));
        ImGui.SetNextWindowSize(size);
        ImGui.SetNextWindowBgAlpha(Panel.W);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Panel);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28f, 22f));
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus;
        if (ImGui.Begin("##player-briefing", flags))
        {
            float full = ImGui.GetContentRegionAvail().X, left = full * 0.52f;
            var dl = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            ImGui.SetWindowFontScale(1.45f); ImGui.TextColored(Text, c.Title); ImGui.SetWindowFontScale(1f);
            ImGui.TextColored(Text, c.YouAre);
            ImGui.Spacing(); ImGui.Spacing();
            ImGui.TextColored(Dim, "Mission");
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + left);
            ImGui.TextWrapped(c.Mission.Length == 0 ? "(this scenario has no description)" : c.Mission);
            ImGui.PopTextWrapPos();
            ImGui.Spacing(); ImGui.Spacing();
            ImGui.TextColored(Dim, "Your objectives");
            for (int i = 0; i < c.Objectives.Count; i++)
            {
                var p = ImGui.GetCursorScreenPos(); float h = ImGui.GetTextLineHeight();
                DrawFlag(dl, new Vector2(p.X + 2f, p.Y + 1f), h - 2f);
                ImGui.SetCursorScreenPos(new Vector2(p.X + h + 6f, p.Y));
                ImGui.TextColored(Text, c.Objectives[i]);
            }
            if (c.Objectives.Count == 0) ImGui.TextColored(Dim, "(none for your side yet)");

            // the RESERVED picture-book area (CE-3417): a dashed frame, its page line, two inert arrows — nothing behind it
            var tl = new Vector2(origin.X + left + 28f, origin.Y + 4f);
            var br = new Vector2(origin.X + full, origin.Y + size.Y - 22f * 2f - 74f);
            DrawDashedRect(dl, tl, br, ImGui.GetColorU32(Frame));
            DrawCentred(dl, tl, br, PictureBookTitle, Dim, -10f);
            DrawCentred(dl, tl, br, PictureBookNote, Dim * new Vector4(1, 1, 1, 0.7f), 10f);
            DrawArrow(dl, new Vector2(tl.X + 14f, br.Y - 18f), left: true); DrawArrow(dl, new Vector2(br.X - 14f, br.Y - 18f), left: false);

            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + size.Y - 22f * 2f - 34f));
            ImGui.TextColored(Dim, Footer);
        }
        ImGui.End();
        ImGui.PopStyleVar(); ImGui.PopStyleColor();
    }

    private static void DrawFlag(ImDrawListPtr dl, Vector2 p, float h)
    {
        uint col = ImGui.GetColorU32(Dim);
        dl.AddLine(p, new Vector2(p.X, p.Y + h), col, 1.5f);
        dl.AddTriangleFilled(new Vector2(p.X, p.Y), new Vector2(p.X + h * 0.8f, p.Y + h * 0.28f), new Vector2(p.X, p.Y + h * 0.56f), col);
    }

    private static void DrawArrow(ImDrawListPtr dl, Vector2 c, bool left)
    {
        uint col = ImGui.GetColorU32(Dim * new Vector4(1, 1, 1, 0.7f)); float s = 7f, d = left ? -1f : 1f;
        dl.AddTriangleFilled(new Vector2(c.X + d * s, c.Y), new Vector2(c.X - d * s, c.Y - s), new Vector2(c.X - d * s, c.Y + s), col);
    }

    private static void DrawCentred(ImDrawListPtr dl, Vector2 tl, Vector2 br, string text, Vector4 col, float dy)
    {
        var sz = ImGui.CalcTextSize(text);
        dl.AddText(new Vector2((tl.X + br.X - sz.X) / 2f, (tl.Y + br.Y - sz.Y) / 2f + dy), ImGui.GetColorU32(col), text);
    }

    private static void DrawDashedRect(ImDrawListPtr dl, Vector2 a, Vector2 b, uint col)
    {
        const float dash = 8f, gap = 5f;
        for (float x = a.X; x < b.X; x += dash + gap) { float x2 = MathF.Min(x + dash, b.X); dl.AddLine(new Vector2(x, a.Y), new Vector2(x2, a.Y), col, 1.5f); dl.AddLine(new Vector2(x, b.Y), new Vector2(x2, b.Y), col, 1.5f); }
        for (float y = a.Y; y < b.Y; y += dash + gap) { float y2 = MathF.Min(y + dash, b.Y); dl.AddLine(new Vector2(a.X, y), new Vector2(a.X, y2), col, 1.5f); dl.AddLine(new Vector2(b.X, y), new Vector2(b.X, y2), col, 1.5f); }
    }
}
