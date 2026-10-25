using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Civilians;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>CE-2372</c> (AQ105 B/E, R-2080, behaviors) — what a man's squad calls remember: when he last called and what (the cool-down),
    /// and the facts whose CHANGE makes him call — reloading, the grenades he carries, the last contacts he called. Brain-local, never
    /// replicated or saved; kept by behaviors' <c>CalloutSystem</c>. 📄 docs/blueprints/Architect_Question_105_Squad_Callouts.md §4.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CalloutState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct CalloutState
    {
        public double LastAt;
        public UtteranceKind LastKind;
        public byte WasReloading;
        /// <summary>Grenades in his throwable mount at the last look (−1 = not looked yet).</summary>
        public int Grenades;
        /// <summary>The contacts he last called (newest first).</summary>
        public long Called0, Called1, Called2, Called3;
        public byte Seeded;

        public readonly bool Called(long id) => id == Called0 || id == Called1 || id == Called2 || id == Called3;
        public void Remember(long id) { Called3 = Called2; Called2 = Called1; Called1 = Called0; Called0 = id; }
    }
}
