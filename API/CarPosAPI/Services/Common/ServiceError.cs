namespace CarPosAPI.Services.Common;

/// <summary>
/// One failure, described for both kinds of reader: a stable <see cref="Code"/>
/// the frontend translates, and an English <see cref="Detail"/> for everything
/// that does not (logs, curl, an outdated client).
///
/// Used where a helper decides <em>which</em> failure happened but not what the
/// result type is — the share window check serves both create and update, and the
/// ack key validator serves both the API and the CLI.
/// </summary>
/// <param name="Code">One of <see cref="ErrorCodes"/>.</param>
/// <param name="Detail">The English sentence, written for the end user.</param>
/// <param name="Parameters">
/// Values the message mentions, keyed by the names the translation uses; null when
/// it mentions none. Serialized as the <c>params</c> extension.
/// </param>
public sealed record ServiceError(
    string Code,
    string Detail,
    IReadOnlyDictionary<string, object>? Parameters = null);
