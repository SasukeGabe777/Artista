using Artista.Core.ColorEngine;
using Artista.Core.Imaging;

namespace Artista.Tests;

public class ColorEngineTests
{
    [Fact]
    public void IdenticalColorsHaveZeroDistance()
    {
        Assert.Equal(0f, OkLab.Distance(120, 60, 200, 120, 60, 200), 5);
    }

    [Fact]
    public void BlackToWhiteDistanceIsAboutOne()
    {
        float d = OkLab.Distance(0, 0, 0, 255, 255, 255);
        Assert.InRange(d, 0.95f, 1.05f);
    }

    [Fact]
    public void PerceptuallyCloserColorsHaveSmallerDistance()
    {
        // Two similar reds vs red-to-green.
        float near = OkLab.Distance(255, 0, 0, 240, 10, 10);
        float far = OkLab.Distance(255, 0, 0, 0, 255, 0);
        Assert.True(near < far);
    }

    [Fact]
    public void ToleranceZeroRemovesExactMatchOnly()
    {
        var matcher = new ColorMatcher(100, 150, 200, tolerance: 0, softness: 0);
        Assert.Equal(1f, matcher.Match(ColorBgra.Pack(200, 150, 100, 255)));
        // One-off color must NOT match at tolerance 0.
        Assert.Equal(0f, matcher.Match(ColorBgra.Pack(201, 150, 100, 255)));
    }

    [Fact]
    public void ToleranceExpandsMatchRange()
    {
        var strict = new ColorMatcher(100, 150, 200, 5, 0);
        var loose = new ColorMatcher(100, 150, 200, 60, 0);
        uint similar = ColorBgra.Pack(190, 140, 90, 255); // slightly different
        Assert.Equal(1f, loose.Match(similar));
        // The strict matcher may or may not match 'similar', but a very
        // different color must not match either matcher fully.
        uint different = ColorBgra.Pack(0, 255, 0, 255);
        Assert.Equal(0f, strict.Match(different));
    }

    [Fact]
    public void SoftnessCreatesPartialMatchBand()
    {
        var hard = new ColorMatcher(128, 128, 128, 10, 0);
        var soft = new ColorMatcher(128, 128, 128, 10, 100);

        // Walk away from gray and find a color that soft-matches partially.
        bool foundPartial = false;
        for (int delta = 1; delta < 120; delta += 2)
        {
            byte v = (byte)(128 + delta);
            float f = soft.Match(ColorBgra.Pack(v, v, v, 255));
            if (f > 0f && f < 1f)
            {
                foundPartial = true;
                // The hard matcher must not partially match anything.
                float fh = hard.Match(ColorBgra.Pack(v, v, v, 255));
                Assert.True(fh == 0f || fh == 1f);
                break;
            }
        }
        Assert.True(foundPartial, "softness should produce a partial-match band");
    }

    [Fact]
    public void RemoveFromPreservesPartialTransparencyProportionally()
    {
        var matcher = new ColorMatcher(255, 0, 0, 0, 0);
        uint halfRed = ColorBgra.Pack(0, 0, 255, 128);
        uint removed = matcher.RemoveFrom(halfRed);
        Assert.Equal(0, ColorBgra.A(removed));

        // Non-matching pixel untouched.
        uint blue = ColorBgra.Pack(255, 0, 0, 128);
        Assert.Equal(blue, matcher.RemoveFrom(blue));
    }

    [Fact]
    public void RemoveFromWithHalfStrengthHalvesAlpha()
    {
        var matcher = new ColorMatcher(255, 0, 0, 0, 0);
        uint red = ColorBgra.Pack(0, 0, 255, 200);
        uint removed = matcher.RemoveFrom(red, 0.5f);
        Assert.InRange<int>(ColorBgra.A(removed), 99, 101);
    }
}
