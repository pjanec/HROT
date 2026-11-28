using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐ <c>CE-4676</c> (DESIGN_LLM_Commander §4, L-1) — the fixed-rule mind the rails run, reading ONLY the feed's JSON forms (what a model's tools would
/// read): at the first snapshot every squad moves to <c>advanceTo</c>; on a SPOT / CONTACT the squad nearest the enemy attacks the reported place and
/// the others support by fire from <c>supportAt</c>; when a squad is out of the fight, the rest break contact to <c>fallBackTo</c>. ⛔ No world.
/// </summary>
public sealed class ScriptedMind : IMind
{
    private readonly string _advanceTo, _supportAt, _fallBackTo;
    private readonly Dictionary<string, Vector2> _squadAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _out = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _squads = new();
    private bool _started, _broken;
    private string? _attacking;
    /// <summary>⭐ CE-4685 — his whole unit's attack is the order KIND Attack: the routing table runs his class's play (a platoon PlatoonAttack, a company
    /// CompanyAttack). ⛔ Never a behaviour name.</summary>
    private const string AttackOrder = "Attack";
    private Vector2 _attackAt;

    public ScriptedMind(string advanceTo = "objective", string supportAt = "support", string fallBackTo = "start")
    {
        _advanceTo = advanceTo; _supportAt = supportAt; _fallBackTo = fallBackTo;
    }

    /// <summary>
    /// ⭐ CE-4680 B-1 (§5b) — on a SPOT run the platoon's own <c>PlatoonAttack</c> play on the reported place (the play's choreography — support
    /// set, then the assault — is the game AI's), instead of ordering each squad itself (L-1's rule, kept for its rail when false).
    /// </summary>
    public bool UsePlays { get; init; }

    /// <summary>A reported enemy this far from the one already attacked is a NEW target (m).</summary>
    public const float NewTargetMetres = 60f;

    public Task<IReadOnlyList<CommanderAction>> ThinkAsync(Briefing briefing, CancellationToken ct)
    {
        var actions = new List<CommanderAction>();
        foreach (var item in briefing.New)
        {
            var j = JsonNode.Parse(item.Json);
            if (j == null) continue;
            switch (item.Kind)
            {
                case FeedKind.Snapshot:
                    ReadSquads(j);
                    if (!_started)
                    {
                        _started = true;
                        foreach (var s in _squads) actions.Add(new CommanderAction(CommanderActionKind.Order, s, "Move", Where: _advanceTo));
                    }
                    break;
                case FeedKind.Report:
                    string? from = (string?)j["from"];
                    if (from != null && At(j) is { } at && (string?)j["report"] is "Sitrep" or "Progress") _squadAt[from] = at;
                    if ((string?)j["report"] is "Spot" or "Contact" && !_broken && At(j) is { } enemy && (string?)j["place"] is { } place)
                        Engage(enemy, place, actions);
                    break;
                case FeedKind.Event:
                    // ⭐ CE-4685 — a split subordinate is NEAR THE ENEMY (X-5): with no SPOT yet, the attack play goes in on the objective. 📌 A company
                    //   seat hears no SPOT of infantry at all — its platoons relay only indirect fire / air defence up (StaffSystems.Relay, CE-2310 HQ-3)
                    if ((string?)j["event"] == "split" && UsePlays && _attacking == null && !_broken)
                    {
                        _attacking = "the unit";
                        _attackAt = new Vector2(float.NaN);   // a later SPOT is a new target
                        actions.Add(new CommanderAction(CommanderActionKind.RunPlay, Order: AttackOrder, Where: _advanceTo));
                    }
                    if ((string?)j["event"] == "combat-ineffective" && (string?)j["squad"] is { } down)
                    {
                        _out.Add(down);
                        if (!_broken)
                        {
                            _broken = true;
                            foreach (var s in _squads)
                                if (!_out.Contains(s)) actions.Add(new CommanderAction(CommanderActionKind.Order, s, "BreakContact", Where: _fallBackTo));
                        }
                    }
                    break;
                case FeedKind.OrderState:
                    if ((string?)j["squad"] == _attacking && (string?)j["state"] is "refused" or "failed") _attacking = null;
                    break;
            }
        }
        return Task.FromResult<IReadOnlyList<CommanderAction>>(actions);
    }

    private void Engage(Vector2 enemy, string place, List<CommanderAction> actions)
    {
        if (_attacking != null && Vector2.Distance(enemy, _attackAt) < NewTargetMetres) return;   // the same enemy: the attack goes on
        if (UsePlays)
        {
            _attacking = "the platoon";
            _attackAt = enemy;
            actions.Add(new CommanderAction(CommanderActionKind.RunPlay, Order: AttackOrder, Where: place));   // ⭐ CE-4685 — the KIND; his class's play runs it
            return;
        }
        string? nearest = null;
        float best = float.MaxValue;
        foreach (var s in _squads)
        {
            if (_out.Contains(s) || !_squadAt.TryGetValue(s, out var p)) continue;
            float d = Vector2.Distance(p, enemy);
            if (d < best) { best = d; nearest = s; }
        }
        if (nearest == null) return;
        _attacking = nearest;
        _attackAt = enemy;
        actions.Add(new CommanderAction(CommanderActionKind.Order, nearest, "Attack", Where: place));
        foreach (var s in _squads)
            if (s != nearest && !_out.Contains(s)) actions.Add(new CommanderAction(CommanderActionKind.Order, s, "SupportByFire", Where: _supportAt));
    }

    private void ReadSquads(JsonNode snapshot)
    {
        _squads.Clear();
        if (snapshot["squads"] is not JsonArray squads) return;
        foreach (var s in squads)
        {
            if ((string?)s?["name"] is not { } name) continue;
            _squads.Add(name);
            if (s is not null && At(s) is { } at) _squadAt[name] = at;
            if ((bool?)s?["inFight"] == false) _out.Add(name);
        }
    }

    private static Vector2? At(JsonNode j)
        => j["at"] is JsonArray a && a.Count >= 2 ? new Vector2((float)a[0]!, (float)a[1]!) : null;
}
