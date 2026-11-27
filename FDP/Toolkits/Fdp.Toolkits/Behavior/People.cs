using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ Capability components C2 (docs/DESIGN_Capability_Components.md §3, §4.2, CC-6) — THE question "is this a PERSON BODY?": a body with
    /// stances (it walks, crouches, is stressed, reacts) that is not an animal. ⛔ Never ask "has a brain" for it — a brain runs a behaviour, and a
    /// vehicle, an animal or a device has one too. No new component: <c>StanceIntent</c> is the capability that already exists (the de-facto test of
    /// <c>StanceRequest.CanCarry</c> and the stress query); 📐 measured: a FREEZING animal carries it too (<c>AnimalTkbTranslator.cs:77-78</c>), hence the
    /// exclusion (DESIGN_Animals AR-1's lesson).
    /// </summary>
    public static class People
    {
        public static bool IsPersonBody(EntityRepository w, Entity e)
            => !e.IsNull && w.IsAlive(e)
               && w.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>()
               && w.HasComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(e)
               && !(w.IsComponentTypeRegistered<Fdp.Toolkit.Animals.AnimalState>() && w.HasComponent<Fdp.Toolkit.Animals.AnimalState>(e));
    }
}
