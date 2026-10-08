# Municipal Problem Reporting & Tracking System

SPM622 local student prototype using C#, ASP.NET Core MVC/Razor, EF Core, SQL Server or Supabase PostgreSQL, SMS notification hooks, and ASP.NET Core Identity. Existing assignment files are preserved. This implementation follows the requested MVP scope.

## Windows prerequisites

- Visual Studio 2026, **ASP.NET and web development** workload. The solution targets .NET 9; `global.json` selects SDK 9.0.317 or a newer .NET 9 feature band. Check with `dotnet --list-sdks`. [Microsoft compatibility guidance](https://learn.microsoft.com/dotnet/core/install/windows).
- SQL Server Developer/Express or LocalDB, and SSMS, or a Supabase PostgreSQL project. LocalDB is SQL Server and supports SSMS. This machine's `(localdb)\MSSQLLocalDB` accepts Windows Authentication.
- Internet access for the first NuGet restore.

Run commands in PowerShell from the solution folder.

**Already configured on this machine:** the dedicated `MunicipalReporting_SPM622_20261007` LocalDB database and administrator `admin@municipal.local` were created during verification. Its randomly generated password is saved in this project's User Secrets. Open **Manage User Secrets** to retrieve it privately; no password is in the source files. You can run immediately with the commands below. To choose different credentials, configure a new dedicated database and run setup there; setup deliberately does not replace the existing administrator.

## Connection and secrets

The committed, password-free example in `Municipal.Web/appsettings.json` uses:

```text
Server=(localdb)\MSSQLLocalDB;Database=MunicipalReporting_SPM622_20261007;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5
```

For Express, change Server to `.\SQLEXPRESS`; for another instance use its exact SSMS server name, such as `PCNAME\INSTANCE` or `localhost`. Choose a **new database dedicated to this prototype** and check SSMS first. Windows Authentication uses the current Windows account. `TrustServerCertificate=True` is for local development certificates. Never commit connection strings containing passwords.

```powershell
dotnet restore Municipal.sln
dotnet tool restore
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Server=(localdb)\MSSQLLocalDB;Database=MunicipalReporting_SPM622_YourName;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5' --project Municipal.Web
$adminEmail = Read-Host 'Administrator email'
$adminSecret = Read-Host 'Administrator password' -AsSecureString
$adminPlain = [System.Net.NetworkCredential]::new('', $adminSecret).Password
dotnet user-secrets set 'Admin:Email' $adminEmail --project Municipal.Web
dotnet user-secrets set 'Admin:Password' $adminPlain --project Municipal.Web
Remove-Variable adminPlain, adminSecret
```

For Supabase, use the project database connection string from Supabase, enable the PostgreSQL provider, and keep the password in User Secrets or environment variables:

```powershell
dotnet user-secrets set 'Database:Provider' 'Supabase' --project Municipal.Web
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Host=YOUR-PROJECT.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=YOUR-DATABASE-PASSWORD;SSL Mode=Require;Trust Server Certificate=true' --project Municipal.Web
```

To enable SMS delivery, configure a provider endpoint that accepts JSON `{ to, from, message }`. If `Sms:ProviderUrl` is empty, the app logs what it would have sent and still saves the request.

```powershell
dotnet user-secrets set 'Sms:ProviderUrl' 'https://your-sms-provider.example/send' --project Municipal.Web
dotnet user-secrets set 'Sms:ApiKey' 'YOUR-SMS-API-KEY' --project Municipal.Web
dotnet user-secrets set 'Sms:From' 'Municipality' --project Municipal.Web
```

Choose a password with at least 12 characters including uppercase, lowercase, number, and punctuation. There are no hardcoded credentials and no public registration. User Secrets live outside the project; they are for local development and are not encrypted. Visual Studio: right-click **Municipal.Web → Manage User Secrets**. Alternatively configure process environment variables `ConnectionStrings__DefaultConnection`, `Admin__Email`, and `Admin__Password`; environment values override appsettings and User Secrets.

## Migrate and provision the administrator

```powershell
dotnet ef database update --project Municipal.Web
dotnet run --project Municipal.Web -- --setup
```

`--setup` also applies pending migrations, so the first command is optional but demonstrates the standard EF workflow. Migrations are checked in under `Municipal.Web/Data/Migrations`. Setup never drops or resets a database or overwrites an existing password. Re-running for the same email is safe; a different administrator is refused. Run setup once at a time and never target an unrelated database. After successful setup, the initial password secret can be removed:

For Supabase/PostgreSQL, run `dotnet run --project Municipal.Web -- --setup`; the checked-in migrations are SQL Server migrations, so setup creates the PostgreSQL schema directly from the EF model instead of applying those SQL Server migration files.

```powershell
dotnet user-secrets remove 'Admin:Password' --project Municipal.Web
```

Inspect migration SQL without applying it:

```powershell
dotnet ef migrations script --idempotent --project Municipal.Web --output migration.sql
```

Normal web startup does not migrate or seed. If SQL Server is unavailable, start the correct instance, verify its name and Windows permissions in SSMS, and re-run setup. For Supabase, confirm the connection string, SSL settings, and database password in the Supabase dashboard.

## Run locally / Visual Studio 2026

```powershell
dotnet dev-certs https --trust
dotnet build Municipal.sln
dotnet run --project Municipal.Web --launch-profile https
```

Open **https://localhost:7124**. HTTPS is required for the administrator's Secure cookie. Port 5049 redirects to HTTPS. In Visual Studio 2026 open `Municipal.sln`, set **Municipal.Web** as the startup project, choose the **https** launch profile, and press F5 or Ctrl+F5. Keep SQL Server running. SSMS manages SQL Server; it is not the database engine.

## Fictional demo data

```powershell
dotnet run --project Municipal.Web -- --seed-demo
```

This separate utility creates seven requests, one per category, labelled `[FICTIONAL DEMO]` in their locations and descriptions. It prints reference codes, applies migrations, and does not create or change administrators. Re-running avoids duplicate demo data. Normal startup never seeds data.

## Automated tests

```powershell
dotnet test Municipal.sln --logger 'console;verbosity=normal'
```

Tests exercise server validation, random references, submission and confirmation, durable SQL persistence, resident phone capture, assignment fields, reference lookup, all workflow stages, priority independence, code uniqueness, concurrent updates, anonymous admin protection, secure cookies, anti-forgery, disabled registration, and filtering. HTTP tests run the real MVC pipeline and Identity login.

SQL integration checks always use a fresh **MunicipalReporting_Tests_<GUID>** database. They never use the application database. The Windows account needs permission to create databases. For another instance:

```powershell
$env:MUNICIPAL_TEST_SQL = 'Server=.\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet test Municipal.sln
Remove-Item Env:MUNICIPAL_TEST_SQL
```

Any supplied database name is replaced by a fresh test name. Test databases are retained for inspection; no test deletes a database. After reviewing them, you may manually remove only the clearly named test databases in SSMS. If SQL is inaccessible the integration checks fail explicitly. Database-independent checks only: `dotnet test Municipal.sln --filter FullyQualifiedName~ValidationTests`.

With the app running, verify the actual HTTPS server separately:

```powershell
./scripts/Verify-Live.ps1
```

This script reads administrator credentials privately from User Secrets or environment variables, submits one labelled fictional report, logs in, changes priority independently, advances each status, and tracks the result. It uses the configured application database. It does not print passwords or bypass certificate validation. Keep the initial password secret available if you intend to run it; the normal application login only needs the stored Identity hash.

## SSMS inspection

1. Open SSMS → **Database Engine** → server `(localdb)\MSSQLLocalDB` (or your configured instance) → **Windows Authentication**. If prompted, trust the local development server certificate.
2. Connect, refresh **Databases**, expand the dedicated database → **Tables**.
3. Inspect `dbo.Requests`, `dbo.StatusHistory`, `dbo.AspNetUsers`, `dbo.AspNetRoles`, `dbo.AspNetUserRoles`, and `dbo.__EFMigrationsHistory`. Identity stores password hashes; do not share the user table.
4. Open **New Query**, select the correct database, and run:

```sql
SELECT ReferenceCode, Category, Location, ResidentPhoneNumber, AssignedRole, AssignedPerson, ReportedAt, Priority, Status, LastUpdatedAt
FROM dbo.Requests ORDER BY Priority DESC, ReportedAt ASC;
SELECT r.ReferenceCode, h.Status, h.ChangedAt
FROM dbo.StatusHistory h JOIN dbo.Requests r ON r.Id = h.ProblemRequestId
ORDER BY r.Id, h.ChangedAt;
```

Category 0-6 follows the dropdown order. Priority: Low=0, Normal=1, High=2, Urgent=3. Status: Submitted=0, Assigned=1, In Progress=2, Resolved=3, Closed=4. Dates are UTC `datetimeoffset`, displayed in SAST (UTC+2). Inspect table **Indexes** and **Constraints** for reference uniqueness and enum constraints.

## Short demo walkthrough

1. Report a fictional pothole address. Confirm **Submitted / Normal** and copy the reference.
2. Track the reference; check category, reporting date, last update, and initial history.
3. Log in using your configured administrator. The list defaults to highest priority, then oldest first.
4. Manage the request, change priority to Urgent while leaving Submitted, and save. Confirm progress is unchanged.
5. Advance to Assigned, In Progress, Resolved, then Closed, saving each stage. The server rejects skipped or reversed stages.
6. Track again; confirm Closed and all five timestamped history entries. Try an invalid reference and confirm a clear error without a public listing.
7. Test admin filters and sorting, log out, then visit `/Admin` and confirm the login redirect.

## Design and assumptions

See [database diagram](docs/database-diagram.md) and [verification record](docs/verification.md). Business logic lives in `Services/RequestService.cs`; form validation in `Models/FormModels.cs`; EF mappings in `Data/ApplicationDbContext.cs`; server authorization in controllers. Razor displays prepared data. EF parameterises all database queries.

References use 96 cryptographically random bits, a unique database index, and collision retries. SQL Server rowversion protects against lost updates in the default LocalDB setup. Each status update and its history are committed atomically. Priority-only and assignment-only changes update LastUpdatedAt but create no status-history entry. Workflow progress never implies a completion estimate. One local administrator, immediate next-stage transitions, SAST display, no public registration or password-reset interface. A resident phone number is required so the app can send an SMS containing the reference number, status, and assigned role/person when SMS is configured. A reference acts as a private lookup token. Photos, maps, analytics, and cloud deployment automation are outside this prototype.
