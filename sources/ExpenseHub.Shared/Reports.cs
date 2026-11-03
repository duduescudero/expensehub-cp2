using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Reports;

internal sealed class ReportRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = string.Empty;
    public string Scope { get; set; } = "Mine";
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

internal sealed record StatusTotal(string Status, int Count, decimal Amount);
internal sealed record ReportResult(DateTimeOffset GeneratedAt, int TotalCount, decimal TotalAmount, IReadOnlyList<StatusTotal> ByStatus);
internal sealed record ReportMessage(Guid ReportId);

internal sealed class ReportException : Exception
{
    public ReportException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
}

internal interface IReportStore
{
    Task<ReportRecord?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(ReportRecord report, CancellationToken cancellationToken);
    Task<IReadOnlyList<Expense>> ExpensesAsync(ReportRecord report, CancellationToken cancellationToken);
}

internal interface IReportPublisher
{
    Task PublishAsync(Guid id, CancellationToken cancellationToken);
}

internal static class ReportRules
{
    public static void Validate(DateOnly fromDate, DateOnly toDate, string scope)
    {
        if (fromDate == default || toDate == default || fromDate > toDate || (scope != "Mine" && scope != "Global"))
        {
            throw new ReportException(400, "Informe datas válidas, em ordem, e escopo Mine ou Global.");
        }
    }

    public static string Authorize(ClaimsPrincipal user, string scope)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            throw new ReportException(401, "Autenticação necessária.");
        }

        if ((scope == "Mine" && !user.IsInRole("Employee")) || (scope == "Global" && !user.IsInRole("Auditor")))
        {
            throw new ReportException(403, "Perfil sem permissão para este escopo.");
        }

        return user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new ReportException(401, "Identidade inválida.");
    }

    public static void RequireOwner(ReportRecord? report, string owner)
    {
        if (report is null || report.OwnerId != owner)
        {
            throw new ReportException(404, "Relatório não encontrado.");
        }
    }

    public static ReportResult Calculate(ReportRecord report, IEnumerable<Expense> expenses, DateTimeOffset now)
    {
        List<Expense> selected = expenses.Where(e => e.ExpenseDate >= report.FromDate && e.ExpenseDate <= report.ToDate && (report.Scope == "Global" || e.OwnerId == report.OwnerId)).ToList();
        StatusTotal[] totals = Enum.GetValues<ExpenseState>().Select(s => new StatusTotal(s.ToString(), selected.Count(e => e.State == s), selected.Where(e => e.State == s).Sum(e => e.Amount))).ToArray();
        return new ReportResult(now, selected.Count, selected.Sum(e => e.Amount), totals);
    }

    public static byte[] Excel(ReportRecord report, ReportResult? result)
    {
        if (report.Status != "Completed" || result is null)
        {
            throw new ReportException(409, "Relatório ainda não concluído.");
        }

        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.Worksheets.Add("Relatório");
        sheet.Cell("A1").Value = "ExpenseHub — Relatório de reembolsos";
        sheet.Cell("A2").Value = "Identificador";
        sheet.Cell("B2").Value = report.Id.ToString();
        sheet.Cell("A3").Value = "Escopo";
        sheet.Cell("B3").Value = report.Scope;
        sheet.Cell("A4").Value = "Início";
        sheet.Cell("B4").Value = report.FromDate.ToString("yyyy-MM-dd");
        sheet.Cell("A5").Value = "Fim";
        sheet.Cell("B5").Value = report.ToDate.ToString("yyyy-MM-dd");
        sheet.Cell("A6").Value = "Gerado em UTC";
        sheet.Cell("B6").Value = result.GeneratedAt.ToString("O");
        sheet.Cell("A8").Value = "Estado";
        sheet.Cell("B8").Value = "Quantidade";
        sheet.Cell("C8").Value = "Valor (R$)";
        int row = 9;
        foreach (StatusTotal item in result.ByStatus)
        {
            sheet.Cell(row, 1).Value = item.Status;
            sheet.Cell(row, 2).Value = item.Count;
            sheet.Cell(row, 3).Value = (double)item.Amount;
            row++;
        }

        sheet.Cell(row, 1).Value = "Total";
        sheet.Cell(row, 2).Value = result.TotalCount;
        sheet.Cell(row, 3).Value = (double)result.TotalAmount;
        sheet.Range(9, 3, row, 3).Style.NumberFormat.Format = "#,##0.00";
        sheet.Range("A8:C8").Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
