using System.Threading.Tasks;
using ExpenseHub.Api.Contracts;
using ExpenseHub.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseHub.Api.Controllers;

[ApiController]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;

    public AuthController(UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        _users = users;
        _signIn = signIn;
    }

    [AllowAnonymous]
    [HttpPost("/register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new AppUser { UserName = request.Email, Email = request.Email };
        IdentityResult result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new ValidationProblemDetails { Title = "Cadastro inválido.", Detail = string.Join("; ", result.Errors) });
        }

        return StatusCode(201, new { user.Id, user.Email });
    }

    [AllowAnonymous]
    [HttpPost("/login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        AppUser? user = await _users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Problem(statusCode: 401, title: "Credenciais inválidas.");
        }

        Microsoft.AspNetCore.Identity.SignInResult result = await _signIn.CheckPasswordSignInAsync(user, request.Password, true);
        if (!result.Succeeded)
        {
            return Problem(statusCode: 401, title: "Credenciais inválidas.");
        }

        return SignIn(await _signIn.CreateUserPrincipalAsync(user), new AuthenticationProperties(), IdentityConstants.BearerScheme);
    }
}
