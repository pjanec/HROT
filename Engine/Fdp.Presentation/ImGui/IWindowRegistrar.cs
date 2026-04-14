using Fdp.Presentation.WindowManager;

namespace Fdp.Toolkit.Runner
{
    /// <summary>
    /// Optional interface for <see cref="ISubsystem"/> implementations that want
    /// to register <see cref="ManagedWindow"/> instances, global menu items, or
    /// status-bar sections with the application <see cref="WindowManager"/> during startup.
    ///
    /// <para>The <see cref="SubsystemOrchestrator"/> calls
    /// <see cref="RegisterWindows"/> immediately after
    /// <see cref="ISubsystem.Initialize"/> for every subsystem that implements
    /// this interface.  The call is omitted in headless mode.</para>
    ///
    /// <para>This interface intentionally lives outside <see cref="SubsystemConfig"/>
    /// because <see cref="SubsystemConfig"/> is a plain data-transfer object and must
    /// not carry system-level service references.</para>
    /// </summary>
    public interface IWindowRegistrar
    {
        /// <summary>
        /// Register panels, menu items, and status-bar sections with the
        /// <paramref name="windowManager"/>.
        /// </summary>
        /// <param name="windowManager">The application window manager.</param>
        void RegisterWindows(WindowManager windowManager);
    }
}
