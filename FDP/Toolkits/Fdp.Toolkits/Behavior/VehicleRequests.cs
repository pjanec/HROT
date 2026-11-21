using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>⭐ <c>CE-4670</c> (VA-4) — what a crewman does with his hatch.</summary>
    public enum OpeningVerb : byte
    {
        /// <summary>Open the hatch serving his seat and ride exposed (observing, an order, the SOP).</summary>
        HeadOut = 0,
        /// <summary>Go back in: the hatch closes.</summary>
        HeadIn = 1,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (VA-4; 📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §6a) — "<see cref="Man"/> puts his head out of / back into
    /// <see cref="Vehicle"/>", raised on the man's brain's node (a behaviour, an order, a possessed crewman). The BOARDING ARBITER applies it — the vehicle's
    /// passenger-list owner (<c>VehicleOpeningsSystem</c>); on any other node the interaction transport carries it there (kind <c>OpeningRequest</c>, republished
    /// with <see cref="IsRemote"/>). The result reaches every node as the replicated <see cref="VehicleOpenings"/> — no reply.
    /// </summary>
    [EventId(CombatConstants.OpeningRequestEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct OpeningRequestEvent
    {
        public Entity Vehicle;
        /// <summary>The crewman — he must sit in <see cref="Vehicle"/>; the hatches serving his seat's role are his.</summary>
        public Entity Man;
        public OpeningVerb Verb;
        /// <summary>True when this event came off the network — its egress must not send it again.</summary>
        public bool IsRemote;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (VA-3; §6b) — a station's light switch: "set <see cref="Vehicle"/>'s light discipline to <see cref="Discipline"/>", raised by a possessed
    /// vehicle's station (or an order's executor) on its node. The LIGHTS OWNER applies it (<c>LightRuleSystem</c>, the <c>dtVehicleLights</c> owner) — the rule
    /// then lights what the discipline allows; elsewhere the interaction transport carries it there (kind <c>LightsRequest</c>). No reply: the replicated
    /// <see cref="VehicleLights"/> is the answer.
    /// </summary>
    [EventId(CombatConstants.LightsRequestEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct LightsRequestEvent
    {
        public Entity Vehicle;
        /// <summary>Who switched (the possessing operator's crewman, an order's executor); <see cref="Entity.Null"/> for an operator.</summary>
        public Entity Actor;
        public LightDiscipline Discipline;
        /// <summary>True when this event came off the network — its egress must not send it again.</summary>
        public bool IsRemote;
    }

    /// <summary>⭐ <c>CE-4670</c> — the one way to ASK for a hatch or a light switch (the transport carries a request to the owner; in one world the owner's
    /// system applies it next frame).</summary>
    public static class VehicleRequests
    {
        /// <summary>Ask the vehicle's boarding arbiter to put <paramref name="man"/>'s head out (or back in). False when the event is not registered.</summary>
        public static bool HeadOut(EntityRepository world, Entity vehicle, Entity man, bool @out)
        {
            if (!world.Bus.IsRegistered<OpeningRequestEvent>()) return false;
            world.Bus.Publish(new OpeningRequestEvent { Vehicle = vehicle, Man = man, Verb = @out ? OpeningVerb.HeadOut : OpeningVerb.HeadIn });
            return true;
        }

        /// <summary>Ask the vehicle's lights owner to switch to <paramref name="discipline"/>. False when the event is not registered.</summary>
        public static bool SwitchLights(EntityRepository world, Entity vehicle, LightDiscipline discipline, Entity actor = default)
        {
            if (!world.Bus.IsRegistered<LightsRequestEvent>()) return false;
            world.Bus.Publish(new LightsRequestEvent { Vehicle = vehicle, Actor = actor, Discipline = discipline });
            return true;
        }
    }
}
