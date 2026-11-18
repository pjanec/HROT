namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ CE-3468 — what THIS COPY of the AI assembly does when a host loads it: the per-copy statics brain code needs before the first tick.
    /// <list type="bullet">
    /// <item>the order-kind table (<see cref="Orders.OrderKinds.Build"/>, R-2089 — never first-built mid-tick);</item>
    /// <item>the brains' phase contribution (<see cref="AiBrainReads.Contribute"/>) — the cross reads and the rules' tree prepare, under ONE key, so a
    /// reloaded copy REPLACES the previous copy's entry.</item>
    /// </list>
    /// ⭐ ONE hook for both loaders: <c>CgfBehaviorSetup.LoadFromAiAssembly</c> calls it on the copy it scanned; the editor's hot-reload coordinator calls
    /// it (by name — <see cref="MethodName"/> on <see cref="TypeName"/>) on every copy it loads into a collectible context.
    /// 📐 Measured (the archery contest, editor, parallel phase): the editor runs the behaviours from its OWN loaded copy, whose <c>RuleTree</c> statics
    /// were never prepared (the contribution came from the default copy) ⇒ inside the phase the tree was empty, a <c>Points</c> root summed nothing.
    /// </summary>
    public static class AiAssemblyLoad
    {
        /// <summary>The type the hot-reload coordinator looks up in a loaded copy.</summary>
        public const string TypeName = "Hrot.AI.Behaviors.Brains.AiAssemblyLoad";

        /// <summary>The method it invokes (static, no arguments).</summary>
        public const string MethodName = nameof(OnLoad);

        /// <summary>Runs this copy's load-time statics (idempotent).</summary>
        public static void OnLoad()
        {
            Orders.OrderKinds.Build();
            AiBrainReads.Contribute();
        }
    }
}
