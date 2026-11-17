using System;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-1 (docs/designs/animals/DESIGN_Animals.md §4a / §6.1, R-4266…R-4269) — the ANIMALS: TKB 700…711, DIS Kind 3 (life form)
    /// with categories 200…208 on land and 205 in the air, spawned NEUTRAL (never change side, AR-6). ⭐ What makes them animals and not people is
    /// their MOVEMENT CLASS (<see cref="LocomotionClass.Quadruped"/> / <see cref="LocomotionClass.Bird"/>, AR-1 / AR-2) and their classes
    /// (<c>LandAnimal</c> / <c>AirAnimal</c>, AR-19). ⭐ Each species' numbers live in ONE row of <see cref="Species"/> (AR-21).
    /// </summary>
    public static class AnimalTkbCatalog
    {
        public const byte DisCategoryCattle = 200, DisCategorySheep = 201, DisCategoryHorse = 202, DisCategoryDog = 203, DisCategoryCat = 204,
                          DisCategoryBird = 205, DisCategoryPoultry = 206, DisCategoryPig = 207, DisCategoryGoat = 208;

        /// <summary>One species' row: the TKB type, name, DIS, body (m, kg), speed and the temperament (AR-21).</summary>
        public sealed record Row(long Type, string Name, byte Domain, byte Category, byte Sub, float Length, float Width, float Height, float Health,
                                 float RunSpeed, AnimalTemperamentDto Temperament);

        /// <summary>⭐ AR-21 — the species table (first estimates, tuned by the AB-10 demo; distances m, speeds m/s, times s).</summary>
        public static readonly Row[] Species =
        {
            new(TkbEntityTypes.Animal_Cattle, "Cattle", 1, DisCategoryCattle, 1, 2.4f, 0.9f, 1.5f, 400f, 11f, T(0, flight: 25f, panic: 10f, flee: 9f, fleeDist: 200f, recover: 60f, cohesion: 0.4f, breaks: true, call: 45f, callRange: 800f, alarmRange: 1200f, walk: 1.0f)),
            new(TkbEntityTypes.Animal_Sheep, "Sheep", 1, DisCategorySheep, 1, 1.2f, 0.5f, 0.8f, 120f, 10f, T(1, flight: 30f, panic: 12f, flee: 8f, fleeDist: 150f, recover: 45f, cohesion: 0.9f, call: 30f, callRange: 500f, alarmRange: 700f, walk: 0.9f)),
            new(TkbEntityTypes.Animal_HorseHerd, "HorseHerd", 1, DisCategoryHorse, 1, 2.4f, 0.7f, 1.6f, 350f, 15f, T(2, flight: 40f, panic: 15f, flee: 13f, fleeDist: 300f, recover: 60f, cohesion: 0.6f, breaks: true, jumps: true, call: 60f, callRange: 800f, alarmRange: 1200f, walk: 1.5f)),
            new(TkbEntityTypes.Animal_HorseRiding, "HorseRiding", 1, DisCategoryHorse, 2, 2.4f, 0.7f, 1.6f, 350f, 15f, T(3, flight: 15f, panic: 6f, flee: 13f, fleeDist: 150f, recover: 30f, breaks: true, jumps: true, call: 90f, callRange: 800f, alarmRange: 1200f, walk: 1.6f, bolt: 0.75f)),
            new(TkbEntityTypes.Animal_HorseWar, "HorseWar", 1, DisCategoryHorse, 3, 2.5f, 0.75f, 1.7f, 400f, 15f, T(4, fearless: true, flee: 13f, fleeDist: 100f, recover: 10f, breaks: true, jumps: true, call: 120f, callRange: 800f, alarmRange: 1200f, walk: 1.6f)),
            new(TkbEntityTypes.Animal_Dog, "Dog", 1, DisCategoryDog, 1, 0.9f, 0.3f, 0.6f, 80f, 12f, T(5, fearless: true, territorial: true, flight: 30f, panic: 8f, flee: 10f, fleeDist: 30f, recover: 20f, call: 120f, callRange: 1000f, alarmRange: 1000f, walk: 1.2f)),
            new(TkbEntityTypes.Animal_Cat, "Cat", 1, DisCategoryCat, 1, 0.45f, 0.15f, 0.3f, 20f, 8f, T(6, flight: 8f, panic: 3f, flee: 7f, fleeDist: 30f, recover: 60f, jumps: true, call: 300f, callRange: 60f, alarmRange: 80f, walk: 0.7f)),
            new(TkbEntityTypes.Animal_BirdFlock, "BirdFlock", 2, DisCategoryBird, 2, 6f, 6f, 1f, 40f, 12f, T(7, flight: 40f, panic: 15f, flee: 12f, fleeDist: 400f, recover: 120f, cohesion: 1f, call: 8f, callRange: 150f, alarmRange: 250f, walk: 5f, lone: TkbEntityTypes.Animal_Bird)),
            new(TkbEntityTypes.Animal_Bird, "Bird", 2, DisCategoryBird, 1, 0.25f, 0.3f, 0.15f, 5f, 10f, T(8, flight: 20f, panic: 8f, flee: 10f, fleeDist: 200f, recover: 90f, call: 20f, callRange: 120f, alarmRange: 200f, walk: 5f)),
            new(TkbEntityTypes.Animal_Hen, "Hen", 1, DisCategoryPoultry, 1, 0.4f, 0.25f, 0.4f, 15f, 4f, T(9, flight: 6f, panic: 2f, flee: 3.5f, fleeDist: 20f, recover: 20f, cohesion: 0.5f, call: 20f, callRange: 150f, alarmRange: 250f, walk: 0.4f)),
            new(TkbEntityTypes.Animal_Pig, "Pig", 1, DisCategoryPig, 1, 1.4f, 0.6f, 0.8f, 200f, 5f, T(10, flight: 10f, panic: 4f, flee: 4.5f, fleeDist: 30f, recover: 40f, cohesion: 0.6f, call: 40f, callRange: 300f, alarmRange: 800f, walk: 0.6f)),
            new(TkbEntityTypes.Animal_Goat, "Goat", 1, DisCategoryGoat, 1, 1.2f, 0.4f, 0.8f, 100f, 8f, T(11, flight: 15f, panic: 6f, flee: 7f, fleeDist: 60f, recover: 40f, cohesion: 0.4f, jumps: true, call: 40f, callRange: 400f, alarmRange: 600f, walk: 0.8f)),
            // ⭐ CE-4649 part C ④ (AN-20c) — the DUCK (~0.5 m, flies LOW: BirdFlightModel.CruiseOf) and its flock; the shootable birds' LoneType is what a hit leaves dead
            new(TkbEntityTypes.Animal_DuckFlock, "DuckFlock", 2, DisCategoryBird, 4, 6f, 6f, 1f, 40f, 14f, T(13, flight: 40f, panic: 15f, flee: 14f, fleeDist: 400f, recover: 120f, cohesion: 0.6f, call: 6f, callRange: 200f, alarmRange: 300f, walk: 6f, lone: TkbEntityTypes.Animal_Duck)),
            new(TkbEntityTypes.Animal_Duck, "Duck", 2, DisCategoryBird, 3, 0.5f, 0.3f, 0.2f, 6f, 14f, T(12, flight: 12f, panic: 5f, flee: 14f, fleeDist: 250f, recover: 90f, call: 12f, callRange: 200f, alarmRange: 300f, walk: 6f)),
        };

        private static AnimalTemperamentDto T(byte species, float flight = 0f, float panic = 0f, float flee = 0f, float fleeDist = 0f, float recover = 0f,
            float cohesion = 0f, bool fearless = false, bool territorial = false, bool breaks = false, bool jumps = false, float call = 0f,
            float callRange = 0f, float alarmRange = 0f, float walk = 1f, float bolt = 0f, long lone = 0)
            => new()
            {
                Species = species, FlightZoneM = flight, PanicZoneM = panic, FleeSpeed = flee, FleeDistanceM = fleeDist, Recovery = recover,
                HerdCohesion = cohesion, Fearless = fearless, Territorial = territorial, Breaks = breaks, Jumps = jumps, CallInterval = call,
                CallRangeM = callRange, AlarmRangeM = alarmRange, WalkSpeed = walk, BoltFear = bolt, LoneType = lone,
            };

        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            foreach (var r in Species) Register(tkb, r);
            // ⭐ AB-9 (AR-16) — the ANIMAL AREA: an area drawn with the one area tool; its species / count / Enclosed, defaults here
            var area = new TkbTemplate("AnimalArea", TkbEntityTypes.AnimalArea);
            area.AddDescriptor(new AnimalAreaDto { Species = 0, Count = 12 });
            tkb.Register(area);
        }

        private static void Register(ITkbDatabase tkb, Row r)
        {
            var t = new TkbTemplate(r.Name, r.Type);
            UrbanCombatTkbCatalog.Master(t, r.Name, new DISEntityType { Kind = 3, Domain = r.Domain, Category = r.Category, Subcategory = r.Sub });
            bool bird = r.Domain == 2;
            t.AddDescriptor(new LocomotionDto { Class = bird ? LocomotionClass.Bird : LocomotionClass.Quadruped });
            if (!bird)
                t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Quadruped, Length = r.Length, Width = r.Width,
                                                          MaxSpeedFwd = r.RunSpeed, MaxAccel = 4f });
            t.AddDescriptor(new StrideRenderModelDefDto { ShapeKind = CollisionShapeKind.OrientedBox, ShapeRadius = r.Width * 0.5f, ShapeHeight = r.Height });
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = r.Health });
            t.AddDescriptor(new InjuryDefDto());   // AN-12: hit, injured, dead like a person
            t.AddDescriptor(r.Temperament);
            t.AddDescriptor(new BehaviorProfileDto
            {
                SimTier = BehaviorConstants.SimTierCivilian, BrainTier = BehaviorConstants.BrainTierHsm,
                CanMove = true, Faction = ForceId.Neutral,   // AR-6: spawned neutral, never changes side
                // ⭐ AB-4 — its one brain (by name, its params its own temperament; the hash the fallback without the managed event)
                DefaultBehavior = Hrot.Map.Definitions.Behavior.BehaviorNames.AnimalHsm,
                DefaultBehaviorHash = BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.AnimalHsm),
            });
            t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 150f, HearingRange = 600f, FieldOfViewDegrees = 300f });
            // movement sound (hooves, paws, wings) — the animal calls are voices (AB-8, AR-7)
            t.AddDescriptor(UrbanCombatTkbCatalog.Sounds(bird ? 60f : Math.Clamp(r.Length * 40f, 15f, 120f), SoundSourceClass.Footsteps));
            if (r.Type == TkbEntityTypes.Animal_HorseRiding || r.Type == TkbEntityTypes.Animal_HorseWar) t.AddDescriptor(HorseSeats(r.Type == TkbEntityTypes.Animal_HorseRiding));
            tkb.Register(t);
        }

        /// <summary>⭐ §6.1 (AR-10) — the riding horse: seat 0 the RIDER (Driver, a firing seat that fires WHILE riding — no stopped-only rule, unlike the
        /// motorcycle), seat 1 a second rider (a firing seat, sides and rear); the war horse seat 0 only.</summary>
        public static CarrierDefDto HorseSeats(bool secondRider)
        {
            var c = new CarrierDefDto { Exposure = PassengerExposure.Open };
            c.Seats.Add(new SeatDto { Role = SeatRole.Driver, Exposed = true, X = 0.15f, Z = 1.35f, Pose = SeatPose.Ride,
                                      TraverseMinDeg = -150f, TraverseMaxDeg = 150f });
            if (secondRider)
                c.Seats.Add(new SeatDto { Role = SeatRole.Passenger, Exposed = true, X = -0.45f, Z = 1.3f, Pose = SeatPose.Ride,
                                          TraverseMinDeg = 30f, TraverseMaxDeg = 330f });
            return c;
        }
    }
}
