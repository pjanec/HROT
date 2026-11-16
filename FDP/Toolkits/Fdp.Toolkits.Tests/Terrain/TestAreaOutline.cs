using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-4624 — THE test stand-in for the editor's <c>EditablePolyline</c> (which this toolkit cannot reference): the toolkit reads an outline by the component id through
    /// <see cref="IAreaOutline"/>. ⚠ ONE type per process: a second test type on the same id is a "Component ID collision" the moment both suites run (measured).
    /// </summary>
    [ComponentId(GlobalComponentIds.EditablePolyline)]
    public sealed record TestAreaOutline : IAreaOutline
    {
        public IReadOnlyList<Vector2> Points { get; init; } = Array.Empty<Vector2>();
    }
}
