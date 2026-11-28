using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Animals
{
    /// <summary>⭐ <c>CE-4623</c> (docs/designs/animals/DESIGN_Animals.md §4a) — the species, in TKB order 700…711 (the sound block's <c>s</c>).</summary>
    public enum AnimalSpecies : byte
    {
        Cattle = 0, Sheep = 1, HorseHerd = 2, HorseRiding = 3, HorseWar = 4, Dog = 5, Cat = 6, BirdFlock = 7, Bird = 8, Hen = 9, Pig = 10, Goat = 11,
        /// <summary>⭐ CE-4649 part C ④ (AN-20c) — the duck (TKB 713) and its flock (714): species values 12, 13 (the TKB order past the area, 712).</summary>
        Duck = 12, DuckFlock = 13,
        /// <summary>⭐ CE-4649 part D (AN-21a, AN-21f) — the rabbit (TKB 715) and the hunting dog (716): species values 14, 15.</summary>
        Rabbit = 14, HuntingDog = 15,
    }

    /// <summary>⭐ <c>CE-4623</c> §6.1 — what an animal is doing, as the presentation needs it (pose, gait, the alarm look).</summary>
    public enum AnimalMode : byte
    {
        /// <summary>Grazing, wandering, pecking, perched — the routine.</summary>
        Calm = 0,
        /// <summary>Head up, listening: fear inside the alert band.</summary>
        Alert = 1,
        /// <summary>Running from the threat (a bird: flying off).</summary>
        Flee = 2,
        /// <summary>Calming down, walking back to its grazing ground / home.</summary>
        Recover = 3,
        /// <summary>Lying down / roosting (night).</summary>
        Rest = 4,
        /// <summary>A guard dog barking or attacking an intruder.</summary>
        Guard = 5,
        /// <summary>⭐ CE-4649 part D (AN-21b) — frozen and crouched (the rabbit): still, low; its stance (<c>StanceIntent</c>) is Crouched.</summary>
        Crouch = 6,
    }

    /// <summary>⭐ <c>CE-4623</c> — flags on <see cref="AnimalState"/>.</summary>
    [System.Flags]
    public enum AnimalFlags : byte
    {
        None = 0,
        /// <summary>A ridden horse bolting (the rider's controls overridden for a while — AR-10).</summary>
        Bolting = 1,
        /// <summary>Attacking right now: a "dangerous animal" — self-defence covers shooting it (AR-6). The animal never changes side.</summary>
        Dangerous = 2,
        /// <summary>Calling (a moo, a bark …) — the presentation may open the mouth.</summary>
        Calling = 4,
        /// <summary>⭐ CE-11008 (RR-8, R-11002) — a BIRD in flight (a lone bird or a flock; set and cleared by its brain with its flight, AB-5): a renderer beats its
        /// wings from this state instead of guessing from its height or speed. A flock also says it in <see cref="FlockState.Mode"/>.</summary>
        InFlight = 8,   // ⚠ not "Flying": the IDL puts enum members in one scope and FlockMode.Flying exists
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4628</c> — what ONLY an animal keeps beside its <see cref="Fdp.Toolkit.Creatures.Fear"/> (the non-fear fields that sat on the old animal <c>Fear</c>): the person it
    /// guards against, its hunting dog's handler and quarry, the bolt of a ridden horse, the flight-zone look schedule. Written by the one fear
    /// system's serial step, read by the animal's brain. ⛔ BRAIN-LOCAL: never replicated, never saved. Added WITH <see cref="AnimalState"/> (<c>AnimalTkbTranslator</c>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimalTasks)]
    [DataPolicy(DataPolicy.NoScenario)]   // ⭐ AB-10 — run-time state: recorded and snapshotted, never written into a saved scenario (rebuilt at run start)
    public struct AnimalTasks
    {
        /// <summary>⭐ AB-5 — a TERRITORIAL animal's intruder: the nearest person on its ground (the guard dog's bark and bite), Null = none.</summary>
        public Entity Intruder;
        /// <summary>⭐ CE-4649 part D (AN-21f) — a HUNTING DOG's handler (the nearest hunter) and quarry (the nearest game animal in flight): written by the fear system's serial step, read by the dog's brain. Null = none.</summary>
        public Entity Handler, Quarry;
        /// <summary>⭐ AB-5 — a ridden horse bolts until this sim time (0 = not bolting).</summary>
        public SimInstant BoltUntil;
        /// <summary>Sim seconds of the next flight-zone look (the zone is looked at ~5 times a second, not every frame).</summary>
        public SimInstant NextZoneLookAt;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> §6.1 — an animal's state as every node sees it: written on the animal's brain node, REPLICATED. 2 bytes. ⭐ <c>CE-4628</c>: its species is read from the TKB (<see cref="AnimalSpeciesOf"/>), its fear band is the replicated <c>Mood</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimalState)]
    [DataPolicy(DataPolicy.Default)]   // world state: recorded, saved, snapshotted
    public struct AnimalState
    {
        public AnimalMode Mode;
        /// <summary>0 calm · 1 uneasy · 2 frightened · 3 panic — the fear system's <see cref="AnimalFear.Band"/> of the shared <c>Fear</c>, written ONCE per
        /// frame at its pass and REPLICATED. ⭐ <c>CE-4628</c> (designs/animals/DESIGN_Animals.md §10b): KEPT, not moved to <c>Mood</c> — it is a DECISION
        /// input (the panic breach reads it, on any node that runs the kinematics) at band 3's own threshold 0.75, which <c>Mood</c>'s quarters cannot say;
        /// and read live off the brain-local <c>Fear</c> it changed when the herd's contagion fires (measured: three herd scenarios drifted).</summary>
        public byte FearBand;
        public AnimalFlags Flags;
    }

    /// <summary>⭐ <c>CE-4623</c> §6.1 (AN-4, AR-14) — a flock's mode.</summary>
    public enum FlockMode : byte { Flying = 0, Landed = 1, Perched = 2 }

    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> §6.1 — ONE entity for a whole flock, drawn as <see cref="Count"/> birds from <see cref="Seed"/> in closed form (never
    /// integrated: a seek, a replay and a late joiner draw the same birds). REPLICATED. 12 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FlockState)]
    [DataPolicy(DataPolicy.Default)]
    public struct FlockState
    {
        /// <summary>Birds alive now (a hit flock drops birds: AR-8).</summary>
        public ushort Count;
        /// <summary>Birds it started with (the presentation drops the highest indices first).</summary>
        public ushort InitialCount;
        public uint Seed;
        public FlockMode Mode;
        private byte _pad0;
        private ushort _pad1;
    }

    /// <summary>⭐ <c>CE-4623</c> (AR-9) — a herd entity's state: the grazing spot it is heading for, its mode, its species (a runtime herd has no TKB type). ⭐ <c>CE-4628</c>: its centre, lead and
    /// member count are the herd entity's <see cref="Fdp.Toolkit.Creatures.Group"/> (Kind Herd).</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.HerdState)]
    [DataPolicy(DataPolicy.NoScenario)]   // ⭐ AB-10 — run-time state: recorded and snapshotted, never written into a saved scenario (rebuilt at run start)
    public struct HerdState
    {
        public WorldPoint NextGrazing;
        public AnimalMode Mode;
        public AnimalSpecies Species;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-9 (docs/designs/animals/DESIGN_Animals.md AR-16) — an ANIMAL AREA (TKB 712, drawn with the one area tool): which species
    /// lives in it, how many, the seed that places them, and whether it is ENCLOSED (a pen: nothing runs out of it). AUTHORED and saved; the animals are
    /// NOT — they are populated at run start (<see cref="AnimalAreaPopulated"/>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimalArea)]
    [DataPolicy(DataPolicy.Default)]
    public struct AnimalArea
    {
        public AnimalSpecies Species;
        public byte Enclosed;
        public ushort Count;
        public uint Seed;
    }

    /// <summary>⭐ AB-9 — the area WAS populated (its animals were asked for). Recorded, so a restore or a replay sees it and never repopulates; never
    /// saved, so a scenario loaded fresh populates once.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimalAreaPopulated)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct AnimalAreaPopulated
    {
        /// <summary>Sim time it was populated.</summary>
        public SimInstant At;
    }
}
