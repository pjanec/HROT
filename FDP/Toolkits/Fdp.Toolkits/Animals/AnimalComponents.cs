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
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-3 (AR-3, AR-4) — an ANIMAL's fear, kept by the ONE fear system (<c>CivilianFearSystem</c>) beside a civilian's
    /// (which stays on <c>CivilianState</c>, unchanged — L-A8). Written on the animal's brain node only; ⛔ not replicated — every other node
    /// sees the band on <see cref="AnimalState.FearBand"/>. 0 = calm … 1 = blind panic.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Fear)]
    [DataPolicy(DataPolicy.NoScenario)]   // ⭐ AB-10 — run-time state: recorded and snapshotted, never written into a saved scenario (rebuilt at run start)
    public struct Fear
    {
        /// <summary>0 = calm … 1 = blind panic.</summary>
        public float Value;
        /// <summary>Where the last fright came from (a shot, the man walking up, the tank) — it runs AWAY from here.</summary>
        public WorldPoint FrightFrom;
        /// <summary>Sim seconds of the last fright (a fright this frame is not faded in the same frame).</summary>
        public SimInstant LastFrightAt;
        /// <summary>Sim seconds of the next flight-zone look (the zone is looked at ~5 times a second, not every frame).</summary>
        public SimInstant NextZoneLookAt;
        /// <summary>⭐ AB-5 — a TERRITORIAL animal's intruder: the nearest person on its ground (the guard dog's bark and bite), Null = none.</summary>
        public Entity Intruder;
        /// <summary>⭐ AB-5 — a ridden horse bolts until this sim time (0 = not bolting).</summary>
        public SimInstant BoltUntil;
        /// <summary>1 once <see cref="FrightFrom"/> holds a place.</summary>
        public byte HasFrightFrom;
        /// <summary>1 when the last fright was the FLIGHT ZONE (a threat seen near), 0 when it was heard or felt.</summary>
        public byte Seen;
        private byte _pad0, _pad1;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> §6.1 — an animal's state as every node sees it: written on the animal's brain node, REPLICATED. 4 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimalState)]
    [DataPolicy(DataPolicy.Default)]   // world state: recorded, saved, snapshotted
    public struct AnimalState
    {
        public AnimalMode Mode;
        /// <summary>0 calm · 1 uneasy · 2 frightened · 3 panic — the shared <c>Fear</c> value in bands.</summary>
        public byte FearBand;
        public AnimalSpecies Species;
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

    /// <summary>⭐ <c>CE-4623</c> (AR-9) — an animal's herd (a light herd entity, NEVER a <c>UnitRoster</c>). 8 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.HerdMember)]
    [DataPolicy(DataPolicy.NoScenario)]   // ⭐ AB-10 — run-time state: recorded and snapshotted, never written into a saved scenario (rebuilt at run start)
    public struct HerdMember
    {
        public Entity Herd;
    }

    /// <summary>
    /// ⭐ <c>CE-4623</c> (AN-5, AN-16, AR-16) — an animal's HOME: the centre it keeps to and the radius it wanders within; an optional TETHER (a dog's
    /// chain: the length it can NEVER exceed, enforced on the motion). <see cref="Enclosed"/> = an enclosed pen it never leaves (AN-18).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AnimalHome)]
    [DataPolicy(DataPolicy.NoScenario)]   // ⭐ AB-10 — run-time state: recorded and snapshotted, never written into a saved scenario (rebuilt at run start)
    public struct AnimalHome
    {
        public WorldPoint Centre;
        public float Radius;
        /// <summary>0 = no tether; else the chain's length from <see cref="Centre"/> (m).</summary>
        public float TetherMetres;
        /// <summary>The area entity it was populated from (Null for an authored one).</summary>
        public Entity Area;
        public byte Enclosed;
        private byte _pad0;
        private ushort _pad1;
    }

    /// <summary>⭐ <c>CE-4623</c> (AR-9) — a herd entity's state: its centre, the grazing spot it is heading for, its lead animal, its mode.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.HerdState)]
    [DataPolicy(DataPolicy.NoScenario)]   // ⭐ AB-10 — run-time state: recorded and snapshotted, never written into a saved scenario (rebuilt at run start)
    public struct HerdState
    {
        public WorldPoint Centre;
        public WorldPoint NextGrazing;
        public Entity Lead;
        public AnimalMode Mode;
        public AnimalSpecies Species;
        /// <summary>⭐ AB-8 — its living members (the herd's call throttle reads it; was padding).</summary>
        public ushort Members;
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
