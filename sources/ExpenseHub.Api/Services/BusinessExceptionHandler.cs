using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseHub.Api.Services;

internal sealed class BusinessExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, System.Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BusinessException business)
        {
            return false;
        }

        httpContext.Response.StatusCode = business.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails { Status = business.StatusCode, Title = business.Message }, cancellationToken);
        return true;
    }
}
