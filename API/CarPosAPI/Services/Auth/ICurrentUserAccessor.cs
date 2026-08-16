namespace CarPosAPI.Services.Auth;

/// <summary>
/// Tells a service which user is making the current request, so business code can
/// ask "who is this?" without taking a dependency on <c>HttpContext</c> or on the
/// exact claim the id happens to live in.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// The authenticated user's id, or null when the request is anonymous.
    /// Behind <c>[Authorize]</c> it is never null — but the type stays nullable so
    /// that assumption has to be made explicitly rather than by habit.
    /// </summary>
    int? UserId { get; }
}
