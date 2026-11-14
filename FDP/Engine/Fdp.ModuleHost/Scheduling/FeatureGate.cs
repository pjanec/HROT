namespace Fdp.ModuleHost
{
    /// <summary>
    /// ⭐ Node Features F-13 — a feature resolved ONCE to a dense index, so asking whether it is on is one array read.
    /// Obtained from <see cref="IFeatureGate.Resolve"/>; never constructed by feature code.
    /// 📄 docs/DESIGN_Node_Features.md §6.2.
    /// </summary>
    public readonly struct FeatureHandle
    {
        /// <summary>The dense index in the node's feature registry.</summary>
        public readonly int Index;

        public FeatureHandle(int index) => Index = index;
    }

    /// <summary>
    /// ⭐ Node Features F-13 — what the scheduler asks: is this feature on, now. Implemented by the node's <c>NodeFeatures</c>
    /// (Hrot.Core); lives here so the FDP-level scheduler never depends on Hrot.
    /// </summary>
    public interface IFeatureGate
    {
        /// <summary>True when the feature is on. One array read.</summary>
        bool On(FeatureHandle feature);

        /// <summary>Resolves a declared feature id to its handle. THROWS on an undeclared id (F-18).</summary>
        FeatureHandle Resolve(string id);
    }
}

namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// ⭐ Node Features F-13 — marks a system, or a module (inherited by the systems it registers), as owned by the feature <see cref="Id"/>.
    /// The <c>SystemScheduler</c> reads it once per system and SKIPS the system while the feature is off. An undeclared id throws at registration (F-18).
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true)]
    public sealed class NodeFeatureAttribute : System.Attribute
    {
        public string Id { get; }
        public NodeFeatureAttribute(string id) => Id = id;
    }
}
