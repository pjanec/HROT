namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Reads a named field or property from a component instance and returns its value as a string.
    /// Constructed once per (componentType, propertyPath) pair; the hot path is allocation-minimal.
    /// </summary>
    public interface IPropertyEvaluator
    {
        /// <summary>
        /// Returns the value of the configured property on <paramref name="component"/> as a string.
        /// </summary>
        /// <param name="component">The boxed component instance to read from.</param>
        string GetValueAsString(object component);
    }
}
