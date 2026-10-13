using JobTracker.Data;
using JobTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Services;

/// <summary>
/// All reads and writes for a user's applications. Every method takes the owner's user id
/// and never touches rows that belong to someone else.
/// </summary>
public class JobService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    /// <summary>Raised after any write so shared UI (e.g. the reminder bell) can refresh.</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Open reminders due before <paramref name="beforeUtc"/>, i.e. overdue or due today.</summary>
    public async Task<int> GetDueCountAsync(string userId, DateTime beforeUtc)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Reminders.CountAsync(r =>
            !r.IsDone && r.DueAt < beforeUtc && r.JobApplication!.UserId == userId && !r.JobApplication.IsArchived);
    }

    // ---------- Applications ----------

    public async Task<List<JobApplication>> GetApplicationsAsync(string userId, bool includeArchived = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.JobApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId && (includeArchived || !a.IsArchived))
            .Include(a => a.Reminders.Where(r => !r.IsDone))
            .OrderBy(a => a.SortOrder)
            .ThenByDescending(a => a.UpdatedAt)
            .ToListAsync();
    }

    public async Task<JobApplication?> GetApplicationAsync(string userId, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.JobApplications
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Notes.OrderByDescending(n => n.CreatedAt))
            .Include(a => a.Reminders.OrderBy(r => r.IsDone).ThenBy(r => r.DueAt))
            .Include(a => a.Contacts.OrderBy(c => c.Name))
            .Include(a => a.Activities.OrderByDescending(x => x.OccurredAt))
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
    }

    public async Task<JobApplication> CreateAsync(string userId, JobApplication draft)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        draft.Id = 0;
        draft.UserId = userId;
        draft.Company = draft.Company.Trim();
        draft.Position = draft.Position.Trim();
        draft.CreatedAt = draft.UpdatedAt = draft.StageChangedAt = now;
        draft.FurthestStage = draft.Stage == ApplicationStage.Rejected ? ApplicationStage.Applied : draft.Stage;
        if (draft.Stage != ApplicationStage.Wishlist)
        {
            draft.AppliedOn ??= DateOnly.FromDateTime(DateTime.Now);
        }

        // New cards go to the top of their column.
        var minOrder = await db.JobApplications
            .Where(a => a.UserId == userId && a.Stage == draft.Stage)
            .Select(a => (int?)a.SortOrder)
            .MinAsync();
        draft.SortOrder = (minOrder ?? 0) - 1;

        draft.Activities.Add(new ActivityEntry
        {
            Kind = ActivityKind.Created,
            Description = $"Added to {draft.Stage.Label()}",
            OccurredAt = now
        });

        db.JobApplications.Add(draft);
        await db.SaveChangesAsync();
        NotifyChanged();
        return draft;
    }

    /// <summary>Saves the editable fields of an application. Returns false if it no longer exists.</summary>
    public async Task<bool> UpdateAsync(string userId, JobApplication edited)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.JobApplications.FirstOrDefaultAsync(a => a.Id == edited.Id && a.UserId == userId);
        if (app is null)
        {
            return false;
        }

        app.Company = edited.Company.Trim();
        app.Position = edited.Position.Trim();
        app.Location = edited.Location;
        app.WorkMode = edited.WorkMode;
        app.EmploymentType = edited.EmploymentType;
        app.SalaryMin = edited.SalaryMin;
        app.SalaryMax = edited.SalaryMax;
        app.Currency = edited.Currency;
        app.JobUrl = edited.JobUrl;
        app.Source = edited.Source;
        app.Description = edited.Description;
        app.Tags = edited.Tags;
        app.Priority = edited.Priority;
        app.AppliedOn = edited.AppliedOn;
        app.Deadline = edited.Deadline;
        app.UpdatedAt = DateTime.UtcNow;

        if (app.Stage != edited.Stage)
        {
            await ChangeStageAsync(db, app, edited.Stage, beforeId: null);
        }

        await db.SaveChangesAsync();
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// Moves a card to a stage and position. The card is placed before <paramref name="beforeId"/>,
    /// or at the end of the column when that is null.
    /// </summary>
    public async Task<bool> MoveAsync(string userId, int id, ApplicationStage stage, int? beforeId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.JobApplications.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (app is null)
        {
            return false;
        }

        await ChangeStageAsync(db, app, stage, beforeId);
        await db.SaveChangesAsync();
        NotifyChanged();
        return true;
    }

    private static async Task ChangeStageAsync(
        ApplicationDbContext db, JobApplication app, ApplicationStage stage, int? beforeId)
    {
        var now = DateTime.UtcNow;

        if (app.Stage != stage)
        {
            db.Activities.Add(new ActivityEntry
            {
                JobApplicationId = app.Id,
                Kind = ActivityKind.StageChanged,
                Description = $"Moved from {app.Stage.Label()} to {stage.Label()}",
                OccurredAt = now
            });

            app.Stage = stage;
            app.StageChangedAt = now;
            app.UpdatedAt = now;

            if (stage != ApplicationStage.Wishlist)
            {
                app.AppliedOn ??= DateOnly.FromDateTime(DateTime.Now);
            }

            if (stage == ApplicationStage.Rejected)
            {
                // A rejection still means the application was sent.
                if (app.FurthestStage < ApplicationStage.Applied)
                {
                    app.FurthestStage = ApplicationStage.Applied;
                }
            }
            else if (stage > app.FurthestStage)
            {
                app.FurthestStage = stage;
            }
        }

        // Renumber the whole target column with the card in its new slot.
        var column = await db.JobApplications
            .Where(a => a.UserId == app.UserId && a.Stage == stage && a.Id != app.Id && !a.IsArchived)
            .OrderBy(a => a.SortOrder)
            .ThenByDescending(a => a.UpdatedAt)
            .ToListAsync();

        var index = beforeId is null ? -1 : column.FindIndex(a => a.Id == beforeId);
        if (index < 0)
        {
            column.Add(app);
        }
        else
        {
            column.Insert(index, app);
        }

        for (var i = 0; i < column.Count; i++)
        {
            column[i].SortOrder = i;
        }
    }

    public async Task SetArchivedAsync(string userId, int id, bool archived)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.JobApplications.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (app is null || app.IsArchived == archived)
        {
            return;
        }

        app.IsArchived = archived;
        app.UpdatedAt = DateTime.UtcNow;
        db.Activities.Add(new ActivityEntry
        {
            JobApplicationId = app.Id,
            Kind = archived ? ActivityKind.Archived : ActivityKind.Restored,
            Description = archived ? "Archived" : "Restored from archive"
        });
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    public async Task DeleteAsync(string userId, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.JobApplications.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (app is null)
        {
            return;
        }

        db.JobApplications.Remove(app);
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    // ---------- Notes ----------

    public async Task AddNoteAsync(string userId, int applicationId, NoteKind kind, string content)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.JobApplications.FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId);
        if (app is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        db.Notes.Add(new ApplicationNote
        {
            JobApplicationId = app.Id,
            Kind = kind,
            Content = content.Trim(),
            CreatedAt = now
        });
        db.Activities.Add(new ActivityEntry
        {
            JobApplicationId = app.Id,
            Kind = ActivityKind.NoteAdded,
            Description = kind == NoteKind.Note ? "Added a note" : $"Logged {kind.ToString().ToLowerInvariant()} notes",
            OccurredAt = now
        });
        app.UpdatedAt = now;
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    public async Task DeleteNoteAsync(string userId, int noteId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId && n.JobApplication!.UserId == userId);
        if (note is null)
        {
            return;
        }

        db.Notes.Remove(note);
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    // ---------- Reminders ----------

    /// <summary>All of a user's reminders on non-archived applications, each with its application loaded.</summary>
    public async Task<List<Reminder>> GetRemindersAsync(string userId, bool includeDone = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Reminders
            .AsNoTracking()
            .Include(r => r.JobApplication)
            .Where(r => r.JobApplication!.UserId == userId && !r.JobApplication.IsArchived)
            .Where(r => includeDone || !r.IsDone)
            .OrderBy(r => r.DueAt)
            .ToListAsync();
    }

    public async Task AddReminderAsync(string userId, int applicationId, string title, DateTime dueAtUtc)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var app = await db.JobApplications.FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId);
        if (app is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        db.Reminders.Add(new Reminder
        {
            JobApplicationId = app.Id,
            Title = title.Trim(),
            DueAt = dueAtUtc,
            CreatedAt = now
        });
        db.Activities.Add(new ActivityEntry
        {
            JobApplicationId = app.Id,
            Kind = ActivityKind.ReminderAdded,
            Description = $"Set reminder: {title.Trim()}",
            OccurredAt = now
        });
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    public async Task SetReminderDoneAsync(string userId, int reminderId, bool done)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reminder = await FindReminderAsync(db, userId, reminderId);
        if (reminder is null || reminder.IsDone == done)
        {
            return;
        }

        reminder.IsDone = done;
        reminder.CompletedAt = done ? DateTime.UtcNow : null;
        if (done)
        {
            db.Activities.Add(new ActivityEntry
            {
                JobApplicationId = reminder.JobApplicationId,
                Kind = ActivityKind.ReminderCompleted,
                Description = $"Completed: {reminder.Title}"
            });
        }

        await db.SaveChangesAsync();
        NotifyChanged();
    }

    /// <summary>Pushes a reminder back by <paramref name="by"/>, counted from now if it is already overdue.</summary>
    public async Task SnoozeReminderAsync(string userId, int reminderId, TimeSpan by)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reminder = await FindReminderAsync(db, userId, reminderId);
        if (reminder is null)
        {
            return;
        }

        var due = DateTime.SpecifyKind(reminder.DueAt, DateTimeKind.Utc);
        var from = due < DateTime.UtcNow ? DateTime.UtcNow : due;
        reminder.DueAt = from + by;
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    public async Task DeleteReminderAsync(string userId, int reminderId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reminder = await FindReminderAsync(db, userId, reminderId);
        if (reminder is null)
        {
            return;
        }

        db.Reminders.Remove(reminder);
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    private static Task<Reminder?> FindReminderAsync(ApplicationDbContext db, string userId, int reminderId) =>
        db.Reminders.FirstOrDefaultAsync(r => r.Id == reminderId && r.JobApplication!.UserId == userId);

    // ---------- Contacts ----------

    public async Task SaveContactAsync(string userId, int applicationId, Contact contact)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ownsApplication = await db.JobApplications.AnyAsync(a => a.Id == applicationId && a.UserId == userId);
        if (!ownsApplication)
        {
            return;
        }

        if (contact.Id == 0)
        {
            db.Contacts.Add(new Contact
            {
                JobApplicationId = applicationId,
                Name = contact.Name.Trim(),
                Role = contact.Role,
                Email = contact.Email,
                Phone = contact.Phone,
                LinkedIn = contact.LinkedIn
            });
        }
        else
        {
            var existing = await db.Contacts.FirstOrDefaultAsync(
                c => c.Id == contact.Id && c.JobApplicationId == applicationId);
            if (existing is null)
            {
                return;
            }

            existing.Name = contact.Name.Trim();
            existing.Role = contact.Role;
            existing.Email = contact.Email;
            existing.Phone = contact.Phone;
            existing.LinkedIn = contact.LinkedIn;
        }

        await db.SaveChangesAsync();
        NotifyChanged();
    }

    public async Task DeleteContactAsync(string userId, int contactId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var contact = await db.Contacts.FirstOrDefaultAsync(
            c => c.Id == contactId && c.JobApplication!.UserId == userId);
        if (contact is null)
        {
            return;
        }

        db.Contacts.Remove(contact);
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    // ---------- Activity ----------

    public async Task<List<ActivityEntry>> GetRecentActivityAsync(string userId, int take = 12)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Activities
            .AsNoTracking()
            .Include(x => x.JobApplication)
            .Where(x => x.JobApplication!.UserId == userId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>Every activity since <paramref name="sinceUtc"/>, for the insights charts.</summary>
    public async Task<List<ActivityEntry>> GetActivitySinceAsync(string userId, DateTime sinceUtc)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Activities
            .AsNoTracking()
            .Where(x => x.JobApplication!.UserId == userId && x.OccurredAt >= sinceUtc)
            .ToListAsync();
    }
}
