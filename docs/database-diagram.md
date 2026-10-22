# Database Diagram

The schema below reflects the current model after all four migrations.

```mermaid
erDiagram
    Requests ||--|{ StatusHistory : "records stages"
    Requests ||--o{ RequestPhotos : "has photos"
    Requests ||--o{ RequestSupporters : "has supporters"
    AspNetUsers ||--o{ AspNetUserRoles : "has roles"
    AspNetRoles ||--o{ AspNetUserRoles : "assigned to users"

    Requests {
        int Id PK
        nvarchar ReferenceCode UK
        int Category
        nvarchar Location
        nvarchar Description "nullable"
        nvarchar ResidentPhoneNumber
        nvarchar ResidentEmail
        nvarchar AssignedRole
        nvarchar AssignedPerson
        nvarchar MunicipalityCode
        nvarchar MunicipalityName
        float Latitude "nullable"
        float Longitude "nullable"
        datetimeoffset ReportedAt
        datetimeoffset LastUpdatedAt
        datetimeoffset EstimatedCompletionAt "nullable"
        datetimeoffset ResolvedAt "nullable"
        int Priority
        int Status
        int ConfirmationEmailStatus
        int ResidentReview
        nvarchar ResolutionNotes "nullable"
        rowversion RowVersion
    }

    StatusHistory {
        int Id PK
        int ProblemRequestId FK
        int Status
        datetimeoffset ChangedAt
        nvarchar Note "nullable"
    }

    RequestPhotos {
        int Id PK
        int ProblemRequestId FK
        nvarchar FileName
        nvarchar ContentType
        varbinary Content
        bit IsResolution
        datetimeoffset UploadedAt
    }

    RequestSupporters {
        int Id PK
        int ProblemRequestId FK
        nvarchar Email
        nvarchar EmailKey UK
        datetimeoffset JoinedAt
    }

    AspNetUsers {
        nvarchar Id PK
        nvarchar Email
        nvarchar PasswordHash
        bit EmailConfirmed
    }

    AspNetRoles {
        nvarchar Id PK
        nvarchar Name
    }

    AspNetUserRoles {
        nvarchar UserId PK,FK
        nvarchar RoleId PK,FK
    }
```

## Notes

- `Requests.ReferenceCode` has a unique index (format: `MUN-{24 hex chars}`).
- `RequestSupporters` has a unique composite index on `(ProblemRequestId, EmailKey)` where `EmailKey` is the SHA-256 hash of the uppercased email — prevents duplicate support entries per report.
- A composite index on `(MunicipalityCode, Status, ReportedAt)` supports the admin dashboard filter queries.
- Check constraints enforce valid enum ranges: `Category` (0–6), `Priority` (0–3), `Status` (0–4).
- All child tables (`StatusHistory`, `RequestPhotos`, `RequestSupporters`) cascade-delete when the parent `Request` is deleted.
- `RowVersion` is a native SQL Server `rowversion` column; on PostgreSQL it is a `bytea` managed manually in application code.
- ASP.NET Identity also creates standard tables for claims, logins, tokens, and role-claims. EF Core tracks applied migrations in `__EFMigrationsHistory`.

## Migration history

| Migration | Date | Changes |
|---|---|---|
| `InitialCreate` | 2026-10-07 | Base `Requests` and `StatusHistory` tables, ASP.NET Identity tables |
| `UpdateMunicipalModel` | 2026-10-08 | Added `ResidentEmail` to `Requests` |
| `RequestRouting` | 2026-10-08 | Added `MunicipalityCode`, `MunicipalityName`, `Latitude`, `Longitude`, `EstimatedCompletionAt`, `ConfirmationEmailStatus` |
| `CommunityFeatures` | 2026-10-12 | Added `RequestPhotos` table, `RequestSupporters` table, `ResolvedAt`, `ResidentReview`, `ResolutionNotes` on `Requests`, `Note` on `StatusHistory`, composite index on `(MunicipalityCode, Status, ReportedAt)` |
