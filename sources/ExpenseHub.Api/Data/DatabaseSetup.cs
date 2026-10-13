using System;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseHub.Api.Data;

internal static class DatabaseSetup
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        using IServiceScope scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExpenseDbContext>();
        await context.Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (string role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                Check(await roles.CreateAsync(new IdentityRole(role)));
            }
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        string email = configuration["Bootstrap:Email"] ?? throw new InvalidOperationException("Configure Bootstrap__Email.");
        AppUser? admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            string password = configuration["Bootstrap:Password"] ?? throw new InvalidOperationException("Configure Bootstrap__Password fora do repositório.");
            admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
            Check(await users.CreateAsync(admin, password));
            Check(await users.AddToRoleAsync(admin, Roles.Admin));
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Falha ao inicializar Identity. Confira a configuração e a política de senha.");
        }
    }
}
