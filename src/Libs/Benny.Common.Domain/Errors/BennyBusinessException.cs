namespace Benny.Common.Domain.Errors;

public class BennyBusinessException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatusCode { get; }

    public BennyBusinessException(string errorCode, string message, int httpStatusCode = 400, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
    }
}

public sealed class InsufficientBalanceException(string accountId, decimal requested, decimal available)
    : BennyBusinessException(
        "INSUFFICIENT_BALANCE",
        $"Account '{accountId}' has available balance {available}, which is less than requested {requested}.",
        422);

public sealed class DuplicateIdempotencyConflictException(string idempotencyKey)
    : BennyBusinessException(
        "IDEMPOTENCY_IN_PROGRESS",
        $"A request with Idempotency-Key '{idempotencyKey}' is currently being processed.",
        409);

public sealed class RiskRejectedException(string reason, string ruleCode, decimal score)
    : BennyBusinessException(
        ruleCode,
        $"Transaction rejected by risk engine (score={score}): {reason}",
        422);
