using IdCard.Domain.Enums;
using IdCard.Infrastructure.Services;

namespace IdCard.Api.Middleware;

public class ExceptionAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionAuditMiddleware> _logger;

    public ExceptionAuditMiddleware(RequestDelegate next, ILogger<ExceptionAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuditService audit)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            try
            {
                await audit.LogAsync(
                    "System.UnhandledException",
                    ActorType.System,
                    severity: AuditSeverity.Error,
                    details: new { ex.Message, path = context.Request.Path.Value },
                    correlationId: context.TraceIdentifier);
            }
            catch
            {
                // ignore audit failures
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { message = "An unexpected error occurred." });
        }
    }
}
