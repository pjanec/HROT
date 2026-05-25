using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace FDP.Eqs
{
    /// <summary>
    /// Typed wrapper around <see cref="Entity"/> that identifies a child entity carrying
    /// an <see cref="Fdp.Toolkit.Spatial.Eqs.EqsSensor"/> component.
    /// Used so Blueprint variable pickers can filter to "sensor handles" rather than
    /// presenting all <see cref="Entity"/> variables.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct EqsSensorHandle : IEquatable<EqsSensorHandle>
    {
        /// <summary>The child entity that carries the EqsSensor component.</summary>
        public readonly Entity ChildId;

        /// <summary>Constructs a handle wrapping the given child entity.</summary>
        public EqsSensorHandle(Entity childId) => ChildId = childId;

        /// <inheritdoc/>
        public bool Equals(EqsSensorHandle other) => ChildId.Equals(other.ChildId);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is EqsSensorHandle other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ChildId.GetHashCode();

        /// <summary>Returns true when both handles wrap the same entity.</summary>
        public static bool operator ==(EqsSensorHandle a, EqsSensorHandle b) => a.Equals(b);

        /// <summary>Returns true when the handles wrap different entities.</summary>
        public static bool operator !=(EqsSensorHandle a, EqsSensorHandle b) => !a.Equals(b);

        /// <summary>Returns true when <see cref="ChildId"/> refers to a non-null entity.</summary>
        public bool IsValid => !ChildId.IsNull;
    }
}
