using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;

namespace Jattac.Libs.Profiling
{
    public static class ExecutionTimeProxyExtensions
    {
        /// <summary>
        /// Scans assemblies for services decorated with <see cref="MeasureExecutionTimeAttribute"/> and registers them for profiling.
        /// </summary>
        /// <remarks>
        /// This is the recommended, automated way to configure profiling. It will discover attributes on either interfaces or classes.
        /// If an attribute is found, the service will be registered with a Scoped lifetime, overwriting any previous registrations for that service type.
        /// </remarks>
        /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
        /// <param name="configuration">The application's configuration, used to check if profiling is enabled.</param>
        /// <param name="assembliesToScan">The assemblies to scan. If not provided, the calling assembly will be scanned.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddProfiledServices(this IServiceCollection services, IConfiguration configuration, params Assembly[] assembliesToScan)
        {
            if (assembliesToScan == null || assembliesToScan.Length == 0)
            {
                assembliesToScan = new[] { Assembly.GetCallingAssembly() };
            }

            var config = new ExecutionTimeConfig();
            configuration.GetSection("ExecutionTime").Bind(config);

            var types = assembliesToScan.SelectMany(a => a.GetTypes()).ToList();
            var interfaces = types.Where(t => t.IsInterface);

            foreach (var interfaceType in interfaces)
            {
                var implementationType = types.FirstOrDefault(t => t.IsClass && !t.IsAbstract && interfaceType.IsAssignableFrom(t));
                if (implementationType != null)
                {
                    bool hasAttribute = interfaceType.GetCustomAttribute<MeasureExecutionTimeAttribute>() != null ||
                                        implementationType.GetCustomAttribute<MeasureExecutionTimeAttribute>() != null;

                    if (hasAttribute)
                    {
                        if (config.EnableTiming)
                        {
                            // Corrected reflection call for extension method
                            var profileScopedMethod = typeof(ExecutionTimeProxyExtensions)
                                .GetMethod(
                                    nameof(ProfileScoped),
                                    BindingFlags.Public | BindingFlags.Static,
                                    null,
                                    new Type[] { typeof(IServiceCollection), typeof(IConfiguration) },
                                    null
                                )! // Added !
                                .MakeGenericMethod(interfaceType, implementationType)!; // Added !

                            // Invoke the extension method. The first argument in the array is the 'this' instance.
                            profileScopedMethod.Invoke(null, new object[] { services, configuration });
                        }
                        else
                        {
                            // If profiling is disabled, register the service normally without the proxy
                            services.AddScoped(interfaceType, implementationType);
                        }
                    }
                }
            }

            return services;
        }

        /// <summary>
        /// Registers a single service for profiling with a Scoped lifetime.
        /// </summary>
        /// <remarks>
        /// Use this method for manual registration if you need more control than the assembly scanning provides (e.g., for interfaces with multiple implementations).
        /// If profiling is disabled in the configuration, this will fall back to a standard `AddScoped` registration.
        /// </remarks>
        /// <typeparam name="TInterface">The interface type of the service.</typeparam>
        /// <typeparam name="TImplementation">The implementation type of the service.</typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
        /// <param name="configuration">The application's configuration, used to check if profiling is enabled.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
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
                    var proxy = ExecutionTimeProxy<TInterface>.Create(implementation, config);
                    return proxy;
                });
        }
    }
}