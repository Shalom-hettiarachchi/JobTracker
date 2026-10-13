using System.ComponentModel.DataAnnotations;
using JobTracker.Models;

namespace JobTracker.Api;

// ---------- Requests ----------

public class ApplicationRequest : IValidatableObject
{
    [Required, MaxLength(120)]
    public string Company { get; set; } = "";

    [Required, MaxLength(160)]
    public string Position { get; set; } = "";

    [MaxLength(120)]
    public string? Location { get; set; }

    public WorkMode WorkMode { get; set; } = WorkMode.Onsite;

    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;

    [Range(0, int.MaxValue)]
    public int? SalaryMin { get; set; }

    [Range(0, int.MaxValue)]
    public int? SalaryMax { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "USD";

    [MaxLength(500)]
    public string? JobUrl { get; set; }

    [MaxLength(60)]
    public string? Source { get; set; }

    public string? Description { get; set; }

    [MaxLength(300)]
    public string? Tags { get; set; }

    public ApplicationStage Stage { get; set; } = ApplicationStage.Wishlist;

    public Priority Priority { get; set; } = Priority.Medium;

    public DateOnly? AppliedOn { get; set; }

    public DateOnly? Deadline { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SalaryMin is int min && SalaryMax is int max && max < min)
        {
            yield return new ValidationResult("SalaryMax must be at least SalaryMin.", [nameof(SalaryMax)]);
        }

        if (!string.IsNullOrWhiteSpace(JobUrl) && Display.SafeUrl(JobUrl) is null)
        {
            yield return new ValidationResult("JobUrl must be an http or https address.", [nameof(JobUrl)]);
        }
    }

    public JobApplication ToEntity(int id = 0) => new()
    {
        Id = id,
        Company = Company,
        Position = Position,
        Location = Location,
        WorkMode = WorkMode,
        EmploymentType = EmploymentType,
        SalaryMin = SalaryMin,
        SalaryMax = SalaryMax,
        Currency = Currency,
        JobUrl = JobUrl,
        Source = Source,
        Description = Description,
        Tags = Tags,
        Stage = Stage,
        Priority = Priority,
        AppliedOn = AppliedOn,
        Deadline = Deadline
    };
}

public class MoveRequest
{
    public ApplicationStage Stage { get; set; }

    /// <summary>Place the card before this application in the column; omit to put it last.</summary>
    public int? BeforeId { get; set; }
}

public class NoteRequest
{
    public NoteKind Kind { get; set; } = NoteKind.Note;

    [Required]
    public string Content { get; set; } = "";
}

public class ReminderRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>When the reminder is due, in UTC.</summary>
    [Required]
    public DateTime? DueAt { get; set; }
}

public class ReminderUpdateRequest
{
    public bool? IsDone { get; set; }

    /// <summary>Push the due date back by this many days.</summary>
    [Range(1, 365)]
    public int? SnoozeDays { get; set; }
}

public class ContactRequest
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(120)]
    public string? Role { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(300)]
    public string? LinkedIn { get; set; }

    public Contact ToEntity(int id = 0) => new()
    {
        Id = id,
        Name = Name,
        Role = Role,
        Email = Email,
        Phone = Phone,
        LinkedIn = LinkedIn
    };
}

// ---------- Responses ----------

public record ApplicationResponse(
    int Id, string Company, string Position, string? Location, WorkMode WorkMode, EmploymentType EmploymentType,
    int? SalaryMin, int? SalaryMax, string Currency, string? JobUrl, string? Source, string[] Tags,
    ApplicationStage Stage, Priority Priority, DateOnly? AppliedOn, DateOnly? Deadline, bool IsArchived,
    DateTime CreatedAt, DateTime UpdatedAt, ReminderResponse? NextReminder);

public record ApplicationDetailResponse(
    ApplicationResponse Application, string? Description, NoteResponse[] Notes, ReminderResponse[] Reminders,
    ContactResponse[] Contacts, ActivityResponse[] Activity);

public record NoteResponse(int Id, NoteKind Kind, string Content, DateTime CreatedAt);

public record ReminderResponse(
    int Id, int ApplicationId, string Title, DateTime DueAt, bool IsDone, DateTime? CompletedAt, bool IsOverdue);

public record ContactResponse(int Id, string Name, string? Role, string? Email, string? Phone, string? LinkedIn);

public record ActivityResponse(ActivityKind Kind, string Description, DateTime OccurredAt);

public record StatsResponse(
    int Total, int InProgress, int Sent, int Responded, int ReachedInterview, int ReachedOffer,
    int OverdueReminders, Dictionary<ApplicationStage, int> ByStage);

public static class ResponseMapping
{
    // Dates are stored in UTC but come back from the database without a kind.
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static ApplicationResponse ToResponse(this JobApplication a) => new(
        a.Id, a.Company, a.Position, a.Location, a.WorkMode, a.EmploymentType, a.SalaryMin, a.SalaryMax, a.Currency,
        a.JobUrl, a.Source, a.TagList.ToArray(), a.Stage, a.Priority, a.AppliedOn, a.Deadline, a.IsArchived,
        Utc(a.CreatedAt), Utc(a.UpdatedAt), a.NextReminder?.ToResponse());

    public static ApplicationDetailResponse ToDetailResponse(this JobApplication a) => new(
        a.ToResponse(),
        a.Description,
        a.Notes.Select(n => n.ToResponse()).ToArray(),
        a.Reminders.Select(r => r.ToResponse()).ToArray(),
        a.Contacts.Select(c => c.ToResponse()).ToArray(),
        a.Activities.Select(x => new ActivityResponse(x.Kind, x.Description, Utc(x.OccurredAt))).ToArray());

    public static NoteResponse ToResponse(this ApplicationNote n) => new(n.Id, n.Kind, n.Content, Utc(n.CreatedAt));

    public static ReminderResponse ToResponse(this Reminder r) => new(
        r.Id, r.JobApplicationId, r.Title, Utc(r.DueAt), r.IsDone,
        r.CompletedAt is { } completed ? Utc(completed) : null, r.IsOverdue());

    public static ContactResponse ToResponse(this Contact c) => new(c.Id, c.Name, c.Role, c.Email, c.Phone, c.LinkedIn);
}
