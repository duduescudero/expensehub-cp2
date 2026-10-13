using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseHub.Api.Controllers;

/// <summary>AdministraÃ§Ã£o de usuÃ¡rios restrita ao perfil Admin.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/users")]
public sealed class AdminController : ControllerBase
{
    private readonly UserManager<AppUser> _users;

    /// <summary>AdministraÃ§Ã£o de usuÃ¡rios restrita ao perfil Admin.</summary>
    public AdminController(UserManager<AppUser> users)
    {
        _users = users;
    }

    /// <summary>Lista recursos acessÃ­veis ao perfil autenticado.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var users = await _users.Users.ToListAsync(HttpContext.RequestAborted);
        var result = new System.Collections.Generic.List<object>();
        foreach (AppUser user in users)
        {
            result.Add(new { user.Id, user.Email, Roles = await _users.GetRolesAsync(user) });
        }

        return Ok(result);
    }

    /// <summary>Atualiza roles conhecidas sem permitir remoÃ§Ã£o da prÃ³pria role Admin.</summary>
    [HttpPut("{id}/roles")]
    public async Task<IActionResult> SetRoles(string id, RolesRequest request)
    {
        if (request.Roles.Any(role => !Roles.All.Contains(role, StringComparer.Ordinal)))
        {
            return Problem(statusCode: 400, title: "Role desconhecida.");
        }

        if (id == User.FindFirstValue(ClaimTypes.NameIdentifier) && !request.Roles.Contains(Roles.Admin, StringComparer.Ordinal))
        {
            return Problem(statusCode: 403, title: "NÃ£o remova sua prÃ³pria role Admin.");
        }

        AppUser? user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var database = HttpContext.RequestServices.GetRequiredService<ExpenseDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        var current = await _users.GetRolesAsync(user);
        IdentityResult removed = await _users.RemoveFromRolesAsync(user, current.Except(request.Roles));
        if (!removed.Succeeded)
        {
            return Problem(statusCode: 400, title: "NÃ£o foi possÃ­vel remover roles.");
        }

        IdentityResult added = await _users.AddToRolesAsync(user, request.Roles.Except(current));
        if (!added.Succeeded)
        {
            return Problem(statusCode: 400, title: "NÃ£o foi possÃ­vel atribuir roles.");
        }

        await transaction.CommitAsync(HttpContext.RequestAborted);
        return Ok(new { user.Id, Roles = await _users.GetRolesAsync(user), Message = "FaÃ§a login novamente para atualizar o token." });
    }
}
