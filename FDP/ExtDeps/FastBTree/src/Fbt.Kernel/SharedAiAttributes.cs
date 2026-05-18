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

    /// <summary>
    /// Marks a static method as a shared AI action (or condition) that also requires access to a
    /// second, "heavy" ECS component alongside the standard minimal blackboard projection.
    /// <para>
    /// For an <b>unmanaged</b> heavy component (e.g., <see cref="!:Blackboard1024"/>), supply all
    /// five arguments: the generator emits a <c>GetComponentRW</c> call followed by an
    /// <c>Unsafe.As</c> cast into <typeparamref name="HeavyDtoType"/> before invoking the method.
    /// </para>
    /// <para>
    /// For a <b>managed</b> heavy component, use the three-argument overload; the generator
    /// detects that <c>HeavyComponentType</c> is a reference type and emits a plain
    /// <c>GetComponent&lt;T&gt;</c> call, passing the instance directly — no field-offset
    /// projection is needed.
    /// </para>
    /// Method signature (unmanaged):
    ///   <c>static NodeStatus M(ref TMinimal minimal, ref THeavyDto heavy, Entity self, EntityRepository repo)</c>
    /// Method signature (managed):
    ///   <c>static NodeStatus M(ref TMinimal minimal, THeavyClass heavy, Entity self, EntityRepository repo)</c>
    /// Apply multiple times on the same method to share it across different parent DTOs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class SharedAiHeavyActionAttribute : Attribute
    {
        /// <summary>The parent DTO struct that contains the projected minimal field.</summary>
        public Type DtoType { get; }

        /// <summary>Name of the field within <see cref="DtoType"/> projected from <see cref="BrainBlackboard"/>.</summary>
        public string FieldName { get; }

        /// <summary>
        /// The heavy ECS component type to fetch from the entity.
        /// For an unmanaged component (struct) this is the container (e.g., <c>Blackboard1024</c>).
        /// For a managed component (class) this is the DTO class itself.
        /// </summary>
        public Type HeavyComponentType { get; }

        /// <summary>
        /// Name of the field inside <see cref="HeavyComponentType"/> whose bytes are projected
        /// as <see cref="HeavyDtoType"/>.  Only required for unmanaged components.
        /// <c>null</c> when <see cref="HeavyComponentType"/> is a managed class.
        /// </summary>
        public string? HeavyFieldName { get; }

        /// <summary>
        /// The concrete DTO type projected from <see cref="HeavyFieldName"/> via <c>Unsafe.As</c>.
        /// Only required for unmanaged components.
        /// <c>null</c> when <see cref="HeavyComponentType"/> is a managed class (the component IS the DTO).
        /// </summary>
        public Type? HeavyDtoType { get; }

        /// <summary>Three-argument constructor for managed heavy components.</summary>
        public SharedAiHeavyActionAttribute(Type dtoType, string fieldName, Type heavyComponentType)
        {
            DtoType            = dtoType;
            FieldName          = fieldName;
            HeavyComponentType = heavyComponentType;
            HeavyFieldName     = null;
            HeavyDtoType       = null;
        }

        /// <summary>Five-argument constructor for unmanaged heavy components.</summary>
        public SharedAiHeavyActionAttribute(
            Type dtoType,
            string fieldName,
            Type heavyComponentType,
            string heavyFieldName,
            Type heavyDtoType)
        {
            DtoType            = dtoType;
            FieldName          = fieldName;
            HeavyComponentType = heavyComponentType;
            HeavyFieldName     = heavyFieldName;
            HeavyDtoType       = heavyDtoType;
        }
    }

    /// <summary>
    /// Marks a static method as a shared AI condition that also requires access to a second,
    /// "heavy" ECS component alongside the standard minimal blackboard projection.
    /// <para>
    /// For an <b>unmanaged</b> heavy component (e.g., <see cref="!:Blackboard1024"/>), supply
    /// all five arguments: the generator emits a <c>GetComponentRW</c> call followed by an
    /// <c>Unsafe.As</c> cast into <typeparamref name="HeavyDtoType"/> before invoking the method.
    /// </para>
    /// <para>
    /// For a <b>managed</b> heavy component, supply only four arguments (omit
    /// <paramref name="heavyFieldName"/>); the generator detects that
    /// <paramref name="heavyComponentType"/> is a reference type and emits a plain
    /// <c>GetComponent&lt;T&gt;</c> call, passing the instance directly.
    /// </para>
    /// Method signature (unmanaged heavy):
    ///   <c>static bool M(ref TMinimal minimal, ref THeavyDto heavy, Entity self, EntityRepository repo)</c>
    /// Method signature (managed heavy):
    ///   <c>static bool M(ref TMinimal minimal, THeavyClass heavy, Entity self, EntityRepository repo)</c>
    /// Apply multiple times on the same method to share it across different parent DTOs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class SharedAiHeavyConditionAttribute : Attribute
    {
        /// <summary>The parent DTO struct that contains the projected minimal field.</summary>
        public Type DtoType { get; }

        /// <summary>Name of the field within <see cref="DtoType"/> projected from <see cref="BrainBlackboard"/>.</summary>
        public string FieldName { get; }

        /// <summary>
        /// The heavy ECS component type to fetch from the entity.
        /// For an unmanaged component (struct) this is the container (e.g., <c>Blackboard1024</c>).
        /// For a managed component (class) this is the DTO class itself.
        /// </summary>
        public Type HeavyComponentType { get; }

        /// <summary>
        /// The concrete DTO type projected from the heavy component via <c>Unsafe.As</c> (unmanaged),
        /// or the component type itself (managed).
        /// </summary>
        public Type HeavyDtoType { get; }

        /// <summary>
        /// Name of the field inside <see cref="HeavyComponentType"/> whose bytes are projected
        /// as <see cref="HeavyDtoType"/>.  Supply this for unmanaged components.
        /// Omit (or pass <c>null</c>) for managed components — no field-offset projection is needed.
        /// </summary>
        public string? HeavyFieldName { get; }

        /// <summary>
        /// Unified constructor.  Omit <paramref name="heavyFieldName"/> for managed heavy
        /// components; supply it for unmanaged (struct) heavy components.
        /// </summary>
        public SharedAiHeavyConditionAttribute(
            Type dtoType,
            string fieldName,
            Type heavyComponentType,
            Type heavyDtoType,
            string? heavyFieldName = null)
        {
            DtoType            = dtoType;
            FieldName          = fieldName;
            HeavyComponentType = heavyComponentType;
            HeavyDtoType       = heavyDtoType;
            HeavyFieldName     = heavyFieldName;
        }
    }

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
