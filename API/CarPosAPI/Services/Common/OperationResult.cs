namespace CarPosAPI.Services.Common;

/// <summary>
/// The outcome of a service call plus, on success, its value. Used instead of
/// exceptions for expected failures so controllers stay a straight
/// <c>switch</c> over <see cref="OperationOutcome"/> with no try/catch anywhere.
///
/// <paramref name="Detail"/> is a message written <em>for the end user</em>, so
/// it must never contain SQL, stack traces, or a fact the caller is not entitled
/// to know (whether an email is registered, for instance). <paramref name="Code"/>
/// names the same failure for a client that translates it — see
/// <see cref="ErrorCodes"/> — and is held to the same rule.
/// </summary>
/// <typeparam name="TValue">Type of the value produced on success.</typeparam>
/// <param name="Outcome">What happened.</param>
/// <param name="Value">The result — non-null exactly when the outcome is success.</param>
/// <param name="Detail">Optional human-readable explanation for a failure.</param>
/// <param name="Code">One of <see cref="ErrorCodes"/> for a failure; null on success.</param>
/// <param name="Parameters">Values the failure's message mentions, if any.</param>
public sealed record OperationResult<TValue>(
    OperationOutcome Outcome,
    TValue? Value,
    string? Detail = null,
    string? Code = null,
    IReadOnlyDictionary<string, object>? Parameters = null)
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

    /// <summary>
    /// Builds a failure of a given outcome from a prepared error — for a helper that
    /// decided both, such as the sharing lookups that serve differently-typed results.
    /// </summary>
    /// <param name="outcome">Which failure; must not be <see cref="OperationOutcome.Success"/>.</param>
    /// <param name="error">The failure to report.</param>
    /// <returns>The failed result.</returns>
    public static OperationResult<TValue> Failed(OperationOutcome outcome, ServiceError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (outcome == OperationOutcome.Success)
        {
            throw new ArgumentException("A failure cannot have a success outcome.", nameof(outcome));
        }

        return new OperationResult<TValue>(outcome, default, error.Detail, error.Code, error.Parameters);
    }

    /// <summary>Builds a "not there, or not yours" result.</summary>
    /// <param name="code">One of <see cref="ErrorCodes"/>.</param>
    /// <param name="detail">Message shown to the caller.</param>
    /// <param name="parameters">Values the message mentions, if any.</param>
    /// <returns>A <see cref="OperationOutcome.NotFound"/> result.</returns>
    public static OperationResult<TValue> NotFound(
        string code,
        string detail,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        return new OperationResult<TValue>(OperationOutcome.NotFound, default, detail, code, parameters);
    }

    /// <summary>Builds a "you may see it but not do this" result.</summary>
    /// <param name="code">One of <see cref="ErrorCodes"/>.</param>
    /// <param name="detail">Message shown to the caller.</param>
    /// <param name="parameters">Values the message mentions, if any.</param>
    /// <returns>A <see cref="OperationOutcome.Forbidden"/> result.</returns>
    public static OperationResult<TValue> Forbidden(
        string code,
        string detail,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        return new OperationResult<TValue>(OperationOutcome.Forbidden, default, detail, code, parameters);
    }

    /// <summary>Builds a "you may see it but not do this" result from a prepared error.</summary>
    /// <param name="error">The failure, decided by a helper.</param>
    /// <returns>A <see cref="OperationOutcome.Forbidden"/> result.</returns>
    public static OperationResult<TValue> Forbidden(ServiceError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return Forbidden(error.Code, error.Detail, error.Parameters);
    }

    /// <summary>Builds a "already exists" result.</summary>
    /// <param name="code">One of <see cref="ErrorCodes"/>.</param>
    /// <param name="detail">Message shown to the caller.</param>
    /// <param name="parameters">Values the message mentions, if any.</param>
    /// <returns>A <see cref="OperationOutcome.Conflict"/> result.</returns>
    public static OperationResult<TValue> Conflict(
        string code,
        string detail,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        return new OperationResult<TValue>(OperationOutcome.Conflict, default, detail, code, parameters);
    }

    /// <summary>Builds a "well-formed but wrong" result.</summary>
    /// <param name="code">One of <see cref="ErrorCodes"/>.</param>
    /// <param name="detail">Message shown to the caller.</param>
    /// <param name="parameters">Values the message mentions, if any.</param>
    /// <returns>An <see cref="OperationOutcome.Invalid"/> result.</returns>
    public static OperationResult<TValue> Invalid(
        string code,
        string detail,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        return new OperationResult<TValue>(OperationOutcome.Invalid, default, detail, code, parameters);
    }

    /// <summary>Builds a "well-formed but wrong" result from a prepared error.</summary>
    /// <param name="error">The failure, decided by a helper.</param>
    /// <returns>An <see cref="OperationOutcome.Invalid"/> result.</returns>
    public static OperationResult<TValue> Invalid(ServiceError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return Invalid(error.Code, error.Detail, error.Parameters);
    }
}
