using FF.Application.Common.Behaviors;
using FF.Application.Features.Projections.Commands.CalculateProjections;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace FF.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Use typeof anchor instead of Assembly.GetExecutingAssembly()
        // to guarantee the correct assembly is scanned regardless of call context
        var appAssembly = typeof(CalculateProjectionsCommandHandler).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(appAssembly);
        });

        services.AddValidatorsFromAssembly(appAssembly);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

        return services;
    }
}