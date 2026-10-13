using JobTracker.Models;

namespace JobTracker.Tests;

public class DisplayTests
{
    [Theory]
    [InlineData(null, null, "")]
    [InlineData(60000, null, "USD 60k+")]
    [InlineData(null, 80000, "Up to USD 80k")]
    [InlineData(70000, 70000, "USD 70k")]
    [InlineData(65000, 78500, "USD 65k – 78.5k")]
    [InlineData(500, 900, "USD 500 – 900")]
    public void Salary_is_formatted_as_a_short_range(int? min, int? max, string expected)
    {
        var app = new JobApplication { SalaryMin = min, SalaryMax = max, Currency = "USD" };

        Assert.Equal(expected, Display.Salary(app));
    }

    [Theory]
    [InlineData("https://example.com/jobs/1", "https://example.com/jobs/1")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("  careers.example.com/role ", "https://careers.example.com/role")]
    public void SafeUrl_accepts_web_addresses(string input, string expected) =>
        Assert.Equal(expected, Display.SafeUrl(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///c:/windows")]
    public void SafeUrl_rejects_everything_else(string? input) =>
        Assert.Null(Display.SafeUrl(input));

    [Theory]
    [InlineData("Contoso", "C")]
    [InlineData("northwind labs", "NL")]
    [InlineData("Wide World Importers", "WW")]
    [InlineData("", "?")]
    public void Initials_use_the_first_two_words(string company, string expected) =>
        Assert.Equal(expected, Display.Initials(company));

    [Theory]
    [InlineData("2026-10-12", "2026-10-12")] // Monday
    [InlineData("2026-10-15", "2026-10-12")] // Thursday
    [InlineData("2026-10-18", "2026-10-12")] // Sunday
    public void Weeks_start_on_monday(string date, string expected) =>
        Assert.Equal(DateTime.Parse(expected), Display.StartOfWeek(DateTime.Parse(date).AddHours(15)));

    [Fact]
    public void Tags_are_trimmed_and_deduplicated()
    {
        var app = new JobApplication { Tags = " backend, API ,backend,, api " };

        Assert.Equal(["backend", "API"], app.TagList);
    }

    [Fact]
    public void Next_reminder_is_the_earliest_one_still_open()
    {
        var now = DateTime.UtcNow;
        var app = new JobApplication
        {
            Reminders =
            [
                new Reminder { Title = "Done", DueAt = now.AddDays(-3), IsDone = true },
                new Reminder { Title = "Later", DueAt = now.AddDays(5) },
                new Reminder { Title = "Sooner", DueAt = now.AddDays(1) }
            ]
        };

        Assert.Equal("Sooner", app.NextReminder?.Title);
    }

    [Fact]
    public void Only_open_reminders_in_the_past_are_overdue()
    {
        var past = DateTime.UtcNow.AddHours(-1);

        Assert.True(new Reminder { DueAt = past }.IsOverdue());
        Assert.False(new Reminder { DueAt = past, IsDone = true }.IsOverdue());
        Assert.False(new Reminder { DueAt = DateTime.UtcNow.AddHours(1) }.IsOverdue());
    }

    [Fact]
    public void Accepted_and_rejected_are_the_closed_stages()
    {
        var closed = Display.Stages.Where(s => s.IsClosed());

        Assert.Equal([ApplicationStage.Accepted, ApplicationStage.Rejected], closed);
    }
}
