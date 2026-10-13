using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Contracts;

public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed class RolesRequest
{
    [Required]
    public string[] Roles { get; set; } = [];
}
