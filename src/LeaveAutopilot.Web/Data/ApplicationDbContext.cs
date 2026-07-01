using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LeaveAutopilot.Web.Data;

/// <summary>
/// EF Core context. Extends the ASP.NET Core Identity schema (users, roles) with the
/// domain tables for leave policies and requests.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();

    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.FullName).IsRequired().HasMaxLength(200);

            // Identity creates a non-unique index on NormalizedEmail by default; enforce
            // uniqueness at the DB level since every employee's email must be unique.
            entity.HasIndex(u => u.NormalizedEmail).IsUnique();

            entity.HasOne(u => u.Manager)
                .WithMany()
                .HasForeignKey(u => u.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LeavePolicy>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.LeaveType)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(p => p.AllocatedDays).HasPrecision(6, 1);

            entity.HasOne(p => p.Employee)
                .WithMany()
                .HasForeignKey(p => p.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            // One policy per employee, leave type and year.
            entity.HasIndex(p => new { p.EmployeeId, p.LeaveType, p.Year }).IsUnique();

            entity.ToTable(tb => tb.HasCheckConstraint(
                "CK_LeavePolicy_AllocatedDays_NonNegative",
                "\"AllocatedDays\" >= 0"));

            // Unpaid leave is uncapped and has no quota — only Annual/Medical policies are valid.
            entity.ToTable(tb => tb.HasCheckConstraint(
                "CK_LeavePolicy_LeaveType_BalanceBacked",
                "\"LeaveType\" IN ('Annual', 'Medical')"));
        });

        builder.Entity<LeaveRequest>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.Property(r => r.LeaveType)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(r => r.State)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(r => r.ChargeableDays).HasPrecision(6, 1);
            entity.Property(r => r.Reason).HasMaxLength(1000);
            entity.Property(r => r.DecisionNote).HasMaxLength(1000);

            entity.HasOne(r => r.Employee)
                .WithMany()
                .HasForeignKey(r => r.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.DecidedByEmployee)
                .WithMany()
                .HasForeignKey(r => r.DecidedByEmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(tb => tb.HasCheckConstraint(
                "CK_LeaveRequest_ChargeableDays_NonNegative",
                "\"ChargeableDays\" >= 0"));

            entity.ToTable(tb => tb.HasCheckConstraint(
                "CK_LeaveRequest_EndDate_NotBeforeStartDate",
                "\"EndDate\" >= \"StartDate\""));
        });
    }
}
