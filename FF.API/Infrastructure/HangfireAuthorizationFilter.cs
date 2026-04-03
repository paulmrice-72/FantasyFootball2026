using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;

namespace FF.API.Infrastructure;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Cookie auth — browser navigation
        var result = httpContext.AuthenticateAsync("HangfireCookie").GetAwaiter().GetResult();
        if (result.Succeeded && result.Principal?.IsInRole("Admin") == true)
            return true;

        // JWT fallback
        if (httpContext.User.Identity?.IsAuthenticated == true &&
            httpContext.User.IsInRole("Admin"))
            return true;

        return false;
    }
}