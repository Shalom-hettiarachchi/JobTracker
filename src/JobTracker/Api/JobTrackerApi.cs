using System.Security.Claims;
using JobTracker.Models;
using JobTracker.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace JobTracker.Api;

/// <summary>
/// The versioned REST API. Every endpoint needs a bearer token from <c>POST /api/auth/login</c>
/// and only ever touches the caller's own data.
/// </summary>
public static class JobTrackerApi
{
    public static IEndpointRouteBuilder MapJobTrackerApi(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(IdentityConstants.BearerScheme)
                .RequireAuthenticatedUser());

        var applications = api.MapGroup("/applications").WithTags("Applications");
        applications.MapGet("/", ListApplications);
        applications.MapGet("/{id:int}", GetApplication);
        applications.MapPost("/", CreateApplication);
        applications.MapPut("/{id:int}", UpdateApplication);
        applications.MapDelete("/{id:int}", DeleteApplication);
        applications.MapPatch("/{id:int}/stage", MoveApplication);
        applications.MapPost("/{id:int}/archive", (int id, ClaimsPrincipal user, JobService jobs) => SetArchived(id, true, user, jobs));
        applications.MapPost("/{id:int}/restore", (int id, ClaimsPrincipal user, JobService jobs) => SetArchived(id, false, user, jobs));

        var notes = api.MapGroup("").WithTags("Notes");
        notes.MapPost("/applications/{id:int}/notes", AddNote);
        notes.MapDelete("/notes/{noteId:int}", DeleteNote);

        var reminders = api.MapGroup("").WithTags("Reminders");
        reminders.MapGet("/reminders", ListReminders);
        reminders.MapPost("/applications/{id:int}/reminders", AddReminder);
        reminders.MapPatch("/reminders/{reminderId:int}", UpdateReminder);
        reminders.MapDelete("/reminders/{reminderId:int}", DeleteReminder);

        var contacts = api.MapGroup("").WithTags("Contacts");
        contacts.MapPost("/applications/{id:int}/contacts", AddContact);
        contacts.MapPut("/applications/{id:int}/contacts/{contactId:int}", UpdateContact);
        contacts.MapDelete("/contacts/{contactId:int}", DeleteContact);

        api.MapGet("/stats", GetStats).WithTags("Stats");

        return routes;
    }

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ---------- Applications ----------

    private static async Task<Ok<ApplicationResponse[]>> ListApplications(
        ClaimsPrincipal user, JobService jobs, ApplicationStage? stage, string? search, bool includeArchived = false)
    {
        IEnumerable<JobApplication> result = await jobs.GetApplicationsAsync(UserId(user), includeArchived);

        if (stage is not null)
        {
            result = result.Where(a => a.Stage == stage);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            result = result.Where(a =>
                a.Company.Contains(term, StringComparison.OrdinalIgnoreCase)
                || a.Position.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (a.Tags ?? "").Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return TypedResults.Ok(result.OrderBy(a => a.Stage).ThenBy(a => a.SortOrder).Select(a => a.ToResponse()).ToArray());
    }

    private static async Task<Results<Ok<ApplicationDetailResponse>, NotFound>> GetApplication(
        int id, ClaimsPrincipal user, JobService jobs) =>
        await jobs.GetApplicationAsync(UserId(user), id) is { } application
            ? TypedResults.Ok(application.ToDetailResponse())
            : TypedResults.NotFound();

    private static async Task<Created<ApplicationResponse>> CreateApplication(
        ApplicationRequest request, ClaimsPrincipal user, JobService jobs)
    {
        var created = await jobs.CreateAsync(UserId(user), request.ToEntity());
        return TypedResults.Created($"/api/v1/applications/{created.Id}", created.ToResponse());
    }

    private static async Task<Results<Ok<ApplicationDetailResponse>, NotFound>> UpdateApplication(
        int id, ApplicationRequest request, ClaimsPrincipal user, JobService jobs)
    {
        if (!await jobs.UpdateAsync(UserId(user), request.ToEntity(id)))
        {
            return TypedResults.NotFound();
        }

        return await GetApplication(id, user, jobs);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteApplication(
        int id, ClaimsPrincipal user, JobService jobs) =>
        await jobs.DeleteAsync(UserId(user), id) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<Ok<ApplicationDetailResponse>, NotFound>> MoveApplication(
        int id, MoveRequest request, ClaimsPrincipal user, JobService jobs)
    {
        if (!await jobs.MoveAsync(UserId(user), id, request.Stage, request.BeforeId))
        {
            return TypedResults.NotFound();
        }

        return await GetApplication(id, user, jobs);
    }

    private static async Task<Results<NoContent, NotFound>> SetArchived(
        int id, bool archived, ClaimsPrincipal user, JobService jobs) =>
        await jobs.SetArchivedAsync(UserId(user), id, archived) ? TypedResults.NoContent() : TypedResults.NotFound();

    // ---------- Notes ----------

    private static async Task<Results<Created<NoteResponse>, NotFound>> AddNote(
        int id, NoteRequest request, ClaimsPrincipal user, JobService jobs) =>
        await jobs.AddNoteAsync(UserId(user), id, request.Kind, request.Content) is { } note
            ? TypedResults.Created($"/api/v1/applications/{id}", note.ToResponse())
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> DeleteNote(
        int noteId, ClaimsPrincipal user, JobService jobs) =>
        await jobs.DeleteNoteAsync(UserId(user), noteId) ? TypedResults.NoContent() : TypedResults.NotFound();

    // ---------- Reminders ----------

    private static async Task<Ok<ReminderResponse[]>> ListReminders(
        ClaimsPrincipal user, JobService jobs, bool includeDone = false)
    {
        var reminders = await jobs.GetRemindersAsync(UserId(user), includeDone);
        return TypedResults.Ok(reminders.Select(r => r.ToResponse()).ToArray());
    }

    private static async Task<Results<Created<ReminderResponse>, NotFound>> AddReminder(
        int id, ReminderRequest request, ClaimsPrincipal user, JobService jobs) =>
        await jobs.AddReminderAsync(UserId(user), id, request.Title, request.DueAt!.Value.ToUniversalTime()) is { } reminder
            ? TypedResults.Created($"/api/v1/applications/{id}", reminder.ToResponse())
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> UpdateReminder(
        int reminderId, ReminderUpdateRequest request, ClaimsPrincipal user, JobService jobs)
    {
        var userId = UserId(user);
        var found = true;

        if (request.SnoozeDays is int days)
        {
            found = await jobs.SnoozeReminderAsync(userId, reminderId, TimeSpan.FromDays(days));
        }

        if (found && request.IsDone is bool done)
        {
            found = await jobs.SetReminderDoneAsync(userId, reminderId, done);
        }

        if (found && request is { SnoozeDays: null, IsDone: null })
        {
            // Nothing to change: still report whether the reminder exists.
            found = (await jobs.GetRemindersAsync(userId, includeDone: true)).Any(r => r.Id == reminderId);
        }

        return found ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteReminder(
        int reminderId, ClaimsPrincipal user, JobService jobs) =>
        await jobs.DeleteReminderAsync(UserId(user), reminderId) ? TypedResults.NoContent() : TypedResults.NotFound();

    // ---------- Contacts ----------

    private static async Task<Results<Created<ContactResponse>, NotFound>> AddContact(
        int id, ContactRequest request, ClaimsPrincipal user, JobService jobs) =>
        await jobs.SaveContactAsync(UserId(user), id, request.ToEntity()) is { } contact
            ? TypedResults.Created($"/api/v1/applications/{id}", contact.ToResponse())
            : TypedResults.NotFound();

    private static async Task<Results<Ok<ContactResponse>, NotFound>> UpdateContact(
        int id, int contactId, ContactRequest request, ClaimsPrincipal user, JobService jobs) =>
        await jobs.SaveContactAsync(UserId(user), id, request.ToEntity(contactId)) is { } contact
            ? TypedResults.Ok(contact.ToResponse())
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> DeleteContact(
        int contactId, ClaimsPrincipal user, JobService jobs) =>
        await jobs.DeleteContactAsync(UserId(user), contactId) ? TypedResults.NoContent() : TypedResults.NotFound();

    // ---------- Stats ----------

    private static async Task<Ok<StatsResponse>> GetStats(ClaimsPrincipal user, JobService jobs)
    {
        var userId = UserId(user);
        var applications = await jobs.GetApplicationsAsync(userId);
        var overdue = await jobs.GetDueCountAsync(userId, DateTime.UtcNow);

        return TypedResults.Ok(new StatsResponse(
            Total: applications.Count,
            InProgress: applications.Count(a => a.Stage is not ApplicationStage.Wishlist && !a.Stage.IsClosed()),
            Sent: applications.Count(a => a.FurthestStage >= ApplicationStage.Applied),
            Responded: applications.Count(a => a.FurthestStage >= ApplicationStage.Screening || a.Stage == ApplicationStage.Rejected),
            ReachedInterview: applications.Count(a => a.FurthestStage >= ApplicationStage.Interview),
            ReachedOffer: applications.Count(a => a.FurthestStage >= ApplicationStage.Offer),
            OverdueReminders: overdue,
            ByStage: Display.Stages.ToDictionary(s => s, s => applications.Count(a => a.Stage == s))));
    }
}
