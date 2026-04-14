using Fdp.Kernel;

namespace Fdp.ModuleHost_Core.Time
{
    public interface ISteppableTimeController : ITimeController
    {
        GlobalTime Step(float deltaTime);
    }
}
