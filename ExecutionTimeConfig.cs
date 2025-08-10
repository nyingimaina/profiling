namespace Jattac.Libs.Profiling
{
    /// <summary>
    /// Provides configuration options for the execution time profiler.
    /// </summary>
    public class ExecutionTimeConfig
    {
        /// <summary>
        /// Gets or sets a value indicating whether profiling is globally enabled. The default is <c>true</c>.
        /// This can be configured in a settings file (e.g., appsettings.json) under the "ExecutionTime" section.
        /// </summary>
        public bool EnableTiming { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether numbers in the console summary tables should be formatted with culture-specific separators (e.g., 1,000). The default is <c>true</c>.
        /// </summary>
        public bool UseCultureFormatting { get; set; } = true;
    }
}