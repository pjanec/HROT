using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CycloneDDS.Runtime;
using CycloneDDS.Schema;
using Fdp.Toolkit.DER;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐ AQ109 §5a/§5b step 1 (X5 GATE) — the generic wire inspector opens one <c>DdsReader&lt;T&gt;</c> per entity-keyed wire type
    /// REFLECTIVELY (<c>MakeGenericType</c> + ctor), with no per-descriptor code. This rail proves that is possible for EVERY such type,
    /// and that "entity-keyed" (first <c>[DdsKey]</c> field is <c>EntityId</c> or <c>ParentNetworkId</c> — the protocol's keyed rule) is
    /// exactly the set <c>docs/reference/NED_DDS_Protocol.md</c> lists as keyed by entity id / parent entity id.
    /// </summary>
    public sealed class TheEntityKeyedWireTypesAreReadableByReflectionTests
    {
        private const uint PrivateDomain = 123;

        /// <summary>⭐ The SAME helper the inspector uses (<see cref="WireTypeCatalog"/>) — the rail and the mirror cannot disagree.</summary>
        internal static IReadOnlyList<Type> EntityKeyedTypes() => WireTypeCatalog.EntityKeyed(TheTopicNamesAreDerivedFromTypesTests.TopicTypes.Value);

        private static string ProtocolDoc()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "docs", "reference", "NED_DDS_Protocol.md");
                if (File.Exists(p)) return p;
            }
            throw new FileNotFoundException("docs/reference/NED_DDS_Protocol.md not found above " + AppContext.BaseDirectory);
        }

        private static SortedSet<string> DocEntityKeyedNames()
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            var row = new Regex(@"^\|\s*`([^`]+)`\s*\|[^|]*\|\s*yes:\s*(entity id|parent entity id)\b", RegexOptions.Compiled);
            foreach (var line in File.ReadLines(ProtocolDoc()))
            {
                var m = row.Match(line);
                if (m.Success) set.Add(m.Groups[1].Value);
            }
            return set;
        }

        /// <summary>A parameter's declared default, in a form reflection accepts (an <c>IntPtr</c> default arrives as the int 0).</summary>
        private static object? DefaultOf(ParameterInfo p)
        {
            var d = p.DefaultValue;
            var t = p.ParameterType;
            if (d is null or DBNull) return t.IsValueType ? Activator.CreateInstance(t) : null;
            if (t == typeof(IntPtr)) return new IntPtr(Convert.ToInt64(d));
            if (t == typeof(UIntPtr)) return new UIntPtr(Convert.ToUInt64(d));
            return t.IsEnum ? Enum.ToObject(t, d) : d;
        }

        [Fact]
        public void TheEntityKeyedSetIsExactlyTheProtocolsEntityKeyedRows()
        {
            var code = EntityKeyedTypes().Select(t => t.FullName!).ToList();
            var doc = DocEntityKeyedNames();
            Assert.True(doc.Count >= 50, $"the protocol doc lists only {doc.Count} entity-keyed rows — the row parser broke");
            Assert.Equal(doc.ToList(), code);
        }

        [Fact]
        public void ADdsReaderOpensByReflectionForEveryEntityKeyedType()
        {
            var types = EntityKeyedTypes();
            Assert.True(types.Count >= 50);
            var failures = new List<string>();
            var readers = new List<IDisposable>();
            using var participant = new DdsParticipant(PrivateDomain);
            try
            {
                foreach (var t in types)
                {
                    try
                    {
                        var readerType = typeof(DdsReader<>).MakeGenericType(t);
                        // the reader's ctor is (participant, optional name/qos...): pick it by its first parameter, leave the optionals missing
                        var ctor = readerType.GetConstructors().Where(c => c.GetParameters() is { Length: > 0 } ps && ps[0].ParameterType == typeof(DdsParticipant)
                                                                           && ps.Skip(1).All(x => x.IsOptional))
                            .OrderBy(c => c.GetParameters().Length).FirstOrDefault()
                            ?? throw new MissingMethodException("no (DdsParticipant, optionals...) ctor; ctors: " + string.Join("; ", readerType.GetConstructors().Select(c => c.ToString())));
                        var args = new object?[ctor.GetParameters().Length];
                        args[0] = participant;
                        var pars = ctor.GetParameters();
                        for (var i = 1; i < args.Length; i++) args[i] = DefaultOf(pars[i]);   // no name: derived (AQ107 A)
                        var r = ctor.Invoke(args);
                        Assert.NotNull(r);
                        readers.Add((IDisposable)r!);
                    }
                    catch (Exception e)
                    {
                        failures.Add($"{t.FullName}: {(e is TargetInvocationException { InnerException: { } i } ? i : e).GetType().Name}: {(e.InnerException ?? e).Message}");
                    }
                }
            }
            finally { foreach (var r in readers) r.Dispose(); }

            Assert.True(failures.Count == 0, "no reflective reader for: " + string.Join(" | ", failures));
            Assert.Equal(types.Count, readers.Count);
        }
    }
}
