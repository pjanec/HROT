namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// What <see cref="ScenarioSerializer"/> does when a scenario names a component that is not
    /// in the current <c>ComponentTypeRegistry</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because the two reasonable answers belong to different hosts, and picking one
    /// globally is wrong either way.
    /// </para>
    /// <para>
    /// The strict answer is the original and it is not an accident: an unresolvable key is how a
    /// typo or a version skew announces itself, and silently dropping the data hides both. That
    /// is what <c>ScenarioSerializerTests.Deserialize_UnknownComponentKey_Throws</c> pins.
    /// </para>
    /// <para>
    /// The lenient answer is what a live cluster needs. Measured 2026-09-28: P4 retired
    /// <c>BrainBlackboard</c> without migrating the scenario corpus, and every scenario still
    /// carrying the key became unloadable — the 2PC PrepareLive faulted and the commit was
    /// skipped, so an exercise could not start at all. A component the engine no longer
    /// understands is not a reason to refuse to run; skipping it costs one entity one component,
    /// and refusing costs the whole session.
    /// </para>
    /// <para>
    /// ⚠ Leniency is a SAFETY NET, not the mechanism for planned retirements. A known schema
    /// change is the migration chain's job — see <c>Migration-system.md</c> and, for this exact
    /// case, <c>V2ToV3_RemoveBrainBlackboard</c>. If a retirement relies on this policy instead
    /// of shipping a migrator, the data is dropped with no record of what it was.
    /// </para>
    /// </remarks>
    public enum UnknownComponentPolicy
    {
        /// <summary>
        /// Throw an <see cref="System.InvalidOperationException"/> naming the component. The
        /// default, and the right choice for the editor, CLI tooling and CI, where a typo or a
        /// skew should fail loudly and close to the edit that caused it.
        /// </summary>
        Throw = 0,

        /// <summary>
        /// Skip the component, keep the rest of the entity, and log a warning naming it. The
        /// right choice for a live cluster load, where refusing the whole scenario is a worse
        /// outcome than loading it one component short.
        /// </summary>
        WarnAndSkip = 1,
    }
}
