using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class SystemHealthCheckJob
{
    private readonly ILogger<SystemHealthCheckJob> _logger;

    public SystemHealthCheckJob(ILogger<SystemHealthCheckJob> logger)
    {
        _logger = logger;
    }

    public void Execute()
    {
        _logger.LogInformation("System health check job executed at {Time}", DateTime.UtcNow);
    }
}