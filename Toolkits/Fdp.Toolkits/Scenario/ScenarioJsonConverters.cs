using Fdp.Core;
using Fdp.Core.Serialization.Converters;

namespace Fdp.Toolkit.Scenario
{
    // Forwarding subclasses kept for backward compatibility with existing callers.
    // The canonical implementations now live in Fdp.Core.Serialization.Converters.
    // These types will be removed once all call sites are migrated (DD-P1-T04).

    [System.Obsolete("Use Fdp.Core.Serialization.Converters.Vector2ArrayConverter instead.")]
    internal sealed class Vector2ArrayConverter : Fdp.Core.Serialization.Converters.Vector2ArrayConverter { }

    [System.Obsolete("Use Fdp.Core.Serialization.Converters.Vector3ArrayConverter instead.")]
    internal sealed class Vector3ArrayConverter : Fdp.Core.Serialization.Converters.Vector3ArrayConverter { }

    [System.Obsolete("Use Fdp.Core.Serialization.Converters.QuaternionArrayConverter instead.")]
    internal sealed class QuaternionArrayConverter : Fdp.Core.Serialization.Converters.QuaternionArrayConverter { }

    [System.Obsolete("Use Fdp.Core.Serialization.Converters.Vector4ArrayConverter instead.")]
    internal sealed class Vector4ArrayConverter : Fdp.Core.Serialization.Converters.Vector4ArrayConverter { }

    [System.Obsolete("Use Fdp.Core.Serialization.Converters.FixedString32Converter instead.")]
    internal sealed class FixedString32Converter : Fdp.Core.Serialization.Converters.FixedString32Converter { }

    [System.Obsolete("Use Fdp.Core.Serialization.Converters.FixedString64Converter instead.")]
    internal sealed class FixedString64Converter : Fdp.Core.Serialization.Converters.FixedString64Converter { }
}
