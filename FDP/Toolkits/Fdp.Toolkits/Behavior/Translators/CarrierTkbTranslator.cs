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

            if (dto.Exposure == PassengerExposure.Open
                && repo.IsComponentTypeRegistered<OpenPassengerCabin>()
                && !repo.HasComponent<OpenPassengerCabin>(entity))
                repo.AddComponent(entity, new OpenPassengerCabin());
        }

        /// <summary>The component a descriptor projects to.</summary>
        public static CarrierSeats Seats(CarrierDefDto dto)
        {
            var seats = new CarrierSeats { SeatCount = (byte)Math.Min(dto.Seats.Count, PassengerBuffer.Capacity) };
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
