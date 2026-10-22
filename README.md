# Municipal Problem Reporting & Tracking System

A web application for South African residents to report and track municipal infrastructure problems, and for municipal administrators to manage and resolve them.

> **Student prototype** — not intended for production use or emergencies.  
> For emergencies: SAPS 10111 · Ambulance 10177 · Cell 112

---

## Contents

- [Overview](#overview)
- [Features](#features)
- [Technology Stack](#technology-stack)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
  - [Database Setup](#database-setup)
  - [Running the Application](#running-the-application)
  - [Creating an Administrator](#creating-an-administrator)
- [Configuration](#configuration)
- [Architecture](#architecture)
- [Data Model](#data-model)
- [Workflow](#workflow)
- [Running Tests](#running-tests)
- [Command-Line Utilities](#command-line-utilities)
- [Project Structure](#project-structure)

---

## Overview

The Municipal Problem Reporting & Tracking System allows residents to submit reports for problems such as potholes, power outages, broken streetlights, water leaks, blocked drains, and illegal dumping. Each report receives a unique reference code that can be used to track progress without requiring an account. Municipal administrators manage requests through a dashboard, advancing them through a status workflow and sending email notifications at each stage.

---

## Features

### Resident-Facing
- **Submit a report** with location (GPS pin or manual address entry), category, description, and up to three photos
- **Track a report** by reference code — view status history, assigned team, estimated completion, and resolution photos
- **Confirm a repair** after the administrator marks it resolved, or reopen it if the problem is still present
- **See nearby reports** — browse active reports by municipality or within 5 km, and indicate "I'm affected too"
- **Email notifications** — confirmation on submission, updates at each workflow stage, and a resolution prompt when complete

### Administrator-Facing
- **Dashboard** — filter and sort all requests by category, priority, status, and municipality; paginated list (25 per page) with overdue flags and key metrics
- **Update requests** — advance status, adjust priority, assign staff, set an estimated completion date, add progress notes, and attach resolution photos (up to 3)
- **Close tickets** — either naturally via confirmed resident review or manually from any stage
- **User management** — create additional administrator accounts directly in the app
- **Metrics panel** — open/overdue/resolved counts, average resolution time, per-municipality breakdown

### Community
- **Affected count** — other residents can register as affected on any open report (deduplicated by email hash)
- **Nearby map data** — public JSON API returns sanitised nearby reports for map rendering
- **Resident review** — reporter confirms or rejects resolution; rejection routes the ticket back to In Progress

---

## Technology Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core 9.0 MVC |
| ORM | Entity Framework Core 9.0 |
| Database | PostgreSQL (Supabase) or SQL Server |
| Identity | ASP.NET Identity with EF Core |
| Views | Razor Pages / cshtml |
| Testing | xUnit with MVC integration testing |
| Target runtime | .NET 9.0 |

**NuGet packages:**

| Package | Version |
|---|---|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 9.0.10 |
| Microsoft.EntityFrameworkCore.SqlServer | 9.0.10 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 |
| Microsoft.EntityFrameworkCore.Design | 9.0.10 |

---

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- SQL Server (LocalDB, Express, or full) **or** a PostgreSQL connection string (e.g. [Supabase](https://supabase.com))
- An SMTP server or Gmail account (optional — email features degrade gracefully if not configured)

---

## Getting Started

### Database Setup

**Option A — SQL Server (default for local development)**

1. Ensure SQL Server LocalDB is installed (ships with Visual Studio).
2. Add the connection string to user secrets:

   ```powershell
   cd Municipal.Web
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=MunicipalReporting;Trusted_Connection=True;"
   ```

3. Apply migrations:

   ```powershell
   dotnet run --project Municipal.Web -- --upgrade-schema
   ```

**Option B — PostgreSQL / Supabase**

1. Set the provider and connection string:

   ```powershell
   cd Municipal.Web
   dotnet user-secrets set "Database:Provider" "Postgres"
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Database=...;Username=...;Password=..."
   ```

2. Apply migrations:

   ```powershell
   dotnet run --project Municipal.Web -- --upgrade-schema
   ```

> Alternatively run the `scripts/Configure-Supabase.ps1` helper script which will prompt for connection details and write secrets automatically.

### Running the Application

```powershell
dotnet run --project Municipal.Web
```

The application starts on `https://localhost:7124` (HTTP redirect from `5049`). The exact port is set in `Municipal.Web/Properties/launchSettings.json`.

### Creating an Administrator

Run the setup utility to create the first administrator account:

```powershell
dotnet run --project Municipal.Web -- --setup
```

You will be prompted for an email address and a generated password will be stored in .NET User Secrets. To retrieve the password later, open **Manage User Secrets** in Visual Studio or run:

```powershell
dotnet user-secrets list --project Municipal.Web
```

Additional administrators can also be created through the running application at `/Admin/CreateUser` (requires an existing admin login).

---

## Configuration

Configuration values are merged from `appsettings.json` → `appsettings.Development.json` → environment variables → user secrets. Sensitive values (connection strings, passwords) must be stored in user secrets, never committed to source control.

### Required settings

| Key | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | Database connection string |
| `Database:Provider` | `Postgres` or `SqlServer` (default: `Postgres`) |

### Optional settings

**Email (`appsettings.json` → `Email` section)**

| Key | Default | Description |
|---|---|---|
| `Email:Enabled` | `false` | Enable outgoing email |
| `Email:Host` | `smtp.gmail.com` | SMTP server hostname |
| `Email:Port` | `587` | SMTP port |
| `Email:EnableSsl` | `true` | Enable STARTTLS |
| `Email:Username` | — | SMTP login username |
| `Email:Password` | — | SMTP login password (use user secrets) |
| `Email:FromAddress` | — | Sender email address |
| `Email:FromName` | `Municipal Connect` | Sender display name |
| `Email:TrackUrl` | — | Base URL for tracking links in emails, e.g. `https://localhost:7124/Requests/Track` |

**Assignments (`appsettings.json` → `Assignments` section)**

Default role and person for each problem category. When `Person` is blank or `"To be assigned"`, a demo staff name is randomly selected.

```json
"Assignments": {
  "Pothole":            { "Role": "Roads maintenance team",       "Person": "To be assigned" },
  "PowerOutage":        { "Role": "Electrical services team",     "Person": "To be assigned" },
  "BrokenStreetlight":  { "Role": "Street lighting technician",   "Person": "To be assigned" },
  "WaterLeak":          { "Role": "Water services technician",    "Person": "To be assigned" },
  "BlockedDrain":       { "Role": "Drainage maintenance team",    "Person": "To be assigned" },
  "IllegalDumping":     { "Role": "Waste management team",        "Person": "To be assigned" },
  "Other":              { "Role": "Municipal support desk",       "Person": "To be assigned" }
}
```

---

## Architecture

```
Municipal.sln
├── Municipal.Web          # ASP.NET Core 9 MVC web application
│   ├── Controllers/       # HTTP request handlers
│   ├── Models/            # Domain models, enums, form/view models
│   ├── Views/             # Razor views (.cshtml)
│   ├── Services/          # Business logic layer
│   ├── Data/              # EF Core DbContext and migrations
│   └── Program.cs         # App startup, DI registration
└── Municipal.Tests        # xUnit integration tests
```

### Service Layer

| Service | Lifetime | Responsibility |
|---|---|---|
| `RequestService` | Scoped | Submit, track, and update problem requests; workflow validation; email dispatch |
| `CommunityService` | Scoped | Nearby reports, affected support, resident review, dashboard metrics |
| `PhotoUploadService` | Scoped | Photo validation, magic-byte type detection, size enforcement |
| `AssignmentService` | Singleton | Role/person assignment per category; ETA calculation |
| `MunicipalityDirectory` | Singleton | In-memory list of 257 South African municipalities |
| `MunicipalityLocationService` | Singleton (HttpClient) | Reverse geocoding and address search via external APIs |
| `EmailService` | Singleton | Branded HTML email templates; SMTP delivery |

### Controllers

| Controller | Auth | Responsibility |
|---|---|---|
| `RequestsController` | Public | Reporting, tracking, nearby, community support, resident review |
| `AdminController` | Administrator role | Dashboard, request updates, user management |
| `AccountController` | Mixed | Admin login/logout |

---

## Data Model

See [docs/database-diagram.md](docs/database-diagram.md) for the full ER diagram.

### Core entities

**`ProblemRequest`** — the central record for each reported problem:

| Field | Type | Description |
|---|---|---|
| `Id` | int | Primary key |
| `ReferenceCode` | string(32) | Unique public tracking code, format `MUN-{24 hex chars}` |
| `Category` | enum | Pothole, PowerOutage, BrokenStreetlight, WaterLeak, BlockedDrain, IllegalDumping, Other |
| `Priority` | enum | Low, Normal, High, Urgent |
| `Status` | enum | Submitted → Assigned → InProgress → Resolved → Closed |
| `Location` | string(200) | Human-readable address or place name |
| `Latitude / Longitude` | double? | GPS coordinates (optional) |
| `MunicipalityCode` | string(10) | Code from the municipalities directory |
| `MunicipalityName` | string(200) | Stored display name |
| `Description` | string(1000) | Optional free-text description |
| `ResidentEmail` | string(254) | Reporter's email for notifications |
| `AssignedRole` | string(120) | Team name (e.g. "Roads maintenance team") |
| `AssignedPerson` | string(120) | Assigned staff member |
| `EstimatedCompletionAt` | DateTimeOffset? | Provisional ETA in UTC; displayed as SAST (UTC+2) |
| `ReportedAt / LastUpdatedAt` | DateTimeOffset | Timestamps |
| `ResolvedAt` | DateTimeOffset? | When the request reached Resolved status |
| `ResidentReview` | enum | Awaiting, Confirmed, NotFixed |
| `ResolutionNotes` | string(1000) | Admin notes on the resolution |
| `RowVersion` | byte[] | Optimistic concurrency token |
| `ConfirmationEmailStatus` | enum | Disabled, Sent, Failed |

**`StatusHistory`** — one record per status transition:

| Field | Description |
|---|---|
| `ProblemRequestId` | FK to Requests |
| `Status` | Status value at that point |
| `ChangedAt` | Timestamp of the change |
| `Note` | Optional note (e.g. progress update, assignment change) |

**`RequestPhoto`** — uploaded image attached to a request:

| Field | Description |
|---|---|
| `FileName` | Original filename (max 120 chars) |
| `ContentType` | Detected MIME type: `image/jpeg`, `image/png`, or `image/webp` |
| `Content` | Raw image bytes (max 5 MB) |
| `IsResolution` | `true` = repair evidence; `false` = initial submission photo |
| `UploadedAt` | Upload timestamp |

**`RequestSupporter`** — resident indicating they're affected by the same problem:

| Field | Description |
|---|---|
| `Email` | Stored email (not exposed publicly) |
| `EmailKey` | SHA-256 of uppercased email — unique index prevents duplicates |
| `JoinedAt` | Timestamp |

### Enums

```csharp
enum Category     { Pothole, PowerOutage, BrokenStreetlight, WaterLeak, BlockedDrain, IllegalDumping, Other }
enum Priority     { Low, Normal, High, Urgent }
enum RequestStatus { Submitted, Assigned, InProgress, Resolved, Closed }
enum ResidentReview { Awaiting, Confirmed, NotFixed }
enum EmailDeliveryStatus { Disabled, Sent, Failed }
```

---

## Workflow

### Status progression

```
Submitted → Assigned → InProgress → Resolved → Closed
                                        ↓
                              Resident review
                             /               \
                     Confirmed              NotFixed
                        ↓                     ↓
                     Closed             InProgress
```

- Admins can only advance one stage at a time, or jump directly to Closed.
- When a request is marked **Resolved**, the reporter receives an email prompting them to confirm or reject the repair.
- If the reporter selects **Still not fixed**, the status reverts to **InProgress** and `ResolvedAt` is cleared.
- If the reporter **confirms** the repair, the status is set to **Closed**.
- Admins can also close a ticket manually at any point.

### Estimated completion times (from submission)

| Category | ETA |
|---|---|
| Power Outage | 4 hours |
| Water Leak | 24 hours |
| Blocked Drain | 48 hours |
| Broken Streetlight | 72 hours |
| Illegal Dumping | 72 hours |
| Pothole | 120 hours (5 days) |
| Other | 168 hours (7 days) |

These are prototype estimates, not confirmed municipal commitments.

### Email notifications

| Trigger | Recipient | Content |
|---|---|---|
| Report submitted | Reporter | Reference code, location, team assignment, ETA |
| Status/priority/assignment changed | Reporter | New status, updated assignment, ETA |
| Marked Resolved | Reporter | Resolution notes, link to confirm or reopen |
| Ticket Closed | Reporter | Final summary with resolution notes |

Email failures never block the resident's request flow — the `EmailDeliveryStatus` is recorded on the request and surfaced to the admin.

---

## Running Tests

```powershell
dotnet test Municipal.sln
```

Tests use a real SQL Server database with a fresh GUID-suffixed name per test run, then delete it on teardown. All 9 tests must pass before merging.

**Test coverage:**

| Test class | What it covers |
|---|---|
| `WorkflowTests` | End-to-end HTTP flow: submit → admin login → status advancement → public tracking; validation, anti-forgery, concurrency, pagination, demo seeding |
| `RoutingTests` | Municipality directory (257 entries, 9 provinces), assignment service, email content and SMTP capture |
| `CommunityFeatureTests` | Photo validation (type detection, size limits), dashboard pagination, support deduplication, API data sanitisation, photo access control, repair evidence and review workflow |
| `MunicipalityLocationTests` | Reverse geocoding: boundary/address API independence, fallback logic, street name validation, cache separation, foreign location rejection |

---

## Command-Line Utilities

Run these from the `Municipal.Web` project directory (`dotnet run --project Municipal.Web -- <flag>`):

| Flag | Description |
|---|---|
| `--upgrade-schema` | Applies all pending EF Core migrations (equivalent to `dotnet ef database update`) |
| `--setup` | Creates the Administrator role and prompts for the first admin user |
| `--seed-demo` | Inserts seven labelled fictional requests (one per category) — safe to run multiple times |
| `--check-database` | Verifies the database connection and schema |
| `--verify-database` | Verifies connection, schema, and admin login |
| `--verify-storage` | Verifies connection, schema, and photo storage |
| `--verify-features` | Verifies connection, schema, and community features |

---

## Project Structure

```
Municipal.Web/
├── Controllers/
│   ├── AccountController.cs     # Login / logout
│   ├── AdminController.cs       # Admin dashboard and request management
│   └── RequestsController.cs    # Public report, track, nearby, review
│
├── Models/
│   ├── ProblemRequest.cs        # Core domain model and enums
│   ├── FormModels.cs            # Request/response view models and form bindings
│   ├── LoginViewModel.cs        # Admin login view model
│   ├── ErrorViewModel.cs        # Error page view model
│   └── CoordinateModelBinder.cs # Custom model binder for lat/lon form fields
│
├── Views/
│   ├── Account/Login.cshtml
│   ├── Admin/
│   │   ├── Index.cshtml         # Dashboard
│   │   ├── Edit.cshtml          # Edit request
│   │   └── CreateUser.cshtml
│   ├── Requests/
│   │   ├── Report.cshtml        # Submit form
│   │   ├── Confirmation.cshtml  # Post-submission confirmation
│   │   ├── Track.cshtml         # Status tracking
│   │   ├── Nearby.cshtml        # Community nearby view
│   │   └── Error.cshtml
│   └── Shared/                  # Layout and partials
│
├── Services/
│   ├── RequestService.cs            # Core business logic: submit, track, update
│   ├── CommunityService.cs          # Nearby, support, review, metrics
│   ├── AssignmentService.cs         # Role/person assignment, ETA calculation
│   ├── EmailService.cs              # HTML email templates and SMTP delivery
│   ├── PhotoUploadService.cs        # Image validation and preparation
│   ├── MunicipalityDirectory.cs     # In-memory municipality lookup
│   ├── MunicipalityLocationService.cs # Geocoding and reverse geocoding
│   ├── SetupUtility.cs              # First-run setup helpers
│   └── DatabaseVerification.cs      # Health check utilities
│
├── Data/
│   ├── ApplicationDbContext.cs      # EF Core context with model configuration
│   ├── municipalities.json          # 257 South African municipalities
│   └── Migrations/                  # EF Core migration history
│
├── appsettings.json                 # Default configuration
├── appsettings.Development.json     # Development overrides
└── Program.cs                       # App startup and DI registration

Municipal.Tests/
├── WorkflowTests.cs             # Integration tests: full request workflow
├── RoutingTests.cs              # Assignment routing and email content
├── CommunityFeatureTests.cs     # Photos, support, nearby, review
└── MunicipalityLocationTests.cs # Geocoding service tests

docs/
├── database-diagram.md          # ER diagram (Mermaid)
├── verification.md              # Setup and verification record
├── tracking-desktop.png         # Screenshot — desktop layout
└── tracking-mobile.png          # Screenshot — mobile layout
```

---

## Security Notes

- Only users in the **Administrator** role can log in; there is no self-registration endpoint.
- Login is protected against user enumeration (generic error messages), brute force (5-attempt lockout, 15-minute duration), and requires a confirmed email.
- All forms are protected by anti-forgery tokens (enforced globally).
- Authentication cookies are HttpOnly, Secure, SameSite=Lax, with a 30-minute sliding expiration.
- Passwords require a minimum of 12 characters.
- Photo content is validated by magic bytes (not file extension) to prevent disguised file uploads.
- Public endpoints never expose resident email addresses, reference codes, or precise coordinates.
- Optimistic concurrency (RowVersion) prevents lost updates when two admins edit the same request simultaneously.
- SQL injection is prevented by parameterised EF Core queries throughout.


## Browser verification scripts

Install Node.js 20 or later and Google Chrome, then run `npm install` from the project folder to install the browser testing dependency. Start the application before running a verification script, for example `node verify-icons.cjs`. Set `REPORTSA_URL` to the application address for scripts that support it; check the selected script for its default URL.
