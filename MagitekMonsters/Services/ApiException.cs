using System;

namespace MagitekMonsters.Services;

public sealed class ApiException(int statusCode, string message, string? code = null, double? retryAfterSeconds = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
    public double? RetryAfterSeconds { get; } = retryAfterSeconds;
}

public sealed class UnconfirmedActionException(Exception inner) : Exception("The connection was interrupted before the action could be confirmed.", inner);
