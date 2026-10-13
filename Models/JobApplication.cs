using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobTracker.Models;

public enum ApplicationStage
{
    Wishlist = 0,
    Applied = 1,
    Screening = 2,
    Interview = 3,
    Offer = 4,
    Accepted = 5,
    Rejected = 6
}

public enum Priority { Low, Medium, High }

public enum WorkMode { Onsite, Hybrid, Remote }

public enum EmploymentType { FullTime, PartTime, Contract, Internship, Freelance }

public enum NoteKind { Note, Call, Email, Interview, Research }

public enum ActivityKind
{
    Created,
    StageChanged,
    Edited,
    NoteAdded,
    ReminderAdded,
    ReminderCompleted,
    Archived,
    Restored
}

public class JobApplication
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";

    [Required(ErrorMessage = "Company is required")]
    [MaxLength(120)]
    public string Company { get; set; } = "";

    [Required(ErrorMessage = "Position is required")]
    [MaxLength(160)]
    public string Position { get; set; } = "";

    [MaxLength(120)]
    public string? Location { get; set; }

    public WorkMode WorkMode { get; set; } = WorkMode.Onsite;

    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;

    [Range(0, int.MaxValue, ErrorMessage = "Salary cannot be negative")]
    public int? SalaryMin { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Salary cannot be negative")]
    public int? SalaryMax { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "USD";

    [MaxLength(500)]
    public string? JobUrl { get; set; }

    [MaxLength(60)]
    public string? Source { get; set; }

    public string? Description { get; set; }

    /// <summary>Comma-separated tags, e.g. "backend, fintech".</summary>
    [MaxLength(300)]
    public string? Tags { get; set; }

    public ApplicationStage Stage { get; set; } = ApplicationStage.Wishlist;

    /// <summary>The furthest pipeline stage this application ever reached (never Rejected).</summary>
    public ApplicationStage FurthestStage { get; set; } = ApplicationStage.Wishlist;

    public Priority Priority { get; set; } = Priority.Medium;

    /// <summary>Position of the card inside its board column.</summary>
    public int SortOrder { get; set; }

    public DateOnly? AppliedOn { get; set; }

    public DateOnly? Deadline { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime StageChangedAt { get; set; } = DateTime.UtcNow;

    public List<ApplicationNote> Notes { get; set; } = [];

    public List<Reminder> Reminders { get; set; } = [];

    public List<Contact> Contacts { get; set; } = [];

    public List<ActivityEntry> Activities { get; set; } = [];

    [NotMapped]
    public IEnumerable<string> TagList =>
        (Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    [NotMapped]
    public Reminder? NextReminder =>
        Reminders.Where(r => !r.IsDone).OrderBy(r => r.DueAt).FirstOrDefault();

    /// <summary>A detached copy of the editable fields, for binding to an edit form.</summary>
    public JobApplication CopyForEdit() => new()
    {
        Id = Id,
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

public class ApplicationNote
{
    public int Id { get; set; }

    public int JobApplicationId { get; set; }

    public JobApplication? JobApplication { get; set; }

    public NoteKind Kind { get; set; } = NoteKind.Note;

    [Required(ErrorMessage = "Write something first")]
    public string Content { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Reminder
{
    public int Id { get; set; }

    public int JobApplicationId { get; set; }

    public JobApplication? JobApplication { get; set; }

    [Required(ErrorMessage = "Title is required")]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>Stored in UTC.</summary>
    public DateTime DueAt { get; set; }

    public bool IsDone { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Contact
{
    public int Id { get; set; }

    public int JobApplicationId { get; set; }

    public JobApplication? JobApplication { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(120)]
    public string? Role { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(300)]
    public string? LinkedIn { get; set; }
}

public class ActivityEntry
{
    public int Id { get; set; }

    public int JobApplicationId { get; set; }

    public JobApplication? JobApplication { get; set; }

    public ActivityKind Kind { get; set; }

    [MaxLength(300)]
    public string Description { get; set; } = "";

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
