using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>Where a soldier's move under fire stands (AQ96 C).</summary>
    public enum UnderFirePhase : byte
    {
        /// <summary>On his way — the play's move, at the gait and speed the threat asks for.</summary>
        Moving = 0,
        /// <summary>Down: prone, the move halted, waiting for a lapse, cover from his half, or a cover point to crawl to.</summary>
        Down = 1,
        /// <summary>Crawling a step sideways before he rises — never up where he dropped.</summary>
        Shifting = 2,
        /// <summary>Crawling to a cover point near him; down again there.</summary>
        Crawling = 3,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2319</c> / <c>CE-2320</c> (AQ96, behaviors) — a soldier's move under fire, kept honest after the move is issued: stamped by the
    /// ONE move helper (<c>LocomotionMoveTo.Issue</c>, behaviors) and kept by <c>UnderFireMoveSystem</c> (the gait re-picked when the threat
    /// changes, the rush speed, the drop, the rise). Brain-local (NoScenario, never sent): the body learns it through the two components the
    /// brain already replicates, <c>StanceIntent</c> and <c>NavigationIntent</c>.
    /// ⭐ <see cref="HoldIntentId"/> is the one thing the move's executor reads: an intent the reflex wrote (a halt, a side step, a crawl) is
    /// not the play's move, so its result never ends the move — the play sees Running throughout.
    /// 📄 docs/blueprints/Architect_Question_96_Movement_Under_Fire.md §2a.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.UnderFireMove)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct UnderFireMove
    {
        /// <summary>The locomotion channel's move this tracks (its <c>ActionInstanceId</c>); 0 = none.</summary>
        public uint ActionInstanceId;
        /// <summary>The <c>NavigationIntent.IntentId</c> the reflex wrote, 0 = the intent is the move's own.</summary>
        public uint HoldIntentId;
        /// <summary>The gait the move was issued with (behaviors' <c>MoveGait</c>, as a byte).</summary>
        public byte Gait;
        /// <summary>1 = he was threatened when last checked (the gait and speed follow it).</summary>
        public byte Threatened;
        /// <summary>1 = he has crawled to cover since he went down (one crawl per drop).</summary>
        public byte Crawled;
        public UnderFirePhase Phase;
        /// <summary>The speed the play asked for (m/s) — the pace out of contact.</summary>
        public float AskedSpeed;
        /// <summary>Where the side step or the crawl goes.</summary>
        public Vector3 SubTarget;
        /// <summary>Sim time he went down / started the sub-move / last rose.</summary>
        public double DownSince, SubSince, UpSince;
        /// <summary>The time of the near miss or hit he last reacted to (so one round drops him once).</summary>
        public double ReactedFireAt;
        /// <summary>⭐ C3 — his half of the squad is covered by the other half's fire until then (stamped by the play that checks the gate).</summary>
        public double CoveredUntil;
        /// <summary>⭐ <c>CE-2343</c> (R-2066 C) — the way the cover he crawled to faces (toward its obstacle; zero = none).</summary>
        public Vector2 CoverFacing;
    }
}
