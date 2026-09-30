using SMSModForge.Localization;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// One entry of the Direct Path list (the author, 1.6.3): a path into the
/// game's own scene, and a short note beside it saying what it is, with an
/// example of using it. See <see cref="GamePathExamples"/>.
/// <para/>
/// <see cref="ToString"/> is the path, exactly as the game's hierarchy has it:
/// the combo is editable with no DisplayMemberPath, so what is picked lands in
/// the box as ToString() - the same arrangement as
/// <see cref="NavigatorTargetOption"/>. The note is only ever shown beside it.
/// </summary>
public sealed class GamePathOption : ISearchText
{
    public GamePathOption(GamePathExamples.Entry entry) => Entry = entry;

    public GamePathExamples.Entry Entry { get; }

    /// <summary>The path, as stored.</summary>
    public string Value => Entry.Path;

    /// <summary>The note beside it, in the editor's language.</summary>
    public string Note => Loc.T(Entry.NoteKey);

    /// <summary>The heading it is listed under.</summary>
    public string Group => Loc.T(Entry.GroupKey);

    public override string ToString() => Entry.Path;

    /// <summary>Path and note: "music" finds the music as surely as
    /// "12_AudioPlayer" does.</summary>
    public string SearchText => Entry.Path + "\n" + Note;
}
