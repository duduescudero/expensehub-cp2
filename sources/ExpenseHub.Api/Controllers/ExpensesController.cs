using System;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseHub.Api.Controllers;

/// <summary>Operações autenticadas do fluxo de reembolsos.</summary>
[ApiController]
[Authorize]
[Route("api/expenses")]
public sealed class ExpensesController : ControllerBase
{
    private readonly ExpenseService _service;

    /// <summary>Operações autenticadas do fluxo de reembolsos.</summary>
    public ExpensesController(IServiceProvider services)
    {
        _service = services.GetRequiredService<ExpenseService>();
    }

    /// <summary>Cria rascunho com proprietário derivado do token.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Employee)]
    public async Task<IActionResult> Create(ExpenseRequest input) => Ok(await _service.CreateAsync(input, User, HttpContext.RequestAborted));

    /// <summary>Edita somente rascunho pertencente ao solicitante.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Employee)]
    public async Task<IActionResult> Edit(Guid id, ExpenseRequest input) => Ok(await _service.EditAsync(id, input, User, HttpContext.RequestAborted));





    /// <summary>Aprova reembolso enviado por outro usuário.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = Roles.Approver)]
    public async Task<IActionResult> Approve(Guid id) => Ok(await _service.DecideAsync(id, User, false, null, HttpContext.RequestAborted));

    /// <summary>Reprova reembolso enviado com justificativa válida.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = Roles.Approver)]
    public async Task<IActionResult> Reject(Guid id, RejectRequest input) => Ok(await _service.DecideAsync(id, User, true, input.Reason, HttpContext.RequestAborted));

}
