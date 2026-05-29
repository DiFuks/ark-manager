namespace ArkManager.Core.Services.Config;

/// <summary>
/// Single-shot rescheduling timer. Each <see cref="Schedule"/> call resets the
/// countdown; <see cref="Elapsed"/> fires once when the latest delay expires.
/// Abstracted so tests can drive timing manually instead of waiting on FSEvents.
/// </summary>
public interface IDebounceTimer
{
    void Schedule(TimeSpan delay);
    event Action? Elapsed;
}
