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
    private static BlackboardVariableEntry Var(string name, bool auto = false)
        => new(name, typeof(int), Comment: null, IsAutoManaged: auto, DefaultValueJson: null);

    private static BlackboardMyBlueprintModel Model(
        BlackboardHostKind host, params BlackboardVariableEntry[] vars)
        => new(host, () => vars);

    /// <summary>
    /// ⭐ <b>Each host yields ONE section, "Variables"</b> (<c>CE-3193</c>, design §5c B3a): a variable has no authored kind,
    /// so nothing splits them. ⛔ HISTORY: three sections — Variables / Working State / Asset Globals — the Role × Scope product.
    /// </summary>
    [Theory]
    [InlineData(BlackboardHostKind.BTree, "btree")]
    [InlineData(BlackboardHostKind.Hsm,   "hsm")]
    public void EachHost_YieldsOneVariablesSection(BlackboardHostKind host, string prefix)
    {
        var section = Assert.Single(Model(host).Sections);

        Assert.Equal("Variables", section.DisplayName);
        Assert.Equal(BlackboardMyBlueprintModel.SectionInputs, section.Id);
        // ⚠ Host-qualified, so two open perspectives cannot pick up each other's commands.
        Assert.StartsWith($"{prefix}.blackboard.create", section.CreateCommandId);
        Assert.True(section.CanCreateItems);
    }

    /// <summary>⚠ <b>EMPTY rather than ABSENT</b> — an asset with no variables still lists the section.</summary>
    [Fact]
    public void AnAssetWithNoVariables_StillListsTheSection()
    {
        var model = Model(BlackboardHostKind.Hsm);

        var section = Assert.Single(model.Sections);
        Assert.Empty(model.GetItems(section.Id));
    }

    /// <summary>⭐ Every variable lands in the one section, in declaration order.</summary>
    [Fact]
    public void EveryVariable_LandsInTheOneSection()
    {
        var model = Model(BlackboardHostKind.Hsm, Var("speed"), Var("cursor"), Var("shared"));

        Assert.Equal(new[] { "speed", "cursor", "shared" },
                     model.GetItems(BlackboardMyBlueprintModel.SectionInputs).Select(i => i.DisplayName));
    }

    /// <summary>
    /// ⛔⛔ <b>No kind anywhere</b> — <c>CE-3193</c> removed <c>Role</c>/<c>Scope</c> from the entry, so no section or command
    /// can offer one.
    /// </summary>
    [Fact]
    public void NoVariableKind_Survives()
    {
        Assert.Null(typeof(BlackboardVariableEntry).GetProperty("Role"));
        Assert.Null(typeof(BlackboardVariableEntry).GetProperty("Scope"));

        foreach (var host in Enum.GetValues<BlackboardHostKind>())
        foreach (var section in Model(host).Sections)
        {
            Assert.DoesNotContain("Role",  section.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Scope", section.DisplayName, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// ⚠ <b>An editor-owned variable is neither renamable nor deletable</b> — §5's row-kind rule,
    /// applied to the outline rather than to the table.
    /// </summary>
    [Fact]
    public void AnAutoManagedVariable_IsHostDefinedAndNotEditable()
    {
        var model = Model(BlackboardHostKind.BTree, Var("owned", auto: true));

        var item = Assert.Single(model.GetItems(BlackboardMyBlueprintModel.SectionInputs));
        Assert.True (item.IsHostDefined);
        Assert.False(item.IsRenamable);
        Assert.False(item.IsDeletable);
    }
}
