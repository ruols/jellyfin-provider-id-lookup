using Jellyfin.Api.Controllers;
using Jellyfin.Database.Implementations;
using Jellyfin.Plugin.ProviderIdLookup;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Xunit;

namespace ProviderIdLookup.Tests;

public class TargetContractTests
{
    [Fact]
    public void TargetIsActually122AndRequiredNativeApisExist()
    {
        Assert.Equal(new Version(12, 2, 0, 0), typeof(InternalItemsQuery).Assembly.GetName().Version);
        // Jellyfin.Api keeps its own 1.0 assembly version in this image.
        Assert.Equal(new Version(1, 0, 0, 0), typeof(ItemsController).Assembly.GetName().Version);
        Assert.NotNull(typeof(InternalItemsQuery).GetProperty("HasAnyProviderIds"));
        Assert.NotNull(typeof(ILibraryManager).GetMethod("GetItemIds", [typeof(InternalItemsQuery)]));
        Assert.NotNull(typeof(ILibraryManager).GetMethod("ConfigureUserAccess"));
        Assert.NotNull(typeof(JellyfinQueryHelperExtensions).GetMethod("WhereHasAnyProviderIds"));
        foreach (var name in new[] { "GetItems", "GetItemsByUserIdLegacy" })
            Assert.Contains(typeof(ItemsController).GetMethod(name)!.GetParameters(), p => p.Name == "ids" && p.ParameterType == typeof(Guid[]));
        Assert.Equal(Guid.Parse("ead7d651-c6a7-4d6d-9fe1-d32b7cc152c6"), new Plugin().Id);
    }
}
