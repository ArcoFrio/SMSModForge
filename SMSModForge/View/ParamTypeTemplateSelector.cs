using System.Windows;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.ViewModel;

namespace SMSModForge.View;

/// <summary>
/// Picks the right <see cref="DataTemplate"/> for a
/// <see cref="ParamRowViewModel"/> based on its <see cref="ParamType"/>.
/// One template per enum value plus a fallback to <see cref="StringTemplate"/>
/// for unrecognised inputs — so adding a new param type means: declare the
/// template, declare the matching property on this class, extend
/// <see cref="SelectTemplate"/>.
/// <para/>
/// All template properties are wired up in MainWindow.xaml where the
/// selector is instantiated. The properties are nullable because XAML
/// fills them via property-element syntax — the selector returns the
/// fallback string template (or null → ContentPresenter shows nothing)
/// when an unset template would otherwise be requested.
/// </summary>
public sealed class ParamTypeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StringTemplate { get; set; }
    public DataTemplate? BoolTemplate { get; set; }
    public DataTemplate? IntTemplate { get; set; }
    public DataTemplate? FloatTemplate { get; set; }
    public DataTemplate? PercentTemplate { get; set; }
    public DataTemplate? PackVarRefTemplate { get; set; }
    public DataTemplate? ListVarRefTemplate { get; set; }
    public DataTemplate? GameVarRefTemplate { get; set; }
    public DataTemplate? LevelRefTemplate { get; set; }
    public DataTemplate? ActorRefTemplate { get; set; }
    public DataTemplate? BustRefTemplate { get; set; }
    public DataTemplate? ExpressionRefTemplate { get; set; }
    public DataTemplate? SceneRefTemplate { get; set; }
    public DataTemplate? SignalRefTemplate { get; set; }
    public DataTemplate? MusicRefTemplate { get; set; }
    public DataTemplate? SfxRefTemplate { get; set; }
    public DataTemplate? GameObjectPathTemplate { get; set; }
    public DataTemplate? ChoiceTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ParamRowViewModel row)
            return base.SelectTemplate(item, container);

        return row.Type switch
        {
            ParamType.Bool => BoolTemplate ?? StringTemplate,
            ParamType.Int => IntTemplate ?? StringTemplate,
            ParamType.Float => FloatTemplate ?? StringTemplate,
            ParamType.Percent => PercentTemplate ?? IntTemplate ?? StringTemplate,
            ParamType.PackVarRef => PackVarRefTemplate ?? StringTemplate,
            ParamType.ListVarRef => ListVarRefTemplate ?? StringTemplate,
            ParamType.GameVarRef => GameVarRefTemplate ?? StringTemplate,
            ParamType.LevelRef => LevelRefTemplate ?? StringTemplate,
            ParamType.ActorRef => ActorRefTemplate ?? StringTemplate,
            ParamType.BustRef => BustRefTemplate ?? StringTemplate,
            ParamType.ExpressionRef => ExpressionRefTemplate ?? StringTemplate,
            ParamType.SceneRef => SceneRefTemplate ?? StringTemplate,
            ParamType.SignalRef => SignalRefTemplate ?? StringTemplate,
            ParamType.MusicRef => MusicRefTemplate ?? StringTemplate,
            ParamType.SfxRef => SfxRefTemplate ?? StringTemplate,
            ParamType.GameObjectPath => GameObjectPathTemplate ?? StringTemplate,
            ParamType.Choice => ChoiceTemplate ?? StringTemplate,
            _ => StringTemplate,
        };
    }
}
