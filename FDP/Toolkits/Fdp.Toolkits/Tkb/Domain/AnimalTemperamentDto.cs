using Fdp.Core;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> (docs/designs/animals/DESIGN_Animals.md AN-1, AR-21) — a species' TEMPERAMENT: the numbers ONE animal brain reads, so a new
    /// species is data. Distances in metres, speeds in m/s, times in seconds. ⚠ The values are first estimates from livestock-handling practice,
    /// tuned by the AB-10 demo; each is recorded in the species table (CE-4623 status file).
    /// </summary>
    [TkbDescriptor("Gen.Animal")]
    public record AnimalTemperamentDto
    {
        /// <summary>The species (TKB order 700…711 — <c>Fdp.Toolkit.Animals.AnimalSpecies</c>).</summary>
        public byte Species { get; init; }
        /// <summary>A threat (a person or a vehicle) inside this distance makes it UNEASY — head up, moves off.</summary>
        public float FlightZoneM { get; init; }
        /// <summary>Inside this distance it PANICS — runs.</summary>
        public float PanicZoneM { get; init; }
        /// <summary>How fast it runs from a fright (the type's top speed is its cap).</summary>
        public float FleeSpeed { get; init; }
        /// <summary>How far it runs before it stops to look back.</summary>
        public float FleeDistanceM { get; init; }
        /// <summary>From the last fright back to calm.</summary>
        public SimDuration Recovery { get; init; }
        /// <summary>0 = loose (each its own way) … 1 = one tight mass (sheep).</summary>
        public float HerdCohesion { get; init; }
        /// <summary>Not frightened by anything (a guard dog, a war horse) — a blast still makes it flinch.</summary>
        public bool Fearless { get; init; }
        /// <summary>Guards its home: barks at people near, attacks one who comes close (the dog, AN-5).</summary>
        public bool Territorial { get; init; }
        /// <summary>Big enough to break a SMALL fence when panicked (cattle, horses — AN-7).</summary>
        public bool Breaks { get; init; }
        /// <summary>Clears low obstacles on a jump link (horses, goats, cats — AN-13, AN-17, AN-11).</summary>
        public bool Jumps { get; init; }
        /// <summary>Mean interval between calls at rest (a moo, a baa — AN-19), seconds; 0 = silent at rest.</summary>
        public SimDuration CallInterval { get; init; }
        /// <summary>How far a call carries (m).</summary>
        public float CallRangeM { get; init; }
        /// <summary>How far an alarm call (a bellow, a squeal, the bark) carries (m).</summary>
        public float AlarmRangeM { get; init; }
        /// <summary>A walk / a graze pace (m/s).</summary>
        public float WalkSpeed { get; init; }
        /// <summary>A ridden horse BOLTS above this fear (0..1); 0 = never (AR-10).</summary>
        public float BoltFear { get; init; }
        /// <summary>⭐ CE-4649 part C ③ — for a FLOCK: the TKB type of ONE of its birds (what a round that hits a bird of the flock leaves, dead); 0 = none.</summary>
        public long LoneType { get; init; }
        /// <summary>⭐ CE-4649 part D (AN-21c) — how it flees: 0 = <c>Straight</c> (one leg away from the fright), 1 = <c>Zigzag</c> (a chain of short legs bending toward cover — the rabbit).</summary>
        public byte FleeStyle { get; init; }
        /// <summary>⭐ CE-4649 part D (AN-21b) — freezes (crouches, stays still) while frightened and the threat has not come inside its panic zone; the rabbit.</summary>
        public bool Freezes { get; init; }
        /// <summary>⭐ CE-4649 part D (AN-21f) — the hunting-dog play: follows its handler, ranges ahead, flushes game; never attacks a person.</summary>
        public bool Ranges { get; init; }
    }
}
