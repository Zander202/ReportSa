# Architecture

This document covers the internal design of the Municipal Problem Reporting & Tracking System in detail — layers, data flow, key design decisions, and the reasoning behind them.

---

## Contents

- [Layers](#layers)
- [Request Lifecycle](#request-lifecycle)
- [Service Responsibilities](#service-responsibilities)
- [Data Access & Concurrency](#data-access--concurrency)
- [Authentication & Authorisation](#authentication--authorisation)
- [Email Pipeline](#email-pipeline)
- [Photo Handling](#photo-handling)
- [Location & Geocoding](#location--geocoding)
- [Municipality Directory](#municipality-directory)
- [Community Features](#community-features)
- [Configuration](#configuration)
- [Test Architecture](#test-architecture)

---

## Layers

```
Browser / HTTP Client
        │
        ▼
┌─────────────────────────────────────────┐
│              Controllers                │  HTTP layer — routing, model binding,
│  RequestsController  AdminController    │  validation, redirects
│  AccountController                      │
└──────────────────┬──────────────────────┘
                   │
        ┌──────────▼──────────┐
        │      Services       │  Business logic — rules enforcement,
        │  RequestService     │  cross-cutting concerns
        │  CommunityService   │
        │  AssignmentService  │
        │  EmailService       │
        │  PhotoUploadService │
        └──────────┬──────────┘
                   │
        ┌──────────▼──────────┐
        │   Data / EF Core    │  Persistence — ApplicationDbContext,
        │  ApplicationDb-     │  Entity Framework Core queries,
        │  Context            │  migrations
        └──────────┬──────────┘
                   │
        ┌──────────▼──────────┐
        │      Database       │  PostgreSQL or SQL Server
        └─────────────────────┘
```

External dependencies (geocoding APIs, SMTP) are accessed from services, never directly from controllers.

---

## Request Lifecycle

### Submission

1. Resident fills the `Report` form — municipality, category, location (typed or GPS), optional description, email, optional photos.
2. `RequestsController.Report` (POST) validates the model state and calls:
   - `MunicipalityDirectory.Find` to verify the municipality code.
   - `RequestService.SubmitAsync` to persist the record.
3. `RequestService.SubmitAsync`:
   - Validates all fields defensively (server-side, regardless of model binding).
   - Calls `AssignmentService.For(category)` to determine the team and person.
   - Calls `AssignmentService.Estimate(category, now)` for the provisional ETA.
   - Calls `PhotoUploadService.PrepareAsync` to validate and prepare uploaded photos.
   - Generates a cryptographically random reference code (`MUN-` + 24 hex chars).
   - Saves the `ProblemRequest` with an initial `StatusHistory` entry.
   - On PostgreSQL unique-key collision, clears the change tracker and retries up to five times.
   - Dispatches a confirmation email via `EmailService.SendRequestReceivedAsync`.
4. Controller stores the reference code in TempData and redirects to `Confirmation`.

### Admin Update

1. Administrator opens the request edit view; the current `RowVersion` is embedded in the form as a hidden Base64 field.
2. `AdminController.Update` calls `RequestService.UpdateAsync(form)`.
3. `RequestService.UpdateAsync`:
   - Validates the status transition (only one step forward, or jump to Closed).
   - Validates resolution photo count (max 3 total, only when marking Resolved).
   - Decodes the Base64 RowVersion and sets it as the EF concurrency token original value.
   - Builds and appends a `StatusHistory` entry with an automatic or user-supplied note.
   - Updates `ResolvedAt` and `ResidentReview` when transitioning to Resolved.
   - On PostgreSQL, sets a new random `RowVersion` before saving (SQL Server uses a server-generated rowversion column).
   - Dispatches the appropriate email (update, resolved, or closed).
4. `DbUpdateConcurrencyException` surfaces to the controller, which redirects back to edit with an error message.

### Resident Review

1. Reporter receives a "resolved" email with a link to the track page.
2. On the `Track` view, the reporter can confirm or reject the repair via the `Review` form.
3. `RequestsController.Review` calls `CommunityService.ReviewAsync(form)`.
4. `CommunityService.ReviewAsync`:
   - Verifies the email matches the original report.
   - Checks the request is in Resolved status.
   - Sets `ResidentReview` and transitions status to Closed (confirmed) or InProgress (rejected).
   - Clears `ResolvedAt` on rejection.
   - Appends a `StatusHistory` note recording the outcome.

---

## Service Responsibilities

### `RequestService`

The central orchestrator for request lifecycle operations:

- **`SubmitAsync(ReportForm)`** — validates, assembles, persists, and emails a new request.
- **`TrackAsync(string code)`** — loads a full request with history and photo metadata by reference code. Validates the code format before querying (`^MUN-[0-9A-F]{24}$`).
- **`ReceiptAsync(string code)`** — lightweight load for the post-submission confirmation page (no history/photos needed).
- **`UpdateAsync(UpdateForm)`** — validates and applies admin updates; handles concurrency, sends email.

### `CommunityService`

Handles all community and reporting features beyond the core request workflow:

- **`NearbyAsync`** — returns up to 100 open reports within 5 km of a coordinate or filtered by municipality/category. Uses a bounding-box pre-filter (±0.06° lat, ±0.1° lon) in SQL, then Haversine distance filtering in memory.
- **`SupportAsync`** — registers a supporter. Deduplicates using a unique index on `(ProblemRequestId, EmailKey)` where `EmailKey = SHA256(email.ToUpper())`. Race-condition-safe: catches the unique-key constraint on concurrent inserts.
- **`ReviewAsync`** — processes resident confirmation or rejection.
- **`DashboardAsync`** — aggregates counts (open, overdue, resolved, awaiting review) and average resolution times, both globally and per municipality.

### `AssignmentService`

Stateless calculation service (Singleton):

- **`For(Category)`** — reads `Assignments:{Category}:Role` and `Person` from configuration. Falls back to hardcoded defaults if the section is absent. When `Person` is blank or `"To be assigned"`, picks a random name from `DemoStaff`.
- **`Estimate(Category, DateTimeOffset)`** — returns a provisional completion time by adding a category-specific offset to the submission timestamp.

### `EmailService`

Singleton that holds configuration state:

- Reads `Email` section from `IConfiguration` at construction time.
- Four send methods: `SendRequestReceivedAsync`, `SendStatusUpdateAsync`, `SendResolvedAsync`, `SendClosedAsync`.
- All send methods return `EmailDeliveryStatus` (Disabled / Sent / Failed); failures are logged but never rethrown so as not to block the resident's request.
- Skips sending if `Enabled = false` or the address ends with `.invalid` (test sentinel).
- HTML templates use a South African flag colour gradient header and HTML-encode all user-supplied strings.

### `PhotoUploadService`

Stateless, instantiated per-operation:

- **`PrepareAsync`** — reads each uploaded file in chunks, enforces the 5 MB per-file limit, calls `DetectType`, builds a `RequestPhoto` list.
- **`DetectType`** — inspects raw bytes: JPEG (SOI/EOI markers), PNG (8-byte signature + IHDR), WebP (RIFF/WEBP). Returns `null` for unrecognised content, causing the operation to fail with a user-facing error.

### `MunicipalityLocationService`

Makes HTTP calls to external geocoding APIs (not documented publicly; accessed via `HttpClient` with a 6-second timeout). Injected as a singleton via `AddHttpClient`.

- **`SearchAsync(query)`** — forward geocoding; filters results to South Africa.
- **`ResolveAsync(lat, lon, token, includeStreet)`** — reverse geocoding; returns `(Municipality, Address, Area)`. When `includeStreet` is false (GPS accuracy > 100 m), the address component is omitted. Failures are handled gracefully — controllers return HTTP 503 with a user-friendly fallback message.

---

## Data Access & Concurrency

`ApplicationDbContext` inherits from `IdentityDbContext<IdentityUser>` and adds four DbSets:

| DbSet | Entity |
|---|---|
| `Requests` | `ProblemRequest` |
| `StatusHistory` | `StatusHistory` |
| `RequestPhotos` | `RequestPhoto` |
| `RequestSupporters` | `RequestSupporter` |

### Model configuration highlights

- `ReferenceCode` has a unique index.
- `RequestSupporter` has a unique composite index on `(ProblemRequestId, EmailKey)`.
- `(MunicipalityCode, Status, ReportedAt)` composite index supports the admin dashboard filter queries.
- Check constraints on `Category` (0–6), `Priority` (0–3), and `Status` (0–4) prevent out-of-range enum values at the database level.
- All child entities cascade-delete from `ProblemRequest`.

### Concurrency

Optimistic concurrency is used to prevent lost updates when two administrators edit the same request simultaneously.

- **SQL Server**: Uses a native `rowversion` column (automatically incremented by the database).
- **PostgreSQL**: Uses a manually managed `bytea` column (random 16 bytes generated in application code before each save, set as `IsConcurrencyToken().ValueGeneratedNever()`).

The current `RowVersion` bytes are Base64-encoded and embedded in the admin edit form. On save, EF Core compares the submitted value against the database; a mismatch raises `DbUpdateConcurrencyException`, which is surfaced to the admin as an error message prompting a reload.

---

## Authentication & Authorisation

### Identity configuration

```
Password minimum length  : 12 characters
Unique email             : required
Lockout max attempts     : 5
Lockout duration         : 15 minutes
Cookie HttpOnly          : true
Cookie Secure            : Always
Cookie SameSite          : Lax
Session timeout          : 30 minutes (sliding)
```

### Login security

`AccountController.Login` explicitly:

1. Looks up the user by email.
2. Checks that the user is in the **Administrator** role.
3. Checks that the email is confirmed (`EmailConfirmed = true`).
4. Calls `CheckPasswordSignInAsync` with `lockoutOnFailure: true`.

Steps 1–3 return the same generic error message to prevent user enumeration.

### Route protection

- `AdminController` carries `[Authorize(Roles = "Administrator")]` at the class level — every action is protected.
- `RequestsController` is fully public (no `[Authorize]`), with photo access controlled at query level (join to `ReferenceCode` for non-admins).
- Anti-forgery is enforced globally by `AutoValidateAntiforgeryTokenAttribute` registered in `Program.cs`.

---

## Email Pipeline

```
Service method (e.g. UpdateAsync)
        │
        ▼
EmailService.SendStatusUpdateAsync(request)
        │
        ├─ Enabled? ──No──▶ return Disabled
        │
        ├─ Email ends with .invalid? ──Yes──▶ return Disabled (test sentinel)
        │
        ▼
SmtpClient.SendMailAsync (20-second timeout)
        │
        ├─ Success ──▶ return Sent
        │
        └─ Exception ──▶ log warning, return Failed
```

`EmailDeliveryStatus.Failed` is recorded on the `ConfirmationEmailStatus` field of the request and shown in the admin UI, but the resident's request is saved regardless.

---

## Photo Handling

Photos are stored as raw bytes in the database (`varbinary(max)` / `bytea`). This avoids the complexity of external storage for a prototype but should be moved to blob storage for production.

### Access control

`RequestsController.Photo` (GET `/Requests/Photo/{id}?referenceCode={code}`):

- If the caller is in the Administrator role: loads by `id` only.
- Otherwise: loads by `id` AND `Request.ReferenceCode == referenceCode` — prevents enumeration of photo IDs.
- Response headers: `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`.

### Upload limits

| Constraint | Value |
|---|---|
| Photos per submission | Max 3 |
| Photos per resolution | Max 3 (shared pool with any existing resolution photos) |
| Max size per photo | 5 MB |
| Request body size limit | 16 MB (`[RequestSizeLimit(16 * 1024 * 1024)]`) |
| Accepted formats | JPEG, PNG, WebP (validated by magic bytes) |

---

## Location & Geocoding

### Address search (forward geocoding)

`GET /Requests/SearchAddresses?query=...`

- Minimum 3 characters, maximum 200.
- Calls `MunicipalityLocationService.SearchAsync` and returns an array of address suggestions as JSON.
- On HTTP / JSON / cancellation errors, returns HTTP 503 with a fallback message so the resident can still type manually.

### Municipality resolution (reverse geocoding)

`GET /Requests/LocateMunicipality?latitude=...&longitude=...&accuracy=...`

- Validates coordinate ranges (`lat` −90…90, `lon` −180…180, finite).
- When GPS accuracy ≤ 100 m, requests street-level address from the API.
- Returns: `{ code, label, address, area, message? }`.
- If neither municipality nor address is found, returns HTTP 404.
- On API errors, returns HTTP 503 with a message prompting manual selection.

---

## Municipality Directory

`MunicipalityDirectory` is a Singleton that loads `Data/municipalities.json` once at startup. The JSON file contains 257 South African municipalities with:

| Field | Description |
|---|---|
| `Code` | Short unique code, e.g. `CPT`, `JHB`, `ETH` |
| `Name` | Display name, e.g. `City of Cape Town` |
| `Province` | Province name |
| `Type` | `A` = Metropolitan, `B` = Local, `C` = District |

The `Label` computed property appends the municipality type, e.g. `City of Cape Town Metropolitan Municipality`.

`Find(code)` returns `null` for unknown codes; controllers treat this as a validation failure.

---

## Community Features

### Nearby reports

The `Nearby` view and its backing `NearbyData` JSON endpoint show open reports within a user's vicinity.

Data flow:
1. Browser calls `NearbyData` with optional `municipalityCode`, `category`, `latitude`, `longitude`.
2. `CommunityService.NearbyAsync` queries the database with a bounding-box predicate, loads up to 300 candidates.
3. In-memory Haversine filtering retains results ≤ 5 km; results are sorted by distance then recency.
4. Only up to 100 records are returned.
5. The JSON response includes `id`, `category`, `status`, `municipality`, `affected`, rounded coordinates, `distanceKm`, and `reportedAt` — never email addresses, reference codes, or precise coordinates.

### Affected/support count

The `Affected` count shown on `NearbyReport` is `1 + Supporters.Count` — the reporter themselves plus additional supporters.

Deduplication:
- `EmailKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.ToUpperInvariant())))`
- A unique index on `(ProblemRequestId, EmailKey)` enforces one entry per email.
- The reporter's own email is checked before insertion and silently skipped.

---

## Configuration

`Program.cs` builds configuration from these sources (in increasing precedence):

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User secrets (keyed by `UserSecretsId = "municipal-spm622-prototype-2026"`)
4. Environment variables
5. Command-line arguments

### DI registrations

```csharp
// Singleton (shared across requests)
services.AddSingleton<AssignmentService>();
services.AddSingleton<MunicipalityDirectory>();
services.AddSingleton<EmailService>();
services.AddMemoryCache();
services.AddHttpClient<MunicipalityLocationService>(...);

// Scoped (one per HTTP request)
services.AddScoped<RequestService>();
services.AddScoped<PhotoUploadService>();
services.AddScoped<CommunityService>();
```

---

## Test Architecture

Tests live in `Municipal.Tests` and use the ASP.NET Core test host (`WebApplicationFactory<Program>`) against a real SQL Server database. A GUID-suffixed database name is generated per test class and dropped on teardown.

### Test helpers

- A custom `WebApplicationFactory` override replaces the connection string and disables email (sets `Email:Enabled=false`).
- Anti-forgery is bypassed in tests by setting a header that the test server recognises.
- SMTP capture: `RoutingTests` starts a local SMTP listener on a random port to intercept and inspect outgoing emails without a real mail server.

### Database isolation

Each test class creates its own database and disposes it after all tests in that class complete. Tests within a class share the same database and rely on predictable seeding or explicit record creation to avoid state conflicts.

### Key test scenarios covered

- Happy path: full HTTP flow from submission through all workflow stages
- Reference code format validation and uniqueness (collision retry)
- Status transition enforcement (forward-only, backward rejection)
- RowVersion concurrency conflict detection
- Anti-forgery token rejection for unauthenticated POSTs
- Administrator role enforcement (anonymous requests return 302/401)
- Photo content validation (JPEG/PNG/WebP pass; disguised files rejected)
- Support deduplication (same email cannot support twice; reporter email skipped)
- Dashboard metrics accuracy (open, overdue, resolved counts)
- Public API sanitisation (no private fields in JSON output)
- Municipality directory completeness (257 entries across 9 provinces)
