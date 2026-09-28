using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace SMSModForge.Localization;

/// <summary>
/// A text in a window's layout: <c>Content="{l:T quests.addQuest}"</c>. XAML finds
/// <c>l:T</c> by this name.
/// <para/>
/// Wherever a binding can go it IS one, to <see cref="LocSource"/>, so the text
/// changes when the language does - the editor switches language without
/// starting again. That is every property of an element, in a template or
/// not, and a style's setter, which hands the binding on to each element it
/// styles.
/// <para/>
/// Anywhere else it is a plain string, read once in whatever language is up
/// then: a binding's own StringFormat, a trigger's Value. None of the
/// editor's layouts puts one there - a count's sentence goes through
/// <see cref="FormatConverter"/> or <see cref="PluralConverter"/> instead,
/// which do follow the language.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var target = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (target?.TargetObject is SetterBase)
            return Live();
        if (target?.TargetProperty is DependencyProperty)
            return Live().ProvideValue(serviceProvider!);
        return Loc.T(Key);
    }

    private Binding Live() => new("[" + Key + "]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };
}
