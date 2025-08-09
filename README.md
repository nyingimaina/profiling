# Jattac.Libs.Profiling

A lightweight, dependency-injection-friendly library for profiling method execution times in .NET applications using a dynamic proxy and attributes.

This tool allows you to non-invasively measure the performance of specific methods or entire classes with minimal configuration.

## Features

-   **Automatic Service Discovery:** Scans your assemblies to find and register services for profiling with a single command.
-   **Attribute-Based:** Simply add an attribute to your interfaces to start profiling.
-   **DI Integration:** Easily registers profiled services with your `IServiceCollection`.
-   **Async Support:** Seamlessly profiles both synchronous and asynchronous (Task-based) methods.
-   **Configuration-Based:** Enable or disable profiling globally via your `appsettings.json` file.
-   **Detailed Summaries:** Provides clear console logs, including execution time summaries and a list of the slowest calls for each service instance.

## Getting Started

Follow these steps to integrate the execution time profiler into your .NET application.

### 1. Install the NuGet Package

Install the package from NuGet using the .NET CLI or the NuGet Package Manager.

**.NET CLI:**
```bash
dotnet add package Jattac.Libs.Profiling
```

**Package Manager Console:**
```powershell
Install-Package Jattac.Libs.Profiling
```

### 2. Apply the Attribute

To mark services for profiling, add the `[MeasureExecutionTime]` attribute to the service's **interface**. The auto-discovery system works by finding interfaces with this attribute.

```csharp
using Jattac.Libs.Profiling;

[MeasureExecutionTime(logSummary: true, trackSlowest: true)]
public interface IMyService
{
    void DoFastWork();
    Task DoSlowWorkAsync();
}

public class MyService : IMyService
{
    public void DoFastWork() => Thread.Sleep(50);
    public async Task DoSlowWorkAsync() => await Task.Delay(200);
}
```

-   `logSummary: true`: Generates a summary table of all calls for the service instance when it's disposed.
-   `trackSlowest: true`: Keeps track of the slowest method calls for the service instance.

### 3. Configure Your Application

Add a section to your `appsettings.json` file to control whether profiling is enabled.

```json
{
  "ExecutionTime": {
    "EnableTiming": true
  }
}
```

-   `EnableTiming: true`: Profiling is enabled.
-   `EnableTiming: false`: Profiling is disabled, and the services will be registered without the proxy, incurring no performance overhead.

### 4. Configure Your Services

In your application's startup file (e.g., `Program.cs`), use the `AddProfiledServices` extension method to automatically find and register all the services you marked with the attribute.

**Example in `Program.cs` (.NET 6+):**

```csharp
using Jattac.Libs.Profiling;

var builder = WebApplication.CreateBuilder(args);

// Automatically find and register all profiled services
// in the current assembly.
builder.Services.AddProfiledServices(builder.Configuration);

// ... other services

var app = builder.Build();
```

By default, this method scans the assembly that calls it. You can also specify other assemblies to scan.

```csharp
// Scan the calling assembly and another specific assembly
builder.Services.AddProfiledServices(
    builder.Configuration,
    typeof(SomeOtherProject.IMarker).Assembly
);
```

### Manual Registration

If you prefer to register services manually, you can still use the `ProfileScoped` method.

```csharp
builder.Services.ProfileScoped<IMyManualService, MyManualService>(builder.Configuration);
```

## Example Output

When your application runs and the profiled methods are called, you will see output in the console.

#### Individual Method Log

```
14:25:10.152: Method MyService.DoFastWork took 52 ms
14:25:10.355: Method MyService.DoSlowWorkAsync took 201 ms
```

#### Slowest Calls Summary

```
Top Execution Times (Slowest):
-----------------------------
| MyService.DoSlowWorkAsync |    201 ms |
| MyService.DoFastWork      |     52 ms |
-----------------------------
```

#### Execution Summary

```
Execution Summary:
-------------------------------------------
| Method Name         | Count |  Avg ms  | Min ms | Max ms |
-------------------------------------------
| MyService.DoSlowWorkAsync |     1 |     201 ms |    201 ms |    201 ms |
| MyService.DoFastWork      |     1 |      52 ms |     52 ms |     52 ms |
-------------------------------------------
```

## Advanced Notes & Important Behaviors

Please be aware of the following behaviors to ensure the library works as expected in your application.

### Dependency Injection: Attribute Wins

If you use the `AddProfiledServices()` auto-discovery feature, it will register any interface decorated with `[MeasureExecutionTime]`. If you have also manually registered a service for that same interface, **the library's registration will overwrite yours.**

This is an intentional design choice. The presence of the `[MeasureExecutionTime]` attribute is treated as the definitive source of truth, signaling a clear intent to profile the service.

**Example:**
```csharp
// You manually register a service
builder.Services.AddSingleton<IMyService, MyService>();

// You also call the auto-discovery method, and IMyService has the attribute
builder.Services.AddProfiledServices(builder.Configuration);

// Result: IMyService will be registered as a Scoped, profiled service,
// overwriting the manual Singleton registration.
```

### Multiple Implementations

The `AddProfiledServices()` auto-discovery feature is designed for scenarios where there is a **one-to-one mapping** between an interface and its implementation.

If you have an interface with multiple implementations, the auto-discovery will only find and register the *first* one it encounters, which can be unpredictable.

**In this scenario, you must use manual registration** for each implementation you wish to profile:
```csharp
// For interfaces with multiple implementations, register manually:
builder.Services.ProfileScoped<IMyInterface, FirstImplementation>(builder.Configuration);
// Note: You would need a way to distinguish which implementation to resolve,
// which is outside the scope of this library.
```

### Attribute Placement for Auto-Discovery

The `AddProfiledServices()` method discovers services by looking for the `[MeasureExecutionTime]` attribute on **interfaces only**.

While the attribute can be placed on a concrete class, the auto-discovery method will ignore it. Attributes on classes will only be effective if you register the service manually using `ProfileScoped()`.

### Service Lifetime

All services registered by this library, whether through auto-discovery (`AddProfiledServices`) or manually (`ProfileScoped`), are registered with a **Scoped lifetime**.