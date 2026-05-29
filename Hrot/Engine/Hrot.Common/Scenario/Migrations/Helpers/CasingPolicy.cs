namespace Hrot.Common.Scenario.Migrations.Helpers
{
    /// <summary>
    /// Controls how casing is handled when adding or renaming fields in entity
    /// components. The Entities payload has mixed casing (D1 in conversation
    /// history); migrators specify the policy explicitly when ambiguous.
    /// </summary>
    public enum CasingPolicy
    {
        /// <summary>
        /// Default: when adding a new field, match the casing of existing fields
        /// in the same component. When renaming, use the same casing as the
        /// old field. If the component has no existing fields, defaults to
        /// PascalCase (matching FdpAutoSerializer convention).
        /// </summary>
        MatchExisting,

        /// <summary>Force PascalCase regardless of existing fields.</summary>
        ForcePascal,

        /// <summary>Force camelCase regardless of existing fields.</summary>
        ForceCamel
    }
}
