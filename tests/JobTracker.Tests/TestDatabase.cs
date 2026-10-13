using JobTracker.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Tests;

/// <summary>A private in-memory SQLite database for one test, exposed the way the services expect it.</summary>
public sealed class TestDatabase : IDbContextFactory<ApplicationDbContext>, IDisposable
{
    // The database lives only as long as this connection stays open.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public TestDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public ApplicationDbContext CreateDbContext() => new(_options);

    /// <summary>Adds a user row (applications have a foreign key to it) and returns its id.</summary>
    public string AddUser(string name)
    {
        using var db = CreateDbContext();
        var user = new ApplicationUser { UserName = name, Email = $"{name}@example.com" };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    public void Dispose() => _connection.Dispose();
}
