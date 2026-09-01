namespace SMSModForge.Services;

/// <summary>
/// True while the editor is being driven by the test harness rather than by a
/// person.
/// <para/>
/// The harness builds real windows, opens real packs and edits them, and it
/// runs on the machine of whoever is working on the editor — so anything the
/// editor does ON THE WAY OUT lands on that person. Closing a window with an
/// edited pack put a modal "save before closing?" on their screen; closing it
/// at all wrote the harness's untouched pane proportions over the ones they had
/// dragged into place. Neither is the test's business.
/// <para/>
/// Set once, on the harness's UI thread, before the first window exists. False
/// in the shipped editor, where nothing reads it that a person would notice.
/// <para/>
/// Deliberately one switch rather than a flag per problem: the three places
/// that consult it are all the same thing — the editor is being operated by a
/// program, so the parts that exist to serve a person should stand down.
/// </summary>
public static class TestMode
{
    public static bool Active { get; set; }
}
