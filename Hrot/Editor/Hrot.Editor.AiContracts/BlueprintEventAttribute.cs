namespace Hrot.Editor.AiShared;

/// <summary>
/// Marks a <b>blittable (unmanaged) struct</b> as a Blueprint custom event — publishable via a
/// <c>PublishEvent</c> node and subscribable via a named-event <c>EventEntry</c> node. The editor
/// reflection-scans loaded assemblies for this attribute and lists the event in the curated, grouped
/// picker under <see cref="Category"/> (designers pick it; they never type an FQN).
///
/// <para>
/// <b>Editor-only discovery metadata (architect Q#14, mirrors <see cref="BlueprintCallableAttribute"/>).</b>
/// The compiler never reads this attribute — the editor reflects the struct's public fields and bakes the
/// <c>EventTypeFqn</c> + per-field <c>(name, TypeId)</c> strings onto the node, so the netstandard2.0
/// generator never needs to load game assemblies. This is the C# hand-authored path (2a); designer-authored
/// events (2b) are defined in the editor and carry the same shape without a C# type.
/// </para>
///
/// <para>
/// Fields must be blittable (§7.3): primitives, enums, and blittable structs such as
/// <c>FixedString32/64</c> — <b>no managed/reference fields</b> (they would force the managed bus stream and
/// break zero-alloc / AAR-replay / net-replication invariants). At most one field may be marked
/// <see cref="EventTargetAttribute"/> to designate the recipient entity for the <c>Self</c>/<c>Any</c> filter.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
public sealed class BlueprintEventAttribute : Attribute
{
    /// <summary>Mandatory picker group (curation knob), e.g. "Combat", "Perception". Sub-groups via '/'.</summary>
    public string Category { get; }

    /// <summary>Optional display label in the picker; defaults to the struct name.</summary>
    public string? DisplayName { get; set; }

    public BlueprintEventAttribute(string category)
    {
        Category = category;
    }
}

/// <summary>
/// Marks the single <c>Fdp.Core.Entity</c> field of a <see cref="BlueprintEventAttribute"/> event that names
/// the recipient entity. Drives the named-event entry node's <c>Self</c>/<c>Any</c> recipient filter: with
/// <c>Self</c>, the dispatch pump delivers only when this field equals the subscribing entity. Optional — an
/// event with no target field is broadcast-only.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class EventTargetAttribute : Attribute
{
}
