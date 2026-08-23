using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L6.5</c>'s rail — the entity predicates, over MEASURED contexts.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L6</c> stage 3; the handoff's gate:
/// <i>"the helper's predicates rail true/false over measured contexts."</i>
///
/// <para>⭐⭐ <b>The contexts come from the production builder</b> *(<c>DetailsContextBuilder.Build</c>,
/// through a real <c>IEntitySelectionSource</c>)* — ⛔ not from a hand-set property. ⚠ A predicate
/// railed against a context nobody builds is a predicate about a shape that does not occur.</para>
/// </summary>
public sealed class TheEntityViewPredicatesTests
{
    /// <summary>⭐ A source that reports exactly the entities a case names — the same seam
    /// <c>WorldEntitySelectionSource</c> implements, so the builder is driven as production drives it.</summary>
    private sealed class Selected : IEntitySelectionSource
    {
        private readonly Entity[] _entities;
        public Selected(params Entity[] entities) => _entities = entities;
        public IReadOnlyList<Entity> Selected_() => _entities;
        IReadOnlyList<Entity> IEntitySelectionSource.Selected() => _entities;
    }

    private static DetailsContext Context(params Entity[] entities)
        => DetailsContextBuilder.Build(
            new EditorSelectionStore(), "Scenario", VariableRunState.Planning, new Selected(entities));

    private static readonly Entity One = new(7, 1);
    private static readonly Entity Two = new(9, 1);

    // ══ ExactlyOneEntity ═════════════════════════════════════════════════════

    /// <summary>⭐ The positive case — one selected entity.</summary>
    [Fact]
    public void OneSelectedEntity_SatisfiesExactlyOneEntity()
        => Assert.True(DetailsViewPredicates.ExactlyOneEntity(Context(One)));

    /// <summary>
    /// ⛔⛔ <b>NONE and TWO are BOTH false, and the second is the one that matters.</b>
    /// ⚠ 📌 <c>R-118</c>: two entities is not "the first one" — both entity views present a SINGLE
    /// entity's data, so offering on a multi-selection would show one and silently ignore the rest.
    /// ⭐ The honest outcome is no offer ⇒ <c>R-117</c>'s grey line.
    /// </summary>
    [Fact]
    public void NoEntityAndTwoEntities_BothFail()
    {
        Assert.False(DetailsViewPredicates.ExactlyOneEntity(Context()));
        Assert.False(DetailsViewPredicates.ExactlyOneEntity(Context(One, Two)));
    }

    // ══ OneEntityWithBrain ═══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The brain signal gates the Mission view, and it composes with the entity half.</b>
    /// ⚠ As-built (c): there is no <c>HasBrain</c> in this codebase — the signal is the host's
    /// <c>GetAvailableBehaviors</c>/<c>GetMissionSnapshot</c> coming back empty, supplied as a delegate
    /// because <c>IMissionEditorService</c> lives above this assembly.
    /// </summary>
    [Fact]
    public void TheBrainSignalDecides_WhenExactlyOneEntityIsSelected()
    {
        var withBrain    = DetailsViewPredicates.OneEntityWithBrain(_ => true);
        var withoutBrain = DetailsViewPredicates.OneEntityWithBrain(_ => false);

        Assert.True (withBrain(Context(One)));
        Assert.False(withoutBrain(Context(One)));
    }

    /// <summary>
    /// ⛔ <b>The entity half still applies — a brain-equipped world with TWO selected offers nothing.</b>
    /// ⚠ Without this, a signal that answered <c>true</c> for everything would make the Mission view
    /// offer on a multi-selection, and <c>MissionPanel</c> would silently take <c>[0]</c>.
    /// </summary>
    [Fact]
    public void EvenWithABrainSignal_TwoEntitiesOfferNothing()
    {
        var always = DetailsViewPredicates.OneEntityWithBrain(_ => true);

        Assert.False(always(Context()));
        Assert.False(always(Context(One, Two)));
    }

    /// <summary>
    /// ⭐⭐ <b>A host that cannot ask answers NO.</b> ⛔ Not a silent default: a <c>null</c> signal means
    /// there is no mission service, so claiming an entity has behaviours would offer a panel that can
    /// render nothing. ⚠ 📌 The <c>2026-08-16</c> rule's qualifier — a default is only a defect when the
    /// caller could have done better, and here it genuinely cannot.
    /// </summary>
    [Fact]
    public void WithNoBrainSignalAtAll_TheViewNeverOffers()
        => Assert.False(DetailsViewPredicates.OneEntityWithBrain(null)(Context(One)));

    /// <summary>
    /// ⭐⭐⭐ <b>The signal is asked about the SELECTED entity, not just asked.</b>
    /// ⛔ A predicate that called <c>hasBrain(default)</c> would pass every rail above while asking
    /// about entity 0 in production — 📌 exactly <c>R-78</c>'s chameleon-sentinel failure, one axis over.
    /// </summary>
    [Fact]
    public void TheSignalIsAskedAboutTheSelectedEntity()
    {
        Entity? asked = null;
        var predicate = DetailsViewPredicates.OneEntityWithBrain(e => { asked = e; return true; });

        Assert.True(predicate(Context(Two)));
        Assert.Equal(Two, asked);
    }
}
