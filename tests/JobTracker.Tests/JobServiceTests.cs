using JobTracker.Models;
using JobTracker.Services;

namespace JobTracker.Tests;

public sealed class JobServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly JobService _jobs;
    private readonly string _alice;
    private readonly string _bob;

    public JobServiceTests()
    {
        _jobs = new JobService(_database);
        _alice = _database.AddUser("alice");
        _bob = _database.AddUser("bob");
    }

    public void Dispose() => _database.Dispose();

    private Task<JobApplication> AddAsync(
        string userId, string company, ApplicationStage stage = ApplicationStage.Wishlist) =>
        _jobs.CreateAsync(userId, new JobApplication { Company = company, Position = "Engineer", Stage = stage });

    private async Task<string[]> ColumnAsync(ApplicationStage stage) =>
        (await _jobs.GetApplicationsAsync(_alice))
            .Where(a => a.Stage == stage)
            .OrderBy(a => a.SortOrder)
            .Select(a => a.Company)
            .ToArray();

    // ---------- Creating ----------

    [Fact]
    public async Task Create_trims_names_and_records_the_first_activity()
    {
        var created = await _jobs.CreateAsync(_alice, new JobApplication { Company = "  Contoso ", Position = " Dev " });

        var loaded = await _jobs.GetApplicationAsync(_alice, created.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Contoso", loaded.Company);
        Assert.Equal("Dev", loaded.Position);
        var activity = Assert.Single(loaded.Activities);
        Assert.Equal(ActivityKind.Created, activity.Kind);
    }

    [Fact]
    public async Task Create_puts_the_newest_card_at_the_top_of_its_column()
    {
        await AddAsync(_alice, "First");
        await AddAsync(_alice, "Second");
        await AddAsync(_alice, "Third");

        Assert.Equal(["Third", "Second", "First"], await ColumnAsync(ApplicationStage.Wishlist));
    }

    [Fact]
    public async Task Create_in_wishlist_has_no_applied_date_but_later_stages_do()
    {
        var wished = await AddAsync(_alice, "Wished");
        var applied = await AddAsync(_alice, "Applied", ApplicationStage.Applied);

        Assert.Null(wished.AppliedOn);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), applied.AppliedOn);
    }

    // ---------- Moving ----------

    [Fact]
    public async Task Move_changes_stage_sets_applied_date_and_logs_it()
    {
        var app = await AddAsync(_alice, "Contoso");

        Assert.True(await _jobs.MoveAsync(_alice, app.Id, ApplicationStage.Applied, beforeId: null));

        var loaded = await _jobs.GetApplicationAsync(_alice, app.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ApplicationStage.Applied, loaded.Stage);
        Assert.Equal(ApplicationStage.Applied, loaded.FurthestStage);
        Assert.NotNull(loaded.AppliedOn);
        Assert.Contains(loaded.Activities, x =>
            x.Kind == ActivityKind.StageChanged && x.Description == "Moved from Wishlist to Applied");
    }

    [Fact]
    public async Task Move_before_a_card_inserts_at_that_position()
    {
        var a = await AddAsync(_alice, "A", ApplicationStage.Applied);
        var b = await AddAsync(_alice, "B", ApplicationStage.Applied);
        var moved = await AddAsync(_alice, "Moved");
        // Column is now B, A (newest first).

        await _jobs.MoveAsync(_alice, moved.Id, ApplicationStage.Applied, beforeId: a.Id);

        Assert.Equal(["B", "Moved", "A"], await ColumnAsync(ApplicationStage.Applied));
        Assert.NotEqual(0, b.Id);
    }

    [Fact]
    public async Task Move_without_a_target_card_appends_to_the_end()
    {
        await AddAsync(_alice, "A", ApplicationStage.Applied);
        var moved = await AddAsync(_alice, "Moved");

        await _jobs.MoveAsync(_alice, moved.Id, ApplicationStage.Applied, beforeId: null);

        Assert.Equal(["A", "Moved"], await ColumnAsync(ApplicationStage.Applied));
    }

    [Fact]
    public async Task Reordering_inside_a_column_does_not_log_a_stage_change()
    {
        var a = await AddAsync(_alice, "A");
        var b = await AddAsync(_alice, "B");

        await _jobs.MoveAsync(_alice, a.Id, ApplicationStage.Wishlist, beforeId: b.Id);

        Assert.Equal(["A", "B"], await ColumnAsync(ApplicationStage.Wishlist));
        var loaded = await _jobs.GetApplicationAsync(_alice, a.Id);
        Assert.DoesNotContain(loaded!.Activities, x => x.Kind == ActivityKind.StageChanged);
    }

    [Fact]
    public async Task Rejection_keeps_the_furthest_stage_that_was_reached()
    {
        var app = await AddAsync(_alice, "Contoso");
        await _jobs.MoveAsync(_alice, app.Id, ApplicationStage.Interview, null);

        await _jobs.MoveAsync(_alice, app.Id, ApplicationStage.Rejected, null);

        var loaded = await _jobs.GetApplicationAsync(_alice, app.Id);
        Assert.Equal(ApplicationStage.Rejected, loaded!.Stage);
        Assert.Equal(ApplicationStage.Interview, loaded.FurthestStage);
    }

    [Fact]
    public async Task Rejecting_a_wishlist_item_counts_it_as_sent()
    {
        var app = await AddAsync(_alice, "Contoso");

        await _jobs.MoveAsync(_alice, app.Id, ApplicationStage.Rejected, null);

        var loaded = await _jobs.GetApplicationAsync(_alice, app.Id);
        Assert.Equal(ApplicationStage.Applied, loaded!.FurthestStage);
    }

    [Fact]
    public async Task Moving_backwards_does_not_lower_the_furthest_stage()
    {
        var app = await AddAsync(_alice, "Contoso");
        await _jobs.MoveAsync(_alice, app.Id, ApplicationStage.Offer, null);

        await _jobs.MoveAsync(_alice, app.Id, ApplicationStage.Screening, null);

        var loaded = await _jobs.GetApplicationAsync(_alice, app.Id);
        Assert.Equal(ApplicationStage.Offer, loaded!.FurthestStage);
    }

    // ---------- Updating, archiving, deleting ----------

    [Fact]
    public async Task Update_saves_fields_and_applies_a_stage_change()
    {
        var app = await AddAsync(_alice, "Contoso");
        var edit = (await _jobs.GetApplicationAsync(_alice, app.Id))!.CopyForEdit();
        edit.Company = "Contoso Ltd";
        edit.SalaryMin = 50000;
        edit.Tags = "backend, Backend, api";
        edit.Stage = ApplicationStage.Screening;

        Assert.True(await _jobs.UpdateAsync(_alice, edit));

        var loaded = await _jobs.GetApplicationAsync(_alice, app.Id);
        Assert.Equal("Contoso Ltd", loaded!.Company);
        Assert.Equal(50000, loaded.SalaryMin);
        Assert.Equal(["backend", "api"], loaded.TagList);
        Assert.Equal(ApplicationStage.Screening, loaded.Stage);
        Assert.Equal(ApplicationStage.Screening, loaded.FurthestStage);
    }

    [Fact]
    public async Task Archived_applications_are_hidden_unless_asked_for()
    {
        var app = await AddAsync(_alice, "Contoso");

        Assert.True(await _jobs.SetArchivedAsync(_alice, app.Id, true));

        Assert.Empty(await _jobs.GetApplicationsAsync(_alice));
        Assert.Single(await _jobs.GetApplicationsAsync(_alice, includeArchived: true));
    }

    [Fact]
    public async Task Delete_removes_the_application_and_everything_attached_to_it()
    {
        var app = await AddAsync(_alice, "Contoso");
        await _jobs.AddNoteAsync(_alice, app.Id, NoteKind.Call, "Spoke to the recruiter");
        await _jobs.AddReminderAsync(_alice, app.Id, "Follow up", DateTime.UtcNow.AddDays(1));

        Assert.True(await _jobs.DeleteAsync(_alice, app.Id));

        Assert.Null(await _jobs.GetApplicationAsync(_alice, app.Id));
        Assert.Empty(await _jobs.GetRemindersAsync(_alice, includeDone: true));
        using var db = _database.CreateDbContext();
        Assert.Empty(db.Notes);
        Assert.Empty(db.Activities);
    }

    // ---------- Each user only sees their own data ----------

    [Fact]
    public async Task Another_user_cannot_read_change_or_delete_an_application()
    {
        var app = await AddAsync(_alice, "Contoso");
        var edit = app.CopyForEdit();
        edit.Company = "Hijacked";

        Assert.Null(await _jobs.GetApplicationAsync(_bob, app.Id));
        Assert.Empty(await _jobs.GetApplicationsAsync(_bob));
        Assert.False(await _jobs.UpdateAsync(_bob, edit));
        Assert.False(await _jobs.MoveAsync(_bob, app.Id, ApplicationStage.Offer, null));
        Assert.False(await _jobs.SetArchivedAsync(_bob, app.Id, true));
        Assert.False(await _jobs.DeleteAsync(_bob, app.Id));
        Assert.Null(await _jobs.AddNoteAsync(_bob, app.Id, NoteKind.Note, "sneaky"));
        Assert.Null(await _jobs.AddReminderAsync(_bob, app.Id, "sneaky", DateTime.UtcNow));
        Assert.Null(await _jobs.SaveContactAsync(_bob, app.Id, new Contact { Name = "Sneaky" }));

        var untouched = await _jobs.GetApplicationAsync(_alice, app.Id);
        Assert.Equal("Contoso", untouched!.Company);
        Assert.Equal(ApplicationStage.Wishlist, untouched.Stage);
        Assert.Empty(untouched.Notes);
    }

    [Fact]
    public async Task Another_user_cannot_touch_notes_reminders_or_contacts()
    {
        var app = await AddAsync(_alice, "Contoso");
        var note = await _jobs.AddNoteAsync(_alice, app.Id, NoteKind.Note, "Mine");
        var reminder = await _jobs.AddReminderAsync(_alice, app.Id, "Mine", DateTime.UtcNow.AddDays(1));
        var contact = await _jobs.SaveContactAsync(_alice, app.Id, new Contact { Name = "Priya" });

        Assert.False(await _jobs.DeleteNoteAsync(_bob, note!.Id));
        Assert.False(await _jobs.SetReminderDoneAsync(_bob, reminder!.Id, true));
        Assert.False(await _jobs.SnoozeReminderAsync(_bob, reminder.Id, TimeSpan.FromDays(1)));
        Assert.False(await _jobs.DeleteReminderAsync(_bob, reminder.Id));
        Assert.False(await _jobs.DeleteContactAsync(_bob, contact!.Id));

        var loaded = await _jobs.GetApplicationAsync(_alice, app.Id);
        Assert.Single(loaded!.Notes);
        Assert.False(Assert.Single(loaded.Reminders).IsDone);
        Assert.Single(loaded.Contacts);
    }

    // ---------- Reminders ----------

    [Fact]
    public async Task Completing_a_reminder_stamps_it_and_reopening_clears_the_stamp()
    {
        var app = await AddAsync(_alice, "Contoso");
        var reminder = await _jobs.AddReminderAsync(_alice, app.Id, "Follow up", DateTime.UtcNow.AddDays(1));

        await _jobs.SetReminderDoneAsync(_alice, reminder!.Id, true);
        var done = Assert.Single(await _jobs.GetRemindersAsync(_alice, includeDone: true));
        Assert.True(done.IsDone);
        Assert.NotNull(done.CompletedAt);
        Assert.Empty(await _jobs.GetRemindersAsync(_alice));

        await _jobs.SetReminderDoneAsync(_alice, reminder.Id, false);
        var reopened = Assert.Single(await _jobs.GetRemindersAsync(_alice));
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task Snoozing_an_overdue_reminder_counts_from_now()
    {
        var app = await AddAsync(_alice, "Contoso");
        var reminder = await _jobs.AddReminderAsync(_alice, app.Id, "Late", DateTime.UtcNow.AddDays(-10));

        await _jobs.SnoozeReminderAsync(_alice, reminder!.Id, TimeSpan.FromDays(1));

        var snoozed = Assert.Single(await _jobs.GetRemindersAsync(_alice));
        var expected = DateTime.UtcNow.AddDays(1);
        Assert.InRange(snoozed.DueAt, expected.AddMinutes(-1), expected.AddMinutes(1));
    }

    [Fact]
    public async Task Snoozing_a_future_reminder_counts_from_its_due_date()
    {
        var app = await AddAsync(_alice, "Contoso");
        var due = DateTime.UtcNow.AddDays(3);
        var reminder = await _jobs.AddReminderAsync(_alice, app.Id, "Soon", due);

        await _jobs.SnoozeReminderAsync(_alice, reminder!.Id, TimeSpan.FromDays(7));

        var snoozed = Assert.Single(await _jobs.GetRemindersAsync(_alice));
        Assert.InRange(snoozed.DueAt, due.AddDays(7).AddSeconds(-1), due.AddDays(7).AddSeconds(1));
    }

    [Fact]
    public async Task Due_count_ignores_done_future_archived_and_other_users_reminders()
    {
        var now = DateTime.UtcNow;
        var app = await AddAsync(_alice, "Contoso");
        await _jobs.AddReminderAsync(_alice, app.Id, "Overdue", now.AddDays(-1));
        await _jobs.AddReminderAsync(_alice, app.Id, "Future", now.AddDays(5));
        var done = await _jobs.AddReminderAsync(_alice, app.Id, "Done", now.AddDays(-2));
        await _jobs.SetReminderDoneAsync(_alice, done!.Id, true);

        var archived = await AddAsync(_alice, "Archived");
        await _jobs.AddReminderAsync(_alice, archived.Id, "Hidden", now.AddDays(-1));
        await _jobs.SetArchivedAsync(_alice, archived.Id, true);

        var bobs = await AddAsync(_bob, "Fabrikam");
        await _jobs.AddReminderAsync(_bob, bobs.Id, "Bob's", now.AddDays(-1));

        Assert.Equal(1, await _jobs.GetDueCountAsync(_alice, now));
    }

    // ---------- Contacts ----------

    [Fact]
    public async Task Contacts_can_be_added_and_edited()
    {
        var app = await AddAsync(_alice, "Contoso");
        var added = await _jobs.SaveContactAsync(_alice, app.Id, new Contact { Name = " Priya ", Role = "Recruiter" });

        var edited = await _jobs.SaveContactAsync(
            _alice, app.Id, new Contact { Id = added!.Id, Name = "Priya Nair", Email = "priya@example.com" });

        Assert.Equal("Priya", added.Name);
        Assert.Equal(added.Id, edited!.Id);
        var contact = Assert.Single((await _jobs.GetApplicationAsync(_alice, app.Id))!.Contacts);
        Assert.Equal("Priya Nair", contact.Name);
        Assert.Equal("priya@example.com", contact.Email);
        Assert.Null(contact.Role);
    }

    // ---------- Change notifications ----------

    [Fact]
    public async Task Writes_raise_the_changed_event()
    {
        var raised = 0;
        _jobs.Changed += () => raised++;

        var app = await AddAsync(_alice, "Contoso");
        await _jobs.MoveAsync(_alice, app.Id, ApplicationStage.Applied, null);
        await _jobs.GetApplicationsAsync(_alice);

        Assert.Equal(2, raised);
    }
}
