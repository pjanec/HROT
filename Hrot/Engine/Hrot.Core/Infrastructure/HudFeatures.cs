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

    /// <summary>Every HUD feature id this class declares.</summary>
    public static readonly string[] All = { HitMarker, DamageArc, KillCam, SquadTakeoverAfterDeath, SpreadRing, NearMiss };

    /// <summary>The six declarations for a host. <paramref name="playerDefault"/>: the default of the hit marker, damage arc, kill cam and squad
    /// takeover (true for the Player, false elsewhere); the spread ring and the near miss are ON on every host.</summary>
    public static IEnumerable<FeatureDeclaration> Declarations(bool playerDefault)
    {
        yield return FeatureDeclaration.Switch(HitMarker, "A cross round the crosshair when you hit someone (red when he is down)", FeatureChange.Live, _ => playerDefault);
        yield return FeatureDeclaration.Switch(DamageArc, "A red arc at the screen edge toward who hit you", FeatureChange.Live, _ => playerDefault);
        yield return FeatureDeclaration.Switch(KillCam, "When the held unit is killed: the fall screen and the 3 s kill cam from the shooter's eye", FeatureChange.Live, _ => playerDefault);
        yield return FeatureDeclaration.Switch(SquadTakeoverAfterDeath, "After death, Tab takes a living squad mate", FeatureChange.Live, _ => playerDefault);
        yield return FeatureDeclaration.Switch(SpreadRing, "The weapon's real spread as a ring round the crosshair", FeatureChange.Live, _ => true);
        yield return FeatureDeclaration.Switch(NearMiss, "A round passing your ear darkens the screen edges", FeatureChange.Live, _ => true);
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
