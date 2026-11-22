using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// A node's 2D light for the editor (1.10.0): the preview window's light panel
/// and handles, and the Places tab's node editor, edit it through here. Every
/// change goes to the model at once and tells the node, which redraws the
/// preview and re-reads whether a bound light now differs from the game's.
/// Values are kept to what the engine takes: radii and angles inside their
/// outer ones, the falloff and glow in 0..1.
/// </summary>
public sealed class LightViewModel : ObservableObject
{
    private readonly Action _edited;

    public LightDef Model { get; }

    public LightViewModel(LightDef model, Action edited)
    {
        Model = model;
        _edited = edited;
    }

    private void Changed(params string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
        _edited();
    }

    /// <summary>The shape's choices, in the order the panel lists them.</summary>
    public static IReadOnlyList<string> Shapes { get; } = new[] { LightDef.ShapeRound, LightDef.ShapeFree, LightDef.ShapePicture, LightDef.ShapeAmbient };

    public string Shape
    {
        get => Model.IsFree ? LightDef.ShapeFree : Model.IsPicture ? LightDef.ShapePicture : Model.IsAmbient ? LightDef.ShapeAmbient : LightDef.ShapeRound;
        set
        {
            string v = Shapes.FirstOrDefault(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) ?? LightDef.ShapeRound;
            if (v == Shape) return;
            Model.Shape = v;
            // A light turned free starts as a square the size of the round one it was.
            if (Model.IsFree && Model.Points.Count < 3)
                Model.Points = LightDef.Square(Math.Max(0.25f, Model.OuterRadius * 0.5f));
            Changed(nameof(Shape), nameof(IsRound), nameof(IsFree), nameof(IsPicture), nameof(IsAmbient), nameof(Points));
        }
    }

    public bool IsAmbient => Model.IsAmbient;
    public bool IsRound => Model.IsRound;
    public bool IsFree => Model.IsFree;
    public bool IsPicture => Model.IsPicture;

    /// <summary>"#RRGGBB".</summary>
    public string Color
    {
        get => LightDef.NormalColor(Model.Color);
        set
        {
            var v = LightDef.NormalColor(value);
            if (v == Color) return;
            Model.Color = v;
            Changed(nameof(Color));
        }
    }

    public float Strength
    {
        get => Model.Strength;
        set { float v = Math.Max(0f, value); if (v == Model.Strength) return; Model.Strength = v; Changed(nameof(Strength)); }
    }

    public float Falloff
    {
        get => Model.Falloff;
        set { float v = Math.Clamp(value, 0f, 1f); if (v == Model.Falloff) return; Model.Falloff = v; Changed(nameof(Falloff)); }
    }

    public float Glow
    {
        get => Model.Glow;
        set { float v = Math.Clamp(value, 0f, 1f); if (v == Model.Glow) return; Model.Glow = v; Changed(nameof(Glow)); }
    }

    /// <summary>The smallest a round light's reach can be: URP divides by it.</summary>
    public const float MinRadius = 0.01f;

    public float OuterRadius
    {
        get => Model.OuterRadius;
        set
        {
            float v = Math.Max(MinRadius, value);
            if (v == Model.OuterRadius) return;
            Model.OuterRadius = v;
            if (Model.InnerRadius > v) Model.InnerRadius = v;
            Changed(nameof(OuterRadius), nameof(InnerRadius));
        }
    }

    public float InnerRadius
    {
        get => Model.InnerRadius;
        set
        {
            // Never quite the outer: URP's inner multiplier is 1 / (1 - inner/outer).
            float v = Math.Clamp(value, 0f, Model.OuterRadius * 0.999f);
            if (v == Model.InnerRadius) return;
            Model.InnerRadius = v;
            Changed(nameof(InnerRadius));
        }
    }

    public float OuterAngle
    {
        get => Model.OuterAngle;
        set
        {
            float v = Math.Clamp(value, 1f, 360f);
            if (v == Model.OuterAngle) return;
            Model.OuterAngle = v;
            if (Model.InnerAngle > v) Model.InnerAngle = v;
            Changed(nameof(OuterAngle), nameof(InnerAngle), nameof(IsCone));
        }
    }

    public float InnerAngle
    {
        get => Model.InnerAngle;
        set
        {
            float v = Math.Clamp(value, 0f, Model.OuterAngle);
            if (v == Model.InnerAngle) return;
            Model.InnerAngle = v;
            Changed(nameof(InnerAngle));
        }
    }

    /// <summary>A round light narrowed to a cone.</summary>
    public bool IsCone => Model.IsRound && Model.OuterAngle < 360f;

    public float Edge
    {
        get => Model.Edge;
        set { float v = Math.Max(0f, value); if (v == Model.Edge) return; Model.Edge = v; Changed(nameof(Edge)); }
    }

    /// <summary>Light the characters too (see <see cref="LightDef.LightsCharacters"/>).</summary>
    public bool LightsCharacters
    {
        get => Model.LightsCharacters;
        set { if (value == Model.LightsCharacters) return; Model.LightsCharacters = value; Changed(nameof(LightsCharacters)); }
    }

    public string Picture
    {
        get => Model.Picture;
        set { var v = value ?? ""; if (v == Model.Picture) return; Model.Picture = v; Changed(nameof(Picture)); }
    }

    /// <summary>The free shape's points (read; the preview's handles change them
    /// through <see cref="SetPoint"/>, <see cref="InsertPoint"/> and <see cref="RemovePoint"/>).</summary>
    public IReadOnlyList<float[]> Points => Model.Points;

    public void SetPoint(int index, float x, float y)
    {
        if (index < 0 || index >= Model.Points.Count) return;
        Model.Points[index] = new[] { x, y };
        Changed(nameof(Points));
    }

    /// <summary>A new point before <paramref name="index"/> (at the end for Count).</summary>
    public void InsertPoint(int index, float x, float y)
    {
        Model.Points.Insert(Math.Clamp(index, 0, Model.Points.Count), new[] { x, y });
        Changed(nameof(Points));
    }

    /// <summary>Remove a point, never below a triangle.</summary>
    public bool RemovePoint(int index)
    {
        if (Model.Points.Count <= 3 || index < 0 || index >= Model.Points.Count) return false;
        Model.Points.RemoveAt(index);
        Changed(nameof(Points));
        return true;
    }

    /// <summary>Re-read everything after the model was replaced behind this view model's back (undo, a reset).</summary>
    public void RefreshAll()
        => Changed(nameof(Shape), nameof(IsRound), nameof(IsFree), nameof(IsPicture), nameof(Color), nameof(Strength),
                   nameof(Falloff), nameof(Glow), nameof(OuterRadius), nameof(InnerRadius), nameof(OuterAngle),
                   nameof(InnerAngle), nameof(IsCone), nameof(Edge), nameof(Picture), nameof(Points), nameof(LightsCharacters));
}
