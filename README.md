# JobTracker

A job application tracker built with ASP.NET Core 10, Blazor (interactive server), EF Core and SQLite.

## Run it

```bash
dotnet run --launch-profile http
```

Then open http://localhost:5015. The database (`Data/app.db`) is created and migrated automatically on start.

## Features

- **Kanban board** – drag cards between Wishlist, Applied, Screening, Interview, Offer, Accepted and Rejected, and reorder them inside a column. Search and filter by priority.
- **Application details** – salary, location, work mode, source, tags, deadline and the pasted job description.
- **Notes, contacts and activity** – typed notes (call, email, interview…), recruiter contacts, and an automatic history of every change.
- **Reminders** – per-application follow-ups with snooze, an overdue/today counter in the sidebar, and an automatic follow-up reminder when a card is moved to Applied.
- **Dashboard and insights** – weekly applications, pipeline, response rate, conversion funnel, interview rate by source, waiting time per stage.
- **Accounts** – register/log in with ASP.NET Core Identity; every user only sees their own data.
- **Extras** – CSV export, archive/restore, light and dark theme, mobile layout.

## Project layout

| Path | What is there |
|---|---|
| `Models/` | Entities (`JobApplication`, `Reminder`, …) and display helpers |
| `Services/JobService.cs` | All data access, scoped to the signed-in user |
| `Components/Pages/` | Dashboard, Board, Applications, Reminders, Insights |
| `Components/Shared/` | Detail panel, form, drawer, icons |
| `Components/Account/` | Identity pages (login, register, manage) |
| `Data/` | `ApplicationDbContext` and EF Core migrations |
| `wwwroot/app.css` | The design system and all styling |

## Changing the data model

```bash
dotnet tool restore
dotnet ef migrations add <Name> -o Data/Migrations
```

## Not included yet

- Reminders are shown in the app only; no email or push notifications are sent.
- Password-reset and confirmation emails are not sent (the template's no-op email sender is still in place).
