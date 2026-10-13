using System;
using System.Linq.Expressions;
using System.Security.Claims;
using ExpenseHub.Api.Contracts;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Services;

internal static class ExpenseRules
{
    public static string Actor(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new BusinessException(401, "Identidade ausente.");

    public static void RequireRole(ClaimsPrincipal user, string role)
    {
        if (!user.IsInRole(role))
        {
            throw new BusinessException(403, "Perfil sem permissão para esta operação.");
        }
    }

    public static Expression<Func<Expense, bool>> Visibility(ClaimsPrincipal user)
    {
        string id = Actor(user);
        bool employee = user.IsInRole(Roles.Employee);
        bool approver = user.IsInRole(Roles.Approver);
        bool finance = user.IsInRole(Roles.Finance);
        bool auditor = user.IsInRole(Roles.Auditor);
        if (!employee && !approver && !finance && !auditor)
        {
            throw new BusinessException(403, "Perfil sem acesso a reembolsos.");
        }

        return expense => auditor || (employee && expense.OwnerId == id) || (approver && expense.State == ExpenseState.Submitted) || (finance && (expense.State == ExpenseState.Approved || expense.State == ExpenseState.Paid));
    }

    public static void Validate(ExpenseRequest input, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length is < 10 or > 500 || input.Amount is < 0.01m or > int.MaxValue || input.ExpenseDate == default || input.ExpenseDate > today || input.CategoryId < 1)
        {
            throw new BusinessException(400, "Descrição, valor, data ou categoria inválidos.");
        }
    }

    public static void RequireOwnerDraft(Expense expense, ClaimsPrincipal user)
    {
        RequireRole(user, Roles.Employee);
        if (expense.OwnerId != Actor(user))
        {
            throw new BusinessException(403, "Somente o proprietário pode alterar ou enviar o reembolso.");
        }

        RequireState(expense, ExpenseState.Draft);
    }

    public static void RequireState(Expense expense, ExpenseState expected)
    {
        if (expense.State != expected)
        {
            throw new BusinessException(409, "Transição incompatível com o estado atual.");
        }
    }

    public static void Decide(Expense expense, ClaimsPrincipal user, bool reject, string? reason, DateTimeOffset now)
    {
        RequireRole(user, Roles.Approver);
        if (expense.OwnerId == Actor(user))
        {
            throw new BusinessException(403, "Autoaprovação e autorreprovação são proibidas.");
        }

        RequireState(expense, ExpenseState.Submitted);
        if (reject && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 500))
        {
            throw new BusinessException(400, "Justificativa deve conter entre 10 e 500 caracteres.");
        }

        Change(expense, user, reject ? ExpenseState.Rejected : ExpenseState.Approved, reject ? "Rejected" : "Approved", now, reason?.Trim());
    }

    public static void Pay(Expense expense, ClaimsPrincipal user, DateTimeOffset now)
    {
        RequireRole(user, Roles.Finance);
        if (expense.OwnerId == Actor(user))
        {
            throw new BusinessException(403, "Autopagamento é proibido.");
        }

        RequireState(expense, ExpenseState.Approved);
        Change(expense, user, ExpenseState.Paid, "Paid", now);
        expense.Payment = new PaymentRecord { ExpenseId = expense.Id, ActorId = Actor(user), PaidAt = now, Amount = expense.Amount };
    }

    public static void Change(Expense expense, ClaimsPrincipal user, ExpenseState next, string action, DateTimeOffset now, string? reason = null)
    {
        ExpenseState previous = expense.State;
        expense.State = next;
        expense.Revision++;
        expense.History.Add(new ExpenseHistory { ExpenseId = expense.Id, Action = action, ActorId = Actor(user), OccurredAt = now, PreviousState = previous, NextState = next, Reason = reason });
    }
}
