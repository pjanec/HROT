namespace FDP.Toolkit.Replication.Patching;

/// <summary>
/// Installer interface for the <see cref="BinaryInterpreter{TRecord}"/> domain-hook extension
/// point.  Implement this interface to register typed attribute handlers and
/// scratchpad-based deferred compute handlers for a specific ECS subsystem.
///
/// <para>
/// Register an installer via <see cref="BinaryInterpreterBuilder{TRecord}.AddInstaller"/>.
/// The installer is called once at startup during interpreter construction; all
/// registration is amortised at build time and costs nothing on the hot path.
/// </para>
/// </summary>
/// <typeparam name="TRecord">
/// The application-layer attribute record type (e.g. <c>Hrot.NED.Messages.AttributeRecord</c>).
/// Must be an unmanaged struct.
/// </typeparam>
/// <example>
/// <code>
/// public sealed class MyInstaller : IBinaryAttributeInstaller&lt;MyRecord&gt;
/// {
///     public void Install(BinaryInterpreterBuilder&lt;MyRecord&gt; builder)
///     {
///         int offset = builder.ReserveScratchpad(Unsafe.SizeOf&lt;MyScratchpad&gt;());
///         builder.RegisterHandler(AttributeIds.Name, (ctx, rec) => { ... });
///         builder.RegisterSubsystemFlusher(0, ctx => { ... });
///     }
/// }
/// </code>
/// </example>
public interface IBinaryAttributeInstaller<TRecord> where TRecord : struct
{
    /// <summary>
    /// Called once by <see cref="BinaryInterpreterBuilder{TRecord}.AddInstaller"/> to allow the
    /// installer to register its handlers and claim a scratchpad offset.
    /// </summary>
    /// <param name="builder">The interpreter builder, ready to accept registrations.</param>
    void Install(BinaryInterpreterBuilder<TRecord> builder);
}
