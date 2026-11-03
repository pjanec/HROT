using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.UI.Common.Map3D;

namespace Hrot.UI.Common.Rules;

/// <summary>One message a trigger sent: when, to whom (in words) and what.</summary>
public sealed record MessageSent(double At, string To, string Text, int MessageId);

/// <summary>
/// ⭐⭐ CE-4241 (SR-106; docs/DESIGN_Rules_View_UI.md §7.10) — <b>what went to whom</b>, pure. Measured: a <c>Message</c> step publishes a <see cref="Fdp.Toolkit.ScenarioRules.PlayerMessageEvent"/> (<c>ToKind</c> All / Side / Station / Unit / Instructor,
/// <c>ToValue</c>, <c>SentAt</c>, <c>Source</c> = the sending trigger's network id) on the local bus and over the <c>PlayerMessage</c> topic, and every node keeps it in <see cref="PlayerMessages"/> (presentation memory, written by the effect system
/// while <c>hud.messages</c> is on — on by default). So the rules view reads the SAME store the player's log reads, keyed by <c>Source</c> — no new record, no wire change.
/// ⚠ It is a node's memory, not a recording: a messages sent before this node started (or a recording opened cold) shows only the authored address, <i>not sent yet</i> being then a statement about THIS memory.
/// </summary>
public static class MessageDelivery
{
    public const string MessageAct = "Message";

    /// <summary>Who an address names, in words: <c>(All, 0)</c> → everyone · <c>(Side, 1)</c> → the Blue side · <c>(Unit, 12)</c> → unit "Alpha 1-1" (#12, Blue) · <c>(Station, 3)</c> → the player at station 3 · <c>Instructor</c> → only the instructor.</summary>
    public static string Recipients(EntityRepository? world, string? toKind, int toValue)
    {
        switch ((toKind ?? "All").Trim().ToLowerInvariant())
        {
            case "" or "all": return "everyone (every side, unit and player)";
            case "side": return $"the {RulesModel.SideName((ForceId)toValue)} side (its units and their players)";
            case "station": return $"the player at station {toValue}";
            case "instructor": return "the instructor only (no player sees it)";
            case "unit":
                if (world != null && toValue != 0)
                {
                    var e = NetworkIdResolver.FindEntityByNetworkId(world, toValue);
                    if (!e.IsNull && world.IsAlive(e) && world.HasComponent<EntityInfo>(e))
                    {
                        var info = world.GetComponentRO<EntityInfo>(e);
                        string name = info.Name.ToString();
                        return $"unit {(name.Length > 0 ? $"\"{name}\" " : "")}(#{toValue}, {RulesModel.SideName(info.ForceId)}) and its player";
                    }
                }
                return $"unit #{toValue} and its player";
            default: return $"{toKind} {toValue}";
        }
    }

    /// <summary>Every message the trigger with network id <paramref name="sourceNetId"/> sent, oldest first, as this node remembers them.</summary>
    public static IReadOnlyList<MessageSent> SentBy(EntityRepository? world, int sourceNetId)
    {
        if (world == null || sourceNetId == 0 || PlayerMessages.Of(world) is not { } store) return Array.Empty<MessageSent>();
        return store.All.Where(m => m.Source == sourceNetId).OrderBy(m => m.SentAt).ThenBy(m => m.MessageId)
                   .Select(m => new MessageSent(m.SentAt, Recipients(world, m.ToKind, m.ToValue), m.Text, m.MessageId)).ToList();
    }

    /// <summary>The trigger's deliveries as lines (<c>0:05  to everyone (…): "Seize the crossroads…"</c>), for the Overview and an agent's probe.</summary>
    public static IReadOnlyList<string> LinesOf(EntityRepository? world, int sourceNetId)
        => SentBy(world, sourceNetId).Select(m => $"{RulesModel.Clock(m.At)}  to {m.To}: \"{m.Text}\"").ToList();

    /// <summary>The note under a <c>Message</c> step of the Do tab (null for another action): its authored address, and how often / when it was sent (matched to the delivered text — a <c>{subject}</c> in it matches the name).</summary>
    public static string? StepNote(EntityRepository? world, int sourceNetId, ActionStep step)
    {
        if (!string.Equals(step.Act, MessageAct, StringComparison.OrdinalIgnoreCase) && !step.Act.EndsWith("." + MessageAct, StringComparison.OrdinalIgnoreCase)) return null;
        string kind = Str(step.Args, "toKind") ?? "All"; int value = Int(step.Args, "toValue");
        string text = Str(step.Args, "text") ?? "";
        var sent = SentBy(world, sourceNetId).Where(m => Matches(text, m.Text)).ToList();
        string to = $"to {Recipients(world, kind, value)}";
        return sent.Count == 0 ? $"{to} - not sent yet" : $"{to} - sent {sent.Count}x, at {string.Join(", ", sent.TakeLast(3).Select(m => RulesModel.Clock(m.At)))}{(sent.Count > 3 ? " (last 3)" : "")}";
    }

    internal static bool Matches(string authored, string delivered)
    {
        int i = authored.IndexOf("{subject}", StringComparison.Ordinal);
        if (i < 0) return authored == delivered;
        string pre = authored[..i], post = authored[(i + 9)..];
        return delivered.Length >= pre.Length + post.Length && delivered.StartsWith(pre, StringComparison.Ordinal) && delivered.EndsWith(post, StringComparison.Ordinal);
    }

    private static string? Str(JsonObject? o, string key)
        => o?.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonObject? o, string key)
        => o?.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
}
