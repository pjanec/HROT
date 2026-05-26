using System;

namespace Hrot.MuscleCharacter.Animation.Events
{
    /// <summary>
    /// Marks a uint field as a stable hash of an animation marker name.
    /// The Blueprint property drawer renders it as a string dropdown populated by
    /// IAnimationTkbQueries.GetAvailableMarkers for the Blueprint's current target
    /// entity class. At compile time, the designer's picked name is hashed via the
    /// DD-4 §3.4 convention and stored as the literal uint hash in the lowered code.
    /// Mirrors the [HsmEventPicker] and [MapPickableEntity] picker attribute pattern.
    /// (DD-3 §3.3)
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class AnimMarkerPickerAttribute : Attribute
    {
    }
}
