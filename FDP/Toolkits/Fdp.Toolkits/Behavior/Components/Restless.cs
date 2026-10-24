using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-2368</c> (AQ103, R-2074, behaviors) — a stationary person's idle life, kept by <c>RestlessSystem</c> (behaviors): where he
    /// settled, since when he has been still, when he next shifts, and the move intent it wrote for him. Brain-local (NoScenario, never
    /// sent): the body learns of a shift through the <c>NavigationIntent</c> the brain already replicates. ⭐ Like
    /// <see cref="UnderFireMove.HoldIntentId"/>, <see cref="HoldIntentId"/> marks the intent as the reflex's, never the play's — a play's
    /// next move simply replaces it.
    /// 📄 docs/blueprints/Architect_Question_103_Idle_Life.md §3.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Restless)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Restless
    {
        /// <summary>Where he settled: his shifts stay within the tether of it.</summary>
        public Vector3 Anchor;
        /// <summary>Where he stood when last checked (a move by anything else re-settles him).</summary>
        public Vector3 LastPos;
        /// <summary>Sim time he came to rest (or ended his last shift).</summary>
        public double StillSince;
        /// <summary>Sim time of his next shift (seeded per man and per shift).</summary>
        public double NextAt;
        /// <summary>The <c>NavigationIntent.IntentId</c> the reflex wrote; 0 = none in hand.</summary>
        public uint HoldIntentId;
        /// <summary>The shifts he has made since he settled (sides alternate, the seed moves on).</summary>
        public byte Moves;
        /// <summary>1 while a shift is under way.</summary>
        public byte Moving;
        /// <summary>⭐ AQ103 I — the idle gesture he shows ends then (s); the next one may start at <see cref="NextGestureAt"/>.</summary>
        public double GestureUntil, NextGestureAt;
        /// <summary>⭐ AQ103 I — when his calm is next judged (s): at most once a second.</summary>
        public double GestureCheckAt;
    }
}
