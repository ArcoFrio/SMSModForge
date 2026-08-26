using System.Collections.Generic;

namespace SMSModForge.Tutorials;

/// <summary>
/// Per-run notepad for a tutorial's steps: the "before" values a check needs to
/// tell that something was <em>added</em> rather than merely present.
/// <para/>
/// Replaces the static fields the first draft used. Those worked while one
/// throwaway tutorial existed, but they survive between runs — start a tutorial
/// twice and the second run begins holding the first run's baselines, so a step
/// can be satisfied before it has been read. Cleared by the runner every time a
/// tutorial starts, which is the whole point.
/// </summary>
public sealed class TutorialScratch
{
    private readonly Dictionary<string, object?> _values = new();

    public void Set(string key, object? value) => _values[key] = value;

    public T? Get<T>(string key)
        => _values.TryGetValue(key, out var v) && v is T t ? t : default;

    /// <summary>Convenience for the commonest case: did this count go up?</summary>
    public bool GrewSince(string key, int now) => now > Get<int>(key);

    public void Clear() => _values.Clear();
}
