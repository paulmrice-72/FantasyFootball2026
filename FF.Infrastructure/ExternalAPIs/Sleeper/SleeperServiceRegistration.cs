// FF.Infrastructure/ExternalApis/Sleeper/SleeperServiceRegistration.cs
//
// Registers the Sleeper API client with the DI container.
// This gets called from FF.Infrastructure/DependencyInjection.cs
// Add this line to your existing AddInfrastructure() method:
//
//     services.AddSleeperApiClient();
//
// HOW THE POLLY RETRY WORKS:
// If a Sleeper API call fails with a transient error (network blip, 5xx),
// Polly will automatically retry it. The wait times are:
//   Attempt 1 fails → wait 1 second  → retry
//   Attempt 2 fails → wait 4 seconds → retry
//   Attempt 3 fails → wait 9 seconds → retry
//   Attempt 4 fails → give up, throw exception
// This is "exponential backoff" - each wait = attempt^2 seconds.

using FF.Infrastructure.ExternalApis.Sleeper;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Refit;

namespace FF.Infrastructure.ExternalApis.Sleeper;

public static class SleeperServiceRegistration
{
    private const string SleeperBaseUrl = "https://api.sleeper.app";

    public static IServiceCollection AddSleeperApiClient(this IServiceCollection services)
    {
        services
            .AddRefitClient<ISleeperApiClient>(new RefitSettings
            {
                // Use System.Text.Json (built into .NET 9, no extra package needed)
                ContentSerializer = new SystemTextJsonContentSerializer()
            })
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(SleeperBaseUrl);

                // Sleeper asks clients to identify themselves
                client.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "FantasyCombine.AI/1.0 (contact@fantasycombine.ai)");

                // Reasonable timeout - the /v1/players/nfl endpoint returns ~2MB
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddPolicyHandler(GetRetryPolicy());

        return services;
    }

    /// <summary>
    /// Retry policy: 3 retries with exponential backoff on transient HTTP errors.
    /// Transient errors = network failures, 5xx server errors, 408 timeouts.
    /// Does NOT retry on 4xx client errors (bad request, not found, etc.)
    /// </summary>
    private static Polly.Retry.AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    // This logs to the console during development so you can see retries happening
                    Console.WriteLine(
                        $"[Sleeper API] Retry {retryAttempt} after {timespan.TotalSeconds}s " +
                        $"due to: {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
                });
    }
}