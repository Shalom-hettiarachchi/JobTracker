using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using JobTracker.Api;
using JobTracker.Components;
using JobTracker.Components.Account;
using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// The website signs in with cookies; the REST API uses bearer tokens.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddBearerToken(IdentityConstants.BearerScheme)
    .AddIdentityCookies();
builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// SQL Server is the real database. SQLite is kept for tests and for running without a database server.
var useSqlite = string.Equals(builder.Configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase);
// The factory serves the long-lived Blazor circuits; it also registers a scoped context for Identity.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    if (useSqlite)
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        // Retries cover a serverless Azure SQL database waking up from auto-pause.
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
            maxRetryCount: 8, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null));
    }
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDataProtection().PersistKeysToDbContext<ApplicationDbContext>();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddApiEndpoints();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<SampleData>();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddValidation();
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsSqlServer())
    {
        db.Database.Migrate();
    }
    else
    {
        // The migrations are written for SQL Server, so SQLite builds its schema from the model.
        db.Database.EnsureCreated();
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// API clients get bare status codes; only the website re-renders them as the "not found" page.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    site => site.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// REST API: /api/auth (register, login, refresh) and /api/v1 (the user's data).
app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>().WithTags("Auth");
app.MapJobTrackerApi();
app.MapOpenApi();
app.MapScalarApiReference("/api/docs");

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

app.MapGet("/export/applications.csv", async (ClaimsPrincipal user, JobService jobs) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var applications = await jobs.GetApplicationsAsync(userId, includeArchived: true);

    static string Cell(object? value)
    {
        var text = value?.ToString() ?? "";
        // Stop spreadsheet apps from treating user text as a formula.
        if (text.Length > 0 && "=+-@".Contains(text[0]))
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    var csv = new StringBuilder();
    csv.AppendLine("Company,Position,Stage,Priority,Location,Work mode,Type,Salary min,Salary max,Currency,Source,Applied on,Deadline,Tags,URL,Archived,Created,Updated");
    foreach (var a in applications.OrderBy(a => a.Stage).ThenBy(a => a.Company))
    {
        csv.AppendLine(string.Join(',',
            Cell(a.Company), Cell(a.Position), Cell(a.Stage.Label()), Cell(a.Priority), Cell(a.Location),
            Cell(a.WorkMode.Label()), Cell(a.EmploymentType.Label()), Cell(a.SalaryMin), Cell(a.SalaryMax),
            Cell(a.Currency), Cell(a.Source), Cell(a.AppliedOn?.ToString("yyyy-MM-dd")),
            Cell(a.Deadline?.ToString("yyyy-MM-dd")), Cell(a.Tags), Cell(a.JobUrl), Cell(a.IsArchived ? "Yes" : "No"),
            Cell(a.CreatedAt.Local().ToString("yyyy-MM-dd HH:mm")), Cell(a.UpdatedAt.Local().ToString("yyyy-MM-dd HH:mm"))));
    }

    var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    return Results.File(bytes, "text/csv", $"job-applications-{DateTime.Now:yyyy-MM-dd}.csv");
}).RequireAuthorization().ExcludeFromDescription();

app.Run();

// Lets the integration tests start the app with WebApplicationFactory<Program>.
public partial class Program;
