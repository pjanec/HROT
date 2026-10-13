using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>CE-2157</c> (O-a) — readable names for a squad drill's kind, phase and phase event, for the debug route
    /// (<see cref="SquadDrillTrace"/>). Read from each manoeuvre's own constants; an unknown value reads as its number.
    /// </summary>
    public static class SquadDrillNames
    {
        public static string Kind(ushort kind) => kind switch
        {
            DangerAreaCrossingManeuver.ManeuverKind  => "DangerAreaCrossing",
            BoundingOverwatchManeuver.ManeuverKind   => "BoundingOverwatch",
            SuppressAndManeuverManeuver.ManeuverKind => "SuppressAndManeuver",
            HillCrestHullDownManeuver.ManeuverKind   => "HillCrestHullDown",
            StackAndRoomEntryManeuver.ManeuverKind   => "StackAndRoomEntry",
            TravellingOverwatchManeuver.ManeuverKind => "TravellingOverwatch",
            SquadHoldManeuver.ManeuverKind           => "SquadHold",
            BreakContactManeuver.ManeuverKind        => "BreakContact",
            SquadMarchManeuver.ManeuverKind          => "SquadMarch",
            SquadDismountManeuver.ManeuverKind       => "SquadDismount",
            CasualtyCareManeuver.ManeuverKind        => "CasualtyCare",
            _                                        => "kind " + kind,
        };

        public static string Phase(ushort kind, ushort phase) => (kind, phase) switch
        {
            (DangerAreaCrossingManeuver.ManeuverKind, DangerAreaCrossingManeuver.PhaseSetSecurity)      => "SetSecurity",
            (DangerAreaCrossingManeuver.ManeuverKind, DangerAreaCrossingManeuver.PhaseCrossElement)     => "CrossElement",
            (DangerAreaCrossingManeuver.ManeuverKind, DangerAreaCrossingManeuver.PhaseFarSideCover)     => "FarSideCover",
            (DangerAreaCrossingManeuver.ManeuverKind, DangerAreaCrossingManeuver.PhaseCollapseSecurity) => "CollapseSecurity",
            (DangerAreaCrossingManeuver.ManeuverKind, DangerAreaCrossingManeuver.PhaseReform)           => "Reform",
            (BoundingOverwatchManeuver.ManeuverKind, BoundingOverwatchManeuver.PhaseElement0Moving)     => "Element0Moving",
            (BoundingOverwatchManeuver.ManeuverKind, BoundingOverwatchManeuver.PhaseElement1Moving)     => "Element1Moving",
            (BoundingOverwatchManeuver.ManeuverKind, BoundingOverwatchManeuver.PhaseAborted)            => "Aborted",
            (SuppressAndManeuverManeuver.ManeuverKind, SuppressAndManeuverManeuver.PhaseSuppressing)     => "Suppressing",
            (SuppressAndManeuverManeuver.ManeuverKind, SuppressAndManeuverManeuver.PhaseAssaulting)      => "Assaulting",
            (SuppressAndManeuverManeuver.ManeuverKind, SuppressAndManeuverManeuver.PhaseAssaultComplete) => "AssaultComplete",
            (SuppressAndManeuverManeuver.ManeuverKind, SuppressAndManeuverManeuver.PhaseAborted)         => "Aborted",
            (CasualtyCareManeuver.ManeuverKind, CasualtyCareManeuver.PhaseSuppress)                     => "Suppress",
            (CasualtyCareManeuver.ManeuverKind, CasualtyCareManeuver.PhaseGo)                           => "Go",
            (CasualtyCareManeuver.ManeuverKind, CasualtyCareManeuver.PhaseDone)                         => "Done",
            (CasualtyCareManeuver.ManeuverKind, CasualtyCareManeuver.PhaseRecall)                       => "Recall",
            (CasualtyCareManeuver.ManeuverKind, CasualtyCareManeuver.PhaseAborted)                      => "Aborted",
            _                                                                                           => "phase " + phase,
        };

        public static string Event(byte evt) => evt switch
        {
            SquadDrillTrace.EventStart   => "Start",
            SquadDrillTrace.EventRetake  => "Retake",
            SquadDrillTrace.EventEnd     => "End",
            SquadDrillTrace.EventTimeout => "Timeout",
            <= (byte)PhaseEventKind.Abort => ((PhaseEventKind)evt).ToString(),
            _                            => "event " + evt,
        };
    }
}
