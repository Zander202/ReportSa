using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Models;
namespace Municipal.Web.Data;
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<ProblemRequest> Requests => Set<ProblemRequest>();
    public DbSet<StatusHistory> StatusHistory => Set<StatusHistory>();
    public DbSet<RequestPhoto> RequestPhotos => Set<RequestPhoto>();
    public DbSet<RequestSupporter> RequestSupporters => Set<RequestSupporter>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        var requests = b.Entity<ProblemRequest>();
        requests.HasIndex(x => x.ReferenceCode).IsUnique();
        requests.HasMany(x => x.History).WithOne(x => x.Request).HasForeignKey(x => x.ProblemRequestId).OnDelete(DeleteBehavior.Cascade);
        requests.HasMany(x => x.Photos).WithOne(x => x.Request).HasForeignKey(x => x.ProblemRequestId).OnDelete(DeleteBehavior.Cascade);
        requests.HasMany(x => x.Supporters).WithOne(x => x.Request).HasForeignKey(x => x.ProblemRequestId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RequestSupporter>().HasIndex(x => new { x.ProblemRequestId, x.EmailKey }).IsUnique();
        requests.HasIndex(x => new { x.MunicipalityCode, x.Status, x.ReportedAt });
        if (Database.IsNpgsql())
            requests.Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedNever().HasColumnType("bytea");
        else
            requests.Property(x => x.RowVersion).IsRowVersion();
        var quoteLeft = Database.IsNpgsql() ? "\"" : "[";
        var quoteRight = Database.IsNpgsql() ? "\"" : "]";
        string Col(string name) => $"{quoteLeft}{name}{quoteRight}";
        b.Entity<ProblemRequest>().ToTable(t => { t.HasCheckConstraint("CK_Request_Category", $"{Col("Category")} BETWEEN 0 AND 6"); t.HasCheckConstraint("CK_Request_Priority", $"{Col("Priority")} BETWEEN 0 AND 3"); t.HasCheckConstraint("CK_Request_Status", $"{Col("Status")} BETWEEN 0 AND 4"); });
        b.Entity<StatusHistory>().ToTable(t => t.HasCheckConstraint("CK_History_Status", $"{Col("Status")} BETWEEN 0 AND 4"));
    }
}
