using System.Reflection;

namespace Hrot.Editor.AiShared.Layout;

public static class LayoutDiscovery
{
    /// <summary>
    /// Scans all public static methods in the given assembly for a method decorated with
    /// <typeparamref name="TAttr"/> whose AssetId property equals <paramref name="assetId"/>.
    /// Invokes the first matching method and returns its result cast to
    /// <typeparamref name="TLayout"/>. Returns null if no match is found.
    /// </summary>
    public static TLayout? TryGetLayout<TAttr, TLayout>(Assembly assembly, Guid assetId)
        where TAttr : Attribute
        where TLayout : class
    {
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<TAttr>();
                if (attr is null) continue;

                var assetIdProp = attr.GetType().GetProperty("AssetId");
                if (assetIdProp is null) continue;

                var rawValue = (string?)assetIdProp.GetValue(attr);
                if (rawValue is null) continue;

                if (!Guid.TryParse(rawValue, out var attrGuid)) continue;
                if (attrGuid != assetId) continue;

                return (TLayout?)method.Invoke(null, null);
            }
        }
        return null;
    }
}
