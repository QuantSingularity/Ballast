namespace Ballast.Core;

public class BallastException(string message, int statusCode, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class ValidationException(string message) : BallastException(message, 400, "validation_error");

public sealed class UnauthorizedException(string message) : BallastException(message, 401, "unauthorized");

public sealed class ForbiddenException(string message) : BallastException(message, 403, "forbidden");

public sealed class NotFoundException(string message) : BallastException(message, 404, "not_found");

public sealed class ConflictException(string message) : BallastException(message, 409, "conflict");

public sealed class LockedException(string message) : BallastException(message, 423, "locked");

public sealed class ServiceUnavailableException(string message) : BallastException(message, 503, "ai_unavailable");

public sealed class ModelNotFittedException(string message) : BallastException(message, 409, "model_not_fitted");
