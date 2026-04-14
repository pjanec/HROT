using System;
using Fdp.Core;

namespace Fdp.Examples.NetworkDemo.Components
{
    [ComponentId(212)]
    public struct TimeConfiguration
    {
        public bool IsPaused;
        public float TimeScale;
    }
}
