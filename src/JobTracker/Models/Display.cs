namespace JobTracker.Models;

/// <summary>Labels, CSS hooks and formatting shared by every page.</summary>
public static class Display
{
    public static readonly ApplicationStage[] Stages = Enum.GetValues<ApplicationStage>();

    /// <summary>Stages an application moves through before it is closed, in order.</summary>
    public static readonly ApplicationStage[] PipelineStages =
    [
        ApplicationStage.Applied,
        ApplicationStage.Screening,
        ApplicationStage.Interview,
        ApplicationStage.Offer,
        ApplicationStage.Accepted
    ];

    public static readonly string[] Sources =
        ["LinkedIn", "Company site", "Referral", "Indeed", "Glassdoor", "Recruiter", "Job fair", "Other"];

    public static readonly string[] Currencies = ["USD", "EUR", "GBP", "LKR", "INR", "AUD", "CAD", "SGD"];

    public static string Label(this ApplicationStage stage) => stage.ToString();

    public static string Css(this ApplicationStage stage) => "stage-" + stage.ToString().ToLowerInvariant();

    public static bool IsClosed(this ApplicationStage stage) =>
        stage is ApplicationStage.Accepted or ApplicationStage.Rejected;

    public static string Label(this WorkMode mode) => mode == WorkMode.Onsite ? "On-site" : mode.ToString();

    public static string Label(this EmploymentType type) => type switch
    {
        EmploymentType.FullTime => "Full-time",
        EmploymentType.PartTime => "Part-time",
        _ => type.ToString()
    };

    public static string Salary(JobApplication app)
    {
        static string Short(int value) => value >= 1000 ? $"{value / 1000d:0.#}k" : value.ToString();

        return (app.SalaryMin, app.SalaryMax) switch
        {
            (null, null) => "",
            (int min, null) => $"{app.Currency} {Short(min)}+",
            (null, int max) => $"Up to {app.Currency} {Short(max)}",
            (int min, int max) when min == max => $"{app.Currency} {Short(min)}",
            (int min, int max) => $"{app.Currency} {Short(min)} – {Short(max)}"
        };
    }

    /// <summary>Returns a web link that is safe to put in an href, or null if the text is not one.</summary>
    public static string? SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        url = url.Trim();
        if (!url.Contains("://"))
        {
            url = "https://" + url;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? url : null;
    }

    public static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static string Initials(string company)
    {
        var words = company.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..1].ToUpperInvariant(),
            _ => (words[0][..1] + words[1][..1]).ToUpperInvariant()
        };
    }

    /// <summary>A stable hue (0-359) per company so its avatar keeps the same colour everywhere.</summary>
    public static int Hue(string company)
    {
        var hash = 17;
        foreach (var c in company.ToLowerInvariant())
        {
            hash = unchecked(hash * 31 + c);
        }

        return Math.Abs(hash % 360);
    }

    public static DateTime Local(this DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    public static string Ago(this DateTime utc)
    {
        var span = DateTime.UtcNow - DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return utc.Local().ToString("d MMM yyyy");
    }

    /// <summary>"Today 14:30", "Tomorrow 09:00", "Mon 3 Nov 09:00" in local time.</summary>
    public static string DueLabel(this DateTime utc)
    {
        var local = utc.Local();
        var days = (local.Date - DateTime.Now.Date).Days;
        var time = local.ToString("HH:mm");
        return days switch
        {
            0 => $"Today {time}",
            1 => $"Tomorrow {time}",
            -1 => $"Yesterday {time}",
            _ => local.ToString(local.Year == DateTime.Now.Year ? "ddd d MMM" : "d MMM yyyy") + " " + time
        };
    }

    public static bool IsOverdue(this Reminder reminder) =>
        !reminder.IsDone && DateTime.SpecifyKind(reminder.DueAt, DateTimeKind.Utc) < DateTime.UtcNow;

    public static bool IsDueToday(this Reminder reminder) =>
        !reminder.IsDone && reminder.DueAt.Local().Date == DateTime.Now.Date;
}
