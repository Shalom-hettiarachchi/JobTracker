using JobTracker.Data;
using JobTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Services;

/// <summary>Fills an empty account with a realistic job search so the app can be explored straight away.</summary>
public class SampleData(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    private sealed record Seed(
        string Company, string Position, string Location, WorkMode Mode, ApplicationStage Stage,
        ApplicationStage Furthest, Priority Priority, int? Min, int? Max, string Source, string Tags, int AgeDays);

    private static readonly Seed[] Seeds =
    [
        new("Northwind Labs", "Senior .NET Developer", "London, UK", WorkMode.Hybrid, ApplicationStage.Interview, ApplicationStage.Interview, Priority.High, 75000, 90000, "LinkedIn", "backend, c#, azure", 21),
        new("Contoso Health", "Full Stack Engineer", "Remote", WorkMode.Remote, ApplicationStage.Offer, ApplicationStage.Offer, Priority.High, 82000, 95000, "Referral", "blazor, healthcare", 34),
        new("Fabrikam Cloud", "Backend Engineer", "Berlin, Germany", WorkMode.Hybrid, ApplicationStage.Screening, ApplicationStage.Screening, Priority.Medium, 70000, 85000, "Company site", "backend, kubernetes", 12),
        new("Tailspin Games", "Gameplay Tools Programmer", "Stockholm, Sweden", WorkMode.Onsite, ApplicationStage.Applied, ApplicationStage.Applied, Priority.Medium, 60000, 72000, "Indeed", "tools, c#", 6),
        new("Adventure Works", "Software Engineer II", "Seattle, USA", WorkMode.Onsite, ApplicationStage.Applied, ApplicationStage.Applied, Priority.Low, 110000, 130000, "LinkedIn", "backend", 9),
        new("Wide World Importers", "API Developer", "Remote", WorkMode.Remote, ApplicationStage.Applied, ApplicationStage.Applied, Priority.Medium, 65000, 78000, "Glassdoor", "api, logistics", 3),
        new("Litware Fintech", "Platform Engineer", "Singapore", WorkMode.Hybrid, ApplicationStage.Wishlist, ApplicationStage.Wishlist, Priority.High, 95000, 120000, "Recruiter", "fintech, platform", 2),
        new("Proseware", "Developer Advocate", "Remote", WorkMode.Remote, ApplicationStage.Wishlist, ApplicationStage.Wishlist, Priority.Low, null, null, "Company site", "devrel", 1),
        new("Woodgrove Bank", "Senior Software Engineer", "Dublin, Ireland", WorkMode.Hybrid, ApplicationStage.Rejected, ApplicationStage.Interview, Priority.Medium, 80000, 92000, "LinkedIn", "fintech, backend", 48),
        new("Lucerne Publishing", "Web Developer", "Zurich, Switzerland", WorkMode.Onsite, ApplicationStage.Rejected, ApplicationStage.Applied, Priority.Low, 85000, 95000, "Indeed", "frontend", 40),
        new("Alpine Ski House", "Lead Engineer", "Denver, USA", WorkMode.Remote, ApplicationStage.Screening, ApplicationStage.Screening, Priority.High, 125000, 145000, "Referral", "leadership, backend", 15),
        new("Graphic Design Institute", "Frontend Engineer", "Amsterdam, Netherlands", WorkMode.Hybrid, ApplicationStage.Interview, ApplicationStage.Interview, Priority.Medium, 68000, 80000, "Job fair", "frontend, typescript", 26),
        new("Margie's Travel", "Mobile Developer (.NET MAUI)", "Lisbon, Portugal", WorkMode.Remote, ApplicationStage.Wishlist, ApplicationStage.Wishlist, Priority.Medium, 55000, 68000, "LinkedIn", "mobile, maui", 4),
        new("Trey Research", "Research Software Engineer", "Cambridge, UK", WorkMode.Onsite, ApplicationStage.Accepted, ApplicationStage.Accepted, Priority.High, 78000, 88000, "Referral", "research, ml", 60)
    ];

    /// <summary>Adds the sample applications. Returns false if the account already has data.</summary>
    public async Task<bool> SeedAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.JobApplications.AnyAsync(a => a.UserId == userId))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var order = 0;

        foreach (var seed in Seeds)
        {
            var created = now.AddDays(-seed.AgeDays).AddHours(-order);
            var app = new JobApplication
            {
                UserId = userId,
                Company = seed.Company,
                Position = seed.Position,
                Location = seed.Location,
                WorkMode = seed.Mode,
                Stage = seed.Stage,
                FurthestStage = seed.Furthest,
                Priority = seed.Priority,
                SalaryMin = seed.Min,
                SalaryMax = seed.Max,
                Currency = "USD",
                Source = seed.Source,
                Tags = seed.Tags,
                SortOrder = order++,
                CreatedAt = created,
                JobUrl = "https://example.com/careers",
                Description = $"{seed.Position} at {seed.Company}. Sample posting text – replace it with the real job description so you can refer back to it before interviews."
            };

            app.Activities.Add(new ActivityEntry
            {
                Kind = ActivityKind.Created,
                Description = "Added to Wishlist",
                OccurredAt = created
            });

            // Walk the application through each stage it reached, spreading the moves over its lifetime.
            var path = Display.PipelineStages.Where(s => s <= seed.Furthest).ToList();
            if (seed.Stage == ApplicationStage.Rejected)
            {
                path.Add(ApplicationStage.Rejected);
            }

            var previous = ApplicationStage.Wishlist;
            var lastMove = created;
            for (var i = 0; i < path.Count; i++)
            {
                lastMove = created.AddDays((i + 1) * (double)seed.AgeDays / (path.Count + 1));
                app.Activities.Add(new ActivityEntry
                {
                    Kind = ActivityKind.StageChanged,
                    Description = $"Moved from {previous.Label()} to {path[i].Label()}",
                    OccurredAt = lastMove
                });
                if (path[i] == ApplicationStage.Applied)
                {
                    app.AppliedOn = DateOnly.FromDateTime(lastMove.ToLocalTime());
                }

                previous = path[i];
            }

            app.StageChangedAt = lastMove;
            app.UpdatedAt = lastMove;

            AddDetails(app, now, lastMove);
            db.JobApplications.Add(app);
        }

        await db.SaveChangesAsync();
        return true;
    }

    private static void AddDetails(JobApplication app, DateTime now, DateTime lastMove)
    {
        switch (app.Stage)
        {
            case ApplicationStage.Wishlist:
                app.Deadline = DateOnly.FromDateTime(DateTime.Now.AddDays(10 + app.Company.Length % 7));
                app.Reminders.Add(new Reminder
                {
                    Title = $"Tailor CV and apply to {app.Company}",
                    DueAt = now.AddDays(1 + app.Company.Length % 4)
                });
                break;

            case ApplicationStage.Applied:
                app.Reminders.Add(new Reminder
                {
                    Title = $"Follow up with {app.Company}",
                    DueAt = app.Company.Length % 2 == 0 ? now.AddDays(-1) : now.AddHours(5)
                });
                break;

            case ApplicationStage.Screening:
                app.Contacts.Add(new Contact
                {
                    Name = "Priya Nair",
                    Role = "Talent Partner",
                    Email = "priya.nair@example.com"
                });
                app.Notes.Add(new ApplicationNote
                {
                    Kind = NoteKind.Call,
                    Content = "30 minute intro call. Team of 8, two-week sprints, on-call once a month. They will send a take-home task next.",
                    CreatedAt = lastMove.AddHours(2)
                });
                app.Reminders.Add(new Reminder
                {
                    Title = "Send availability for the technical screen",
                    DueAt = now.AddDays(2)
                });
                break;

            case ApplicationStage.Interview:
                app.Contacts.Add(new Contact
                {
                    Name = "Daniel Okafor",
                    Role = "Engineering Manager",
                    Email = "daniel.okafor@example.com",
                    LinkedIn = "https://www.linkedin.com/in/example"
                });
                app.Notes.Add(new ApplicationNote
                {
                    Kind = NoteKind.Interview,
                    Content = "Technical round went well. Questions on async/await pitfalls, EF Core query tuning and system design for a notification service. Need to brush up on caching strategies.",
                    CreatedAt = lastMove.AddDays(1)
                });
                app.Notes.Add(new ApplicationNote
                {
                    Kind = NoteKind.Research,
                    Content = "Recently raised a Series B. Product is moving from a monolith to services – ask about the migration plan.",
                    CreatedAt = lastMove.AddHours(-6)
                });
                app.Reminders.Add(new Reminder
                {
                    Title = "Prepare for the final panel interview",
                    DueAt = now.AddDays(3)
                });
                break;

            case ApplicationStage.Offer:
                app.Contacts.Add(new Contact
                {
                    Name = "Hannah Lindqvist",
                    Role = "Head of People",
                    Email = "hannah.lindqvist@example.com",
                    Phone = "+44 20 7946 0000"
                });
                app.Notes.Add(new ApplicationNote
                {
                    Kind = NoteKind.Email,
                    Content = "Offer received: base at the top of the band, 25 days leave, remote-first. They want an answer by the end of next week.",
                    CreatedAt = lastMove.AddHours(1)
                });
                app.Reminders.Add(new Reminder
                {
                    Title = "Reply to the offer",
                    DueAt = now.AddDays(4)
                });
                break;

            case ApplicationStage.Rejected:
                app.Notes.Add(new ApplicationNote
                {
                    Content = "Polite rejection. Asked for feedback – they went with someone with more domain experience.",
                    CreatedAt = lastMove.AddHours(3)
                });
                break;

            case ApplicationStage.Accepted:
                app.Notes.Add(new ApplicationNote
                {
                    Content = "Signed the contract. Start date agreed for the first Monday of next month.",
                    CreatedAt = lastMove.AddHours(4)
                });
                break;
        }
    }
}
