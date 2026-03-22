using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Common.Behaviors;

public class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly Stopwatch _timer = new();
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger = logger;

    // Warn if a request takes longer than this
    private const int WarningThresholdMs = 500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _timer.Start();

        var response = await next();

        _timer.Stop();

        var elapsed = _timer.ElapsedMilliseconds;

        if (elapsed > WarningThresholdMs)
        {
            var requestName = typeof(TRequest).Name;

            _logger.LogWarning(
                "FF Slow Request: {RequestName} took {ElapsedMs}ms {@Request}",
                requestName,
                elapsed,
                request);
        }

        return response;
    }
}