using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Reflection;
using Fdp.Toolkit.Replication.Events;
using Fdp.Toolkit.Replication.Patching;
using Hrot.SimHost.Installers;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>R-134</c>'s STRUCTURAL GUARD — the rail that fails if a DDS type re-enters the FDP-internal
/// write path.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §11 · <c>RULINGS.md</c> <c>R-134</c>:
/// 🔒 *"No DDS type crosses into the FDP-internal path; the egress translator is the SOLE boundary."*</para>
///
/// <para>⭐⭐⭐ <b>Why this rail exists at all.</b> The coupling it forbids is the one this slice SHIPPED and
/// then had to undo: <c>AttributeEntityComponentWriter</c> built a DDS <c>AttributeRecord</c> because the
/// interpreter's record type WAS the wire type, and nothing said no. ⛔ A ruling in a document does not
/// stop the next author doing it again — 📌 the same lesson as <c>UXI-30</c>: *"correct, and also
/// forgettable."* ⭐ Here the check is mechanical.</para>
///
/// <para>⚠ <b>What it cannot check.</b> A DDS type reached through <c>object</c>, reflection or a
/// <c>dynamic</c> call is invisible to a signature scan. ⭐ Stated rather than glossed: this rail catches
/// the coupling that actually happens — a wire struct named in a field, a parameter or a generic
/// argument — not a determined author routing around it.</para>
/// </summary>
public class StrictNetworkSeparationTests
{
    /// <summary>⭐ The namespace every generated CycloneDDS message type lives in.</summary>
    private const string DdsMessageNamespace = "Hrot.NED.Messages";

    /// <summary>
    /// ⭐⭐ <b>Broadened `2026-08-26`: ANY <c>Hrot.NED.*</c> namespace, not just <c>Messages</c>.</b>
    ///
    /// <para>🔴 The original check named only <c>Hrot.NED.Messages</c>, and that is how four files in the
    /// apply path kept a <c>Hrot.NED.Descriptors</c> dependency while the rail reported green. ⇒ the prefix
    /// is what matters: everything under <c>Hrot.NED.</c> is network-layer.</para>
    /// </summary>
    private const string DdsNamespacePrefix = "Hrot.NED.";

    /// <summary>
    /// ⭐⭐⭐ <b>THE ALLOWLIST — the SOLE boundary, named.</b>
    ///
    /// <para>⭐ A type here is ALLOWED to mention a DDS message type. ⛔ Anything else in the attribute
    /// write path is not. ⚠ Adding a name to this list is a deliberate act that shows up in review —
    /// which is the whole mechanism.</para>
    /// </summary>
    private static readonly HashSet<string> BoundaryTypes = new(StringComparer.Ordinal)
    {
        // ⭐ The conversion itself — both directions, on purpose (see its class remarks).
        "Hrot.SimHost.Installers.AttributeRecordConversion",
    };

    // ══ ① the STRONGEST form: the assembly cannot even see the DDS types ══════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>Fdp.Toolkits</c> — home of <see cref="EntityAttributeChange"/>,
    /// <see cref="IEntityComponentWriter"/> and <see cref="UpdateEntityAttributeCommand"/> — does not
    /// reference the assembly the DDS messages live in.</b>
    ///
    /// <para>⭐⭐ This is the guard that cannot be forgotten, because it is the PROJECT GRAPH: putting an
    /// <c>AttributeRecord</c> in the internal record would not fail a test, it would fail to compile.
    /// ⭐ Railed anyway so that ADDING the reference — the change that would quietly unlock it — is
    /// itself a red.</para>
    /// </summary>
    [Fact]
    public void TheInternalRecordsAssemblyCannotSeeTheDdsMessages()
    {
        var toolkits = typeof(EntityAttributeChange).Assembly;
        var ddsAssemblyName = typeof(Hrot.NED.Messages.AttributeRecord).Assembly.GetName().Name;

        Assert.DoesNotContain(
            toolkits.GetReferencedAssemblies(),
            a => string.Equals(a.Name, ddsAssemblyName, StringComparison.Ordinal));
    }

    /// <summary>
    /// ⭐⭐ <b>And the presentation assembly — home of <c>EntityDragGizmo</c> since <c>AX-007</c> — is the
    /// same shape.</b>
    ///
    /// <para>📌 This is exactly why <see cref="IEntityComponentWriter"/> was moved to <c>Fdp.Toolkits</c>:
    /// the drag gizmo needed the SEAM, and giving <c>Hrot.Presentation</c> a reference to the network
    /// assembly to get it would have dragged CycloneDDS into the presentation layer to satisfy an
    /// interface that mentions no network type at all.</para>
    /// </summary>
    [Fact]
    public void ThePresentationAssemblyCannotSeeTheDdsMessages()
    {
        var presentation = typeof(Hrot.ScenarioEditor.Gizmos.EntityDragGizmo).Assembly;
        var ddsAssemblyName = typeof(Hrot.NED.Messages.AttributeRecord).Assembly.GetName().Name;

        Assert.DoesNotContain(
            presentation.GetReferencedAssemblies(),
            a => string.Equals(a.Name, ddsAssemblyName, StringComparison.Ordinal));
    }

    // ══ ② inside the NETWORK assembly, where DDS types ARE reachable ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THAT DOES REAL WORK.</b> Inside <c>Hrot.Network.NED</c> the DDS types are one
    /// <c>using</c> away, so ① cannot help. ⇒ every type in the attribute-write namespace
    /// *(<c>Hrot.SimHost.Installers</c>)* is scanned, and the set that mentions a DDS message type must be
    /// EXACTLY <see cref="BoundaryTypes"/>.
    ///
    /// <para>⭐⭐ <b>It is an equality, not a subset.</b> A new coupling reddens it — and so does DELETING
    /// the boundary, which would mean the conversion moved somewhere unnamed.</para>
    /// </summary>
    [Fact]
    public void OnlyTheDeclaredBoundaryMentionsADdsTypeInTheWritePath()
    {
        var offenders = TypesInWritePathMentioningDds()
            .Select(t => t.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(BoundaryTypes.OrderBy(n => n, StringComparer.Ordinal).ToList(), offenders);
    }

    /// <summary>
    /// ⚠ <b>The rail's own red-proof, asserted rather than described.</b> If the scan simply found nothing
    /// — a wrong namespace, a wrong assembly — ① and ② would both pass vacuously and prove nothing.
    /// ⭐ So: the scan must SEE types, and the boundary type must be among the ones it can see.
    /// </summary>
    [Fact]
    public void TheScanActuallySeesTheWritePath()
    {
        var scanned = WritePathTypes().ToList();

        Assert.NotEmpty(scanned);
        Assert.Contains(scanned, t => t == typeof(AttributeEntityComponentWriter));
        Assert.Contains(scanned, t => t == typeof(AttributeRecordConversion));
        Assert.Contains(scanned, t => t == typeof(SimTransformHeadingInstaller));

        // ⭐ And the detector is not blind: the boundary type IS detected as mentioning DDS.
        Assert.Contains(typeof(AttributeRecordConversion), TypesInWritePathMentioningDds());
    }

    // ══ ③ the SOURCE scan — what reflection structurally CANNOT see ═══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE DEPENDENCY INVENTORY of the attribute apply path, asserted as an EQUALITY.</b>
    ///
    /// <para>🔴🔴 <b>WHY A SOURCE SCAN AND NOT MORE REFLECTION — this is the important part.</b> The four
    /// installers reference <c>Hrot.NED.Descriptors.EDescriptorType</c> like this:
    /// <c>private const long GeoSpatialOrdinal = (long)EDescriptorType.dtWorldPos;</c>
    /// ⛔ A <c>const long</c> is **folded to a literal at compile time**, so the assembly contains the number
    /// <c>2</c> and **no reference to the enum whatsoever**. ⇒ ⭐⭐ <b>no reflection rail can ever detect this
    /// class of coupling</b> — 📐 proven: broadening <see cref="IsDds"/> from <c>Hrot.NED.Messages</c> to the
    /// whole <c>Hrot.NED.</c> prefix left rails ①/② green while the dependency plainly exists in source.</para>
    ///
    /// <para>⚠⚠ <b>THIS RAIL DOES NOT ENDORSE THE ALLOWLIST BELOW.</b> It PINS it. The
    /// <c>Hrot.NED.Descriptors</c> entries are an <b>OPEN DESIGN QUESTION</b> *(recorded as <c>AX-013</c>)*:
    /// a descriptor ordinal is wire numbering, so it is arguable the apply path is legitimately
    /// network-layer — and equally arguable those ordinals should be injected so the path can move out of
    /// the DDS assembly. ⭐ Until that is decided, the value of this rail is that the list <b>cannot grow
    /// silently</b>, and shrinking it is a visible, deliberate edit.</para>
    ///
    /// <para>📌 It also corrects an OVERCLAIM: <c>AX-005a</c> was reported as *"no DDS type survives in the
    /// FDP-internal write path"*. ⭐ The true statement is narrower — <b>no DDS MESSAGE type survives; a DDS
    /// descriptor-ordinal enum does.</b></para>
    /// </summary>
    [Fact]
    public void TheApplyPathsNetworkDependenciesAreExactlyTheDeclaredOnes()
    {
        var dir = AttributePathDirectory();

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var namespaces = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(file))
            {
                var m = UsingHrotNed.Match(line);
                if (m.Success) namespaces.Add(m.Groups[1].Value);
            }
            if (namespaces.Count > 0)
                actual[Path.GetFileName(file)] = string.Join("+", namespaces);
        }

        Assert.Equal(DeclaredNetworkDependencies, actual);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The declared inventory. ⛔ Adding a line here is a DESIGN act, not a fix.</b>
    ///
    /// <list type="bullet">
    ///   <item>⭐ <b><c>Messages</c></b> = the wire structs. Legitimate in exactly two places: the declared
    ///   conversion boundary, and a system whose whole job is publishing a DDS schema.</item>
    ///   <item>⚠ <b><c>Descriptors</c></b> = <c>EDescriptorType</c> ordinals, used only to mark a descriptor
    ///   dirty. 📌 <c>AX-013</c> — the open question above.</item>
    /// </list>
    /// </summary>
    private static readonly SortedDictionary<string, string> DeclaredNetworkDependencies =
        new(StringComparer.Ordinal)
        {
            // ⭐ R-134's sole conversion boundary — both directions, by design.
            ["AttributeRecordConversion.cs"]          = "Hrot.NED.Messages",
            // ⭐ Publishes the attribute schema onto DDS; network-layer by definition.
            ["EntityAttributeSchemaPublisherSystem.cs"] = "Hrot.NED.Messages",
            // ⚠ AX-013 — descriptor ordinals only. See the rail's remarks.
            ["AttributeCompilerFactory.cs"]           = "Hrot.NED.Descriptors",
            ["EntityDataAttributeInstaller.cs"]       = "Hrot.NED.Descriptors",
            ["SimTransformAttributeInstaller.cs"]     = "Hrot.NED.Descriptors",
            ["SimTransformHeadingInstaller.cs"]       = "Hrot.NED.Descriptors",
        };

    private static readonly Regex UsingHrotNed =
        new(@"^\s*using\s+(Hrot\.NED\.[A-Za-z0-9_.]+)\s*;", RegexOptions.Compiled);

    /// <summary>
    /// ⭐ Locates the apply path on disk. ⛔ Fails LOUDLY rather than skipping — a source rail that quietly
    /// opts out when it cannot find sources reports green for ever.
    /// </summary>
    private static string AttributePathDirectory()
    {
        const string Relative = "Hrot/Network/Hrot.Network.NED/Attributes";

        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var probe = start;
            while (!string.IsNullOrEmpty(probe))
            {
                var candidate = Path.Combine(probe, Relative.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(candidate)) return candidate;
                probe = Path.GetDirectoryName(probe);
            }
        }

        Assert.Fail($"Could not locate '{Relative}' from either the working directory or the output " +
                    "directory. This rail scans source and cannot run without it.");
        return string.Empty;   // unreachable
    }

    // ══ helpers ══════════════════════════════════════════════════════════════════

    private static IEnumerable<Type> WritePathTypes()
        => typeof(AttributeEntityComponentWriter).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Hrot.SimHost.Installers" && !t.IsNested);

    private static IEnumerable<Type> TypesInWritePathMentioningDds()
        => WritePathTypes().Where(MentionsADdsType);

    /// <summary>
    /// ⭐ Does any member SIGNATURE of <paramref name="type"/> name a DDS message type — as a field or
    /// property type, a method return, a parameter, or a generic argument of any of those?
    /// </summary>
    private static bool MentionsADdsType(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                               | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var f in type.GetFields(All))
            if (IsDds(f.FieldType)) return true;

        foreach (var p in type.GetProperties(All))
            if (IsDds(p.PropertyType)) return true;

        foreach (var m in type.GetMethods(All))
        {
            if (IsDds(m.ReturnType)) return true;
            if (m.GetParameters().Any(p => IsDds(p.ParameterType))) return true;
        }

        foreach (var c in type.GetConstructors(All))
            if (c.GetParameters().Any(p => IsDds(p.ParameterType))) return true;

        return false;
    }

    /// <summary>⭐ Unwraps arrays, by-refs and generic arguments — a <c>List&lt;AttributeRecord&gt;</c> counts.</summary>
    private static bool IsDds(Type t)
    {
        if (t.IsByRef || t.IsArray || t.IsPointer)
            return IsDds(t.GetElementType()!);

        if (t.IsGenericType && t.GetGenericArguments().Any(IsDds)) return true;

        return t.Namespace != null
            && t.Namespace.StartsWith(DdsNamespacePrefix, StringComparison.Ordinal);
    }
}
