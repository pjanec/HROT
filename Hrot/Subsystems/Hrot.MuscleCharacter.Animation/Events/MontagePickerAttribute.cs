using System;

namespace Hrot.MuscleCharacter.Animation.Events
{
    /// <summary>
    /// Marks an int field as a montage asset ID.
    /// The Blueprint property drawer renders it as a montage-name dropdown populated
    /// by IAnimationTkbQueries.GetPlayableMontages for the Blueprint's current target
    /// entity class. The designer picks "Reload_Rifle"; the drawer resolves to the
    /// FNV-1a hash (DD-4 §3.1) at compile time and stores the literal int hash in the
    /// lowered code.
    /// Defined here alongside the event types that use it; the DD-5 Blueprint
    /// authoring primitives also reference this attribute for PlayMontageNode fields.
    /// (DD-3 §3.4, DD-5 §7)
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class MontagePickerAttribute : Attribute
    {
    }
}
