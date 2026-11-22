using System;
using System.IO;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Where File ▸ New puts a pack (1.10.0, the author: "prompts the user to select a folder and name the modpack. It
/// should detect whether the selected folder has unrelated files inside, and if it does, it instead places the
/// modpack inside a new folder inside the selected one with the selected name").
/// <para/>
/// An empty folder - or one that does not exist yet - is the pack's folder itself. A folder with anything in it
/// (files of something else, or another pack, which a new one must never be written over) gets a new folder inside
/// it, named after the pack; if that name is taken by a folder that is not empty, "Name 2", "Name 3"...
/// </summary>
public static class NewPackPlace
{
    /// <summary>The folder the new pack is made in.</summary>
    public static string Where(string chosen, string name)
    {
        string folder = Path.GetFullPath(chosen);
        if (IsFree(folder)) return folder;
        string first = Path.Combine(folder, FolderName(name));
        if (IsFree(first)) return first;
        for (int n = 2; ; n++)
        {
            string next = first + " " + n;
            if (IsFree(next)) return next;
        }
    }

    /// <summary>Whether a pack can be made right in <paramref name="folder"/>: it is not there yet, or it is empty.</summary>
    public static bool IsFree(string folder)
        => !Directory.Exists(folder) && !File.Exists(folder)
           || Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any();

    /// <summary>A pack's name as a folder's: what Windows does not allow in one left out, and never empty.</summary>
    public static string FolderName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        string cleaned = new string((name ?? "").Where(c => !bad.Contains(c)).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "Modpack" : cleaned;
    }
}
