using Fdp.Kernel;

namespace Fdp.ModuleHost.Core.Time
{
    public interface ISteppableTimeController : ITimeController
    {
        GlobalTime Step(float deltaTime);
    }
}
