// FF.API/Infrastructure/HangfireAuthorizationFilter.cs
using Hangfire.Dashboard;

namespace FF.API.Infrastructure;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // Allow all requests in production — dashboard is Admin-only by obscurity
        // TODO: wire up proper cookie auth when needed
        return true;
    }
}