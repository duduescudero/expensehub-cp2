using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ExpenseHub.Api.Domain;
using ExpenseHub.Reports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>Comportamento de relatórios sem serviços externos.</summary>
[TestClass]
public sealed class ReportBehaviorTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Datas inclusivas, escopo e precisão decimal.</summary>
    [TestMethod]
    public void CalculatesKnownTotalsAndDateBoundaries()
    {
        ReportRecord report = Report();
        Expense[] expenses = { Expense(100m, ExpenseState.Approved), Expense(50m, ExpenseState.Approved), Expense(25m, ExpenseState.Paid), Expense(200m, ExpenseState.Approved, "other"), Expense(500m, ExpenseState.Paid, date: new DateOnly(2026, 10, 1)) };
        ReportResult mine = ReportRules.Calculate(report, expenses, _now);
        Assert.AreEqual(3, mine.TotalCount);
        Assert.AreEqual(175m, mine.TotalAmount);
        Assert.HasCount(5, mine.ByStatus);
        Assert.AreEqual(new StatusTotal("Approved", 2, 150m), mine.ByStatus[2]);
        report.Scope = "Global";
        Assert.AreEqual(375m, ReportRules.Calculate(report, expenses, _now).TotalAmount);
        Assert.AreEqual(0.30m, ReportRules.Calculate(Report(), new[] { Expense(0.10m, ExpenseState.Draft), Expense(0.20m, ExpenseState.Draft, date: new DateOnly(2026, 9, 30)) }, _now).TotalAmount);
    }

    /// <summary>Vazio inclui os cinco estados com zero.</summary>
    [TestMethod]
    public void EmptyReturnsAllZeroStates()
    {
        ReportResult result = ReportRules.Calculate(Report(), Array.Empty<Expense>(), _now);
        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(0m, result.TotalAmount);
        Assert.HasCount(5, result.ByStatus);
        foreach (StatusTotal item in result.ByStatus)
        {
            Assert.AreEqual(0, item.Count);
            Assert.AreEqual(0m, item.Amount);
        }
    }

    /// <summary>Entrada inválida não prossegue.</summary>
    [TestMethod]
    public void InvalidPeriodAndScopeAreRejected()
    {
        Assert.AreEqual(400, Assert.Throws<ReportException>(() => ReportRules.Validate(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1), "Mine")).StatusCode);
        Assert.Throws<ReportException>(() => ReportRules.Validate(default, default, "Mine"));
        Assert.Throws<ReportException>(() => ReportRules.Validate(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Other"));
    }

    /// <summary>Reinício do processador não elimina a deduplicação persistida.</summary>
    [TestMethod]
    public async Task DuplicateAndChangedExpensesDoNotChangeSavedResult()
    {
        var store = new FakeStore { Items = new[] { Expense(100m, ExpenseState.Approved) } };
        await new ReportProcessor(store, TimeProvider.System).ProcessAsync(store.Record.Id, default);
        string? saved = store.Record.ResultJson;
        DateTimeOffset? completed = store.Record.CompletedAt;
        store.Items = new[] { Expense(900m, ExpenseState.Paid) };
        Assert.AreEqual("duplicate", await new ReportProcessor(store, TimeProvider.System).ProcessAsync(store.Record.Id, default));
        Assert.AreEqual(saved, store.Record.ResultJson);
        Assert.AreEqual(completed, store.Record.CompletedAt);
        Assert.AreEqual(1, store.Reads);
        Assert.AreEqual(2, store.Saves);
    }

    /// <summary>Processing pode ser retomado e falha de persistência propaga sem sucesso falso.</summary>
    [TestMethod]
    public async Task ProcessingIsResumableAndSaveFailurePropagates()
    {
        var store = new FakeStore();
        store.Record.Status = "Processing";
        Assert.AreEqual("completed", await new ReportProcessor(store, TimeProvider.System).ProcessAsync(store.Record.Id, default));
        var failing = new FakeStore { FailSave = true };
        await Assert.ThrowsAsync<IOException>(() => new ReportProcessor(failing, TimeProvider.System).ProcessAsync(failing.Record.Id, default));
        Assert.AreEqual(0, failing.Reads);
    }

    /// <summary>Falha definitiva é persistida e não reprocessada.</summary>
    [TestMethod]
    public async Task InvalidStoredReportFailsOnce()
    {
        var store = new FakeStore();
        store.Record.Scope = "invalid";
        Assert.AreEqual("failed", await new ReportProcessor(store, TimeProvider.System).ProcessAsync(store.Record.Id, default));
        Assert.AreEqual("REPORT_INVALID", store.Record.ErrorCode);
        Assert.IsNull(store.Record.ResultJson);
        Assert.AreEqual("duplicate", await new ReportProcessor(store, TimeProvider.System).ProcessAsync(store.Record.Id, default));
        Assert.AreEqual(1, store.Saves);
    }

    /// <summary>Planilha é legível, tem valores numéricos e corresponde ao resultado salvo.</summary>
    [TestMethod]
    public void ExcelContainsPersistedNumbersAndMetadata()
    {
        ReportRecord report = Report();
        report.Status = "Completed";
        ReportResult result = ReportRules.Calculate(report, new[] { Expense(125.50m, ExpenseState.Paid) }, _now);
        report.ResultJson = JsonSerializer.Serialize(result);
        using var stream = new MemoryStream(ReportRules.Excel(report, JsonSerializer.Deserialize<ReportResult>(report.ResultJson)));
        using var book = new XLWorkbook(stream);
        IXLWorksheet sheet = book.Worksheet(1);
        Assert.AreEqual(report.Id.ToString(), sheet.Cell("B2").GetString());
        Assert.AreEqual(1, sheet.Cell("B14").GetValue<int>());
        Assert.AreEqual(125.50m, sheet.Cell("C14").GetValue<decimal>());
        Assert.AreEqual(XLDataType.Number, sheet.Cell("C9").DataType);
        Assert.AreEqual(0m, sheet.Cell("C9").GetValue<decimal>());
    }

    /// <summary>Planilha não é exposta em estados não concluídos.</summary>
    [TestMethod]
    [DataRow("Pending")]
    [DataRow("Processing")]
    [DataRow("Failed")]
    public void UnfinishedExcelReturnsConflict(string status)
    {
        ReportRecord report = Report();
        report.Status = status;
        Assert.AreEqual(409, Assert.Throws<ReportException>(() => ReportRules.Excel(report, null)).StatusCode);
    }

    private static ReportRecord Report() => new() { OwnerId = "owner", FromDate = new DateOnly(2026, 9, 1), ToDate = new DateOnly(2026, 9, 30), CreatedAt = _now };
    private static Expense Expense(decimal amount, ExpenseState state, string owner = "owner", DateOnly? date = null) => new() { OwnerId = owner, Amount = amount, State = state, ExpenseDate = date ?? new DateOnly(2026, 9, 1) };

    private sealed class FakeStore : IReportStore
    {
        public ReportRecord Record { get; } = Report();
        public IReadOnlyList<Expense> Items { get; set; } = Array.Empty<Expense>();
        public int Saves { get; private set; }
        public int Reads { get; private set; }
        public bool FailSave { get; set; }
        public Task<ReportRecord?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ReportRecord?>(id == Record.Id ? Record : null);
        public Task SaveAsync(ReportRecord report, CancellationToken cancellationToken)
        {
            if (FailSave)
            {
                throw new IOException("indisponível");
            }

            Saves++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Expense>> ExpensesAsync(ReportRecord report, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Items);
        }
    }
}
