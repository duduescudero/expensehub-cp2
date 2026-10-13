using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Services;

internal sealed class ExpenseService
{
    private readonly ExpenseDbContext _database;
    private readonly TimeProvider _clock;

    public ExpenseService(ExpenseDbContext database, TimeProvider clock)
    {
        _database = database;
        _clock = clock;
    }

    public async Task<Expense> CreateAsync(ExpenseRequest input, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ExpenseRules.RequireRole(user, Roles.Employee);
        await ValidateAsync(input, cancellationToken);
        var expense = new Expense { OwnerId = ExpenseRules.Actor(user), Description = input.Description.Trim(), Amount = input.Amount, ExpenseDate = input.ExpenseDate, CategoryId = input.CategoryId, State = ExpenseState.Draft };
        expense.History.Add(new ExpenseHistory { ExpenseId = expense.Id, Action = "Created", ActorId = expense.OwnerId, OccurredAt = _clock.GetUtcNow(), NextState = ExpenseState.Draft });
        _database.Expenses.Add(expense);
        await SaveAsync(cancellationToken);
        return expense;
    }

    public async Task<Expense> EditAsync(Guid id, ExpenseRequest input, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        Expense expense = await LoadAsync(id, cancellationToken);
        ExpenseRules.RequireOwnerDraft(expense, user);
        await ValidateAsync(input, cancellationToken);
        string before = JsonSerializer.Serialize(new { expense.Description, expense.Amount, expense.ExpenseDate, expense.CategoryId });
        expense.Description = input.Description.Trim();
        expense.Amount = input.Amount;
        expense.ExpenseDate = input.ExpenseDate;
        expense.CategoryId = input.CategoryId;
        string after = JsonSerializer.Serialize(new { expense.Description, expense.Amount, expense.ExpenseDate, expense.CategoryId });
        ExpenseRules.Change(expense, user, ExpenseState.Draft, "Edited", _clock.GetUtcNow());
        expense.History[^1].Changes = JsonSerializer.Serialize(new { Before = before, After = after });
        await SaveAsync(cancellationToken);
        return expense;
    }

    public async Task<List<Expense>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken) => await _database.Expenses.AsNoTracking().Where(ExpenseRules.Visibility(user)).ToListAsync(cancellationToken);

    public async Task<Expense> DetailAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken) => await _database.Expenses.AsNoTracking().Include(e => e.Payment).Where(ExpenseRules.Visibility(user)).FirstOrDefaultAsync(e => e.Id == id, cancellationToken) ?? throw new BusinessException(404, "Reembolso não encontrado.");


    public async Task<Expense> SubmitAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        Expense expense = await LoadAsync(id, cancellationToken);
        ExpenseRules.RequireOwnerDraft(expense, user);
        ExpenseRules.Change(expense, user, ExpenseState.Submitted, "Submitted", _clock.GetUtcNow());
        await SaveAsync(cancellationToken);
        return expense;
    }

    public async Task<Expense> DecideAsync(Guid id, ClaimsPrincipal user, bool reject, string? reason, CancellationToken cancellationToken)
    {
        Expense expense = await LoadAsync(id, cancellationToken);
        ExpenseRules.Decide(expense, user, reject, reason, _clock.GetUtcNow());
        await SaveAsync(cancellationToken);
        return expense;
    }


    private async Task<Expense> LoadAsync(Guid id, CancellationToken cancellationToken) => await _database.Expenses.Include(e => e.History).Include(e => e.Payment).FirstOrDefaultAsync(e => e.Id == id, cancellationToken) ?? throw new BusinessException(404, "Reembolso não encontrado.");

    private async Task ValidateAsync(ExpenseRequest input, CancellationToken cancellationToken)
    {
        ExpenseRules.Validate(input, DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
        if (!await _database.Categories.AnyAsync(c => c.Id == input.CategoryId, cancellationToken))
        {
            throw new BusinessException(400, "Categoria não encontrada.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException(409, "Reembolso alterado por outra operação. Consulte novamente.");
        }
    }
}
