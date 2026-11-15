using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Validation;

/// <summary>
/// The pack as it would be saved, written out and parsed once, and each of its
/// translation files read once - for the checks of one Validate, which each made
/// their own: three writings-out of the whole pack and three readings of every
/// translation per Validate, most of what validating a large pack cost (1.9.0).
/// <para/>
/// Nothing that holds one may change what it holds. The checks only read the
/// JSON (<see cref="PackTexts.Of"/> collects, never writes) and the files, and
/// the pack does not change while it is validated.
/// </para>
/// </summary>
internal sealed class SavedPack
{
    private readonly ModPack _pack;
    private JObject? _json;
    private readonly Dictionary<string, TextFile> _files = new(StringComparer.OrdinalIgnoreCase);

    public SavedPack(ModPack pack) => _pack = pack;

    /// <summary>The pack as it would be saved, parsed: written out the first time it is asked for.</summary>
    public JObject Json => _json ??= JObject.Parse(PackRepository.SerializeAsSaved(_pack));

    /// <summary>A translation file, read the first time it is asked for. One
    /// that cannot be read is asked again, as before.</summary>
    public TextFile? Read(string path)
    {
        if (_files.TryGetValue(path, out var file)) return file;
        file = Loc.Read(path);
        if (file != null) _files[path] = file;
        return file;
    }
}
