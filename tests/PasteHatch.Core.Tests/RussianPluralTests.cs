namespace PasteHatch.Core.Tests;

public sealed class RussianPluralTests
{
    [Theory]
    [InlineData(0, "файлов")]
    [InlineData(1, "файл")]
    [InlineData(2, "файла")]
    [InlineData(3, "файла")]
    [InlineData(4, "файла")]
    [InlineData(5, "файлов")]
    [InlineData(11, "файлов")]
    [InlineData(12, "файлов")]
    [InlineData(14, "файлов")]
    [InlineData(21, "файл")]
    [InlineData(22, "файла")]
    [InlineData(25, "файлов")]
    [InlineData(100, "файлов")]
    [InlineData(101, "файл")]
    [InlineData(111, "файлов")]
    public void Files_UsesRussianPluralForm(int count, string expectedNoun)
    {
        Assert.Equal($"{count} {expectedNoun}", RussianPlural.Files(count));
    }

    [Theory]
    [InlineData(1, "корзина")]
    [InlineData(2, "корзины")]
    [InlineData(3, "корзины")]
    [InlineData(5, "корзин")]
    [InlineData(11, "корзин")]
    [InlineData(21, "корзина")]
    [InlineData(22, "корзины")]
    public void Baskets_UsesRussianPluralForm(int count, string expectedNoun)
    {
        Assert.Equal($"{count} {expectedNoun}", RussianPlural.Baskets(count));
    }

    [Theory]
    [InlineData(1, "день")]
    [InlineData(3, "дня")]
    [InlineData(5, "дней")]
    public void Noun_UsesCustomWords(int count, string expectedNoun)
    {
        Assert.Equal(expectedNoun, RussianPlural.Noun(count, "день", "дня", "дней"));
    }
}
