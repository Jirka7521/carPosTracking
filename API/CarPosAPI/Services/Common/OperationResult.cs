namespace CarPosAPI.Services.Common;

/// <summary>
/// The outcome of a service call plus, on success, its value. Used instead of
/// exceptions for expected failures so controllers stay a straight
/// <c>switch</c> over <see cref="OperationOutcome"/> with no try/catch anywhere.
///
/// <paramref name="Detail"/> is a message written <em>for the end user</em>, so
/// it must never contain SQL, stack traces, or a fact the caller is not entitled
/// to know (whether an email is registered, for instance).
/// </summary>
/// <typeparam name="TValue">Type of the value produced on success.</typeparam>
/// <param name="Outcome">What happened.</param>
/// <param name="Value">The result — non-null exactly when the outcome is success.</param>
/// <param name="Detail">Optional human-readable explanation for a failure.</param>
public sealed record OperationResult<TValue>(
    OperationOutcome Outcome,
    TValue? Value,
    string? Detail = null)
{
    /// <summary>True when the call succeeded and <see cref="Value"/> is populated.</summary>
    public bool IsSuccess => Outcome == OperationOutcome.Success;

    /// <summary>Builds a successful result.</summary>
    /// <param name="value">The produced value.</param>
    /// <returns>A success result carrying <paramref name="value"/>.</returns>
    public static OperationResult<TValue> Success(TValue value)
    {
        return new OperationResult<TValue>(OperationOutcome.Success, value);
    }

    /// <summary>Builds a "not there, or not yours" result.</summary>
    /// <param name="detail">Message shown to the caller.</param>
    /// <returns>A <see cref="OperationOutcome.NotFound"/> result.</returns>
    public static OperationResult<TValue> NotFound(string detail)
    {
        return new OperationResult<TValue>(OperationOutcome.NotFound, default, detail);
    }

    /// <summary>Builds a "you may see it but not do this" result.</summary>
    /// <param name="detail">Message shown to the caller.</param>
    /// <returns>A <see cref="OperationOutcome.Forbidden"/> result.</returns>
    public static OperationResult<TValue> Forbidden(string detail)
    {
        return new OperationResult<TValue>(OperationOutcome.Forbidden, default, detail);
    }

    /// <summary>Builds a "already exists" result.</summary>
    /// <param name="detail">Message shown to the caller.</param>
    /// <returns>A <see cref="OperationOutcome.Conflict"/> result.</returns>
    public static OperationResult<TValue> Conflict(string detail)
    {
        return new OperationResult<TValue>(OperationOutcome.Conflict, default, detail);
    }

    /// <summary>Builds a "well-formed but wrong" result.</summary>
    /// <param name="detail">Message shown to the caller.</param>
    /// <returns>An <see cref="OperationOutcome.Invalid"/> result.</returns>
    public static OperationResult<TValue> Invalid(string detail)
    {
        return new OperationResult<TValue>(OperationOutcome.Invalid, default, detail);
    }
}
