# JobTracker

A job application tracker: a kanban board for your applications, with notes, contacts, follow-up reminders and insights, plus a REST API.

Built with ASP.NET Core 10, Blazor (interactive server), EF Core and SQL Server. Tested with xUnit, built by GitHub Actions, shipped as a Docker container to Azure Container Apps.

[![CI/CD](https://github.com/Shalom-hettiarachchi/JobTracker/actions/workflows/ci.yml/badge.svg)](https://github.com/Shalom-hettiarachchi/JobTracker/actions/workflows/ci.yml)

## Features

- **Kanban board** – drag cards between Wishlist, Applied, Screening, Interview, Offer, Accepted and Rejected, and reorder them inside a column. Search and filter by priority.
- **Application details** – salary, location, work mode, source, tags, deadline and the pasted job description.
- **Notes, contacts and activity** – typed notes (call, email, interview…), recruiter contacts, and an automatic history of every change.
- **Reminders** – per-application follow-ups with snooze, an overdue/today counter in the sidebar, and an automatic follow-up reminder when a card is moved to Applied.
- **Dashboard and insights** – weekly applications, pipeline, response rate, conversion funnel, interview rate by source, waiting time per stage.
- **Accounts** – register/log in with ASP.NET Core Identity; every user only sees their own data.
- **REST API** – everything the website does, over JSON with bearer tokens.
- **Extras** – CSV export, archive/restore, light and dark theme, mobile layout.

## Run it

### With Docker (app + SQL Server)

```bash
docker compose up --build
```

Open http://localhost:8080. The database schema is created automatically on first start.

### With the .NET SDK

Start only the database, then run the app against it:

```bash
docker compose up -d db
```

```bash
dotnet run --project src/JobTracker --launch-profile http
```

Open http://localhost:5015.

### Without any database server

For a quick look, the `sqlite` profile stores everything in a local file instead of SQL Server:

```bash
dotnet run --project src/JobTracker --launch-profile sqlite
```

## REST API

Interactive documentation is served at `/api/docs`, and the OpenAPI description at `/openapi/v1.json`.

Get a token, then send it as a bearer token:

```bash
curl -X POST http://localhost:5015/api/auth/register -H "Content-Type: application/json" -d '{"email":"me@example.com","password":"Sup3rSecret"}'
curl -X POST http://localhost:5015/api/auth/login -H "Content-Type: application/json" -d '{"email":"me@example.com","password":"Sup3rSecret"}'
curl http://localhost:5015/api/v1/applications -H "Authorization: Bearer <accessToken>"
```

| Method | Path | What it does |
|---|---|---|
| `POST` | `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh` | Create an account, get and renew tokens |
| `GET` | `/api/v1/applications?stage=&search=&includeArchived=` | List applications |
| `POST` | `/api/v1/applications` | Create one |
| `GET` `PUT` `DELETE` | `/api/v1/applications/{id}` | Read (with notes, reminders, contacts, activity), replace, delete |
| `PATCH` | `/api/v1/applications/{id}/stage` | Move to a stage and position: `{ "stage": "Interview", "beforeId": 12 }` |
| `POST` | `/api/v1/applications/{id}/archive`, `/restore` | Archive or restore |
| `POST` | `/api/v1/applications/{id}/notes` | Add a note |
| `DELETE` | `/api/v1/notes/{id}` | Delete a note |
| `GET` | `/api/v1/reminders?includeDone=` | List reminders |
| `POST` | `/api/v1/applications/{id}/reminders` | Add a reminder |
| `PATCH` `DELETE` | `/api/v1/reminders/{id}` | Complete or snooze (`{ "isDone": true }`, `{ "snoozeDays": 7 }`), delete |
| `POST` `PUT` | `/api/v1/applications/{id}/contacts[/{contactId}]` | Add or update a contact |
| `DELETE` | `/api/v1/contacts/{id}` | Delete a contact |
| `GET` | `/api/v1/stats` | Pipeline counts |

Invalid input returns `400` with a problem-details body, a missing or foreign record returns `404`, and a missing token returns `401`.

## Tests

```bash
dotnet test
```

- `JobServiceTests` – the business rules (stage moves, ordering, reminders, per-user isolation) against an in-memory SQLite database.
- `DisplayTests` – formatting and URL-safety helpers.
- `ApiTests` – the real app started in memory and called over HTTP.

The API tests use SQLite locally. Set `JOBTRACKER_TEST_SQLSERVER` to a SQL Server connection string to run them against SQL Server instead, which is what CI does.

## CI/CD

`.github/workflows/ci.yml` runs on every push and pull request:

1. **Build and test** – restores, builds and runs the tests with a SQL Server service container, so the migrations are exercised too.
2. **Build container image** – builds the Docker image and, on `main`, pushes it to `ghcr.io/shalom-hettiarachchi/jobtracker`.
3. **Deploy** – on `main`, rolls the new image out to Azure Container Apps. This job is skipped until the repository variables below exist.

## Deploy to Azure (free tier)

The app runs on two always-free offers: **Azure SQL Database free offer** (serverless, auto-pauses when the monthly allowance is used up) and **Azure Container Apps** consumption plan (scales to zero when idle, inside the monthly free grant).

1. Install the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) and sign in with `az login`.
2. Make the container image public: GitHub → your profile → Packages → `jobtracker` → Package settings → Change visibility. (Or pass `-RegistryUser` and `-RegistryToken` to the script instead.)
3. Create everything and deploy. Pick a globally unique app name and your time zone; you are prompted for a new SQL admin password:

   ```powershell
   ./infra/deploy-azure.ps1 -AppName jobtracker-yourname -Location southeastasia -TimeZone Asia/Colombo
   ```

4. To deploy automatically on every push, add what the script prints at the end to the repository settings (Settings → Secrets and variables → Actions): variables `AZURE_CONTAINER_APP` and `AZURE_RESOURCE_GROUP`, and secret `AZURE_CREDENTIALS`.

The first request after a quiet period can take up to a minute while the container and the database wake up.

## Project layout

| Path | What is there |
|---|---|
| `src/JobTracker/Api/` | REST endpoints and their request/response contracts |
| `src/JobTracker/Components/` | Blazor pages, shared components and the Identity pages |
| `src/JobTracker/Services/JobService.cs` | All data access, scoped to the signed-in user |
| `src/JobTracker/Models/` | Entities and display helpers |
| `src/JobTracker/Data/` | `ApplicationDbContext` and the SQL Server migrations |
| `tests/JobTracker.Tests/` | xUnit tests |
| `infra/deploy-azure.ps1` | Azure provisioning and deployment |
| `Dockerfile`, `docker-compose.yml` | Container image and local stack |

## Changing the data model

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/JobTracker -o Data/Migrations
```

## Known limitations

- Reminders are shown in the app only; no email or push notifications are sent.
- Password-reset and confirmation emails are not sent (the template's no-op email sender is still in place).
- Dates and times are shown in the server's time zone (the `TZ` setting), not each visitor's.
- The app runs as a single instance, because Blazor Server keeps each session in memory.
