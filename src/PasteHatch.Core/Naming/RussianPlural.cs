namespace PasteHatch.Core;

/// <summary>Russian plural forms used by the counts in the UI ("3 файла", "5 файлов").</summary>
public static class RussianPlural
{
    public static string Files(int count) => $"{count} {Noun(count, "файл", "файла", "файлов")}";

    public static string Items(int count) => $"{count} {Noun(count, "элемент", "элемента", "элементов")}";

    public static string Baskets(int count) => $"{count} {Noun(count, "корзина", "корзины", "корзин")}";

    public static string Noun(int count, string one, string few, string many)
    {
        var lastTwo = Math.Abs(count) % 100;
        var last = Math.Abs(count) % 10;

        if (lastTwo is >= 11 and <= 14)
        {
            return many;
        }

        return last switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }
}
