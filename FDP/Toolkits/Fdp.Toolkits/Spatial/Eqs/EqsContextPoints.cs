using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-3272</c> (Q94 B, R-3018 — 📄 docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md §5.3) — up to
    /// <see cref="Capacity"/> points a sensor's owner hands the node that SOLVES it, slot by slot, carried on <c>EqsSensorConfig</c>
    /// (<c>ContextPoints</c>). The danger-area sensor's are its unit's remembered contacts (slot i = memory / pool slot i): the solver
    /// answers which of them see each area on ITS terrain, so the brain never reads geometry for it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.EqsContextPoints)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct EqsContextPoints
    {
        /// <summary>The slots carried (= the memory's and the squad pool's 16).</summary>
        public const int Capacity = 16;

        public int Count;
        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Z[Capacity];

        public readonly Vector3 this[int i] => new(X[i], Y[i], Z[i]);

        public void Set(int i, Vector3 p) { X[i] = p.X; Y[i] = p.Y; Z[i] = p.Z; }

        /// <summary>True when <paramref name="other"/> has another count or any slot farther than <paramref name="tolerance"/> (m) away.</summary>
        public readonly bool DiffersFrom(in EqsContextPoints other, float tolerance)
        {
            if (Count != other.Count) return true;
            float t2 = tolerance * tolerance;
            for (int i = 0; i < Count && i < Capacity; i++)
                if (Vector3.DistanceSquared(this[i], other[i]) > t2) return true;
            return false;
        }
    }
}
