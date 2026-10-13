using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExpenseHub.Api;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<BusinessExceptionHandler>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ExpenseService>();
        builder.Services.AddDbContext<ExpenseDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("ExpenseHub") ?? "Data Source=expensehub.db"));
        builder.Services.AddAuthentication().AddBearerToken(IdentityConstants.BearerScheme);
        builder.Services.AddAuthorization();
        builder.Services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ExpenseDbContext>().AddSignInManager().AddDefaultTokenProviders();
        builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        WebApplication app = builder.Build();
        await DatabaseSetup.InitializeAsync(app.Services, app.Configuration);
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("GetHealth");
        app.MapControllers();
        await app.RunAsync();
    }
}
