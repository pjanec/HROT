using System;
using System.Collections.Generic;
using Fdp.Toolkit.Behavior;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-308</c> — the ONE place that answers *"what type does this predicate's slot
    /// hold, and what slots may it choose from?"*</b>
    ///
    /// <para>📐 <b>Why it exists: three callers had the same expression.</b>
    /// <c>PredicateCompiler</c>, <c>PropertyPathFieldDrawer</c> and <c>PredicateValueFieldDrawer</c>
    /// each carried <c>target == Blackboard1024 ? def.HeavyDtoType : def.BlackboardLayoutType</c>
    /// — a verbatim duplicate in three files, in two assemblies. ⛔ Re-pointing it in three places
    /// is how the fourth caller gets it wrong. ⇒ routed, not copied.</para>
    ///
    /// <para>⚠ <b>The compiler and the drawers must agree EXACTLY.</b> A drawer that offers a
    /// property path the compiler cannot bind produces a search that silently matches nothing —
    /// the same silent-wrong-answer shape <c>CE-312</c> was. One resolver makes disagreement
    /// impossible rather than unlikely.</para>
    /// </summary>
    public static class BehaviorParamSlotResolver
    {
        /// <summary>The slot key meaning "this behaviour's root params", not a stateful slot.</summary>
        public const int RootParamsSlotKey = 0;

        /// <summary>
        /// The DTO type stored in <paramref name="slotKey"/> for <paramref name="def"/>, or
        /// <c>null</c> when the behaviour has no such slot or the slot carries no typed layout.
        /// </summary>
        public static Type? ResolveDtoType(BehaviorDefinition? def, int slotKey)
        {
            if (def == null) return null;

            if (slotKey == RootParamsSlotKey)
                return def.BlackboardLayoutType;

            if (def.StatefulWorkingSlots is not { Count: > 0 } slots) return null;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].SlotKey == slotKey)
                    return slots[i].WorkingStateType;

            return null;
        }

        /// <summary>
        /// The choices a slot picker should offer for <paramref name="def"/>: root params first,
        /// then every stateful slot that carries a typed working state.
        ///
        /// <para>⛔ Slots with a <c>null</c> <c>WorkingStateType</c> are OMITTED — a predicate cannot
        /// bind a property path against an untyped region, so offering it would be offering a search
        /// that cannot compile.</para>
        /// </summary>
        public static IReadOnlyList<SlotChoice> GetChoices(BehaviorDefinition? def)
        {
            var choices = new List<SlotChoice>();
            if (def == null) return choices;

            if (def.BlackboardLayoutType != null)
                choices.Add(new SlotChoice(RootParamsSlotKey, "Root parameters", def.BlackboardLayoutType));

            if (def.StatefulWorkingSlots is { Count: > 0 } slots)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    if (s.WorkingStateType == null) continue;
                    string label = s.NodeLabel ?? $"slot 0x{s.SlotKey:X8}";
                    choices.Add(new SlotChoice(
                        s.SlotKey, $"[{ScopeName(s.Scope)}] {label}", s.WorkingStateType));
                }
            }

            return choices;
        }

        /// <summary>The display label for one selectable slot.</summary>
        public readonly struct SlotChoice
        {
            public SlotChoice(int slotKey, string label, Type dtoType)
            {
                SlotKey = slotKey;
                Label   = label;
                DtoType = dtoType;
            }

            public int    SlotKey { get; }
            public string Label   { get; }
            public Type   DtoType { get; }
        }

        /// <summary>
        /// ⚠ Mirrors <c>StatefulWorkingStateProjection.ScopeName</c> deliberately — that one lives in
        /// the Hrot presentation layer, which this assembly sits BELOW and cannot reference. ⛔ The
        /// alternative is moving a display string into the toolkit purely to share it, which trades a
        /// two-line duplicate for a layering inversion.
        /// </summary>
        private static string ScopeName(byte scope) => scope switch
        {
            1 => "Behavior",
            2 => "Entity",
            _ => "Node",
        };
    }
}
