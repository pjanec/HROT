using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Two windows must not claim one id, and two windows on one perspective must not claim one
/// title.</b>
///
/// <para>🔴 <b>What the user's first visual check found</b>, verbatim: <i>"for Blueprint the variables
/// window <b>still show the old version</b> with Parameters section and Working State section."</i></para>
///
/// <para>📐 <b>Measured, and it was worse than a naming ambiguity.</b> The Track C table registered as
/// <c>ai_variables_{perspective}</c>, which on Blueprint is <c>ai_variables_blueprint</c> — the id
/// <c>BlueprintVariablesManagedWindow</c> has claimed since AIE-048.
/// <c>WindowManager.RegisterWindow</c> is <c>_windows[id] = window</c>, and the legacy window is
/// registered LATER (as an "extra"), so the new table was <b>silently evicted from the registry</b>:
/// absent from the Window menu, from the dock, from every lookup, with nothing logged.
/// ⛔ The designer was not choosing the wrong window — <b>the new one was not there.</b></para>
///
/// <para>⭐⭐ <b>Why no existing rail caught it.</b> Batch 79/80's rails assert on
/// <c>registrar.RegisteredWindows</c> — the registrar's own list, which the eviction never touches.
/// ⇒ the seventh instance of <i>ask the ARTEFACT, not the thing that produced it</i>. These rails ask
/// the <see cref="WindowManager"/>, after the real <see cref="EditorSubsystem"/> has run.</para>
///
/// <para>⚠⚠ <b>Measured while probing: the TITLE rail alone would not have caught this.</b> An evicted
/// window contributes no title, so <see cref="NoTwoWindowsOnOnePerspective_ShareATitle"/> stayed green
/// under the revert probe while the other four went red. ⛔ <b>A duplicate title and a duplicate id are
/// different defects</b> — the first is confusing, the second is invisible. Both rails are here because
/// neither implies the other.</para>
///
/// <para>⭐ <b>The rename, per the user's ruling</b> (<i>"If many different windows and title
/// 'Variables', rename them to unique names pls."</i>): each is named by its ROLE —
/// <b>Variable Values</b> (the Track C table, Name/Type/Value), <b>Blueprint Variables</b> (the legacy
/// declaration editor), <b>Blackboard Variables</b> (the authoring/bin-packing surface).
/// ⛔ Not a removal: the user ruled the surfaces coexist until <c>Architect_Question_38</c>.</para>
/// </summary>
public sealed class WindowIdentityIsDistinctTests
{
    private static WindowManager MakeWindowManager()
        => new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));

    /// <summary>
    /// 🔴🔴 <b>RED before Batch 81.</b> Both variables surfaces must survive registration on the
    /// Blueprint perspective — ⛔ before the id split, looking up the Track C id found nothing at all.
    /// </summary>
    [Fact]
    public void TheSurvivingVariablesTable_KeepsItsOwnId_AfterTheLegacyRetirement()
    {
        var wm = MakeWindowManager();
        new EditorSubsystem().RegisterWindows(wm);

        Assert.True(wm.TryGetWindow("ai_variable_values_blueprint", out var trackC),
            "Expected the Track C variables table to be registered on the Blueprint perspective. " +
            "Before Batch 81 it shared an id with BlueprintVariablesManagedWindow and was evicted.");

        // ⛔⛔ L5 — the LEGACY half is RETIRED (Q38's list), so this rail can no longer be about
        //    "both survive". ⭐ What it is about NOW is the half that still matters: the surviving
        //    table keeps its OWN id and title, i.e. the Batch 81 eviction cannot come back by the
        //    retirement handing its id to someone else.
        // ⚠ The id it used to collide with must now resolve to NOTHING — that is the assertion that
        //   makes the retirement real, and it is stronger than the one it replaces.
        Assert.False(wm.TryGetWindow("ai_variables_blueprint", out _),
            "The legacy Blueprint variables window is retired (L5); its id must resolve to nothing.");

        Assert.Equal("Variable Values", trackC!.Title);
    }

    /// <summary>
    /// ⭐ The same table on the two AI perspectives — ⛔ the id moved for all three, not just the one
    /// that collided, so a per-perspective special case cannot creep back in.
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    public void TheVariablesTable_IsRegistered_OnEachAiPerspective(string suffix)
    {
        var wm = MakeWindowManager();
        new EditorSubsystem().RegisterWindows(wm);

        Assert.True(wm.TryGetWindow($"ai_variable_values_{suffix}", out var win),
            $"Expected 'ai_variable_values_{suffix}' to be registered.");
        Assert.Equal("Variable Values", win!.Title);
    }

    /// <summary>
    /// ⭐⭐ <b>The sweep, as a property.</b> Every window the composition root registers on a
    /// perspective must have a title unique within that perspective — ⛔ two identical entries in one
    /// Window-menu group give the designer no way to ask for the one they want.
    ///
    /// <para>⚠ Driven off the ids the composition root actually uses, because
    /// <see cref="WindowManager"/> exposes no enumeration of everything registered.</para>
    /// </summary>
    [Fact]
    public void NoTwoWindowsOnOnePerspective_ShareATitle()
    {
        var wm = MakeWindowManager();
        new EditorSubsystem().RegisterWindows(wm);

        foreach (var group in KnownWindowIds.GroupBy(id => id.Perspective))
        {
            var titles = new List<string>();
            foreach (var (_, id) in group)
            {
                if (wm.TryGetWindow(id, out var win)) titles.Add(win!.Title);
            }

            var duplicates = titles.GroupBy(t => t, StringComparer.Ordinal)
                                   .Where(g => g.Count() > 1)
                                   .Select(g => g.Key)
                                   .ToArray();

            Assert.True(duplicates.Length == 0,
                $"Perspective '{group.Key}' has duplicate window titles: {string.Join(", ", duplicates)}");
        }
    }

    /// <summary>
    /// ⭐ And the ids themselves: a duplicate would not show up as two entries, it would show up as
    /// ONE — ⛔ which is exactly what made this invisible. Every id below must resolve to a window,
    /// and no two ids may resolve to the same instance.
    /// </summary>
    [Fact]
    public void EveryKnownWindowId_ResolvesToADistinctWindow()
    {
        var wm = MakeWindowManager();
        new EditorSubsystem().RegisterWindows(wm);

        var seen = new Dictionary<ManagedWindow, string>();
        foreach (var (_, id) in KnownWindowIds)
        {
            Assert.True(wm.TryGetWindow(id, out var win), $"Window id '{id}' is not registered.");
            Assert.False(seen.TryGetValue(win!, out var other),
                $"Ids '{other}' and '{id}' resolve to the same window instance — one has evicted the other.");
            seen[win!] = id;
        }
    }

    /// <summary>
    /// ⭐ Every window the composition root registers on one of the three authoring perspectives.
    /// ⚠ Maintained by hand because there is no enumeration surface; a new window added without a row
    /// here is simply not covered — ⛔ it is not silently claimed to be covered.
    /// </summary>
    private static readonly (string Perspective, string Id)[] KnownWindowIds =
    [
        // ── the per-perspective core set (PerspectiveWorkspaceRegistrar.RegisterWindows) ──
        ("BTree",     "ai_runtime_inspector_btree"),
        ("BTree",     "ai_trace_timeline_btree"),
        ("BTree",     "ai_find_results_btree"),
        ("BTree",     "ai_blackboard_variables_btree"),
        ("BTree",     "ai_diagnostics_btree"),
        ("BTree",     "ai_variable_values_btree"),
        ("BTree",     "ai_my_blueprint_btree"),
        ("BTree",     "ai_canvas_btree"),
        ("HSM",       "ai_runtime_inspector_hsm"),
        ("HSM",       "ai_trace_timeline_hsm"),
        ("HSM",       "ai_find_results_hsm"),
        ("HSM",       "ai_blackboard_variables_hsm"),
        ("HSM",       "ai_diagnostics_hsm"),
        ("HSM",       "ai_variable_values_hsm"),
        ("HSM",       "ai_my_blueprint_hsm"),
        ("HSM",       "ai_canvas_hsm"),
        ("Blueprint", "ai_runtime_inspector_blueprint"),
        ("Blueprint", "ai_trace_timeline_blueprint"),
        ("Blueprint", "ai_find_results_blueprint"),
        ("Blueprint", "ai_blackboard_variables_blueprint"),
        ("Blueprint", "ai_diagnostics_blueprint"),
        ("Blueprint", "ai_variable_values_blueprint"),
        ("Blueprint", "ai_canvas_blueprint"),

        // ── the Blueprint extras (EditorSubsystem.RegisterExtraWindow) ──
        ("Blueprint", "ai_my_blueprint_blueprint"),
        ("Blueprint", "ai_bookmarks_blueprint"),
        ("Blueprint", "ai_details_blueprint"),
        // ⛔ L5 — ("Blueprint", "ai_variables_blueprint") REMOVED: BlueprintVariablesManagedWindow is
        //    retired (Q38's list). ⚠ Its absence is asserted directly in
        //    TheSurvivingVariablesTable_KeepsItsOwnId_AfterTheLegacyRetirement — ⛔ so dropping the row
        //    here does not lose the coverage, it moves it to a rail that says the RIGHT thing.
        ("Blueprint", "ai_graph_signature_blueprint"),
    ];
}
