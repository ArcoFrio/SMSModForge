using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// An object of the place moved by its mask (after 1.10.0, the author: "a way to add masks to other GOs in the place.
/// We should be able to choose between the jiggle and level masks, and edit like we'd edit either"), as the game moves
/// it (PlaceFactory.ApplyObjectMask): its own mask through the level's pass (the place layers' Milking) or the
/// characters' jiggle (an NPC's), or the back layer's mask read over its own picture. While the mask studio paints
/// it, the strokes show at once. Objects' masks were not drawn moving in the preview at all before.
/// </summary>
public sealed partial class PlacePreview
{
    private void ShadeObject(GameObjectViewModel o, Image img, string artAbs, string root, Matrix toCanvas)
    {
        string Abs(string rel) => Path.Combine(root, Normalize(rel));
        bool own = o.LiveMask != null || !string.IsNullOrWhiteSpace(o.Mask);
        switch (o.MaskKind)
        {
            case GameObjectDef.MaskLevel when own:
                TrackLevelShader(img, artAbs, Abs(o.Mask), o.LiveMask, anyImage: true);
                break;
            case GameObjectDef.MaskJiggle when own:
                TrackShader(img, artAbs, Abs(o.Mask), o.Model.Jiggle ??= NpcDef.NewNpcJiggle(), toCanvas, o.LiveMask);
                break;
            case GameObjectDef.MaskBackLayer when LiveSecondaryMaskBgra != null || !string.IsNullOrWhiteSpace(SecondaryMaskSprite):
                TrackLevelShader(img, artAbs, Abs(SecondaryMaskSprite), LiveSecondaryMaskBgra, anyImage: true);
                break;
        }
    }
}
