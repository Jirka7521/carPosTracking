namespace CarPosAPI.Services.Trips;

/// <summary>One window of a device's history the worker recomputed.</summary>
/// <param name="From">The restart point the window began at, or null for the device's first fix.</param>
/// <param name="Starts">The trip starts detected in it.</param>
/// <param name="ResumeFrom">Where the next window begins when this one was cut short; null when complete.</param>
/// <param name="IsComplete">True when the window reached the newest fix.</param>
internal sealed record TripWindow(DateTime? From, IReadOnlyList<DateTime> Starts, DateTime? ResumeFrom, bool IsComplete);
