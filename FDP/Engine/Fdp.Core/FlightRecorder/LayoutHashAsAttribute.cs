using System;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <c>CE-11027</c> S12-1 — a value type that must hash, in a recorded layout, as ANOTHER type (<see cref="As"/>): <c>ComponentLayoutHasher</c> folds a field's type name into the layout hash, so retyping a
    /// field (<c>NodeId</c> → <c>StationId</c>, the same 4 bytes) would refuse every recording made before. A field whose type carries this attribute hashes under <see cref="As"/>'s <c>FullName</c>.
    /// ⛔ Only for a retype that keeps the bytes and the meaning of old data; it does not change what is written.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class LayoutHashAsAttribute : Attribute
    {
        public LayoutHashAsAttribute(Type @as) => As = @as ?? throw new ArgumentNullException(nameof(@as));
        /// <summary>The type whose name the layout hash folds in.</summary>
        public Type As { get; }
    }
}
