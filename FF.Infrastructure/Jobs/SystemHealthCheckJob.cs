using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class SystemHealthCheckJob(ILogger<SystemHealthCheckJob> logger)
{
    private readonly ILogger<SystemHealthCheckJob> _logger = logger;

    public void Execute()
    {
        _logger.LogInformation("System health check job executed at {Time}", DateTime.UtcNow);
    }
}