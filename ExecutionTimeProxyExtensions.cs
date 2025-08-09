using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;

namespace Jattac.Libs.Profiling
{
    public static class ExecutionTimeProxyExtensions
    {
        public static IServiceCollection AddProfiledServices(this IServiceCollection services, IConfiguration configuration, params Assembly[] assembliesToScan)
        {
            if (assembliesToScan == null || assembliesToScan.Length == 0)
            {
                assembliesToScan = new[] { Assembly.GetCallingAssembly() };
            }

            var config = new ExecutionTimeConfig();
            configuration.GetSection("ExecutionTime").Bind(config);

            var types = assembliesToScan.SelectMany(a => a.GetTypes());

            var interfacesWithAttribute = types
                .Where(t => t.IsInterface && t.GetCustomAttribute<MeasureExecutionTimeAttribute>() != null);

            foreach (var interfaceType in interfacesWithAttribute)
            {
                var implementationType = types.FirstOrDefault(t => t.IsClass && !t.IsAbstract && interfaceType.IsAssignableFrom(t));
                if (implementationType != null)
                {
                    if (config.EnableTiming)
                    {
                        var method = typeof(ExecutionTimeProxyExtensions)
                            .GetMethod(nameof(ProfileScoped))
                            .MakeGenericMethod(interfaceType, implementationType);
                        
                        method.Invoke(null, new object[] { services, configuration });
                    }
                    else
                    {
                        services.AddScoped(interfaceType, implementationType);
                    }
                }
            }

            return services;
        }

        public static IServiceCollection ProfileScoped<TInterface, TImplementation>(
            this IServiceCollection services, IConfiguration configuration)
            where TImplementation : class, TInterface
            where TInterface : class
        {
            var config = new ExecutionTimeConfig();
            configuration.GetSection("ExecutionTime").Bind(config);

            if (config.EnableTiming == false)
            {
                return services.AddScoped<TInterface, TImplementation>();
            }

            return services
                .AddScoped<TImplementation>()
                .AddScoped(provider =>
                {
                    var implementation = provider.GetRequiredService<TImplementation>();
                    var proxy = ExecutionTimeProxy<TInterface>.Create(implementation);
                    return proxy;
                });
        }
    }
}