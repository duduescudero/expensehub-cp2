using System;

namespace ExpenseHub.Api.Services;

internal sealed class BusinessException : Exception
{
    public BusinessException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
