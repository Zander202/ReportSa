The script prompts for the database password and saves the connection in local User Secrets. It checks the existing application tables. Each group member needs to configure their own machine and use an existing authorised administrator account.

### Setting up your own database

For a separate installation use your own database connection and administrator details. Replace the example values below before running the commands.

```powershell
dotnet user-secrets set "Database:Provider" "Postgres" --project Municipal.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "YOUR_POSTGRES_CONNECTION_STRING" --project Municipal.Web
dotnet user-secrets set "Admin:Email" "your-admin@example.com" --project Municipal.Web
dotnet user-secrets set "Admin:Password" "YOUR_STRONG_UNIQUE_PASSWORD" --project Municipal.Web
dotnet run --project Municipal.Web -- --setup
dotnet run --project Municipal.Web
```

Use a password of at least 12 characters that meets the ASP.NET Identity password rules. Obtain the connection string and SSL settings from your database provider. For SQL Server set `Database:Provider` to `SqlServer` and supply a SQL Server connection string.

The setup command initialises the database and administrator account. Group members joining the existing shared database should use the joining instructions above instead.

Open the HTTPS address printed by the application. Administrator login cookies require HTTPS. For local development you can trust the development certificate with the following command.

```powershell
dotnet dev-certs https --trust
```

Keep database passwords and administrator credentials in User Secrets or deployment environment variables. User Secrets stay on your machine and are not shared through Git.

### Optional email notifications

Configure the `Email` settings in User Secrets for your SMTP service. These include `Enabled` and `Host` as well as `Port` and `EnableSsl`. Set `Username` and `Password` if your server requires authentication. Set `FromAddress` and `TrackUrl` to match your sender and application address.

The project includes a helper for the group Gmail setup.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Configure-Gmail.ps1
```

Restart the application after changing the settings. A report can be saved even when its email notification fails. The reference remains the way to track that report.

### Checking the database

```powershell
dotnet run --project Municipal.Web -- --check-database
```

The database owner can review and apply a schema upgrade when required. Back up shared data before applying schema changes.

```powershell
dotnet run --project Municipal.Web -- --upgrade-schema
```

## Tests

```powershell
dotnet test Municipal.sln
```

Database workflow tests need a separate SQL Server test environment. They use `MUNICIPAL_TEST_SQL` when supplied and otherwise try SQL Server LocalDB. Use a dedicated test server where the test runner can create and remove its temporary databases.

The retained test run from 20 October 2026 recorded 34 passed tests with no failures or skipped tests. That result applies to that saved run. Later changes need another test run. It does not establish code coverage or application performance. PostgreSQL still needs its own full workflow check.

### Optional browser checks

Install Node.js 20 or later and Google Chrome if you want to use the browser verification scripts. Start the application first.

```powershell
npm install
node verify-icons.cjs
```

Some scripts accept `REPORTSA_URL`. Check the selected script for its default address and required settings.

## Project folders

```text
Municipal.Web/       Application code and pages
Municipal.Tests/     Automated tests
scripts/            Setup and verification helpers
docs/               Documentation diagrams and screenshots
output/             Generated documents and presentations
Municipal.sln       Solution file
global.json         .NET SDK configuration
```

## Work we still want to do

We want to test the full workflow against PostgreSQL and observe residents and staff completing the main tasks. Their feedback would help us find the parts that are difficult to use.

We also want to strengthen repair review verification and abuse prevention. Reliable background email retries and tested backup recovery would make the application more suitable for a future pilot. We have not measured whether the system improves municipal repair times.

## Our group

Group 3. SPM622 Software Project Management.

- Zander du Plessis.
- Stern George.
- Caleb Lebron.

## Credits

The application uses [Leaflet](https://leafletjs.com/) and [OpenStreetMap](https://www.openstreetmap.org/copyright) for mapping. Municipality selection uses a bundled snapshot from the National Treasury municipal data directory. See the project source and bundled dependency notices for further details.
