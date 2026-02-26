using System;
using Fdp.Kernel;

namespace Fdp.Examples.NetworkDemo.Components
{
    [ComponentId(212)]
    public struct TimeConfiguration
    {
        public bool IsPaused;
        public float TimeScale;
    }
}
