using System.Linq;
using Fdp.Toolkit.Input;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Map3D;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ U-12 / CE-4280 — the HUD feature texts (the Features window, <c>HudFeatures.Declarations</c>) name the keys the embodied keymap REALLY binds: the scoreboard is the key bound to <c>EmbodiedKeys.Scoreboard</c> (Tab, R-3076), the after-death squad takeover the
/// key bound to <c>EmbodiedKeys.TeamNext</c> (J). The texts used to say K and Tab (the pre-R-3076 keys). 🔴 Red-proof (compiles): a text back to "K" ⇒ red.
/// </summary>
public sealed class HudFeatureTextsNameTheRealKeysTests
{
    private static string Text(string id) => HudFeatures.Declarations(playerDefault: true).Single(d => d.Id == id).Description;

    private static string KeyOf(Keymap map, ActionRef action, KeyBinding[] candidates)
    {
        foreach (var c in candidates)
            if (map.TryFind(InputScope.Hat, EmbodiedKeys.Hat, c, out var e) && e.Action.Equals(action)) return c.Key.ToString();
        return "(not bound)";
    }

    [Fact]
    public void CE4280_TheHudTexts_NameTheKeysTheEmbodiedKeymapBinds()
    {
        var map = new Keymap();
        EmbodiedKeys.Declare(map);
        var all = System.Enum.GetValues<EditorKey>().Select(k => new KeyBinding(k, KeyModifiers.None)).ToArray();
        string scoreboardKey = KeyOf(map, EmbodiedKeys.Scoreboard, all);
        string nextManKey = KeyOf(map, EmbodiedKeys.TeamNext, all);
        Assert.Equal("Tab", scoreboardKey);
        Assert.Equal("J", nextManKey);
        Assert.Contains(scoreboardKey, Text(HudFeatures.Scoreboard));
        Assert.DoesNotContain("K is held", Text(HudFeatures.Scoreboard));
        Assert.Contains(nextManKey, Text(HudFeatures.SquadTakeoverAfterDeath));
        Assert.DoesNotContain("Tab takes", Text(HudFeatures.SquadTakeoverAfterDeath));
    }
}
