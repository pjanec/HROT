using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>C-outline</c> — BTree and HSM supply their own My Blueprint outline.</b>
///
/// <para>
/// 📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §1c: <b>sections ARE the classification</b>, and
/// they are the <c>Role × Scope</c> product made visible. ⛔ <b>Still no <c>Role</c>/<c>Scope</c>
/// control on any host</b> — asserted below, because both members exist on the carrier and their
/// absence has to read as a decision.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Not verifiable, visual check suspended:</b> the outline DRAWING, the section headers' order
/// on screen, and the per-section "+" affordance.
/// </para>
/// </summary>
public sealed class BlackboardOutlineTests
{
    private static BlackboardVariableEntry Var(
        string name, BlackboardVariableRole role, WorkingStateScope scope, bool auto = false)
        => new(name, typeof(int), Comment: null, IsAutoManaged: auto,
               DefaultValueJson: null, Role: role, Scope: scope);

    private static BlackboardMyBlueprintModel Model(
        BlackboardHostKind host, params BlackboardVariableEntry[] vars)
        => new(host, () => vars);

    /// <summary>
    /// ⭐ <b>Each host yields its section list in <c>SortOrder</c></b>, and the two lists are the same
    /// SHAPE — the hosts differ in their create-command ids, not in what a section means.
    /// </summary>
    [Theory]
    [InlineData(BlackboardHostKind.BTree, "btree")]
    [InlineData(BlackboardHostKind.Hsm,   "hsm")]
    public void EachHost_YieldsItsSectionsInSortOrder(BlackboardHostKind host, string prefix)
    {
        var sections = Model(host).Sections;

        Assert.Equal(new[] { "Inputs", "Working State", "Asset Globals" },
                     sections.OrderBy(s => s.SortOrder).Select(s => s.DisplayName));

        // ⚠ Host-qualified, so two open perspectives cannot pick up each other's commands.
        Assert.All(sections, s => Assert.StartsWith($"{prefix}.blackboard.create", s.CreateCommandId));
        Assert.All(sections, s => Assert.True(s.CanCreateItems));
    }

    /// <summary>
    /// ⚠ <b>EMPTY rather than ABSENT</b> — <c>SectionLocalVariables</c>' rule: <i>"a section that
    /// appears and disappears reads as a broken feature."</i> ⇒ an asset with no variables at all still
    /// lists all three.
    /// </summary>
    [Fact]
    public void AnAssetWithNoVariables_StillListsEverySection()
    {
        var model = Model(BlackboardHostKind.Hsm);

        Assert.Equal(3, model.Sections.Count);
        Assert.All(model.Sections, s => Assert.Empty(model.GetItems(s.Id)));
    }

    /// <summary>
    /// ⭐⭐ <b>The <c>Role × Scope</c> routing, §1c's table exactly.</b> Inputs is <c>Role.Input</c>;
    /// Working State is <c>Role.State</c> at <c>Node</c> scope; Asset Globals is <c>Role.State</c>
    /// shared wider.
    /// </summary>
    [Theory]
    [InlineData(BlackboardVariableRole.Input, WorkingStateScope.Node,     BlackboardMyBlueprintModel.SectionInputs)]
    [InlineData(BlackboardVariableRole.Input, WorkingStateScope.Behavior, BlackboardMyBlueprintModel.SectionInputs)]
    [InlineData(BlackboardVariableRole.State, WorkingStateScope.Node,     BlackboardMyBlueprintModel.SectionWorkingState)]
    [InlineData(BlackboardVariableRole.State, WorkingStateScope.Behavior, BlackboardMyBlueprintModel.SectionAssetGlobals)]
    [InlineData(BlackboardVariableRole.State, WorkingStateScope.Entity,   BlackboardMyBlueprintModel.SectionAssetGlobals)]
    public void EachVariable_LandsInItsSection(
        BlackboardVariableRole role, WorkingStateScope scope, string expectedSection)
    {
        var v     = Var("v", role, scope);
        var model = Model(BlackboardHostKind.BTree, v);

        Assert.Equal(expectedSection, BlackboardMyBlueprintModel.SectionOf(v));
        Assert.Equal("v", Assert.Single(model.GetItems(expectedSection)).DisplayName);

        // ⛔ And nowhere else.
        foreach (var other in model.Sections.Where(s => s.Id != expectedSection))
            Assert.Empty(model.GetItems(other.Id));
    }

    /// <summary>⭐ A populated asset splits across sections rather than piling into one.</summary>
    [Fact]
    public void APopulatedAsset_SplitsAcrossSections()
    {
        var model = Model(BlackboardHostKind.Hsm,
            Var("speed",  BlackboardVariableRole.Input, WorkingStateScope.Node),
            Var("cursor", BlackboardVariableRole.State, WorkingStateScope.Node),
            Var("shared", BlackboardVariableRole.State, WorkingStateScope.Behavior),
            Var("wide",   BlackboardVariableRole.State, WorkingStateScope.Entity));

        Assert.Equal(new[] { "speed"  }, model.GetItems(BlackboardMyBlueprintModel.SectionInputs).Select(i => i.DisplayName));
        Assert.Equal(new[] { "cursor" }, model.GetItems(BlackboardMyBlueprintModel.SectionWorkingState).Select(i => i.DisplayName));
        Assert.Equal(new[] { "shared", "wide" },
                     model.GetItems(BlackboardMyBlueprintModel.SectionAssetGlobals).Select(i => i.DisplayName));
    }

    /// <summary>
    /// ⛔⛔ <b>No <c>Role</c>/<c>Scope</c> control anywhere in the outline</b> (§1c) — the section IS
    /// the classification. ⭐ Asserted because <c>BlackboardVariableEntry</c> carries both members, so
    /// their absence must be a decision rather than an oversight.
    /// </summary>
    [Fact]
    public void NoSection_OffersARoleOrScopeControl()
    {
        Assert.NotNull(typeof(BlackboardVariableEntry).GetProperty("Role"));
        Assert.NotNull(typeof(BlackboardVariableEntry).GetProperty("Scope"));

        foreach (var host in Enum.GetValues<BlackboardHostKind>())
        foreach (var section in Model(host).Sections)
        {
            Assert.DoesNotContain("Role",  section.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Scope", section.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("role",  section.CreateCommandId!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("scope", section.CreateCommandId!, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// ⚠ <b>An editor-owned variable is neither renamable nor deletable</b> — §5's row-kind rule,
    /// applied to the outline rather than to the table.
    /// </summary>
    [Fact]
    public void AnAutoManagedVariable_IsHostDefinedAndNotEditable()
    {
        var model = Model(BlackboardHostKind.BTree,
            Var("owned", BlackboardVariableRole.State, WorkingStateScope.Node, auto: true));

        var item = Assert.Single(model.GetItems(BlackboardMyBlueprintModel.SectionWorkingState));
        Assert.True (item.IsHostDefined);
        Assert.False(item.IsRenamable);
        Assert.False(item.IsDeletable);
    }
}
