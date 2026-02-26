using System.Collections.Generic;
using System.Threading.Tasks;

namespace Bagira.Runner.Models
{
    /// <summary>
    /// Contract for a single named action that the <c>HeadlessTestExecutor</c>
    /// can dispatch during a test run.
    /// </summary>
    public interface ITestActionHandler
    {
        /// <summary>
        /// Unique name that matches the <c>"action"</c> field in a <see cref="TestStep"/>.
        /// </summary>
        string ActionName { get; }

        /// <summary>
        /// Executes the action with the supplied <paramref name="args"/>.
        /// </summary>
        /// <param name="args">Key-value dictionary from the test step JSON.</param>
        /// <returns>
        /// An optional result object whose fields can be used by the executor's
        /// assertion logic. Return <see langword="null"/> if there is nothing to assert.
        /// </returns>
        Task<object?> ExecuteAsync(Dictionary<string, object> args);
    }
}
