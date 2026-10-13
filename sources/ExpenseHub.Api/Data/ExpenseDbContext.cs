using ExpenseHub.Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Data;

internal sealed class ExpenseDbContext : IdentityDbContext<AppUser>
{
    public ExpenseDbContext(DbContextOptions<ExpenseDbContext> options) : base(options)
    {
    }

    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseCategory> Categories => Set<ExpenseCategory>();
    public DbSet<ExpenseHistory> Histories => Set<ExpenseHistory>();
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Expense>().Property(e => e.Id).ValueGeneratedNever();
        builder.Entity<ExpenseHistory>().Property(e => e.Id).ValueGeneratedNever();
        builder.Entity<PaymentRecord>().Property(e => e.Id).ValueGeneratedNever();
        builder.Entity<Expense>().Property(e => e.Description).HasMaxLength(500);
        builder.Entity<Expense>().Property(e => e.Amount).HasPrecision(18, 2);
        builder.Entity<Expense>().Property(e => e.Revision).IsConcurrencyToken();
        builder.Entity<Expense>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Expense>().HasOne<ExpenseCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Expense>().HasMany(e => e.History).WithOne().HasForeignKey(e => e.ExpenseId);
        builder.Entity<Expense>().HasOne(e => e.Payment).WithOne().HasForeignKey<PaymentRecord>(e => e.ExpenseId);
        builder.Entity<ExpenseHistory>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PaymentRecord>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PaymentRecord>().Property(e => e.Amount).HasPrecision(18, 2);
        builder.Entity<ExpenseCategory>().HasData(new ExpenseCategory { Id = 1, Name = "Transporte" }, new ExpenseCategory { Id = 2, Name = "Alimentação" }, new ExpenseCategory { Id = 3, Name = "Hospedagem" }, new ExpenseCategory { Id = 4, Name = "Outros" });
    }
}
