namespace SMSModForge.ViewModel;

/// <summary>
/// A folder in one of the sidebars, renamed where it stands: F2, or Rename on
/// its right-click menu, turns its name into a box (the author, 1.7.0, taking
/// the Rename buttons off every tab's toolbar - they renamed only folders, and
/// with a record selected they did nothing at all, which read as broken).
/// <para/>
/// One interface over the four kinds of folder - the shared unit trees and the
/// dialogue, variable and integration ones - so one behaviour drives them all.
/// </summary>
public interface IRenamableFolder
{
    string Name { get; set; }

    /// <summary>Whether its name is being typed over, in place in the tree.</summary>
    bool IsRenaming { get; set; }
}
