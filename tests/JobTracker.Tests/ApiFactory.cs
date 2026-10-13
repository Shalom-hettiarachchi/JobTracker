using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace JobTracker.Tests;

/// <summary>
/// Runs the real app in memory against its own empty database: SQLite by default, or SQL Server
/// when <c>JOBTRACKER_TEST_SQLSERVER</c> holds a connection string (as it does in CI).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "Test-Passw0rd";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _databaseName = $"JobTrackerTests_{Guid.NewGuid():N}";
    private SqliteConnection? _keepAlive;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var sqlServer = Environment.GetEnvironmentVariable("JOBTRACKER_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(sqlServer))
        {
            var connectionString = $"DataSource=file:{_databaseName}?mode=memory&cache=shared";
            // A shared in-memory database disappears once its last connection closes.
            _keepAlive = new SqliteConnection(connectionString);
            _keepAlive.Open();

            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        }
        else
        {
            var connectionString = new SqlConnectionStringBuilder(sqlServer) { InitialCatalog = _databaseName };
            builder.UseSetting("Database:Provider", "SqlServer");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString.ConnectionString);
        }
    }

    /// <summary>Registers a new user and returns a client that sends their bearer token.</summary>
    public async Task<HttpClient> CreateClientForNewUserAsync()
    {
        var client = CreateClient();
        var credentials = new { email = $"user-{Guid.NewGuid():N}@example.com", password = Password };

        var register = await client.PostAsJsonAsync("/api/auth/register", credentials);
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", credentials);
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _keepAlive?.Dispose();
    }
}
