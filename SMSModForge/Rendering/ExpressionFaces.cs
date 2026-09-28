using System;
using System.Collections.Generic;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// Which face a line's expression shows, decided the way the game decides it
/// (<c>ActorRegistry.RouteExpression</c> in the plugin), so a picture in the
/// editor shows what the player will see.
/// <para/>
/// A line names an expression by its KEY. The speaker's expression list turns
/// a key into the name of a FACE - the art <c>{prefix}{face}.PNG</c>, or a
/// texture slot on one of the game's busts - and a row naming no face is
/// exactly what <c>neutral</c> is: no face showing. A key the list does not
/// have is taken as the face's own name, which is how the game's four -
/// Happy, Angry, Sad, Flirty - work without being listed.
/// <para/>
/// The dialogue preview used to look the key up as though it were the face,
/// so a row whose face was named differently from its key showed nothing in
/// the editor and the right face in the game.
/// </summary>
public static class ExpressionFaces
{
    /// <summary>The face <paramref name="key"/> shows on a speaker who declares
    /// <paramref name="declared"/>; "" for none.</summary>
    public static string FaceFor(IEnumerable<ActorExpressionDef>? declared, string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        string? face = null;
        if (declared != null)
            foreach (var e in declared)
                // Last one wins, as it does in the game's map of them.
                if (e != null && string.Equals(e.Key, key, StringComparison.Ordinal))
                    face = e.ExpressionGoName ?? "";
        return face ?? key;
    }
}
