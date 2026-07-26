using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper over one <see cref="NpcTransform"/> — a placed NPC part's
/// position / rotation / scale. Bound directly by the placement editor and,
/// later, driven by the transform gizmo. A shared <c>Changed</c> hook lets the
/// owning placement re-raise its display / preview when any channel moves.
/// </summary>
public sealed class NpcTransformViewModel : ObservableObject
{
    public NpcTransform Model { get; }
    private readonly System.Action? _changed;

    public NpcTransformViewModel(NpcTransform model, System.Action? changed = null)
    {
        Model = model;
        _changed = changed;
    }

    public float X { get => Model.X; set { Model.X = value; OnPropertyChanged(); _changed?.Invoke(); } }
    public float Y { get => Model.Y; set { Model.Y = value; OnPropertyChanged(); _changed?.Invoke(); } }
    public float Z { get => Model.Z; set { Model.Z = value; OnPropertyChanged(); _changed?.Invoke(); } }
    public float RotX { get => Model.RotX; set { Model.RotX = value; OnPropertyChanged(); _changed?.Invoke(); } }
    public float RotY { get => Model.RotY; set { Model.RotY = value; OnPropertyChanged(); _changed?.Invoke(); } }
    public float RotZ { get => Model.RotZ; set { Model.RotZ = value; OnPropertyChanged(); _changed?.Invoke(); } }
    public float ScaleX { get => Model.ScaleX; set { Model.ScaleX = value; OnPropertyChanged(); _changed?.Invoke(); } }
    public float ScaleY { get => Model.ScaleY; set { Model.ScaleY = value; OnPropertyChanged(); _changed?.Invoke(); } }
    public float ScaleZ { get => Model.ScaleZ; set { Model.ScaleZ = value; OnPropertyChanged(); _changed?.Invoke(); } }
}
