// FF.API/Infrastructure/HangfireAuthorizationFilter.cs
using Hangfire.Dashboard;

namespace FF.API.Infrastructure;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
            return false;

        return httpContext.User.IsInRole("Admin");
    }
}