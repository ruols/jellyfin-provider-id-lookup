using System.Diagnostics;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Extensions;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProviderIdLookup;

public sealed class ProviderIdLookupFilter(
    ILibraryManager libraryManager,
    IUserManager userManager,
    ILogger<ProviderIdLookupFilter> logger) : IAsyncActionFilter
{
    public const int MaxCandidates = 4096;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || action.ControllerTypeInfo.AsType() != typeof(ItemsController)
            || action.MethodInfo.Name is not (nameof(ItemsController.GetItems) or "GetItemsByUserIdLegacy")
            || !HttpMethods.IsGet(context.HttpContext.Request.Method)
            || !context.HttpContext.Request.Query.TryGetValue("AnyProviderIdEquals", out var raw))
        {
            await next().ConfigureAwait(false);
            return;
        }

        context.HttpContext.Items[RequestTraceStartupFilter.LookupEntered] = true;
        var traceId = context.HttpContext.TraceIdentifier;
        logger.LogInformation("Provider ID trace {TraceId} entered lookup filter", traceId);

        // MVC authorization runs before action filters; also fail closed in direct invocations.
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        if (!context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(context.ModelState);
            return;
        }
        if (!ProviderIdParser.TryParse(raw, out var pairs))
        {
            context.Result = new BadRequestObjectResult("AnyProviderIdEquals requires 1–16 comma-separated Provider.Id values (maximum 2048 characters).");
            return;
        }
        if (!context.ActionArguments.TryGetValue("ids", out var idsArgument) || idsArgument is not Guid[] originalIds)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            return;
        }
        // This target's native SearchTerm path UNIONs search hits with Ids. Reject the combination
        // rather than broaden an exact lookup or invent different search/pagination semantics.
        if (context.ActionArguments.TryGetValue("searchTerm", out var search) && search is string term && !string.IsNullOrWhiteSpace(term))
        {
            context.Result = new BadRequestObjectResult("SearchTerm cannot be combined with AnyProviderIdEquals on this Jellyfin build.");
            return;
        }

        var principal = context.HttpContext.User;
        var authenticatedId = principal.GetUserId();
        var requestedId = Argument<Guid?>(context, "userId");
        var effectiveId = requestedId.GetValueOrDefault() == Guid.Empty ? authenticatedId : requestedId!.Value;
        // Matches the pinned build's internal RequestHelpers.GetUserId rule.
        if (effectiveId != authenticatedId && !principal.IsInRole(UserRoles.Administrator))
        {
            context.Result = new ForbidResult();
            return;
        }
        User? user = effectiveId == Guid.Empty ? null : userManager.GetUserById(effectiveId);
        if (effectiveId != Guid.Empty && user is null)
        {
            context.Result = new NotFoundResult();
            return;
        }
        if (user is null && !principal.GetIsApiKey())
        {
            context.Result = new BadRequestObjectResult("userId is required");
            return;
        }

        var query = new InternalItemsQuery(user)
        {
            Recursive = true,
            EnableTotalRecordCount = false,
            GroupByPresentationUniqueKey = false,
            Limit = MaxCandidates + 1,
            HasAnyProviderIds = pairs.GroupBy(p => p.Provider, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Value).ToArray(), StringComparer.OrdinalIgnoreCase)
        };
        // Configure access BEFORE adding ParentId or ItemIds: AddUserToQuery skips library scoping
        // once either is present in this build. Do not insert client Ids in the lookup query.
        if (user is not null)
            libraryManager.ConfigureUserAccess(query, user);

        var parentId = Argument<Guid?>(context, "parentId");
        if (parentId.HasValue && parentId != Guid.Empty)
        {
            var parent = libraryManager.GetItemById(parentId.Value);
            if (parent is null)
            {
                context.Result = new NotFoundResult();
                return;
            }
            if (user is not null && !parent.IsVisible(user))
            {
                context.Result = new ForbidResult();
                return;
            }
            if (Argument<bool?>(context, "recursive") is false)
                query.ParentId = parent.Id;
            else
            {
                query.AncestorIds = parent.GetIdsForAncestorQuery().ToArray();
                if (query.AncestorIds.Length == 0)
                {
                    context.Result = Empty(context);
                    return;
                }
            }
        }

        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        IReadOnlyList<Guid> candidates;
        try
        {
            candidates = libraryManager.GetItemIds(query);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning("Provider ID trace {TraceId} lookup failed during candidate query ({ErrorType}) after {ElapsedMs:F1} ms",
                traceId, error.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }

        var lookupMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (candidates.Count > MaxCandidates)
        {
            LogOutcome(traceId, "candidate limit exceeded", lookupMs, 0, 0);
            context.Result = new UnprocessableEntityObjectResult("Provider lookup matched too many items; use more specific provider IDs.");
            return;
        }
        var allowed = candidates.ToHashSet();
        if (originalIds.Length > 0)
            allowed.IntersectWith(originalIds);
        if (allowed.Count == 0)
        {
            LogOutcome(traceId, "no candidates", lookupMs, 0, 0);
            // Empty Ids means NO FILTER to Jellyfin. Never forward an empty match to the action.
            context.Result = Empty(context);
            return;
        }
        context.ActionArguments["ids"] = originalIds.Length > 0
            ? originalIds.Where(allowed.Contains).Distinct().ToArray()
            : allowed.ToArray();
        var nativeStarted = Stopwatch.GetTimestamp();
        var executed = await next().ConfigureAwait(false);
        var nativeMs = Stopwatch.GetElapsedTime(nativeStarted).TotalMilliseconds;
        if (executed.Exception is not null)
        {
            LogOutcome(traceId, "native action failed", lookupMs, nativeMs, 0);
            return;
        }
        // Defense against native query changes or another filter broadening the result.
        var payload = executed.Result switch
        {
            ObjectResult result => result.Value,
            JsonResult result => result.Value,
            _ => null
        };
        if (payload is QueryResult<BaseItemDto> resultData)
        {
            if (resultData.Items.Any(item => !allowed.Contains(item.Id)))
            {
                logger.LogError("Provider ID lookup returned an item outside the candidate set; response blocked.");
                executed.Result = new StatusCodeResult(StatusCodes.Status502BadGateway);
                LogOutcome(traceId, "response blocked", lookupMs, nativeMs, 0);
                return;
            }
            LogOutcome(traceId, resultData.Items.Count == 0 ? "empty page" : "matched", lookupMs, nativeMs, resultData.Items.Count);
        }
        else if (executed.Result is ObjectResult { StatusCode: null or >= 200 and < 300 })
        {
            logger.LogError("Provider ID lookup received an unexpected successful response type; response blocked.");
            executed.Result = new StatusCodeResult(StatusCodes.Status502BadGateway);
            LogOutcome(traceId, "response blocked", lookupMs, nativeMs, 0);
        }
        else
        {
            LogOutcome(traceId, "native action returned no item payload", lookupMs, nativeMs, 0);
        }
    }

    private void LogOutcome(string traceId, string outcome, double lookupMs, double nativeMs, int returned) =>
        logger.LogInformation(
            "Provider ID lookup: {Outcome} [trace {TraceId}]; returned {Returned}; candidate query {LookupMs:F1} ms; native Items action {NativeMs:F1} ms",
            outcome, traceId, returned, lookupMs, nativeMs);

    private static T? Argument<T>(ActionExecutingContext context, string name) =>
        context.ActionArguments.TryGetValue(name, out var value) && value is T typed ? typed : default;

    private static OkObjectResult Empty(ActionExecutingContext context) =>
        new(new QueryResult<BaseItemDto>(Argument<int?>(context, "startIndex"), 0, []));
}
