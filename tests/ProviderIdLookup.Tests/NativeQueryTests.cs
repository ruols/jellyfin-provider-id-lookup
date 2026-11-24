using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ProviderIdLookup.Tests;

// Runs the EXACT image's provider predicate against a temporary SQLite database.
// This validates native OR/exact-match SQL, not the production library's authorization wiring.
public class NativeQueryTests
{
    private sealed class QueryDb(DbContextOptions<QueryDb> options) : DbContext(options)
    {
        public DbSet<BaseItemEntity> Items => Set<BaseItemEntity>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            var entity = model.Entity<BaseItemEntity>();
            foreach (var property in typeof(BaseItemEntity).GetProperties())
                if (property.Name is not ("Id" or "Type" or "Provider"))
                    entity.Ignore(property.Name);
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Provider).WithOne(p => p.Item).HasForeignKey(p => p.ItemId);
            model.Entity<BaseItemProvider>().HasKey(p => new { p.ItemId, p.ProviderId });
        }
    }

    [Fact]
    public void NativeSqlMatchesAnyExactProviderValueAcrossMediaKindsAndFreshMetadata()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new QueryDb(new DbContextOptionsBuilder<QueryDb>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        var items = new[] { Item("Movie", "Tmdb", "1"), Item("Series", "Tmdb", "2"), Item("Season", "Tvdb", "3"), Item("Episode", "Imdb", "tt4"), Item("Movie", "Tmdb", "10") };
        db.Items.AddRange(items);
        db.SaveChanges();
        var filter = new Dictionary<string, string[]> { ["Tmdb"] = ["1", "2"], ["Tvdb"] = ["3"], ["Imdb"] = ["tt4"] };
        var results = db.Items.WhereHasAnyProviderIds(filter).Select(e => e.Id).ToArray();
        Assert.Equal(items.Take(4).Select(i => i.Id).Order(), results.Order());
        Assert.Empty(db.Items.WhereHasAnyProviderIds(new Dictionary<string, string[]> { ["Tmdb"] = ["missing"] }));
        items[0].Provider!.Single().ProviderValue = "99";
        db.SaveChanges();
        Assert.DoesNotContain(items[0].Id, db.Items.WhereHasAnyProviderIds(filter).Select(e => e.Id).ToArray());
        Assert.Equal(items[0].Id, db.Items.WhereHasAnyProviderIds(new Dictionary<string, string[]> { ["Tmdb"] = ["99"] }).Single().Id);
    }

    private static BaseItemEntity Item(string kind, string provider, string value)
    {
        var item = new BaseItemEntity { Id = Guid.NewGuid(), Type = kind, Provider = [] };
        item.Provider.Add(new BaseItemProvider { Item = item, ItemId = item.Id, ProviderId = provider, ProviderValue = value });
        return item;
    }
}
