using System;
using System.Linq;
using System.Reflection;
using Hrot.Player.UI;
using Xunit;

namespace Hrot.Player.Tests;

/// <summary>
/// Automated audit that ensures all IG UI panel classes conform to the
/// "dumb view" rule (PACK2-U001):
/// no panel may hold a field of type IDdsWriter&lt;T&gt; or DdsWriter&lt;T&gt;.
///
/// NOTE: Hrot.ExCon is not referenced by Hrot.Player.Tests; the equivalent
/// ExCon panel audit lives in Hrot.ExCon.Tests/UiLogicSeparationAuditTests.cs.
/// </summary>
public class UiLogicSeparationAuditTests
{
    // ── IG UI panel namespace ─────────────────────────────────────────────────

    [Fact]
    public void PlayerUiPanels_HaveNoDirectDdsWriterFields()
    {
        AssertNoDdsWriterFields(typeof(PlayerApplication).Assembly,
            namespacePredicate: ns => ns != null && ns.StartsWith("Hrot.Player.UI"));
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
