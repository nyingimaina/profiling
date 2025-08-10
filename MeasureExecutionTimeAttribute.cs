namespace Jattac.Libs.Profiling
{
    using System;

    /// <summary>
    /// Marks a class, interface, or method to be profiled for execution time.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Interface)]
    public class MeasureExecutionTimeAttribute : Attribute
    {
        /// <summary>
        /// Gets a value indicating whether a summary of all calls should be logged when the service instance is disposed.
        /// </summary>
        public bool LogSummary { get; }

        /// <summary>
        /// Gets a value indicating whether a list of the slowest calls should be logged when the service instance is disposed.
        /// </summary>
        public bool TrackSlowest { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeasureExecutionTimeAttribute"/> class.
        /// </summary>
        /// <param name="logSummary">If true, a summary of all calls will be logged when the service instance is disposed.</param>
        /// <param name="trackSlowest">If true, a list of the slowest calls will be logged when the service instance is disposed.</param>
        public MeasureExecutionTimeAttribute(bool logSummary = false, bool trackSlowest = false)
        {
            LogSummary = logSummary;
            TrackSlowest = trackSlowest;
        }
    }


}