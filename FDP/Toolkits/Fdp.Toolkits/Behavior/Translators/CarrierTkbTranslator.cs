using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Behavior.Translators
{
    /// <summary>
    /// ⭐⭐ <c>CE-2151</c> (R-274 / R-2009, docs/DESIGN_Entity_Interactions.md §7.4) — projects a carrier type's <see cref="CarrierDefDto"/>
    /// (<c>Gen.Carrier</c>) onto its entity on every node: the <see cref="PassengerBuffer"/>, the <see cref="CarrierSeats"/> and, for an open
    /// type, the <see cref="OpenPassengerCabin"/> tag. ⭐ A type without the descriptor gets none of them — it carries nobody (before CE-2151
    /// every brained entity, soldiers included, got an 8-place buffer from <c>BehaviorTkbTranslator</c>).
    /// </summary>
    public sealed class CarrierTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(CarrierDefDto);
        }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(PassengerBuffer);
            yield return typeof(CarrierSeats);
            yield return typeof(OpenPassengerCabin);
            yield return typeof(PersonCarrier);   // ⭐ CE-2152 — only on a type with a Person block
            yield return typeof(CarrierStations);   // ⭐ Weapon systems WS-1 — only on a type that declares a Station seat
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<CarrierDefDto>();
            if (dto == null) return;

            if (dto.Seats.Count > PassengerBuffer.Capacity)
                throw new InvalidOperationException(
                    $"TKB type '{template.Name}' declares {dto.Seats.Count} seats in Gen.Carrier; the maximum is {PassengerBuffer.Capacity} " +
                    "(the passenger list and its wire topic carry 8). Declare fewer seats.");

            if (repo.IsComponentTypeRegistered<PassengerBuffer>() && !repo.HasComponent<PassengerBuffer>(entity))
                repo.AddComponent(entity, new PassengerBuffer());

            // ⭐ The seats are a pure TYPE fact: always (re)written from the TKB — never trusted from a saved scenario, whose auto-serialiser
            //   skips the role array ([ScenarioIgnore]) and would otherwise leave every seat a passenger seat.
            if (repo.IsComponentTypeRegistered<CarrierSeats>())
            {
                if (repo.HasComponent<CarrierSeats>(entity)) repo.SetComponent(entity, Seats(dto));
                else repo.AddComponent(entity, Seats(dto));
            }

            // ⭐ CE-2152 (R-2011 C1) — a PERSON carrier's factors and offsets: a type fact, always (re)written like the seats.
            if (dto.Person != null && repo.IsComponentTypeRegistered<PersonCarrier>())
            {
                var person = new PersonCarrier
                {
                    CarrySpeedFactor = dto.Person.CarrySpeedFactor, DragSpeedFactor = dto.Person.DragSpeedFactor,
                    CarryBehindMetres = dto.Person.CarryBehindMetres, DragBehindMetres = dto.Person.DragBehindMetres,
                };
                if (repo.HasComponent<PersonCarrier>(entity)) repo.SetComponent(entity, person);
                else repo.AddComponent(entity, person);
            }

            // ⭐ Weapon systems WS-1 (R-2016) — a host's STATION seats: a type fact, always (re)written like the seats.
            if (repo.IsComponentTypeRegistered<CarrierStations>() && Stations(dto) is { } stations)
            {
                if (repo.HasComponent<CarrierStations>(entity)) repo.SetComponent(entity, stations);
                else repo.AddComponent(entity, stations);
            }

            if (dto.Exposure == PassengerExposure.Open
                && repo.IsComponentTypeRegistered<OpenPassengerCabin>()
                && !repo.HasComponent<OpenPassengerCabin>(entity))
                repo.AddComponent(entity, new OpenPassengerCabin());
        }

        /// <summary>⭐ WS-1 — the station seats a descriptor declares, or null when it declares none (the component is then absent).</summary>
        public static CarrierStations? Stations(CarrierDefDto dto)
        {
            var stations = new CarrierStations();
            const float deg = MathF.PI / 180f;
            for (int i = 0; i < Math.Min(dto.Seats.Count, PassengerBuffer.Capacity); i++)
            {
                var seat = dto.Seats[i];
                if (seat.Role != SeatRole.Station) continue;
                stations.StationMask |= (byte)(1 << i);
                bool arc = seat.TraverseMinDeg.HasValue || seat.TraverseMaxDeg.HasValue || seat.ElevationMinDeg.HasValue || seat.ElevationMaxDeg.HasValue;
                stations.Seats[i] = new StationSeat
                {
                    Accepts = (byte)seat.Accepts,
                    OperatedBy = (sbyte)Math.Clamp(seat.OperatedBy, -1, PassengerBuffer.Capacity - 1),
                    NeedsGunner = (byte)(seat.NeedsGunner ? 1 : 0),
                    HasArc = (byte)(arc ? 1 : 0),
                    TraverseMin = (seat.TraverseMinDeg ?? -180f) * deg, TraverseMax = (seat.TraverseMaxDeg ?? 180f) * deg,
                    ElevationMin = (seat.ElevationMinDeg ?? -90f) * deg, ElevationMax = (seat.ElevationMaxDeg ?? 90f) * deg,
                    X = seat.X, Y = seat.Y, Z = seat.Z, Facing = seat.FacingDegrees * deg,
                };
            }
            return stations.StationMask == 0 ? null : stations;
        }

        /// <summary>The component a descriptor projects to.</summary>
        public static CarrierSeats Seats(CarrierDefDto dto)
        {
            var seats = new CarrierSeats
            {
                SeatCount = (byte)Math.Min(dto.Seats.Count, PassengerBuffer.Capacity),
                PassengersSurvive = (byte)(dto.DiesWithCarrier ? 0 : 1),   // ⭐ CE-2152 (R-2011 C9)
            };
            for (int i = 0; i < seats.SeatCount; i++)
            {
                seats.Roles[i] = dto.Seats[i].Role;
                bool exposed = dto.Seats[i].Exposed ?? dto.Exposure == PassengerExposure.Open;
                if (exposed) seats.ExposedMask |= (byte)(1 << i);
            }
            return seats;
        }
    }
}
