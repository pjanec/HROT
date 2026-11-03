using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// ⭐ CE-4148 (Node Features S2; DESIGN_Player_Join §7a G1–G6, R-4104/R-4113/R-4114) — the gameplay HUD aids of the first-person station as
/// Node Features, each switchable on its own, <see cref="FeatureChange.Live"/>: the hit marker, the damage arc, the kill cam, the squad takeover
/// after death, the spread ring, the near-miss shock. ON by default for the Player (the IG), OFF elsewhere — except the spread ring and the near
/// miss, which are ON everywhere (the S2 row's lean). Declared per host (<c>SharedApplicationBootstrapper.HostFeatures</c>); asked through
/// <see cref="IsOn"/> by the station (<c>PossessionStation</c>) and the hit memory's recorder (<c>EffectSpawnSystem</c>).
/// </summary>
public static class HudFeatures
{
    public const string HitMarker = "hud.hit-marker";
    public const string DamageArc = "hud.damage-arc";
    public const string KillCam = "hud.kill-cam";
    public const string SquadTakeoverAfterDeath = "hud.squad-takeover-after-death";
    public const string SpreadRing = "hud.spread-ring";
    public const string NearMiss = "hud.near-miss";
    /// <summary>⭐ CE-1461 W-M7 (docs/DESIGN_Node_Features.md §10) — SR-106 player messages: the subtitle and the message log.</summary>
    public const string Messages = "hud.messages";

    // ⭐ H1 / H2 (CE-4226..4228; docs/DESIGN_Player_HUD_And_Debrief.md PH-3…PH-5, PH-8 amended) — the player's training HUD and game HUD. Every one is ON by default
    //   (R-4115: gameplay features are on where installed); a scenario's feature preset switches the OTHER profile's elements off. An element with no data draws nothing.
    /// <summary>Training HUD (top-right): the objectives my side may see, required first, with the active one's progress.</summary>
    public const string Objectives = "hud.objectives";
    /// <summary>Training HUD (below the objectives): <i>My standards</i> — the measures about me, value against target.</summary>
    public const string OwnEvaluation = "hud.own-evaluation";
    /// <summary>Game HUD (top bar): each side's tickets.</summary>
    public const string Tickets = "game.tickets";
    /// <summary>Game HUD (top bar): the capture points, and the <i>Capturing B</i> strip while I stand in one that is changing hands.</summary>
    public const string CapturePoints = "game.capture-points";
    /// <summary>Game HUD (top bar): the round time — a countdown to a deadline, else the elapsed time.</summary>
    public const string RoundTimer = "game.round-timer";
    /// <summary>The kill feed (top-right): the last five downings, killer to victim.</summary>
    public const string KillFeed = "hud.kill-feed";
    /// <summary>Game HUD: the score pop-ups and, under them, the kill streak.</summary>
    public const string ScorePopups = "game.score-popups";
    /// <summary>Game HUD: a gold marker over a man carrying a bounty.</summary>
    public const string BountyMarker = "game.bounty-marker";

    /// <summary>Every HUD feature id this class declares.</summary>
    public static readonly string[] All = { HitMarker, DamageArc, KillCam, SquadTakeoverAfterDeath, SpreadRing, NearMiss, Messages,
        Objectives, OwnEvaluation, Tickets, CapturePoints, RoundTimer, KillFeed, ScorePopups, BountyMarker };

    /// <summary>The declarations for a host (the seven aids, then the player HUD's eight). <paramref name="playerDefault"/>: the default of the hit marker, damage arc, kill cam and squad
    /// takeover (true for the Player, false elsewhere); the spread ring, the near miss and the messages are ON on every host.</summary>
    public static IEnumerable<FeatureDeclaration> Declarations(bool playerDefault)
    {
        yield return FeatureDeclaration.Switch(HitMarker, "A cross round the crosshair when you hit someone (red when he is down)", FeatureChange.Live, _ => playerDefault);
        yield return FeatureDeclaration.Switch(DamageArc, "A red arc at the screen edge toward who hit you", FeatureChange.Live, _ => playerDefault);
        yield return FeatureDeclaration.Switch(KillCam, "When the held unit is killed: the fall screen and the 3 s kill cam from the shooter's eye", FeatureChange.Live, _ => playerDefault);
        yield return FeatureDeclaration.Switch(SquadTakeoverAfterDeath, "After death, Tab takes a living squad mate", FeatureChange.Live, _ => playerDefault);
        yield return FeatureDeclaration.Switch(SpreadRing, "The weapon's real spread as a ring round the crosshair", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(NearMiss, "A round passing your ear darkens the screen edges", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(Messages, "Messages to the player: a timed subtitle at the bottom, and a log on N", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(Objectives, "Training HUD: the objectives you may see, with the active one's progress", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(OwnEvaluation, "Training HUD: your own standards - value against target, coloured by how you are doing", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(Tickets, "Game HUD: each side's tickets in the top bar", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(CapturePoints, "Game HUD: the capture points in the top bar and the capturing strip", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(RoundTimer, "Game HUD: the round time in the top bar", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(KillFeed, "The kill feed: the last five downings, killer to victim", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(ScorePopups, "Game HUD: score pop-ups and the kill streak under them", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(BountyMarker, "Game HUD: a gold marker over a man with a bounty on him", FeatureChange.Live, _ => true);
    }

    /// <summary>
    /// A host that builds NO node of its own (the Replay Browser) builds its gate here: the HUD declarations with <paramref name="playerDefault"/>, the
    /// <c>--feature</c> overrides of this process applied (as <see cref="Map3dInLoadFeature.OnAtBoot"/> does). Live features are not recorded, so a replay uses THIS host's settings.
    /// </summary>
    public static NodeFeatures BootGate(bool playerDefault, NodeRole role = NodeRole.Map2D)
    {
        var declarations = Declarations(playerDefault).ToArray();
        NodeFeatureOverrides.NoteDeclared(declarations.Select(d => d.Id));
        var gate = new NodeFeatures(new FeatureRegistry(declarations), role);
        foreach (var (id, json) in NodeFeatureOverrides.FromCommandLine)
            if (gate.Registry.Available(id)) gate.SetNodeOverride(id, json);
        return gate;
    }

    /// <summary>
    /// Is the HUD feature <paramref name="id"/> on? ⭐ <paramref name="gate"/> null ⇒ <b>ON</b> (a rail or tool with no node: today's ungated
    /// behaviour — a host that HAS a gate must pass it, the forwarding rail pins that). A gate whose registry does not declare the id ⇒ OFF
    /// (the host did not compose the feature).
    /// </summary>
    public static bool IsOn(IFeatureGate? gate, string id)
    {
        if (gate is null) return true;
        if (gate is NodeFeatures f) return f.Registry.Available(id) && f.On(id);
        try { return gate.On(gate.Resolve(id)); } catch (Exception) { return false; }
    }

    /// <summary>
    /// ⭐ The hit memory (<c>HitFeedback</c>) is kept while ANY of its readers is on — the hit marker, the damage arc, the kill cam (who killed me).
    /// All three off ⇒ nothing is recorded.
    /// </summary>
    public static bool RecordsHitFeedback(IFeatureGate? gate)
        => IsOn(gate, HitMarker) || IsOn(gate, DamageArc) || IsOn(gate, KillCam);
}
