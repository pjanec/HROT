using System;
using System.Collections.Generic;
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

        return t.Namespace == DdsMessageNamespace;
    }
}
