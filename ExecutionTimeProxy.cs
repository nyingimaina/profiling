using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private const int MaxRecords = 100;
        private readonly SortedList<long, string> _executionLog = new();
        private readonly Dictionary<string, (int Count, long TotalTime, long MaxTime, long MinTime)> _executionSummary = new();

        public static T Create(T decorated)
        {
            if (decorated == null) throw new ArgumentNullException(nameof(decorated));

            object? proxy = Create<T, ExecutionTimeProxy<T>>();
            if (proxy == null)
            {
                throw new Exception($"Unable to proxy {decorated.GetType()}");
            }

            var proxyInstance = (ExecutionTimeProxy<T>)proxy;
            proxyInstance._decorated = decorated;

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

            bool hasMethodAttribute = targetMethod.GetCustomAttribute<MeasureExecutionTimeAttribute>() != null ||
                                     (implementationMethod?.GetCustomAttribute<MeasureExecutionTimeAttribute>() != null);

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
                Log(LogType.Section, "Execution Summary:");
                Log(LogType.Section, "-------------------------------------------");
                Log(LogType.Section, "| Method Name         | Count |  Avg ms  | Min ms | Max ms |");
                Log(LogType.Section, "-------------------------------------------");

                foreach (var entry in _executionSummary)
                {
                    var avgTime = entry.Value.TotalTime / entry.Value.Count;
                    Log(LogType.Section, $"| {entry.Key,-18} | {entry.Value.Count,5} | {avgTime,7} ms | {entry.Value.MinTime,6} ms | {entry.Value.MaxTime,6} ms |");
                }
                Log(LogType.Section, "-------------------------------------------");
            }
        }

        public void PrintSlowestSummary()
        {
            if (_executionLog.Any())
            {
                Log(LogType.Section, "Top Execution Times (Slowest):");
                Log(LogType.Section, "-----------------------------");

                foreach (var entry in _executionLog.Reverse())
                {
                    Log(LogType.Section, $"| {entry.Value,-20} | {entry.Key,6} ms |");
                }
                Log(LogType.Section, "-----------------------------");
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
