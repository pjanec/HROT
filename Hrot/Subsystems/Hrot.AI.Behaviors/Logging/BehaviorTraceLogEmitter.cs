using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.AI.Behaviors.Logging
{
    /// <summary>
    /// Concrete <see cref="IBehaviorTraceLogEmitter"/> that delegates to
    /// <see cref="BehaviorLog.Trace(Entity, EntityRepository, string, string)"/>.
    /// Registered at the composition root via
    /// <c>BehaviorTraceLog.Instance = new BehaviorTraceLogEmitter();</c>.
    /// </summary>
    public sealed class BehaviorTraceLogEmitter : IBehaviorTraceLogEmitter
    {
        public bool IsTraceEnabled => BehaviorLog.IsTraceEnabled;

        public void EmitTrace(Entity entity, EntityRepository repo, string message, string actionName)
        {
            BehaviorLog.Trace(entity, repo, message, actionName);
        }
    }
}
