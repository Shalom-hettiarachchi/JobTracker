using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobTracker.Api;
using JobTracker.Models;

namespace JobTracker.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = ApiFactory.Json;

    private static async Task<ApplicationResponse> CreateAsync(
        HttpClient client, string company = "Contoso", string stage = "Wishlist")
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/applications", new { company, position = "Backend Engineer", stage });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApplicationResponse>(Json))!;
    }

    // ---------- Authentication ----------

    [Theory]
    [InlineData("/api/v1/applications")]
    [InlineData("/api/v1/applications/1")]
    [InlineData("/api/v1/reminders")]
    [InlineData("/api/v1/stats")]
    public async Task Endpoints_return_401_without_a_token(string url)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(url);

        // A bare 401, not a redirect to the website's login page.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_rejected()
    {
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.Password });

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-Passw0rd" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registering_with_a_weak_password_returns_validation_errors()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { email = $"user-{Guid.NewGuid():N}@example.com", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Applications ----------

    [Fact]
    public async Task Create_returns_201_with_a_location_and_the_new_application()
    {
        var client = await factory.CreateClientForNewUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/applications", new
        {
            company = "Northwind Labs",
            position = "Senior .NET Developer",
            stage = "Applied",
            priority = "High",
            workMode = "Hybrid",
            salaryMin = 75000,
            salaryMax = 90000,
            tags = "backend, azure"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ApplicationResponse>(Json);
        Assert.NotNull(created);
        Assert.Equal($"/api/v1/applications/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Northwind Labs", created.Company);
        Assert.Equal(ApplicationStage.Applied, created.Stage);
        Assert.Equal(Priority.High, created.Priority);
        Assert.Equal(["backend", "azure"], created.Tags);
        Assert.NotNull(created.AppliedOn);
    }

    [Fact]
    public async Task Enums_are_sent_as_names_not_numbers()
    {
        var client = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(client, stage: "Interview");

        var json = await client.GetFromJsonAsync<JsonElement>($"/api/v1/applications/{created.Id}");

        Assert.Equal("Interview", json.GetProperty("application").GetProperty("stage").GetString());
    }

    [Theory]
    [InlineData("""{ "position": "Engineer" }""", "Company")]
    [InlineData("""{ "company": "Contoso" }""", "Position")]
    [InlineData("""{ "company": "Contoso", "position": "Engineer", "salaryMin": 90000, "salaryMax": 50000 }""", "SalaryMax")]
    [InlineData("""{ "company": "Contoso", "position": "Engineer", "jobUrl": "javascript:alert(1)" }""", "JobUrl")]
    public async Task Create_rejects_invalid_input_with_400(string body, string invalidField)
    {
        var client = await factory.CreateClientForNewUserAsync();

        var response = await client.PostAsync(
            "/api/v1/applications", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(problem.GetProperty("errors").EnumerateObject(), e => e.Name == invalidField);
        Assert.Empty((await client.GetFromJsonAsync<ApplicationResponse[]>("/api/v1/applications", Json))!);
    }

    [Fact]
    public async Task List_can_be_filtered_by_stage_and_search_text()
    {
        var client = await factory.CreateClientForNewUserAsync();
        await CreateAsync(client, "Contoso", "Applied");
        await CreateAsync(client, "Fabrikam", "Applied");
        await CreateAsync(client, "Contoso Health", "Offer");

        var applied = await client.GetFromJsonAsync<ApplicationResponse[]>("/api/v1/applications?stage=Applied", Json);
        var contoso = await client.GetFromJsonAsync<ApplicationResponse[]>("/api/v1/applications?search=contoso", Json);

        Assert.Equal(2, applied!.Length);
        Assert.All(applied, a => Assert.Equal(ApplicationStage.Applied, a.Stage));
        Assert.Equal(["Contoso", "Contoso Health"], contoso!.Select(a => a.Company).Order());
    }

    [Fact]
    public async Task Update_replaces_the_editable_fields()
    {
        var client = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/applications/{created.Id}", new
        {
            company = "Contoso Ltd",
            position = "Staff Engineer",
            location = "Remote",
            stage = "Screening"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ApplicationDetailResponse>(Json);
        Assert.Equal("Contoso Ltd", updated!.Application.Company);
        Assert.Equal("Staff Engineer", updated.Application.Position);
        Assert.Equal("Remote", updated.Application.Location);
        Assert.Equal(ApplicationStage.Screening, updated.Application.Stage);
    }

    [Fact]
    public async Task Moving_to_a_new_stage_is_recorded_in_the_activity_history()
    {
        var client = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/applications/{created.Id}/stage", new { stage = "Interview" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ApplicationDetailResponse>(Json);
        Assert.Equal(ApplicationStage.Interview, detail!.Application.Stage);
        Assert.NotNull(detail.Application.AppliedOn);
        Assert.Contains(detail.Activity, a => a.Description == "Moved from Wishlist to Interview");
    }

    [Fact]
    public async Task Archive_hides_an_application_and_restore_brings_it_back()
    {
        var client = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(client);

        var archive = await client.PostAsync($"/api/v1/applications/{created.Id}/archive", null);
        var hidden = await client.GetFromJsonAsync<ApplicationResponse[]>("/api/v1/applications", Json);
        var all = await client.GetFromJsonAsync<ApplicationResponse[]>("/api/v1/applications?includeArchived=true", Json);
        var restore = await client.PostAsync($"/api/v1/applications/{created.Id}/restore", null);
        var visible = await client.GetFromJsonAsync<ApplicationResponse[]>("/api/v1/applications", Json);

        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        Assert.Empty(hidden!);
        Assert.True(Assert.Single(all!).IsArchived);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        Assert.Single(visible!);
    }

    [Fact]
    public async Task Delete_returns_204_and_then_the_application_is_gone()
    {
        var client = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(client);

        var delete = await client.DeleteAsync($"/api/v1/applications/{created.Id}");
        var get = await client.GetAsync($"/api/v1/applications/{created.Id}");
        var deleteAgain = await client.DeleteAsync($"/api/v1/applications/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task One_user_cannot_see_or_change_another_users_application()
    {
        var alice = await factory.CreateClientForNewUserAsync();
        var bob = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(alice);
        var url = $"/api/v1/applications/{created.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(url, new { company = "X", position = "Y" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PatchAsJsonAsync($"{url}/stage", new { stage = "Offer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"{url}/notes", new { content = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(url)).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<ApplicationResponse[]>("/api/v1/applications", Json))!);

        var untouched = await alice.GetFromJsonAsync<ApplicationDetailResponse>(url, Json);
        Assert.Equal("Contoso", untouched!.Application.Company);
        Assert.Empty(untouched.Notes);
    }

    // ---------- Notes, reminders, contacts ----------

    [Fact]
    public async Task Notes_can_be_added_and_deleted()
    {
        var client = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(client);

        var add = await client.PostAsJsonAsync(
            $"/api/v1/applications/{created.Id}/notes", new { kind = "Interview", content = "Went well" });
        var note = await add.Content.ReadFromJsonAsync<NoteResponse>(Json);
        var empty = await client.PostAsJsonAsync($"/api/v1/applications/{created.Id}/notes", new { content = "" });
        var delete = await client.DeleteAsync($"/api/v1/notes/{note!.Id}");
        var detail = await client.GetFromJsonAsync<ApplicationDetailResponse>($"/api/v1/applications/{created.Id}", Json);

        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal(NoteKind.Interview, note.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(detail!.Notes);
    }

    [Fact]
    public async Task Reminders_can_be_added_snoozed_completed_and_deleted()
    {
        var client = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(client);
        var due = DateTime.UtcNow.AddDays(2);

        var add = await client.PostAsJsonAsync(
            $"/api/v1/applications/{created.Id}/reminders", new { title = "Follow up", dueAt = due });
        var reminder = await add.Content.ReadFromJsonAsync<ReminderResponse>(Json);
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.False(reminder!.IsOverdue);

        var snooze = await client.PatchAsJsonAsync($"/api/v1/reminders/{reminder.Id}", new { snoozeDays = 7 });
        var open = await client.GetFromJsonAsync<ReminderResponse[]>("/api/v1/reminders", Json);
        Assert.Equal(HttpStatusCode.NoContent, snooze.StatusCode);
        Assert.InRange(Assert.Single(open!).DueAt, due.AddDays(7).AddSeconds(-1), due.AddDays(7).AddSeconds(1));

        var complete = await client.PatchAsJsonAsync($"/api/v1/reminders/{reminder.Id}", new { isDone = true });
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ReminderResponse[]>("/api/v1/reminders", Json))!);
        Assert.True(Assert.Single((await client.GetFromJsonAsync<ReminderResponse[]>("/api/v1/reminders?includeDone=true", Json))!).IsDone);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/reminders/{reminder.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/reminders/{reminder.Id}")).StatusCode);
    }

    [Fact]
    public async Task Reminder_requests_are_validated()
    {
        var client = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(client);

        var noDate = await client.PostAsJsonAsync($"/api/v1/applications/{created.Id}/reminders", new { title = "Follow up" });
        var badSnooze = await client.PatchAsJsonAsync("/api/v1/reminders/1", new { snoozeDays = 0 });
        var missing = await client.PatchAsJsonAsync("/api/v1/reminders/999999", new { isDone = true });

        Assert.Equal(HttpStatusCode.BadRequest, noDate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badSnooze.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Contacts_can_be_added_updated_and_deleted()
    {
        var client = await factory.CreateClientForNewUserAsync();
        var created = await CreateAsync(client);
        var url = $"/api/v1/applications/{created.Id}/contacts";

        var add = await client.PostAsJsonAsync(url, new { name = "Priya Nair", role = "Recruiter" });
        var contact = await add.Content.ReadFromJsonAsync<ContactResponse>(Json);
        var update = await client.PutAsJsonAsync($"{url}/{contact!.Id}", new { name = "Priya N.", email = "priya@example.com" });
        var updated = await update.Content.ReadFromJsonAsync<ContactResponse>(Json);
        var delete = await client.DeleteAsync($"/api/v1/contacts/{contact.Id}");

        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Priya N.", updated!.Name);
        Assert.Equal("priya@example.com", updated.Email);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    // ---------- Stats and infrastructure ----------

    [Fact]
    public async Task Stats_summarise_the_pipeline()
    {
        var client = await factory.CreateClientForNewUserAsync();
        await CreateAsync(client, "Wished");
        await CreateAsync(client, "Applied", "Applied");
        var interviewing = await CreateAsync(client, "Interviewing", "Interview");
        var rejected = await CreateAsync(client, "Rejected", "Applied");
        await client.PatchAsJsonAsync($"/api/v1/applications/{rejected.Id}/stage", new { stage = "Rejected" });
        await client.PostAsJsonAsync(
            $"/api/v1/applications/{interviewing.Id}/reminders",
            new { title = "Late", dueAt = DateTime.UtcNow.AddDays(-1) });

        var stats = await client.GetFromJsonAsync<StatsResponse>("/api/v1/stats", Json);

        Assert.NotNull(stats);
        Assert.Equal(4, stats.Total);
        Assert.Equal(2, stats.InProgress);
        Assert.Equal(3, stats.Sent);
        Assert.Equal(2, stats.Responded);
        Assert.Equal(1, stats.ReachedInterview);
        Assert.Equal(0, stats.ReachedOffer);
        Assert.Equal(1, stats.OverdueReminders);
        Assert.Equal(1, stats.ByStage[ApplicationStage.Wishlist]);
        Assert.Equal(1, stats.ByStage[ApplicationStage.Rejected]);
    }

    [Fact]
    public async Task Health_check_and_api_description_are_public()
    {
        var client = factory.CreateClient();

        var health = await client.GetAsync("/healthz");
        var openApi = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.True(openApi.GetProperty("paths").TryGetProperty("/api/v1/applications", out _));
    }

    [Fact]
    public async Task Website_pages_still_redirect_signed_out_visitors_to_the_login_page()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/board");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
    }
}
