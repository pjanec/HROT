using System;

namespace Fbt.Kernel
{
    /// <summary>
    /// Marks a static method as a shared AI condition usable from both BTree and HSM behaviors.
    /// Signature: static bool MethodName(ref TValue dto, Entity self, EntityRepository repo)
    /// TValue must be the type of the field <see cref="FieldName"/> on <see cref="DtoType"/>.
    /// The source generator computes the byte offset of that field within the parent DTO via
    /// Roslyn's semantic model and emits adapters keyed as "{MethodName}@{computedOffset}".
    /// Apply multiple times on the same method to share it across different parent DTOs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class SharedAiConditionAttribute : Attribute
    {
        /// <summary>The parent DTO struct that contains the projected field.</summary>
        public Type DtoType { get; }

        /// <summary>Name of the field within <see cref="DtoType"/> that TValue is projected from.</summary>
        public string FieldName { get; }

        public SharedAiConditionAttribute(Type dtoType, string fieldName)
        {
            DtoType   = dtoType;
            FieldName = fieldName;
        }
    }

    /// <summary>
    /// Marks a static method as a shared AI action usable from both BTree and HSM behaviors.
    /// Signature: static NodeStatus MethodName(ref TValue dto, Entity self, EntityRepository repo)
    /// HSM adapter discards the NodeStatus return (HSM is event-driven, not polling).
    /// Apply multiple times on the same method to share it across different parent DTOs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class SharedAiActionAttribute : Attribute
    {
        /// <summary>The parent DTO struct that contains the projected field.</summary>
        public Type DtoType { get; }

        /// <summary>Name of the field within <see cref="DtoType"/> that TValue is projected from.</summary>
        public string FieldName { get; }

        public SharedAiActionAttribute(Type dtoType, string fieldName)
        {
            DtoType   = dtoType;
            FieldName = fieldName;
        }
    }

    // ⛔⛔⛔ CE-327 (2026-09-23) — `SharedAiHeavyActionAttribute` and
    //   `SharedAiHeavyConditionAttribute` ARE DELETED. 📄 DESIGN_Occurrence_Scoped_Storage.md §30.29.
    //
    //   They gave an action access to a second, "heavy" ECS component. TWO arms, retired for TWO
    //   different reasons — and the one-line verdict "heavy storage died" only covered the first:
    //     ① 5-arg UNMANAGED — emitted GetComponentRW + Unsafe.As over Blackboard1024's bytes.
    //        Superseded by the occurrence tier ladder, and its container is deleted (P4).
    //     ② 3-arg MANAGED — emitted a plain GetComponent<TClass>. ⚠ NOT replaced by the ladder: an
    //        occurrence slot is unmanaged byte storage and cannot hold a managed class.
    //
    //   ⭐ What retired ② is the CONTRACT above, not a usage count: SharedAiAction already passes
    //     `Entity self, EntityRepository repo`, so the managed arm saved exactly one line —
    //       var heavy = repo.GetComponent<TClass>(self);
    //     — and granted no reach the plain attribute lacks. ⇒ deleting it takes NO capability.
    //
    //   📐 Adoption at deletion, measured with the graph: ONE decorated method in the whole repo,
    //     and it was the schema exporter's own test fixture. Production adoption: zero.
    //   ⇒ an action needing more room declares a wider params/working region and lands on a larger
    //     tier; an action needing a managed component asks `repo` for it, in one line.

    /// <summary>
    /// Annotates a BTree action or HSM action method to declare that it writes to an actuator
    /// channel. The source generator uses this to emit failure-cleanup wrappers (BTree) and
    /// exit-cleanup thunks (HSM), plus a channel-safety registry.
    /// AllowMultiple = true so a method that writes to several channels can be annotated once
    /// per channel.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class WritesChannelAttribute : Attribute
    {
        public ChannelKind Channel { get; }

        public WritesChannelAttribute(ChannelKind channel)
        {
            Channel = channel;
        }
    }

    /// <summary>
    /// Identifies an actuator channel component.  Must live in Fbt.Kernel so that both
    /// Fbt.SourceGen and Fhsm.SourceGen can reference it by fully qualified name.
    /// </summary>
    public enum ChannelKind
    {
        Locomotion,
        Weapon,
        Interaction,
    }
}
