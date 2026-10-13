using System;
using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Contracts;

/// <summary>Dados editáveis de uma despesa.</summary>
public sealed class ExpenseRequest
{
    /// <summary>Descrição da despesa entre 10 e 500 caracteres.</summary>
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;
    /// <summary>Valor do reembolso em reais.</summary>
    [Range(typeof(decimal), "0.01", "2147483647", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Amount { get; set; }
    /// <summary>Data da despesa, que não pode ser futura.</summary>
    [Required]
    public DateOnly ExpenseDate { get; set; }
    /// <summary>Identificador da categoria persistida.</summary>
    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }
}

/// <summary>Justificativa obrigatória de reprovação.</summary>
public sealed class RejectRequest
{
    /// <summary>Justificativa da reprovação entre 10 e 500 caracteres.</summary>
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;
}

