using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Services;
using ExpenseHub.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Controllers;

/// <summary>Entrada validada para solicitação de relatório.</summary>
public sealed class ReportRequest
{
    /// <summary>Primeiro dia, inclusive.</summary>
    public DateOnly FromDate { get; set; }
    /// <summary>Último dia, inclusive.</summary>
    public DateOnly ToDate { get; set; }
    /// <summary>Mine ou Global.</summary>
    [Required]
    public string Scope { get; set; } = string.Empty;
}

/// <summary>Solicitações autenticadas e resultados persistidos.</summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly ExpenseDbContext _database;
    private readonly IReportPublisher _publisher;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReportsController> _logger;

    /// <summary>Obtém os serviços da requisição.</summary>
    public ReportsController(IServiceProvider services)
    {
        _database = services.GetRequiredService<ExpenseDbContext>();
        _publisher = services.GetRequiredService<IReportPublisher>();
        _clock = services.GetRequiredService<TimeProvider>();
        _logger = services.GetRequiredService<ILogger<ReportsController>>();
    }

    /// <summary>Registra e publica um pedido sem esperar pelo cálculo.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(ReportRequest input)
    {
        try
        {
            ReportRules.Validate(input.FromDate, input.ToDate, input.Scope);
            string owner = ReportRules.Authorize(User, input.Scope);
            var report = new ReportRecord { OwnerId = owner, Scope = input.Scope, FromDate = input.FromDate, ToDate = input.ToDate, CreatedAt = _clock.GetUtcNow() };
            _database.Set<ReportRecord>().Add(report);
            await _database.SaveChangesAsync(HttpContext.RequestAborted);
            try
            {
                await _publisher.PublishAsync(report.Id, HttpContext.RequestAborted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogWarning("Publicação não confirmada para reportId={ReportId}; tipo={FailureType}", report.Id, exception.GetType().Name);
                return StatusCode(503, new { id = report.Id, error = new { code = "PUBLICATION_UNCONFIRMED", message = "Publicação sem confirmação. Consulte o identificador; o processamento pode ou não ocorrer." } });
            }

            return AcceptedAtAction(nameof(Detail), new { id = report.Id }, Envelope(report));
        }
        catch (ReportException exception)
        {
            throw new BusinessException(exception.StatusCode, exception.Message);
        }
    }

    /// <summary>Lista somente solicitações criadas pelo usuário.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        string owner = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        ReportRecord[] reports = await _database.Set<ReportRecord>().AsNoTracking().Where(r => r.OwnerId == owner).ToArrayAsync(HttpContext.RequestAborted);
        return Ok(reports.OrderByDescending(r => r.CreatedAt).Select(Envelope));
    }

    /// <summary>Consulta o estado e o resultado salvo da solicitação própria.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id) => Ok(Envelope(await OwnAsync(id)));

    /// <summary>Exporta o resultado salvo em uma planilha verdadeira.</summary>
    [HttpGet("{id:guid}/excel")]
    public async Task<IActionResult> Excel(Guid id)
    {
        ReportRecord report = await OwnAsync(id);
        try
        {
            return File(ReportRules.Excel(report, Result(report)), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"report-{id}.xlsx");
        }
        catch (ReportException exception)
        {
            throw new BusinessException(exception.StatusCode, exception.Message);
        }
    }

    private async Task<ReportRecord> OwnAsync(Guid id)
    {
        string owner = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        return await _database.Set<ReportRecord>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == owner, HttpContext.RequestAborted) ?? throw new BusinessException(404, "Relatório não encontrado.");
    }

    private static ReportResult? Result(ReportRecord report) => report.ResultJson is null ? null : JsonSerializer.Deserialize<ReportResult>(report.ResultJson);
    private static object Envelope(ReportRecord report) => new { report.Id, report.Scope, report.FromDate, report.ToDate, report.Status, report.CreatedAt, report.StartedAt, report.CompletedAt, result = Result(report), error = report.ErrorCode is null ? null : new { code = report.ErrorCode, message = report.ErrorMessage } };
}
