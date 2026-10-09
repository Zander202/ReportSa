Municipal Problem Reporting and Tracking System

Setup

Municipality selection uses a bundled snapshot of National Treasury's municipality directory, excluding disestablished codes. Sources: https://municipaldata.treasury.gov.za/api/cubes/municipalities/facts and https://municipaldata.treasury.gov.za/api/cubes/demarcation_changes/facts (retrieved 8 October 2026).

Browser location attaches GPS coordinates after permission is granted. Residents can still enter an address manually. Unconfigured assignees use fictional demo staff; completion times are provisional prototype estimates. Administrators can replace the assignee and adjust the ETA.

After pulling schema changes, run `dotnet run --project Municipal.Web -- --setup` before starting the website. Supabase setup adds the new report fields while preserving existing records.

Gmail setup: enable Google 2-Step Verification and create an app password at https://myaccount.google.com/apppasswords, then run `powershell -ExecutionPolicy Bypass -File .\scripts\Configure-Gmail.ps1`. The script prompts for the sender address and a hidden app password, sends a test email to the sender, and saves settings in User Secrets. Restart the website afterward. See https://support.google.com/accounts/answer/185833.

1. Set the Supabase PostgreSQL connection string with user secrets or environment variables.

```powershell
dotnet user-secrets set "Database:Provider" "Postgres" --project Municipal.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=YOUR_SUPABASE_HOST;Port=5432;Database=postgres;Username=postgres.YOUR_PROJECT_REF;Password=YOUR_PASSWORD;SSL Mode=Require;Trust Server Certificate=true" --project Municipal.Web
```

2. Set the first administrator account.

```powershell
dotnet user-secrets set "Admin:Email" "admin@municipal.gov.za" --project Municipal.Web
dotnet user-secrets set "Admin:Password" "Use-a-strong-12-character-password" --project Municipal.Web
dotnet run --project Municipal.Web -- --setup
```

3. Optional email notifications use SMTP settings.

```powershell
dotnet user-secrets set "Email:Enabled" "true" --project Municipal.Web
dotnet user-secrets set "Email:Host" "smtp.gmail.com" --project Municipal.Web
dotnet user-secrets set "Email:Port" "587" --project Municipal.Web
dotnet user-secrets set "Email:Username" "your-email@example.com" --project Municipal.Web
dotnet user-secrets set "Email:Password" "your-app-password" --project Municipal.Web
dotnet user-secrets set "Email:FromAddress" "your-email@example.com" --project Municipal.Web
dotnet user-secrets set "Email:TrackUrl" "https://localhost:7124/Requests/Track" --project Municipal.Web
```

Run and test

Reports, municipality details, GPS coordinates, assignments, email delivery status,
tracking history, users, password hashes, and roles are stored in the configured
PostgreSQL database. Logins continue to use ASP.NET Identity, not Supabase Auth.
Use the same Supabase database on every teammate's machine to share these records
and keep the existing accounts. User Secrets are local to each machine and are not
included in Git; each teammate must configure their connection separately.
In Supabase's Connect dialog, use the Session pooler connection if the teammate's
network only supports IPv4. Never commit the database password.

Verify the database and existing administrator without changing records:

```powershell
dotnet run --project Municipal.Web -- --verify-database
```

Test report creation, readback, and updates in a transaction that rolls back:

```powershell
dotnet run --project Municipal.Web -- --verify-storage
```

Compiled local launches load the project's existing User Secrets too. Environment
variables override them, so a deployed server can use
`ConnectionStrings__DefaultConnection` and `Database__Provider=Postgres`.

```powershell
dotnet run --project Municipal.Web
dotnet test Municipal.sln --no-restore
```
