namespace SMSModForge.ViewModel;

/// <summary>
/// One entry in a labelled-key combo box (navigator targets, vanilla source
/// picker, dialogue roomtalk picker, actor default-bust picker, …).
/// <see cref="Token"/> is the wire-format string written into the manifest
/// (e.g. <c>"vanilla:14_Beach"</c>, <c>"self:beachRoom"</c>, or a bust GO
/// name like <c>"Anna_YellowSexy"</c>); <see cref="DisplayLabel"/> is the
/// human-readable text rendered in the dropdown.
/// <para/>
/// <see cref="ToString"/> deliberately returns <see cref="Token"/>, not
/// <see cref="DisplayLabel"/>. The combo boxes in MainWindow.xaml all use
/// <c>IsEditable="True"</c> with both <c>SelectedValue</c> and <c>Text</c>
/// two-way bound to the same backing property; without a <c>DisplayMemberPath</c>
/// the ComboBox sets <c>Text = SelectedItem.ToString()</c> after picking,
/// which round-trips the Token cleanly. Friendly labels still appear in
/// the dropdown via each ComboBox's <c>ItemTemplate</c> — see the
/// <c>BustNameOptions</c> / <c>RoomTalkOptions</c> / <c>AllTargetOptions</c>
/// combos in MainWindow.xaml.
/// </summary>
public sealed record NavigatorTargetOption(string Token, string DisplayLabel) : ISearchText
{
    public override string ToString() => Token;

    /// <summary>Both halves: the label is what the list shows and so what a
    /// person types, and the token is what the box shows once one is chosen.</summary>
    public string SearchText => Token + "\n" + DisplayLabel;
}

/// <summary>
/// The text a dropdown's search matches an option against, when that is not
/// simply what <c>ToString</c> returns.
/// <para/>
/// Needed wherever an option stores one thing and shows another. Searching
/// only the stored half meant typing a word from the list found nothing - a
/// quest listed as "Into the Dark (Astrid)" is stored as "Astrid Quest" - so
/// the search looked absent rather than merely picky.
/// </summary>
public interface ISearchText
{
    string SearchText { get; }
}
