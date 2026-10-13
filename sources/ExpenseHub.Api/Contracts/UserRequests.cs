using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Contracts;

/// <summary>Dados de cadastro sem permissão para informar roles.</summary>
public sealed class RegisterRequest
{
    /// <summary>Endereço de e-mail do usuário.</summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    /// <summary>Senha recebida apenas para validação pelo Identity.</summary>
    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Credenciais de autenticação.</summary>
public sealed class LoginRequest
{
    /// <summary>Endereço de e-mail do usuário.</summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    /// <summary>Senha recebida apenas para validação pelo Identity.</summary>
    [Required]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Conjunto de roles a atribuir ao usuário.</summary>
public sealed class RolesRequest
{
    /// <summary>Roles conhecidas a atribuir ao usuário.</summary>
    [Required]
    public string[] Roles { get; set; } = [];
}
