using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// Hit-point pool. <see cref="Current"/> &lt;= 0 means the entity is destroyed/defeated.
    /// </summary>
    /// <remarks>
    /// <b>BUG2-A001:</b> Moved from <c>FDP.Toolkit.Combat</c> into this thin
    /// <c>FDP.Toolkit.Combat.Contracts</c> assembly so that <c>FDP.Toolkit.Behavior</c>
    /// can reference it without creating a circular dependency with <c>FDP.Toolkit.Combat</c>.
    /// Both Combat and Behavior now reference Contracts; neither references the other
    /// directly.  The component ID and field layout are unchanged.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CombatHealth)]
    public struct Health
    {
        public float Current;
        public float Max;
    }
}
