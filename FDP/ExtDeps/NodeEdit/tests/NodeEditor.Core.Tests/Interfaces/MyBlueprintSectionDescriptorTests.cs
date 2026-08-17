using NodeEditor.Core.Interfaces;
using Xunit;

namespace NodeEditor.Core.Tests.Interfaces;

/// <summary>
/// ⭐⭐ <b>A section's "+" can say WHY it is unusable.</b>
///
/// <para>📌 <b>User ruling, <c>2026-08-17</c>, verbatim:</b> <i>"Disabling/graying a [+] on variable
/// section but showing explanatory tooltip (same as the info window now) would be better than allowing
/// user to click the button and then saying that it is not possible — same information value, no false
/// expectations."</i></para>
///
/// <para>⭐ <b>A REFINEMENT of <c>Q26-B2</c>, not a reversal.</b> That ruling forbids the "+"
/// <b>vanishing</b>; greying is not vanishing. The button stays and the reason is still taught —
/// ⭐ only now before the work rather than after it.</para>
/// </summary>
public sealed class MyBlueprintSectionDescriptorTests
{
    /// <summary>
    /// ⭐⭐ <b>Defaulted, so every existing construction site is unchanged.</b> ⛔ The field had to
    /// arrive without touching the five files that build descriptors, or the change would have been a
    /// rename disguised as a capability.
    /// </summary>
    [Fact]
    public void ASectionWithoutAReason_IsCreatableAsBefore()
    {
        var section = new MyBlueprintSectionDescriptor(
            "vars", "Variables", 0, null, true, true, "editor.create-variable");

        Assert.Null(section.CreateDisabledReason);
    }

    /// <summary>
    /// ⭐ <b>The reason is independent of <c>CanCreateItems</c>.</b> ⛔ Collapsing them would make the
    /// "+" vanish, which is the one thing <c>Q26-B2</c> forbids — the section still declares that it
    /// CAN create; it just cannot right now, and says so.
    /// </summary>
    [Fact]
    public void ADisabledReason_DoesNotRemoveTheSectionsAbilityToCreate()
    {
        var section = new MyBlueprintSectionDescriptor(
            "locals", "Local Variables", 5, null, true, true, "editor.create-local-variable",
            CreateDisabledReason: "'Blend' is a macro — macros cannot own local variables.");

        Assert.True(section.CanCreateItems);
        Assert.NotNull(section.CreateCommandId);
        Assert.Equal("'Blend' is a macro — macros cannot own local variables.",
                     section.CreateDisabledReason);
    }

    /// <summary>
    /// ⭐ <c>with</c> is how a model projects the reason per read without rebuilding its section list
    /// — ⛔ the identity and order must survive, because the section order is asserted
    /// position-by-position downstream.
    /// </summary>
    [Fact]
    public void ProjectingAReason_KeepsEveryOtherFieldIdentical()
    {
        var template = new MyBlueprintSectionDescriptor(
            "locals", "Local Variables", 5, "icon", true, false, "cmd");

        var projected = template with { CreateDisabledReason = "no graph is open" };

        Assert.Equal(template.Id,                projected.Id);
        Assert.Equal(template.DisplayName,       projected.DisplayName);
        Assert.Equal(template.SortOrder,         projected.SortOrder);
        Assert.Equal(template.IconKey,           projected.IconKey);
        Assert.Equal(template.CanCreateItems,    projected.CanCreateItems);
        Assert.Equal(template.CanHaveCategories, projected.CanHaveCategories);
        Assert.Equal(template.CreateCommandId,   projected.CreateCommandId);
        Assert.Null(template.CreateDisabledReason);
    }
}
