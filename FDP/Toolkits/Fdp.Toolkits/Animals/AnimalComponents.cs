using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Animals
{
    /// <summary>⭐ <c>CE-4623</c> (docs/designs/animals/DESIGN_Animals.md §4a) — the species, in TKB order 700…711 (the sound block's <c>s</c>).</summary>
    public enum AnimalSpecies : byte
    {
        Cattle = 0, Sheep = 1, HorseHerd = 2, HorseRiding = 3, HorseWar = 4, Dog = 5, Cat = 6, BirdFlock = 7, Bird = 8, Hen = 9, Pig = 10, Goat = 11,
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
    [DataPolicy(DataPolicy.Default)]
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
    [DataPolicy(DataPolicy.Default)]
    public struct AnimalHome
    {
        public Vector3 Centre;
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
    [DataPolicy(DataPolicy.Default)]
    public struct HerdState
    {
        public Vector3 Centre;
        public Vector3 NextGrazing;
        public Entity Lead;
        public AnimalMode Mode;
        public AnimalSpecies Species;
        private ushort _pad;
    }
}
