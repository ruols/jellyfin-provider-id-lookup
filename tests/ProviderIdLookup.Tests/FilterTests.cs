using System.Reflection;
using System.Security.Claims;
using Jellyfin.Api.Controllers;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ProviderIdLookup;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ProviderIdLookup.Tests;

public class FilterTests
{
    private readonly Mock<ILibraryManager> library = new(MockBehavior.Strict);
    private readonly Mock<IUserManager> users = new(MockBehavior.Strict);
    private readonly Guid userId = Guid.NewGuid();
    private int calls;

    private ProviderIdLookupFilter Filter => new(library.Object, users.Object);

    private ActionExecutingContext Context(string? query = "Tmdb.1", string method = "GetItems", bool authenticated = true, bool apiKey = false)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.QueryString = query is null ? QueryString.Empty : QueryString.Create("AnyProviderIdEquals", query);
        var claims = apiKey ? new[] { new Claim("Jellyfin-IsApiKey", "true"), new Claim(ClaimTypes.Role, "Administrator") }
            : new[] { new Claim("Jellyfin-UserId", userId.ToString()) };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null));
        var descriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(ItemsController).GetTypeInfo(),
            MethodInfo = typeof(ItemsController).GetMethod(method)!,
            ActionName = method
        };
        var action = new ActionContext(http, new RouteData(), descriptor, new ModelStateDictionary());
        var args = new Dictionary<string, object?>
        {
            ["ids"] = Array.Empty<Guid>(), ["userId"] = null, ["startIndex"] = 0,
            ["limit"] = 10, ["recursive"] = true, ["parentId"] = null,
            ["searchTerm"] = null
        };
        return new ActionExecutingContext(action, [], args, new object());
    }

    private void SetupUser()
    {
        var user = new User("test", "auth", "reset") { Id = userId };
        users.Setup(m => m.GetUserById(userId)).Returns(user);
        library.Setup(m => m.ConfigureUserAccess(It.IsAny<InternalItemsQuery>(), user));
    }

    private ActionExecutionDelegate Next(ActionExecutingContext context, params Guid[] ids) => () =>
    {
        calls++;
        return Task.FromResult(new ActionExecutedContext(context, [], context.Controller)
        {
            Result = new OkObjectResult(new QueryResult<BaseItemDto>(0, ids.Length, ids.Select(id => new BaseItemDto { Id = id }).ToArray()))
        });
    };

    [Theory]
    [InlineData("GetItems")]
    [InlineData("GetItemsByUserIdLegacy")]
    public async Task ResolvesNativeIdsThenPreservesPagingAndOtherArguments(string method)
    {
        SetupUser();
        var id = Guid.NewGuid();
        var context = Context("Tmdb.1,Tmdb.2,Imdb.tt3", method);
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Callback<InternalItemsQuery>(q =>
        {
            Assert.NotNull(q.User);
            Assert.Empty(q.ItemIds);
            Assert.Null(q.StartIndex);
            Assert.Equal(ProviderIdLookupFilter.MaxCandidates + 1, q.Limit);
            Assert.False(q.GroupByPresentationUniqueKey);
            Assert.Equal(["1", "2"], q.HasAnyProviderIds!["Tmdb"]);
            Assert.Equal(["tt3"], q.HasAnyProviderIds["Imdb"]);
        }).Returns([id]);
        await Filter.OnActionExecutionAsync(context, Next(context, id));
        Assert.Equal([id], Assert.IsType<Guid[]>(context.ActionArguments["ids"]));
        Assert.Equal(10, context.ActionArguments["limit"]);
        Assert.Equal(1, calls);
        library.Verify(m => m.ConfigureUserAccess(It.IsAny<InternalItemsQuery>(), It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task NoMatchNeverRunsUnfilteredNativeAction()
    {
        SetupUser();
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([]);
        var context = Context();
        context.ActionArguments["startIndex"] = 5;
        await Filter.OnActionExecutionAsync(context, Next(context));
        var data = Assert.IsType<QueryResult<BaseItemDto>>(Assert.IsType<OkObjectResult>(context.Result).Value);
        Assert.Empty(data.Items);
        Assert.Equal(0, data.TotalRecordCount);
        Assert.Equal(5, data.StartIndex);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingIdsAreIntersected(bool intersects)
    {
        SetupUser();
        var id = Guid.NewGuid();
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([id, Guid.NewGuid()]);
        var context = Context();
        context.ActionArguments["ids"] = new[] { intersects ? id : Guid.NewGuid() };
        await Filter.OnActionExecutionAsync(context, Next(context, id));
        Assert.Equal(intersects ? 1 : 0, calls);
        if (intersects) Assert.Equal([id], Assert.IsType<Guid[]>(context.ActionArguments["ids"]));
    }

    [Theory]
    [InlineData("Tmdb.")]
    [InlineData("")]
    public async Task MalformedInputDoesNotQuery(string query)
    {
        var context = Context(query);
        await Filter.OnActionExecutionAsync(context, Next(context));
        Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task OrdinaryRequestPassesThrough()
    {
        var context = Context(null);
        await Filter.OnActionExecutionAsync(context, Next(context));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OtherEndpointPassesThrough()
    {
        var context = Context();
        context.ActionDescriptor = new ActionDescriptor();
        await Filter.OnActionExecutionAsync(context, Next(context));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnauthenticatedFailsClosed()
    {
        var context = Context(authenticated: false);
        await Filter.OnActionExecutionAsync(context, Next(context));
        Assert.IsType<UnauthorizedResult>(context.Result);
    }

    [Fact]
    public async Task CannotImpersonateAnotherUserEvenWhenNoMatch()
    {
        var context = Context();
        context.ActionArguments["userId"] = Guid.NewGuid();
        await Filter.OnActionExecutionAsync(context, Next(context));
        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public async Task UnknownUserFailsClosed()
    {
        users.Setup(m => m.GetUserById(userId)).Returns((User?)null);
        var context = Context();
        await Filter.OnActionExecutionAsync(context, Next(context));
        Assert.IsType<NotFoundResult>(context.Result);
    }

    [Fact]
    public async Task ApiKeyWithoutUserUsesNativeGlobalScope()
    {
        var id = Guid.NewGuid();
        library.Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(q => q.User == null))).Returns([id]);
        var context = Context(apiKey: true);
        await Filter.OnActionExecutionAsync(context, Next(context, id));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SearchTermCombinationRejected()
    {
        var context = Context();
        context.ActionArguments["searchTerm"] = "another movie";
        await Filter.OnActionExecutionAsync(context, Next(context));
        Assert.IsType<BadRequestObjectResult>(context.Result);
    }

    [Fact]
    public async Task TooManyCandidatesNotSilentlyTruncated()
    {
        SetupUser();
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns(Enumerable.Range(0, ProviderIdLookupFilter.MaxCandidates + 1).Select(_ => Guid.NewGuid()).ToArray());
        var context = Context();
        await Filter.OnActionExecutionAsync(context, Next(context));
        Assert.IsType<UnprocessableEntityObjectResult>(context.Result);
    }

    [Fact]
    public async Task UnexpectedNativeResultIsBlocked()
    {
        SetupUser();
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Returns([Guid.NewGuid()]);
        var context = Context();
        ActionExecutedContext? executed = null;
        await Filter.OnActionExecutionAsync(context, async () => executed = await Next(context, Guid.NewGuid())());
        Assert.Equal(502, Assert.IsType<StatusCodeResult>(executed!.Result).StatusCode);
    }

    [Fact]
    public async Task PermissionsAreConfiguredBeforeAddingScopeAndOriginalIdOrderIsKept()
    {
        SetupUser();
        var libraryId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        library.Setup(m => m.ConfigureUserAccess(It.IsAny<InternalItemsQuery>(), It.IsAny<User>()))
            .Callback<InternalItemsQuery, User>((q, _) =>
            {
                Assert.Equal(Guid.Empty, q.ParentId);
                Assert.Empty(q.ItemIds);
                q.TopParentIds = [libraryId];
            });
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => Assert.Equal([libraryId], q.TopParentIds))
            .Returns([second, first]);
        var context = Context();
        context.ActionArguments["ids"] = new[] { first, second };
        await Filter.OnActionExecutionAsync(context, Next(context, first, second));
        Assert.Equal([first, second], Assert.IsType<Guid[]>(context.ActionArguments["ids"]));
    }

    [Fact]
    public async Task ExplicitParentIsAppliedBeforeLookup()
    {
        var parent = new Folder { Id = Guid.NewGuid() };
        var context = Context(apiKey: true);
        context.ActionArguments["parentId"] = parent.Id;
        context.ActionArguments["recursive"] = false;
        library.Setup(m => m.GetItemById(parent.Id)).Returns(parent);
        library.Setup(m => m.GetItemIds(It.Is<InternalItemsQuery>(q => q.ParentId == parent.Id))).Returns([]);
        await Filter.OnActionExecutionAsync(context, Next(context));
        library.Verify(m => m.GetItemIds(It.IsAny<InternalItemsQuery>()), Times.Once);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NativeLookupErrorNeverFallsBackToBroadQuery()
    {
        SetupUser();
        library.Setup(m => m.GetItemIds(It.IsAny<InternalItemsQuery>())).Throws(new InvalidOperationException("database unavailable"));
        var context = Context();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Filter.OnActionExecutionAsync(context, Next(context)));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void GlobalFilterRegistrationUsesNativeMvcOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new ServiceRegistrator().RegisterServices(services, null!);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
        Assert.Contains(options.Filters, f => f is ServiceFilterAttribute s && s.ServiceType == typeof(ProviderIdLookupFilter));
    }
}
