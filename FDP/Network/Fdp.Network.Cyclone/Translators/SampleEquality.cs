using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fdp.Network.Cyclone.Translators
{
    /// <summary>
    /// ⭐ CE-2406 (R-2084) — the ONE "is this sample the one I last sent?" test for egress translators' change caches.
    /// <para>📐 The generated DDS sample structs implement no <c>IEquatable&lt;T&gt;</c>, so <c>prev.Equals(sample)</c> was
    /// <c>ValueType.Equals(object)</c>: it BOXES the sample and, for a struct with a float field, compares field by field through
    /// reflection — per owned entity, per translator, per frame (~25 translators).</para>
    /// <para>⭐ A struct without references is compared as bytes. Bytes equal ⇒ every field equal, so the test can only answer "changed"
    /// MORE often than the field test (e.g. <c>-0f</c> vs <c>+0f</c>), which costs at most an extra send — never a lost one. A struct WITH
    /// references uses its own equality (as before). Allocation-free for the byte path.</para>
    /// </summary>
    public static class SampleEquality
    {
        public static bool Same<T>(in T a, in T b) where T : struct
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                var x = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in a), 1));
                var y = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in b), 1));
                return x.SequenceEqual(y);
            }
            return EqualityComparer<T>.Default.Equals(a, b);
        }
    }
}
