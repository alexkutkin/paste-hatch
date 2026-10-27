namespace PasteHatch.Core.Tests;

public sealed class NaturalStringComparerTests
{
    [Fact]
    public void Compare_PutsNumberedNamesInNumericOrder()
    {
        var names = new[] { "file10.txt", "file2.txt", "file1.txt", "File3.txt" };

        var sorted = names.OrderBy(name => name, NaturalStringComparer.Instance).ToArray();

        Assert.Equal(["file1.txt", "file2.txt", "File3.txt", "file10.txt"], sorted);
    }

    [Fact]
    public void Compare_IgnoresLetterCase()
    {
        // An ordinal sort would rank "alpha" after "BETA"; the natural order compares case-insensitively.
        Assert.True(NaturalStringComparer.Instance.Compare("alpha", "BETA") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("BETA", "alpha") > 0);
    }

    [Fact]
    public void Compare_CaseVariants_TieBreakOnOrdinalOrder()
    {
        Assert.True(NaturalStringComparer.Instance.Compare("Alpha", "alpha") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("alpha", "Alpha") > 0);
    }

    [Fact]
    public void Compare_HandlesLeadingZeroesAndLargeNumbers()
    {
        var names = new[] { "item0002", "item2", "item10", "item99999999999999999999" };

        var sorted = names.OrderBy(name => name, NaturalStringComparer.Instance).ToArray();

        Assert.Equal(["item2", "item0002", "item10", "item99999999999999999999"], sorted);
    }

    [Fact]
    public void Compare_NullSortsBeforeAnyName()
    {
        Assert.True(NaturalStringComparer.Instance.Compare(null, "a") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("a", null) > 0);
    }
}
