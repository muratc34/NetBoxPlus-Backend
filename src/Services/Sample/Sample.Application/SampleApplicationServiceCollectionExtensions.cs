using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Sample.Application;

public static class SampleApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and every handler and validator of the sample service.</summary>
    public static IServiceCollection AddSampleApplication(this IServiceCollection services)
        => services.AddCqrs(typeof(SampleApplicationServiceCollectionExtensions).Assembly);
}
