using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SMSModForge.Localization;
using SMSModForge.Model;

namespace SMSModForge.Services.Translation;

/// <summary>
/// How far along each of the pack's translations is, worked out when it costs
/// nobody a wait (the author, 1.6.3).
/// <para/>
/// The pack's translations window used to work every language out as it
/// opened - a couple of seconds for a large pack. Now every language is worked
/// out when the pack is opened, away from the window's thread, and the window
/// takes those; it works out again only the ones that have changed since:
/// a translation whose file was written (the language being edited is written
/// out as the window opens, and a transfer or a machine run writes theirs), or
/// every one when the pack's own texts have changed, which is what they are all
/// measured against.
/// </summary>
public sealed class TranslationSummaryCache
{
    private sealed record Entry(string Signature, DateTime Written, long Length, PackTranslations.Summary Summary);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private string? _root;

    /// <summary>The work begun when the pack was opened. For tests, and for a
    /// window opened before it is finished, which waits for it rather than doing
    /// the same work beside it.</summary>
    internal Task Warming { get; private set; } = Task.CompletedTask;

    /// <summary>How many translations the last <see cref="Get"/> had to work
    /// out rather than take as they were. For tests.</summary>
    internal int LastWorkedOut { get; private set; }

    /// <summary>Let go of everything: another pack, or none.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _byPath.Clear();
            _root = null;
        }
    }

    /// <summary>
    /// Work out every translation of the pack, away from the window's thread.
    /// The pack's texts are read here, on the caller's; the files there.
    /// </summary>
    public void Warm(ModPack pack, string? packRoot)
    {
        if (string.IsNullOrEmpty(packRoot)) return;
        var files = PackTranslations.Files(packRoot);
        if (files.Count == 0) return;
        var snapshot = PackTranslations.Snapshot(pack);
        Use(packRoot);
        Warming = Task.Run(() =>
        {
            foreach (string path in files)
            {
                try { Store(snapshot, path); }
                catch (Exception) { /* a file that cannot be read now is worked out when asked for */ }
            }
        });
    }

    /// <summary>
    /// Every translation's standing now: as worked out before where neither
    /// its file nor the pack's texts have changed since, and worked out now
    /// where either has.
    /// </summary>
    public List<PackTranslations.Summary> Get(ModPack pack, string packRoot)
    {
        var snapshot = PackTranslations.Snapshot(pack);
        Use(packRoot);
        try { Warming.Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }

        var list = new List<PackTranslations.Summary>();
        int worked = 0;
        foreach (string path in PackTranslations.Files(packRoot))
        {
            var (written, length) = Stamp(path);
            Entry? had;
            lock (_gate) _byPath.TryGetValue(path, out had);
            if (had != null && had.Signature == snapshot.Signature && had.Written == written && had.Length == length)
            {
                list.Add(had.Summary);
                continue;
            }
            worked++;
            var fresh = Store(snapshot, path);
            if (fresh != null) list.Add(fresh);
        }
        LastWorkedOut = worked;
        return list;
    }

    private void Use(string packRoot)
    {
        lock (_gate)
        {
            if (string.Equals(_root, packRoot, StringComparison.OrdinalIgnoreCase)) return;
            _byPath.Clear();
            _root = packRoot;
        }
    }

    private PackTranslations.Summary? Store(PackTranslations.SourceSnapshot snapshot, string path)
    {
        // Stamped before it is read: a write that lands in between is then
        // seen as a change next time, rather than hidden behind the old stamp.
        var (written, length) = Stamp(path);
        var summary = PackTranslations.SummaryOf(snapshot, path);
        if (summary == null) return null;
        lock (_gate) _byPath[path] = new Entry(snapshot.Signature, written, length, summary);
        return summary;
    }

    private static (DateTime Written, long Length) Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return (info.LastWriteTimeUtc, info.Exists ? info.Length : -1);
        }
        catch (Exception) { return (DateTime.MinValue, -1); }
    }
}
