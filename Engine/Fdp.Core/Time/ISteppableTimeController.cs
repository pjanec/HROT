using Fdp.Kernel;

namespace ModuleHost.Core.Time
{
    public interface ISteppableTimeController : ITimeController
    {
        GlobalTime Step(float deltaTime);
    }
}
