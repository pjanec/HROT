namespace FDP.Toolkit.Replication.Patching;

/// <summary>
/// Installer interface for the <see cref="BinaryInterpreter"/> domain-hook extension
/// point.  Implement this interface to register typed attribute handlers and
/// scratchpad-based deferred compute handlers for a specific ECS subsystem.
///
/// <para>
/// Register an installer via <see cref="BinaryInterpreterBuilder.AddInstaller"/>.
/// The installer is called once at startup during interpreter construction; all
/// registration is amortised at build time and costs nothing on the hot path.
/// </para>
/// </summary>
/// <example>
/// <code>
/// public sealed class MyInstaller : IBinaryAttributeInstaller
/// {
///     public void Install(BinaryInterpreterBuilder builder)
///     {
///         int offset = builder.ReserveScratchpad(Unsafe.SizeOf&lt;MyScratchpad&gt;());
///         builder.RegisterHandler(AttributeIds.Name, (ctx, rec) => { ... });
///         builder.RegisterSubsystemFlusher(0, ctx => { ... });
///     }
/// }
/// </code>
/// </example>
public interface IBinaryAttributeInstaller
{
    /// <summary>
    /// Called once by <see cref="BinaryInterpreterBuilder.AddInstaller"/> to allow the
    /// installer to register its handlers and claim a scratchpad offset.
    /// </summary>
    /// <param name="builder">The interpreter builder, ready to accept registrations.</param>
    void Install(BinaryInterpreterBuilder builder);
}
