using System;
using System.Linq;
using System.Reflection;
using Hrot.ExCon.Panels;
using Xunit;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Automated audit that ensures all ExCon UI panel classes conform to the
/// "dumb view" rule (PACK2-U001):
/// no panel may hold a field of type IDdsWriter&lt;T&gt; or DdsWriter&lt;T&gt;.
///
/// Placed here (Hrot.ExCon.Tests) rather than Hrot.IG.Tests because
/// Hrot.IG.Tests does not reference the Hrot.ExCon assembly.
/// </summary>
public class UiLogicSeparationAuditTests
{
    // ── ExCon panel namespace ─────────────────────────────────────────────────

    [Fact]
    public void ExConPanels_HaveNoDirectDdsWriterFields()
    {
        AssertNoDdsWriterFields(typeof(OrbatPanel).Assembly,
            namespacePredicate: ns => ns != null && ns.StartsWith("Hrot.ExCon.Panels"));
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static void AssertNoDdsWriterFields(Assembly assembly, Func<string?, bool> namespacePredicate)
    {
        var violations = assembly.GetTypes()
            .Where(t => namespacePredicate(t.Namespace))
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(f => IsDdsWriterType(f.FieldType))
            .Select(f => $"{f.DeclaringType!.Name}.{f.Name} : {f.FieldType.Name}")
            .ToList();

        Assert.True(violations.Count == 0,
            $"DDS writer field(s) found in UI panels:\n  {string.Join("\n  ", violations)}");
    }

    private static bool IsDdsWriterType(Type t)
    {
        if (t.IsGenericType)
        {
            var def  = t.GetGenericTypeDefinition();
            var name = def.Name;
            if (name.StartsWith("DdsWriter") || name.StartsWith("IDdsWriter"))
                return true;
            foreach (var iface in def.GetInterfaces())
                if (iface.Name.StartsWith("IDdsWriter")) return true;
        }

        return t.Name.StartsWith("DdsWriter") || t.Name.StartsWith("IDdsWriter");
    }
}
