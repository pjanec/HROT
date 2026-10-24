using System;
using System.Linq;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1309</c> (ui) — R-3036: the scheduler throws on the first frame for a system with no <c>[TickMode]</c>, and nothing caught it
    /// before a host ran. 📌 Measured `2026-10-24`: behaviors' <c>StressSystem</c> was registered in <c>CgfLogicPack</c> without one, and the
    /// editor crashed at once on every scenario. ⇒ every concrete system of the brain assemblies (Hrot.AI.Behaviors, Hrot.CGF) declares it.
    /// 🔴 Red-proof: drop <c>[TickMode]</c> from <c>StressSystem</c> ⇒ red, naming it.
    /// </summary>
    public sealed class BrainSystemsDeclareTickModeTests
    {
        [Fact]
        public void EveryBrainSystem_DeclaresItsTickMode()
        {
            var assemblies = new[] { typeof(Hrot.AI.Behaviors.Brains.StressSystem).Assembly, typeof(Hrot.CGF.CgfLogicPack).Assembly };
            var missing = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IEcsModuleSystem).IsAssignableFrom(t))
                .Where(t => { try { TickModes.OfSystem(t); return false; } catch (InvalidOperationException) { return true; } })
                .Select(t => t.FullName).ToList();
            Assert.True(missing.Count == 0, "no [TickMode]: " + string.Join(", ", missing));
        }
    }
}
