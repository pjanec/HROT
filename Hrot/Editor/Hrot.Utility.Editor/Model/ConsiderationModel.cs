using System;
using Fdp.Toolkit.Utility;

namespace Hrot.Utility.Editor.Model;

// Editor-side, mutable model for one consideration row.
public sealed class ConsiderationModel
{
    // Resolves to In.<InputName>; validated by the authoring analyzer.
    public string         InputName   = string.Empty;
    public InputContext   Context     = InputContext.Self;
    public InputParamsModel Params    = new();
    public ResponseCurveModel Curve   = new();
    public float          Weight      = 1f;
    // Stable identifier for deterministic emit and comparison annotation.
    public string         VisualId    = Guid.NewGuid().ToString("N");
}
