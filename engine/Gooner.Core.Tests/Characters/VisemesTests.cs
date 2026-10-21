using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// The mouth while a line is spoken: one shape per letter, the vowels open in their own shape, the rest
// closed, a breath after the end of a sentence. There is no voice yet, so the text is the timing.
public sealed class VisemesTests
{
    private const double Step = 1.0 / Visemes.LettersPerSecond;

    [Fact]
    public void vowels_open_the_mouth_in_their_shape()
    {
        var shapes = Visemes.Timeline("aeiou");

        Assert.Equal(["aa", "ee", "ih", "oh", "ou"], shapes.Select(s => s.Viseme));
        Assert.Equal(Step, shapes[1].At, 6);
    }

    [Fact]
    public void consonants_and_spaces_close_it()
    {
        var shapes = Visemes.Timeline("m a");

        Assert.Equal([null, null, "aa"], shapes.Select(s => s.Viseme));
    }

    [Fact]
    public void accented_vowels_and_umlauts_count_as_their_vowel()
    {
        Assert.Equal(["ee", "ou", "aa", "oh"], Visemes.Timeline("éüäÔ").Select(s => s.Viseme));
    }

    [Fact]
    public void the_end_of_a_sentence_takes_a_breath()
    {
        var shapes = Visemes.Timeline("a. a");

        // a, then the full stop and its breath, then the space: the second a comes after all three.
        Assert.Equal(Step * (3 + Visemes.BreathLetters), shapes[3].At, 6);
        Assert.Equal(Visemes.Duration("a. a"), shapes[3].At + Step, 6);
    }

    [Fact]
    public void nothing_to_say_is_no_time() => Assert.Equal(0, Visemes.Duration(""));
}
