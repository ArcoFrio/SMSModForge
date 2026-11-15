using System.Collections.Generic;
using SMSModForge.Services;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Undo keeps its steps as UTF-8 (1.9.0, half the memory). Undo and redo must
/// hand back exactly the text each step was - every letter, and a text UTF-8
/// cannot hold (a lone half of a pair) kept as it was.
/// </summary>
public sealed class UndoKeepsExactTextTests
{
    [Fact]
    public void UndoAndRedoGiveBackExactlyWhatWasKept()
    {
        var states = new List<string>
        {
            "{ \"a\": 1 }",
            "{ \"a\": \"你好 😀 é\" }",
            "{ \"a\": \"half \uD83D alone\" }",       // a lone high surrogate: not UTF-8
            "{ \"a\": \"\uDE00 low first\" }",
            "{ \"a\": \"plain again\" }",
        };
        int now = 0;
        var undo = new UndoService(() => states[now]);
        var restored = new List<string>();
        undo.RestoreRequested += s => restored.Add(s);
        undo.Reset();
        for (now = 1; now < states.Count; now++) undo.Checkpoint();
        now = states.Count - 1;

        for (int i = states.Count - 2; i >= 0; i--) undo.Undo();
        Assert.Equal(new[] { states[3], states[2], states[1], states[0] }, restored);
        restored.Clear();
        for (int i = 1; i < states.Count; i++) undo.Redo();
        Assert.Equal(new[] { states[1], states[2], states[3], states[4] }, restored);

        // The control: the lone halves really are what they look like, so the
        // texts kept as they are were exercised.
        Assert.True(char.IsHighSurrogate(states[2][13]));
        Assert.Throws<System.Text.EncoderFallbackException>(
            () => new System.Text.UTF8Encoding(false, true).GetBytes(states[2]));
    }
}
