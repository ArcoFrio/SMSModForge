using System;
using System.IO;
using System.Text.RegularExpressions;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// One pack of the start page's recent list (1.10.0): its name, where it is, and whether it is still there. A
/// pack that is gone stays listed, greyed, with a way to take it off - it may be on a drive that is not plugged in.
/// </summary>
public sealed class RecentPackRow
{
    public RecentPackRow(string path)
    {
        Path = path;
        string? manifest = null;
        try { manifest = Directory.Exists(path) ? PackRepository.ResolveManifestPath(path) : null; }
        catch (Exception) { manifest = null; }
        Exists = manifest != null && File.Exists(manifest);
        Name = (Exists ? NameIn(manifest!) : null) ?? System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        Where = System.IO.Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? path;
    }

    public string Path { get; }
    public string Name { get; }
    public string Where { get; }
    public bool Exists { get; }
    public bool IsMissing => !Exists;

    private static readonly Regex PackId = new("\"packId\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    /// <summary>The pack's name, read from the top of its manifest - where it is written first - without reading
    /// the rest: a large pack's manifest is megabytes, and the start page lists ten of them.</summary>
    private static string? NameIn(string manifest)
    {
        try
        {
            using var reader = new StreamReader(manifest);
            var head = new char[4096];
            int read = reader.ReadBlock(head, 0, head.Length);
            var m = PackId.Match(new string(head, 0, read));
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }
        catch (Exception) { return null; }
    }
}
