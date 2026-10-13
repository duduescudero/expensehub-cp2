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

    /// <summary>Lista recursos acessíveis ao perfil autenticado.</summary>
    [HttpGet]
    [Authorize(Roles = "Employee,Approver,Finance,Auditor")]
    public async Task<IActionResult> List() => Ok(await _service.ListAsync(User, HttpContext.RequestAborted));

    /// <summary>Consulta um reembolso no escopo de leitura do usuário.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Employee,Approver,Finance,Auditor")]
    public async Task<IActionResult> Detail(Guid id) => Ok(await _service.DetailAsync(id, User, HttpContext.RequestAborted));


    /// <summary>Envia rascunho próprio para análise.</summary>
    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = Roles.Employee)]
    public async Task<IActionResult> Submit(Guid id) => Ok(await _service.SubmitAsync(id, User, HttpContext.RequestAborted));



}
