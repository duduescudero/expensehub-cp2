using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Reports;

internal sealed class ReportStore : IReportStore
{
    private readonly ExpenseDbContext _database;
    public ReportStore(ExpenseDbContext database) => _database = database;
    public Task<ReportRecord?> FindAsync(Guid id, CancellationToken cancellationToken) => _database.Set<ReportRecord>().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    public async Task SaveAsync(ReportRecord report, CancellationToken cancellationToken) => await _database.SaveChangesAsync(cancellationToken);
    public async Task<IReadOnlyList<Expense>> ExpensesAsync(ReportRecord report, CancellationToken cancellationToken) => await _database.Expenses.AsNoTracking().Where(e => e.ExpenseDate >= report.FromDate && e.ExpenseDate <= report.ToDate && (report.Scope == "Global" || e.OwnerId == report.OwnerId)).ToListAsync(cancellationToken);

    public static async Task UpgradeAsync(ExpenseDbContext database, CancellationToken cancellationToken = default)
    {
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Reports (
                Id TEXT NOT NULL CONSTRAINT PK_Reports PRIMARY KEY,
                OwnerId TEXT NOT NULL, Scope TEXT NOT NULL,
                FromDate TEXT NOT NULL, ToDate TEXT NOT NULL,
                Status TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                StartedAt TEXT NULL, CompletedAt TEXT NULL,
                ResultJson TEXT NULL, ErrorCode TEXT NULL, ErrorMessage TEXT NULL);
            CREATE INDEX IF NOT EXISTS IX_Reports_OwnerId ON Reports (OwnerId);
            """, cancellationToken);
    }
}
