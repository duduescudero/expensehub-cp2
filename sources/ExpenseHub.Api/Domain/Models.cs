using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;

namespace ExpenseHub.Api.Domain;

/// <summary>Usuário persistido pelo ASP.NET Core Identity.</summary>
public sealed class AppUser : IdentityUser
{
}

internal enum ExpenseState
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Paid,
}

internal sealed class ExpenseCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public int CategoryId { get; set; }
    public ExpenseState State { get; set; }
    public int Revision { get; set; }
    public List<ExpenseHistory> History { get; set; } = [];
    public PaymentRecord? Payment { get; set; }
}

internal sealed class ExpenseHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExpenseId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public ExpenseState? PreviousState { get; set; }
    public ExpenseState NextState { get; set; }
    public string? Reason { get; set; }
    public string? Changes { get; set; }
}

internal sealed class PaymentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExpenseId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public DateTimeOffset PaidAt { get; set; }
    public decimal Amount { get; set; }
}
