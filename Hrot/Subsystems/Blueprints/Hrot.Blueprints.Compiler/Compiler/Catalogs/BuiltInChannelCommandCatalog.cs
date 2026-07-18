namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed class BuiltInChannelCommandCatalog : IChannelCommandCatalog
{
    public static readonly BuiltInChannelCommandCatalog Instance = new();

    // Action names are short unqualified strings (e.g. "MoveTo", "AimAndFire") rather than
    // the hierarchical paths in the design doc (e.g. "Locomotion/MoveTo", "Weapon/AimAndFire").
    // This is intentional: the short names are the authoritative ActionId strings stored in
    // Blueprint JSON assets and matched by the runtime validator. Changing to hierarchical paths
    // would require a coordinated migration of all authored assets. (DEBT-023)
    // Baked ParamFields (Blocker-1, ChannelCommand enricher): one entry per decomposable public
    // field of the real executor-param struct, in DECLARATION order — mirrors exactly what
    // NodePinSchema.ReflectDataMembers reflects when the game assembly IS loaded (net8 editor
    // host). Stage0 (netstandard2.0 generator) cannot load Fdp.Toolkits to reflect, so these are
    // hand-transcribed from the struct source (same reason EngineEventCatalogEntry.PayloadFields
    // is baked for PublishEvent — see EnrichChannelCommandPins in Stage0_Rehydrate). Round-out: the
    // FULL field list is baked for every entry (not just the fields a given asset happens to wire)
    // so any future asset using these actions round-trips pin-less without another baking pass.
    private static readonly ParamField[] MoveToFields =
    {
        new("Destination",    "System.Numerics.Vector3"),
        new("ArrivalRadius",  "System.Single"),
        new("Speed",          "System.Single"),
        new("RouteHandle",    "System.Int32"),
        new("LayerMask",      "System.UInt32"),
        new("ReverseAllowed", "System.Byte"),
        new("Flags",          "System.Byte"),
        new("MaxReplans",     "System.Byte"),
        new("BackendForce",   "System.Byte"),
    };

    private static readonly ParamField[] FollowRouteFields =
    {
        new("TrajectoryId", "System.Int32"),
        new("IsLooped",     "System.Byte"),
    };

    private static readonly ParamField[] AimAndFireFields =
    {
        new("Target",          "Fdp.Core.Entity"),
        new("CooldownSeconds", "System.Single"),
    };

    private static readonly ParamField[] OpenDoorFields =
    {
        new("TargetDoor", "Fdp.Core.Entity"),
    };

    // EjectPassengers has no executor-param struct (see comment on the entry below): the
    // NodePinSchema degradation path for a primitive ParamsTypeFqn projects one pin named after
    // the type's short name, typed as the params type itself.
    private static readonly ParamField[] EjectPassengersFields =
    {
        new("Int32", "System.Int32"),
    };

    private static readonly ParamField[] DemoEnumActionFields =
    {
        new("TargetPos", "System.Numerics.Vector3"),
        // Enum field: stamped "global::" per AN6 (NodePinSchema.EnumStampedTypeFqn), matching the
        // sentinel StaticTypeRegistry expects for a project/unmanaged enum type.
        new("Stance",    "global::Fdp.Toolkit.Behavior.Demo.DemoStance"),
        new("Repeat",    "System.Int32"),
    };

    public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() =>
        new List<ChannelCommandCatalogEntry>
        {
            // ParamsTypeFqn: real executor-param struct FQN so NodePinSchema projects rich per-field
            // data-IN pins in the editor (net8).  The netstandard2.0 generator host does not load
            // Fdp.Toolkits; NodePinSchema degrades gracefully to a single typed pin (BCF-D03).
            new("MoveTo",           "Fdp.Toolkit.Behavior.Components.LocomotionChannel",  1, "Fdp.Toolkit.Navigation.MoveToParams",        MoveToFields),
            new("FollowRoute",      "Fdp.Toolkit.Behavior.Components.LocomotionChannel",  3, "Fdp.Toolkit.Navigation.FollowRouteParams",   FollowRouteFields),
            new("AimAndFire",       "Fdp.Toolkit.Behavior.Components.WeaponChannel",      1, "Fdp.Toolkit.Combat.Executors.AimAndFireParams", AimAndFireFields),
            new("OpenDoor",         "Fdp.Toolkit.Behavior.Components.InteractionChannel", 4, "Fdp.Toolkit.Behavior.Executors.OpenDoorParams", OpenDoorFields),
            // EjectPassengers has no executor-param struct: the executor reads PassengerBuffer
            // directly from the entity.  Leave System.Int32 as a safe no-op placeholder so
            // NodePinSchema emits a single value pin (exec-only degradation path).
            new("EjectPassengers",  "Fdp.Toolkit.Behavior.Components.InteractionChannel", 3, "System.Int32", EjectPassengersFields),

            // ── DEMO — AN6 enum-pin editor live test (REMOVABLE) ──────────────────────────
            // ActionId 99 is intentionally unused on LocomotionChannel (no executor).
            // Purpose: palette surfaces this action; NodePinSchema projects a DemoStance data-IN
            // pin with TypeId "global::Fdp.Toolkit.Behavior.Demo.DemoStance" (AN6 stamping),
            // which the EnumSentinelPinEditorRegistry routes to EnumPinEditor for the combo.
            new("DemoEnumAction",   "Fdp.Toolkit.Behavior.Components.LocomotionChannel", 99, "Fdp.Toolkit.Behavior.Demo.DemoEnumActionParams", DemoEnumActionFields),
        };
}
