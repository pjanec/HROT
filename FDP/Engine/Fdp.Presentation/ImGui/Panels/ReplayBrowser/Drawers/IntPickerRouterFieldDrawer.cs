using System;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

/// <summary>
/// ⭐⭐ Routes an <c>int</c> field to the picker its ATTRIBUTE asks for.
///
/// <para>📐 <b>Why a router and not a third registration.</b> <c>ComponentEditDrawer</c> keys drawers
/// by TARGET TYPE — one entry per <see cref="Type"/> — so <c>int</c> has exactly one slot, and
/// <c>CE-308</c> needs a second <c>int</c> picker beside the behaviour-hash one. ⛔ Folding the slot
/// arm into <c>BehaviorHashFieldDrawer</c> would make a class named for one attribute serve two, and
/// its rails assert that name. ⭐ This keeps each picker single-purpose and puts the dispatch in one
/// obvious place.</para>
///
/// <para>⚠ Order matters only in that each arm is gated on its OWN attribute; a field carrying
/// neither falls through to the behaviour-hash drawer, whose no-attribute path is a plain
/// <c>InputInt</c> — the behaviour every other <c>int</c> field had before this router existed.</para>
/// </summary>
internal sealed class IntPickerRouterFieldDrawer : IImGuiFieldDrawer
{
    private readonly WorkingSlotFieldDrawer _workingSlot;
    private readonly BehaviorHashFieldDrawer _behaviorHash;

    public IntPickerRouterFieldDrawer(
        WorkingSlotFieldDrawer workingSlot,
        BehaviorHashFieldDrawer behaviorHash)
    {
        _workingSlot  = workingSlot  ?? throw new ArgumentNullException(nameof(workingSlot));
        _behaviorHash = behaviorHash ?? throw new ArgumentNullException(nameof(behaviorHash));
    }

    public Type TargetType => typeof(int);

    public bool DrawInput(ref object value, EditNode node)
    {
        bool isWorkingSlot = node.Metadata.CustomAttributes
            .Any(a => a is WorkingSlotPickerAttribute);

        return isWorkingSlot
            ? _workingSlot.DrawInput(ref value, node)
            : _behaviorHash.DrawInput(ref value, node);
    }
}
