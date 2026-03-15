using System.Reflection;
using Fdp.Kernel;
using FDP.Toolkit.Time.Controllers;
using ModuleHost.Core;

namespace ModuleHost.Core
{
    /// <summary>
    /// Local copy of the kernel test setup extension for use in Bagira.SimHost.Integration.Tests.
    /// Mirrors <c>ModuleHost.Core.Tests.ModuleHostKernelTestExtensions</c>.
    /// </summary>
    internal static class ModuleHostKernelTestExtensions
    {
        internal static void InitializeForTest(this ModuleHostKernel kernel)
        {
            var field = typeof(ModuleHostKernel).GetField(
                "_timeController", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.GetValue(kernel) == null)
            {
                var bus        = new FdpEventBus();
                var config     = new TimeControllerConfig { Role = TimeRole.Standalone };
                var controller = TimeControllerFactory.Create(bus, config);
                kernel.SetTimeController(controller);
            }

            kernel.Initialize();
        }
    }
}
