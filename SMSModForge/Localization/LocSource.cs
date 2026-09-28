using System;
using System.ComponentModel;
using System.Windows.Data;

namespace SMSModForge.Localization;

/// <summary>
/// Every text a window takes from <c>{l:T key}</c>, as something a binding can
/// watch: <c>LocSource.Instance["quests.addQuest"]</c>. When the language on
/// screen changes, it says so once and every one of those texts reads itself
/// again - which is what lets the editor change language without starting
/// again.
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource()
    {
        Loc.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}
