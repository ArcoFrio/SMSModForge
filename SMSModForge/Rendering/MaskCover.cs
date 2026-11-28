namespace SMSModForge.Rendering;

/// <summary>
/// What covers a layer being painted in the mask studio, on the mask's 256x256 grid (after 1.10.0, the author: a
/// place's front mask painted where nothing showed it moving - in SMSAndroids' bathroom 43% of the front picture is
/// under the object BathroomNear). <see cref="Covered"/>: a picture drawn in front of the layer hides that part of it,
/// in the preview and in the game; <see cref="By"/>: the name of the topmost one.
/// </summary>
public sealed class MaskCover
{
    public bool[] Covered { get; }
    public string?[] By { get; }

    public MaskCover(bool[] covered, string?[] by)
    {
        Covered = covered;
        By = by;
    }

    /// <summary>How much of the layer is covered, 0 to 1.</summary>
    public double Share
    {
        get
        {
            int n = 0;
            foreach (bool c in Covered) if (c) n++;
            return Covered.Length == 0 ? 0 : (double)n / Covered.Length;
        }
    }
}
