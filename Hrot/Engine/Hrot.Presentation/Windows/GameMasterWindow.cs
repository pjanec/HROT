using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Editing;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Fdp.Toolkit.Weather;
using Hrot.Presentation.Behavior;
using Hrot.UI.Common.Rules;
using ImGuiNET;

namespace Hrot.Presentation.Windows;

/// <summary>What the <see cref="GameMasterWindow"/> shows — the rails and an agent read it.</summary>
public sealed record GameMasterProbe(string? Step, string? Blocked, bool Enabled, IReadOnlyList<string> Skill, IReadOnlyList<string> Made, string? Message);

/// <summary>The panel snapshot of the game master's palette.</summary>
public sealed record GameMasterViewModel(string PanelId, string PanelKind, GameMasterProbe Shown) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ CE-4243 (SR-104 Z2 · Z3 · Z4; docs/DESIGN_Rules_View_UI.md §8, GM-1 … GM-5) — <b>Window ▸ Game master</b>: <i>run any shared action by hand</i> (the Do tab's own one-step drawer: the <c>ActionCatalogue</c> picker + the args form, pre-filled from the selection, GM-2)
/// and a <i>skill row per side</i> (−2 … +2 tiers, the current <see cref="SkillAmplifier"/> shown, GM-3). Either is a <see cref="GmOneShot"/> — a transient one-shot trigger made through the host's ONE creation path (<c>createOneShot</c>), so nothing here is a command: the
/// rule fires through the normal runner, journals itself and appears in the rules view under <i>Game master (live)</i> (GM-4). Disabled with its reason in edit mode. The MSEL is the rules view's <i>Coming up</i> (GM-5).
/// </summary>
public sealed class GameMasterWindow : ManagedWindow
{
    public const string Kind = "game-master";
    public const int MaxTiers = SkillAmplifier.MaxTiers;

    private readonly Func<EntityRepository?> _world;
    private readonly Func<string, bool> _createOneShot;
    private readonly Func<(int NetworkId, ForceId Side)?> _selection;
    private readonly Func<string?> _blocked;
    private readonly Func<bool> _enabled;
    private readonly ActionListContext _ctx = new();
    private readonly ActionListFieldDrawer? _drawer;
    private object _list = new List<ActionStep>();
    private readonly List<string> _made = new();
    private GameMasterProbe _probe = new(null, null, true, Array.Empty<string>(), Array.Empty<string>(), null);

    /// <param name="createOneShot">The host's creation path: asks for a TRANSIENT trigger entity with these params (<c>RuleAdd.TkbOf("Trigger")</c>, <c>isTransient</c>); false when it cannot.</param>
    /// <param name="selection">The primary selected entity's network id and side, or null (GM-2).</param>
    /// <param name="blocked">Why nothing can be run now (a recording), or null.</param>
    /// <param name="enabled">The node feature <c>gm.palette</c>.</param>
    public GameMasterWindow(Func<EntityRepository?> world, string id, string perspective, Vector4 titleBarColor, RuleEditorServices services, Func<string, bool> createOneShot,
        Func<(int NetworkId, ForceId Side)?>? selection = null, Func<string?>? blocked = null, Func<bool>? enabled = null)
        : base(id, "Game master", perspective, WindowScope.PerspectiveBound)
    {
        _world = world; _createOneShot = createOneShot; _selection = selection ?? (() => null); _blocked = blocked ?? (() => null); _enabled = enabled ?? (() => true);
        if (services.EditService() is { } edit) _drawer = new ActionListFieldDrawer(edit, _ctx);
        _ctx.OpenPicker = services.OpenPicker?.Invoke();
        _ctx.Picks = services.Picks?.Invoke();
        TitleBarColor = titleBarColor;
        IsOpen = false;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>The step in the form (the last one added), or null.</summary>
    public ActionStep? Step => (_list as List<ActionStep>)?.LastOrDefault();
    public string? Message { get; private set; }
    public GameMasterProbe Probe => _probe;
    /// <summary>The titles of the one-shots this window made this run.</summary>
    public IReadOnlyList<string> Made => _made;

    /// <summary>Why nothing can be run now, or null: a recording, the feature off, edit mode (no rule runs yet).</summary>
    public string? Refusal()
    {
        if (!_enabled()) return "the game master's palette is switched off (feature gm.palette)";
        if (_blocked() is { } b) return b;
        var w = _world();
        if (w == null) return "no world";
        if (!w.IsComponentTypeRegistered<ObjectiveStatus>() || !w.Query().With<ObjectiveStatus>().Build().Any()) return GmOneShot.EditModeRefusal;
        return null;
    }

    /// <summary>The current amplifier of <paramref name="side"/> on the World Environment (0 when none).</summary>
    public int SkillOf(ForceId side)
    {
        var w = _world();
        if (w == null || !w.IsComponentTypeRegistered<SkillAmplifier>() || !w.IsComponentTypeRegistered<ScenarioClock>()) return 0;
        foreach (var e in w.Query().With<ScenarioClock>().WithLifecycle(EntityLifecycle.All).Build())
            return w.HasComponent<SkillAmplifier>(e) ? w.GetComponentRO<SkillAmplifier>(e).TiersOf(side) : 0;
        return 0;
    }

    /// <summary>Runs <paramref name="step"/> as a one-shot (GM-1). False, with <see cref="Message"/>, when refused.</summary>
    public bool Run(ActionStep step)
    {
        if (Refusal() is { } why) { Message = why; return false; }
        if (ActionListEdit.EntryOf(step.Act) is { Refusal: { } r }) { Message = r; return false; }
        if (!_createOneShot(GmOneShot.TriggerParamsJson(step))) { Message = "the host could not create the one-shot"; return false; }
        _made.Add(GmOneShot.TitleOf(step.Act));
        Message = $"{GmOneShot.TitleOf(step.Act)}: asked — see Rules ▸ {GmOneShot.GroupName}";
        return true;
    }

    /// <summary>GM-3 — the skill row's click: <c>SetSkillAmplifier { side, tiers }</c> as a one-shot.</summary>
    public bool SetSkill(ForceId side, int tiers) => Run(GmOneShot.SkillStep(side, tiers));

    /// <summary>GM-2 — a freshly added step takes the selection (a <c>target</c> link, a <c>side</c>).</summary>
    internal ActionStep Prefill(ActionStep added)
    {
        if (added.Args is { Count: > 0 } || ActionListEdit.EntryOf(added.Act) is not { } entry || _selection() is not { } sel) return added;
        return new ActionStep { Act = added.Act, Args = GmOneShot.Prefilled(entry, sel.NetworkId, sel.Side), Bind = added.Bind };
    }

    internal void Rebuild()
    {
        string? refusal = Refusal();
        _probe = new GameMasterProbe(Step is { } s ? GmOneShot.Describe(s) : null, refusal, refusal == null,
            new[] { ForceId.Friend, ForceId.Hostile }.Select(f => $"{RulesModel.SideName(f)} {SkillOf(f):+0;-0;0}").ToList(), _made.ToList(), Message);
    }

    internal GameMasterViewModel SimulateDrawClientArea()
    {
        Rebuild();
        var vm = new GameMasterViewModel(Id, Kind, _probe);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    protected override void BeforeBegin()
    {
        ImGui.SetNextWindowSize(new Vector2(560f, 520f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(1f);
    }

    protected override void DrawClientArea()
    {
        SimulateDrawClientArea();
        string? refusal = _probe.Blocked;
        if (refusal != null) ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1f), refusal);

        ImGui.Text("Run an action");
        ImGui.TextDisabled("Pick an action, fill its arguments, Run. It becomes a one-shot rule: see Rules ▸ " + GmOneShot.GroupName + ".");
        if (_drawer == null) ImGui.TextDisabled("this host has no params form");
        else
        {
            var before = (_list as List<ActionStep>)?.Count ?? 0;
            if (_drawer.DrawInput(ref _list, null!) && _list is List<ActionStep> l)
            {
                // ONE step: the last one added wins; a fresh one takes the selection. Reopen the form only when the list CHANGED SHAPE (a new step) — an edit inside a step must keep its focus
                if (l.Count == 0) { _list = l; _drawer.Reset(); goto drawn; }
                bool fresh = l.Count > before;
                var last = fresh ? Prefill(l[^1]) : l[^1];
                _list = l.Count == 1 && !fresh ? l : new List<ActionStep> { last };
                if (fresh) _drawer.Reset();
            }
        }
        drawn:
        if (refusal != null) ImGui.BeginDisabled();
        bool canRun = Step is { Act.Length: > 0 };
        if (!canRun) ImGui.BeginDisabled();
        if (ImGui.Button("Run now") && Step is { } step) Run(step);
        if (!canRun) ImGui.EndDisabled();
        if (refusal != null) ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.Text("Skill per side");
        ImGui.TextDisabled("an amplifier on the side's own level: −2 … +2 tiers (0 restores)");
        foreach (var side in new[] { ForceId.Friend, ForceId.Hostile })
        {
            int cur = SkillOf(side);
            ImGui.TextUnformatted(RulesModel.SideName(side)); ImGui.SameLine(70f);
            for (int t = -MaxTiers; t <= MaxTiers; t++)
            {
                if (t != -MaxTiers) ImGui.SameLine();
                bool on = t == cur;
                if (on) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
                if (refusal != null) ImGui.BeginDisabled();
                if (ImGui.Button($"{t:+0;-0;0}##skill-{side}-{t}") && !on) SetSkill(side, t);
                if (refusal != null) ImGui.EndDisabled();
                if (on) ImGui.PopStyleColor();
            }
        }
        ImGui.Separator();
        if (Message != null) ImGui.TextWrapped(Message);
        if (_made.Count > 0) { ImGui.TextDisabled("made this run"); foreach (var m in _made.TakeLast(8)) ImGui.TextUnformatted(m); }
        ImGui.TextDisabled("The MSEL is the rules view's Coming up and its timeline.");
    }
}
