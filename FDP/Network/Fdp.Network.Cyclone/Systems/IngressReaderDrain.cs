using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using CycloneDDS.Runtime;

namespace Fdp.Network.Cyclone.Modules
{
    /// <summary>
    /// ⭐⭐ CE-3329 W5 (DESIGN_Replay_Time_Axis_And_Review §12u) — takes and drops every sample a translator's readers hold.
    ///
    /// <para>A translator that implements <see cref="Fdp.Interfaces.IDiscardableIngress"/> discards itself. 📐 The others — the
    /// world-STATE translators, ~170 of them — share no base class that owns the reader (measured `2026-10-27`: 161
    /// <c>new DdsReader&lt;</c> sites; <c>CycloneBaseTranslator</c> has 13 adopters, all events), so each is drained through its
    /// <see cref="DdsReader{T}"/> FIELDS, found once per translator type across its whole class hierarchy and cached.
    /// ⛔ The alternative — an <c>IDiscardableIngress</c> on every one — was ~170 hand edits, and a translator added later without it
    /// would silently keep its replay-era state.</para>
    ///
    /// <para>⚠ A reader held anywhere but a field of the translator itself (a helper object, a list) is not reached;
    /// <see cref="ReaderFieldCount"/> says how many were found.</para>
    /// </summary>
    public static class IngressReaderDrain
    {
        private static readonly ConcurrentDictionary<Type, Func<object, int>[]> Drains = new();

        private static readonly MethodInfo DrainOne =
            typeof(IngressReaderDrain).GetMethod(nameof(DrainReader), BindingFlags.NonPublic | BindingFlags.Static)!;

        /// <summary>Takes and drops every pending sample of <paramref name="translator"/>'s readers. Returns how many were dropped.</summary>
        public static int Discard(object translator)
        {
            if (translator is Fdp.Interfaces.IDiscardableIngress discardable) return discardable.DiscardIngress();
            int dropped = 0;
            foreach (var drain in Drains.GetOrAdd(translator.GetType(), Build)) dropped += drain(translator);
            return dropped;
        }

        /// <summary>How many <see cref="DdsReader{T}"/> fields <see cref="Discard"/> drains on an instance of <paramref name="translatorType"/>.</summary>
        public static int ReaderFieldCount(Type translatorType) => Drains.GetOrAdd(translatorType, Build).Length;

        private static Func<object, int>[] Build(Type type)
        {
            var drains = new List<Func<object, int>>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var ft = field.FieldType;
                    if (!ft.IsGenericType || ft.GetGenericTypeDefinition() != typeof(DdsReader<>)) continue;
                    // ⚠ the loan is a ref struct — it cannot cross a reflective call, so the take runs in a method closed over the sample type
                    var drain = (Func<object?, int>)DrainOne.MakeGenericMethod(ft.GetGenericArguments()[0]).CreateDelegate(typeof(Func<object?, int>));
                    var f = field;
                    drains.Add(owner => drain(f.GetValue(owner)));
                }
            }
            return drains.ToArray();
        }

        private static int DrainReader<T>(object? reader) where T : new()
        {
            if (reader is not DdsReader<T> r) return 0;
            int dropped = 0;
            try
            {
                while (true)
                {
                    using var loan = r.Take(256);
                    if (loan.Count == 0) return dropped;
                    dropped += loan.Count;
                }
            }
            catch (ObjectDisposedException) { return dropped; }   // a translator torn down with its node: nothing left to drop
        }
    }
}
