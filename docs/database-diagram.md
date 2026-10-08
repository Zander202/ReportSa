# Database diagram

```mermaid
erDiagram
    Requests ||--|{ StatusHistory : "records stages"
    AspNetUsers ||--o{ AspNetUserRoles : "has roles"
    AspNetRoles ||--o{ AspNetUserRoles : "assigned to users"
    Requests {
        int Id PK
        nvarchar ReferenceCode UK
        int Category
        nvarchar Location
        nvarchar Description "nullable"
        nvarchar ResidentPhoneNumber
        nvarchar AssignedRole
        nvarchar AssignedPerson
        datetimeoffset ReportedAt
        datetimeoffset LastUpdatedAt
        int Priority
        int Status
        rowversion RowVersion
    }
    StatusHistory {
        int Id PK
        int ProblemRequestId FK
        int Status
        datetimeoffset ChangedAt
    }
    AspNetUsers {
        nvarchar Id PK
        nvarchar Email
        nvarchar PasswordHash
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

Each request begins with a Submitted history entry, followed by one entry per forward transition through Assigned, In Progress, Resolved, and Closed. `Requests.Id (1) -> StatusHistory.ProblemRequestId (many)` is a required relationship. Identity also creates standard claim, login, token, and role-claim tables. EF tracks applied migrations in `__EFMigrationsHistory`. Requests store the resident SMS number and the assigned municipal role/person needed for notifications, but residents still do not need accounts.
