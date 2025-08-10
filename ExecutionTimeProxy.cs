using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Jattac.Libs.Profiling
{
    public class ExecutionTimeProxy<T> : DispatchProxy, IDisposable
    {
        private T? _decorated;
        private bool _measureAllMethods;
        private bool _logSummary;
        private bool _trackSlowest;
        private ExecutionTimeConfig? _config;

        private const int MaxRecords = 100;
        private readonly SortedList<long, string> _executionLog = new();
        private readonly Dictionary<string, (int Count, long TotalTime, long MaxTime, long MinTime)> _executionSummary = new();

        public static T Create(T decorated, ExecutionTimeConfig config)
        {
            if (decorated == null) throw new ArgumentNullException(nameof(decorated));

            object? proxy = Create<T, ExecutionTimeProxy<T>>();
            if (proxy == null)
            {
                throw new Exception($"Unable to proxy {decorated.GetType()}");
            }

            var proxyInstance = (ExecutionTimeProxy<T>)proxy;
            proxyInstance._decorated = decorated;
            proxyInstance._config = config;

            var classAttr = decorated.GetType().GetCustomAttribute<MeasureExecutionTimeAttribute>();
            var interfaceAttr = typeof(T).GetCustomAttribute<MeasureExecutionTimeAttribute>();

            // Class attribute takes precedence over interface attribute
            var effectiveAttr = classAttr ?? interfaceAttr;

            proxyInstance._measureAllMethods = effectiveAttr != null;
            proxyInstance._logSummary = effectiveAttr?.LogSummary ?? false;
            proxyInstance._trackSlowest = effectiveAttr?.TrackSlowest ?? false;

            return (T)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) throw new ArgumentNullException(nameof(targetMethod));

            // Find the corresponding method on the implementation class
            var implementationMethod = _decorated.GetType().GetMethod(targetMethod.Name,
                targetMethod.GetParameters().Select(p => p.ParameterType).ToArray());

            // Get attribute from targetMethod (which is guaranteed non-null here)
            #pragma warning disable CS8602 // Dereference of a possibly null reference.
            var targetMethodAttribute = ((MethodInfo)targetMethod).GetCustomAttribute<MeasureExecutionTimeAttribute>();
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            bool hasMethodAttribute = targetMethodAttribute != null;

            if (implementationMethod != null) // Explicit null check for implementationMethod
            {
                // Get attribute from implementationMethod
                var implMethodAttribute = implementationMethod.GetCustomAttribute<MeasureExecutionTimeAttribute>();
                hasMethodAttribute = hasMethodAttribute || (implMethodAttribute != null);
            }

            bool shouldMeasure = _measureAllMethods || hasMethodAttribute;

            if (!shouldMeasure)
            {
                return targetMethod.Invoke(_decorated, args);
            }

            var returnType = targetMethod.ReturnType;

            if (typeof(Task).IsAssignableFrom(returnType))
            {
                return HandleAsyncMethod(targetMethod, args, returnType);
            }
            else
            {
                return HandleSyncMethod(targetMethod, args);
            }
        }

        private object HandleAsyncMethod(MethodInfo targetMethod, object?[]? args, Type returnType)
        {
            var stopwatch = Stopwatch.StartNew();
            object? taskObj = targetMethod.Invoke(_decorated, args);

            if (taskObj is Task task)
            {
                return AwaitTaskProperly(task, targetMethod, stopwatch, returnType);
            }

            Log(LogType.Error, $"Method {targetMethod.Name} did not return a Task.");
            return null!;
        }

        private object AwaitTaskProperly(Task task, MethodInfo method, Stopwatch stopwatch, Type returnType)
        {
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                // Extract T from Task<T>
                Type taskResultType = returnType.GetGenericArguments()[0];

                MethodInfo handleGenericTaskMethod = typeof(ExecutionTimeProxy<T>)
                    .GetMethod(nameof(HandleGenericTask), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(taskResultType);

                return handleGenericTaskMethod.Invoke(this, new object[] { task, method, stopwatch })!;
            }
            else
            {
                return HandleVoidTask(task, method, stopwatch);
            }
        }

        private async Task HandleVoidTask(Task task, MethodInfo method, Stopwatch stopwatch)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log(LogType.Error, $"Exception in {method.Name}: {ex}");
                throw;
            }
            finally
            {
                LogExecutionTime(method, stopwatch.ElapsedMilliseconds);
            }
        }

        private async Task<TOut> HandleGenericTask<TOut>(Task task, MethodInfo method, Stopwatch stopwatch)
        {
            try
            {
                var genericTask = (Task<TOut>)task;
                TOut result = await genericTask.ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                Log(LogType.Error, $"Exception in {method.Name}: {ex}");
                throw;
            }
            finally
            {
                LogExecutionTime(method, stopwatch.ElapsedMilliseconds);
            }
        }

        private object? HandleSyncMethod(MethodInfo targetMethod, object?[]? args)
        {
            var stopwatch = Stopwatch.StartNew();
            object? result = targetMethod.Invoke(_decorated, args);
            LogExecutionTime(targetMethod, stopwatch.ElapsedMilliseconds);
            return result;
        }

        private void LogExecutionTime(MethodInfo method, long elapsedMs)
        {
            string methodName = method.DeclaringType != null ? $"{method.DeclaringType.Name}.{method.Name}" : method.Name;
            Log(LogType.Info, $"Method {methodName} took {elapsedMs} ms");

            if (_trackSlowest)
            {
                AddToExecutionLog(methodName, elapsedMs);
            }
            if (_logSummary)
            {
                AddToExecutionSummary(methodName, elapsedMs);
            }
        }

        private void AddToExecutionLog(string methodName, long executionTime)
        {
            lock (_executionLog)
            {
                _executionLog.Add(executionTime, methodName);
                if (_executionLog.Count > MaxRecords)
                {
                    _executionLog.RemoveAt(0);
                }
            }
        }

        private void AddToExecutionSummary(string methodName, long executionTime)
        {
            lock (_executionSummary)
            {
                if (_executionSummary.TryGetValue(methodName, out var stats))
                {
                    _executionSummary[methodName] = (stats.Count + 1, stats.TotalTime + executionTime, Math.Max(stats.MaxTime, executionTime), Math.Min(stats.MinTime, executionTime));
                }
                else
                {
                    _executionSummary[methodName] = (1, executionTime, executionTime, executionTime);
                }
            }
        }

        public void PrintSummary()
        {
            if (_executionSummary.Any())
            {
                var culture = new CultureInfo("en-GB");
                Func<long, string> formatNumber = n => _config!.UseCultureFormatting ? n.ToString("N0", culture) : n.ToString();

                // Calculate maximum widths for each column
                var methodNameTitle = "Method Name";
                var countTitle = "Count";
                var avgTitle = "Avg ms";
                var minTitle = "Min ms";
                var maxTitle = "Max ms";

                int methodNameWidth = Math.Max(methodNameTitle.Length, _executionSummary.Keys.Max(k => k.Length));
                int countWidth = Math.Max(countTitle.Length, _executionSummary.Values.Max(v => formatNumber(v.Count).Length));
                int avgWidth = Math.Max(avgTitle.Length, _executionSummary.Values.Max(v => formatNumber(v.TotalTime / v.Count).Length + 3));
                int minWidth = Math.Max(minTitle.Length, _executionSummary.Values.Max(v => formatNumber(v.MinTime).Length + 3));
                int maxWidth = Math.Max(maxTitle.Length, _executionSummary.Values.Max(v => formatNumber(v.MaxTime).Length + 3));

                // Header
                string header = $"| {methodNameTitle.PadRight(methodNameWidth)} | {countTitle.PadLeft(countWidth)} | {avgTitle.PadLeft(avgWidth)} | {minTitle.PadLeft(minWidth)} | {maxTitle.PadLeft(maxWidth)} |";
                string divider = new string('-', header.Length);

                Log(LogType.Section, "\nExecution Summary:");
                Log(LogType.Section, divider);
                Log(LogType.Section, header);
                Log(LogType.Section, divider);

                // Rows
                foreach (var entry in _executionSummary)
                {
                    var avgTime = $"{formatNumber(entry.Value.TotalTime / entry.Value.Count)} ms";
                    var minTime = $"{formatNumber(entry.Value.MinTime)} ms";
                    var maxTime = $"{formatNumber(entry.Value.MaxTime)} ms";

                    string row = $"| {entry.Key.PadRight(methodNameWidth)} | {formatNumber(entry.Value.Count).PadLeft(countWidth)} | {avgTime.PadLeft(avgWidth)} | {minTime.PadLeft(minWidth)} | {maxTime.PadLeft(maxWidth)} |";
                    Log(LogType.Section, row);
                }
                Log(LogType.Section, divider);
            }
        }

        public void PrintSlowestSummary()
        {
            if (_executionLog.Any())
            {
                var culture = new CultureInfo("en-GB");
                Func<long, string> formatNumber = n => _config!.UseCultureFormatting ? n.ToString("N0", culture) : n.ToString();

                var methodNameTitle = "Method Name";
                var timeTitle = "Time";

                int methodNameWidth = Math.Max(methodNameTitle.Length, _executionLog.Values.Max(v => v.Length));
                int timeWidth = Math.Max(timeTitle.Length, _executionLog.Keys.Max(k => formatNumber(k).Length + 3));

                string header = $"| {methodNameTitle.PadRight(methodNameWidth)} | {timeTitle.PadLeft(timeWidth)} |";
                string divider = new string('-', header.Length);

                Log(LogType.Section, "\nTop Execution Times (Slowest):");
                Log(LogType.Section, divider);
                Log(LogType.Section, header);
                Log(LogType.Section, divider);

                foreach (var entry in _executionLog.Reverse())
                {
                    var execTime = $"{formatNumber(entry.Key)} ms";
                    string row = $"| {entry.Value.PadRight(methodNameWidth)} | {execTime.PadLeft(timeWidth)} |";
                    Log(LogType.Section, row);
                }
                Log(LogType.Section, divider);
            }
        }

        public void Dispose()
        {
            if (_logSummary)
            {
                PrintSummary();
            }

            if (_trackSlowest)
            {
                PrintSlowestSummary();
            }
        }

        private static void Log(LogType logType, string message)
        {
            ConsoleColor color = logType switch
            {
                LogType.Info => ConsoleColor.DarkMagenta,
                LogType.Warning => ConsoleColor.Yellow,
                LogType.Error => ConsoleColor.Red,
                LogType.Section => ConsoleColor.Cyan,
                _ => ConsoleColor.White
            };

            var logTime = DateTime.Now.ToString("HH:mm:ss.fff");

            Console.ForegroundColor = color;
            Console.WriteLine($"{logTime}: {message}");
            Console.ResetColor();
        }


        private enum LogType
        {
            Info,
            Warning,
            Error,
            Section
        }
    }
}