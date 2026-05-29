using System;
using System.Collections.Generic;
using Fdp.Toolkit.Utility;

namespace Hrot.Utility.Editor.Model;

// Editor-side, mutable model for one option inside a utility decision.
public sealed class OptionModel
{
    public ushort         OptionId         = 0;
    public string         Name             = string.Empty;
    public ScoringMode    Mode             = ScoringMode.WeightedProduct;
    public List<ConsiderationModel> Considerations = new();
    // Stable identifier for deterministic emit.
    public string         VisualId         = Guid.NewGuid().ToString("N");
}
