namespace PasteHatch.Core.Tests;

public sealed class RussianPluralTests
{
    [Theory]
    [InlineData(0, PluralForm.Many)]
    [InlineData(1, PluralForm.One)]
    [InlineData(2, PluralForm.Few)]
    [InlineData(3, PluralForm.Few)]
    [InlineData(4, PluralForm.Few)]
    [InlineData(5, PluralForm.Many)]
    [InlineData(11, PluralForm.Many)]
    [InlineData(12, PluralForm.Many)]
    [InlineData(14, PluralForm.Many)]
    [InlineData(21, PluralForm.One)]
    [InlineData(22, PluralForm.Few)]
    [InlineData(25, PluralForm.Many)]
    [InlineData(100, PluralForm.Many)]
    [InlineData(101, PluralForm.One)]
    [InlineData(111, PluralForm.Many)]
    [InlineData(121, PluralForm.One)]
    public void Form_FollowsRussianPluralRules(int count, PluralForm expected)
    {
        Assert.Equal(expected, RussianPlural.Form(count));
    }
}
