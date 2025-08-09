# Jattac.Libs.Profiling

A lightweight, dependency-injection-friendly library for profiling method execution times in .NET applications using a dynamic proxy and attributes.

This tool allows you to non-invasively measure the performance of specific methods or entire classes with minimal configuration.

## Features

-   **Automatic Service Discovery:** Scans your assemblies to find and register services for profiling with a single command.
-   **Flexible Attribute-Based Control:** Add an attribute to interfaces, classes, or methods to control profiling.
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

To mark services for profiling, add the `[MeasureExecutionTime]` attribute. You can place it on an **interface**, a **class**, or an individual **method**.

**Example:**
```csharp
using Jattac.Libs.Profiling;

// You can place the attribute on the interface...
[MeasureExecutionTime(logSummary: true)]
public interface IMyService
{
    void DoWork();
    
    [MeasureExecutionTime(logSummary: false)] // Or on a specific method
    Task DoSpecificWorkAsync();
}

// ...or you can place it on the class.
[MeasureExecutionTime(trackSlowest: true)]
public class MyOtherService : IMyOtherService 
{
    //...
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
-   `EnableTiming: false`: Profiling is disabled, and services will be registered without the proxy, incurring no performance overhead.

### 4. Configure Your Services

In your application's startup file (e.g., `Program.cs`), use the `AddProfiledServices` extension method to automatically find and register all the services you marked with the attribute.

**Example in `Program.cs` (.NET 6+):**

```csharp
using Jattac.Libs.Profiling;

var builder = WebApplication.CreateBuilder(args);

// Automatically find and register all services marked for profiling
// in the current assembly.
builder.Services.AddProfiledServices(builder.Configuration);

// ... other services

var app = builder.Build();
```

The `AddProfiledServices` method will discover any service where the interface or the implementation class has the `[MeasureExecutionTime]` attribute.

### Manual Registration

If you prefer to register services manually, you can still use the `ProfileScoped` method.

```csharp
builder.Services.ProfileScoped<IMyManualService, MyManualService>(builder.Configuration);
```

## Example Output

When your application runs and the profiled methods are called, you will see output in the console.

#### Individual Method Log

```
14:25:10.152: Method MyService.DoWork took 52 ms
14:25:10.355: Method MyService.DoSpecificWorkAsync took 201 ms
```

#### Slowest Calls Summary

```
Top Execution Times (Slowest):
-----------------------------
| MyService.DoSpecificWorkAsync |    201 ms |
| MyService.DoWork      |     52 ms |
-----------------------------
```

#### Execution Summary

```
Execution Summary:
-------------------------------------------
| Method Name         | Count |  Avg ms  | Min ms | Max ms |
-------------------------------------------
| MyService.DoSpecificWorkAsync |     1 |     201 ms |    201 ms |    201 ms |
| MyService.DoWork      |     1 |      52 ms |     52 ms |     52 ms |
-------------------------------------------
```

## Advanced Notes & Important Behaviors

Please be aware of the following behaviors to ensure the library works as expected in your application.

### Dependency Injection: Attribute Wins

If the auto-discovery feature finds a service marked for profiling, **it will overwrite any existing manual registration for that service.**

This is an intentional design choice. The presence of the `[MeasureExecutionTime]` attribute is treated as the definitive source of truth, signaling a clear intent to profile the service.

### Attribute Placement and Precedence

The library is flexible about where you place the `[MeasureExecutionTime]` attribute. Here is the order of precedence:

*   **For enabling profiling on all methods of a service:** An attribute on the **class** takes precedence over an attribute on the **interface**.
*   **For profiling a specific method:** An attribute on either the **class method** or the **interface method** will cause it to be profiled (if profiling isn't already enabled for the whole class).

The `AddProfiledServices()` auto-discovery method will find services if the attribute is placed on either the interface or the class.

### Multiple Implementations

The `AddProfiledServices()` auto-discovery feature is designed for scenarios where there is a **one-to-one mapping** between an interface and its implementation. If you have an interface with multiple implementations, you must use **manual registration** for each one.

### Service Lifetime

All services registered by this library, whether through auto-discovery or manually, are registered with a **Scoped lifetime**.