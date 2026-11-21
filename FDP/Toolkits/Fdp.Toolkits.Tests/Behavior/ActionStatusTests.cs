using System;
using Fdp.Toolkit.Behavior.Diagnostics;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>⭐ The executors' "why am I not acting" words (<see cref="ActionStatusOf"/>) — what the gizmo and the debug API show.</summary>
    public sealed class ActionStatusTests
    {
        /// <summary>
        /// ⭐ <c>CE-2218</c> — every reason has a word: six (the weapon system's three holds, IF-1's two, <c>Masked</c>) were blank on the
        /// map and in the debug API. 🔴 Red-proof: drop one arm of <see cref="ActionStatusOf.Label"/> ⇒ red.
        /// </summary>
        [Fact]
        public void EveryReason_HasALabel()
        {
            foreach (ActionReason r in Enum.GetValues<ActionReason>())
                if (r != ActionReason.None) Assert.False(string.IsNullOrEmpty(ActionStatusOf.Label(r)), $"{r} has no label");
        }

        /// <summary>⭐ <c>CE-2218</c> — a weapon held because the terrain stops its round reads as a HOLD (drawn amber), as the friendly-line hold.</summary>
        [Fact]
        public void Masked_IsAHold()
        {
            Assert.True(ActionStatusOf.IsHold(ActionReason.Masked));
            Assert.True(ActionStatusOf.IsHold(ActionReason.HoldFriendlyOnLine));
            Assert.False(ActionStatusOf.IsHold(ActionReason.Firing));
        }
    }
}
