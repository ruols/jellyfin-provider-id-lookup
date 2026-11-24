using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProviderIdLookup;

internal sealed class RequestTraceStartupFilter(ILogger<RequestTraceStartupFilter> logger) : IStartupFilter
{
    internal static readonly object LookupEntered = new();

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, downstream) =>
        {
            var endpoint = Classify(context.Request.Path);
            if (endpoint is null)
            {
                await downstream(context).ConfigureAwait(false);
                return;
            }

            var id = context.TraceIdentifier;
            var start = Stopwatch.GetTimestamp();
            var hasProviderId = context.Request.Query.ContainsKey("AnyProviderIdEquals");
            logger.LogInformation("Provider ID trace {TraceId} arrived: {Method} {Endpoint}; provider filter={HasProviderFilter}",
                id, context.Request.Method, endpoint, hasProviderId);

            context.Response.OnStarting(() =>
            {
                logger.LogInformation("Provider ID trace {TraceId} response: {StatusCode}; lookup entered={LookupEntered}; headers {ElapsedMs:F1} ms",
                    id, context.Response.StatusCode, context.Items.ContainsKey(LookupEntered),
                    Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                return Task.CompletedTask;
            });
            context.Response.OnCompleted(() =>
            {
                logger.LogInformation("Provider ID trace {TraceId} completed: {StatusCode}; total {ElapsedMs:F1} ms; canceled={Canceled}",
                    id, context.Response.StatusCode, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    context.RequestAborted.IsCancellationRequested);
                return Task.CompletedTask;
            });

            try
            {
                await downstream(context).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                logger.LogWarning("Provider ID trace {TraceId} pipeline ended with {ErrorType} after {ElapsedMs:F1} ms",
                    id, error.GetType().Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                throw;
            }
        });
        next(app);
    };

    private static string? Classify(PathString path)
    {
        // Log patterns only. Actual paths can embed media and user identifiers.
        var parts = (path.Value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p.Equals("Images", StringComparison.OrdinalIgnoreCase)))
            return "Images";
        if (parts.LastOrDefault()?.Equals("Items", StringComparison.OrdinalIgnoreCase) is true)
            return "Items list";
        if (parts.LastOrDefault()?.Equals("Episodes", StringComparison.OrdinalIgnoreCase) is true)
            return "Episodes";
        if (parts.LastOrDefault()?.Equals("Seasons", StringComparison.OrdinalIgnoreCase) is true)
            return "Seasons";
        if (parts.Any(p => p.Equals("Items", StringComparison.OrdinalIgnoreCase)))
            return "Item details or action";
        return null;
    }
}
