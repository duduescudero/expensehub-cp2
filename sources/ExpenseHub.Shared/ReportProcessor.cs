using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Reports;

internal sealed class ReportProcessor
{
    private readonly IReportStore _store;
    private readonly TimeProvider _clock;
    public ReportProcessor(IReportStore store, TimeProvider clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task<string> ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        ReportRecord? report = await _store.FindAsync(id, cancellationToken);
        if (report is null)
        {
            return "missing";
        }

        if (report.Status is "Completed" or "Failed")
        {
            return "duplicate";
        }

        try
        {
            ReportRules.Validate(report.FromDate, report.ToDate, report.Scope);
            report.Status = "Processing";
            report.StartedAt ??= _clock.GetUtcNow();
            await _store.SaveAsync(report, cancellationToken);
            IReadOnlyList<Expense> expenses = await _store.ExpensesAsync(report, cancellationToken);
            ReportResult result = ReportRules.Calculate(report, expenses, _clock.GetUtcNow());
            report.ResultJson = JsonSerializer.Serialize(result);
            report.Status = "Completed";
            report.CompletedAt = _clock.GetUtcNow();
            report.ErrorCode = null;
            report.ErrorMessage = null;
            await _store.SaveAsync(report, cancellationToken);
            return "completed";
        }
        catch (ReportException)
        {
            report.Status = "Failed";
            report.CompletedAt = _clock.GetUtcNow();
            report.ResultJson = null;
            report.ErrorCode = "REPORT_INVALID";
            report.ErrorMessage = "Não foi possível processar os parâmetros persistidos do relatório.";
            await _store.SaveAsync(report, cancellationToken);
            return "failed";
        }
    }
}
