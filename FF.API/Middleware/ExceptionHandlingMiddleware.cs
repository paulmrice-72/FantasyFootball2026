using System.Net;
using System.Text.Json;

namespace FF.API.Middleware;

/*
PSEUDOCODE (detailed plan):
- Create a single cached JsonSerializerOptions instance with CamelCase naming policy:
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
- Keep the existing middleware constructor and fields intact.
- In InvokeAsync, preserve current try/catch behavior that logs and delegates to HandleExceptionAsync.
- In HandleExceptionAsync:
    - Determine appropriate HttpStatusCode based on exception type.
    - Build the anonymous response object (status, error, message, traceId).
    - Set response content type and status code.
    - Use JsonSerializer.Serialize(response, s_jsonOptions) to avoid creating a new JsonSerializerOptions per call.
- This addresses CA1869 by caching and reusing the JsonSerializerOptions instance.
*/

public class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger = logger;

    // Cached JsonSerializerOptions to avoid creating a new instance on every serialization (fixes CA1869).
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {RequestMethod} {RequestPath}",
                context.Request.Method,
                context.Request.Path);

            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var statusCode = exception switch
        {
            ArgumentNullException => HttpStatusCode.BadRequest,
            ArgumentException => HttpStatusCode.BadRequest,
            KeyNotFoundException => HttpStatusCode.NotFound,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            _ => HttpStatusCode.InternalServerError
        };

        var response = new
        {
            status = (int)statusCode,
            error = statusCode.ToString(),
            message = exception.Message,
            traceId = context.TraceIdentifier
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response, s_jsonOptions));
    }
}