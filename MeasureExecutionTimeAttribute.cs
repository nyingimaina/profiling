namespace Jattac.Libs.Profiling
{
    using System;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class MeasureExecutionTimeAttribute : Attribute
{
    public bool LogSummary { get; }
    public bool TrackSlowest { get; }

    public MeasureExecutionTimeAttribute(bool logSummary = false, bool trackSlowest = false)
    {
        LogSummary = logSummary;
        TrackSlowest = trackSlowest;
    }
}


}