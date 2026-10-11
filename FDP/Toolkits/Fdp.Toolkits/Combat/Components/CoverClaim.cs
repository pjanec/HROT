using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-3158</c> G5 (R-257, 📄 docs/DESIGN_Peek_And_Fire.md §10.7) — <b>"first claim wins"</b>: the cover point a unit fighting
    /// from cover is going to (<see cref="Hide"/>) and the spot it steps out to (<see cref="Peek"/>), live until <see cref="Until"/>.
    /// Written by the <c>PeekAndFire</c> node the moment it COMMITS to a point and refreshed every tick it runs; a unit picking a
    /// point skips any within <c>ClaimRadius</c> (3-D, R-252) of a live claim of a unit of its own force. No messages, no locks, no
    /// commander: a brain that dies or aborts simply stops refreshing and the claim expires.
    /// <para>⭐ An ECS COMPONENT, not unit memory: other units must READ it, and AQ87 G (R-237) keeps unit memory "self only".
    /// ⚠ Brain-local — never replicated (a squad's members share one CGF node, Squad design §2 "All Brain-resident"); never saved.
    /// Registered once in the shared registry (R-254).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CoverClaim)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct CoverClaim
    {
        /// <summary>The cover point the unit hides at.</summary>
        public Vector3 Hide;
        /// <summary>The spot a step peek steps out to (= <see cref="Hide"/> for a stance peek).</summary>
        public Vector3 Peek;
        /// <summary>Sim time the claim lapses unless refreshed; 0 = released.</summary>
        public double Until;
    }
}
