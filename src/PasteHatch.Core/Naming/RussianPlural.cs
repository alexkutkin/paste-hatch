namespace PasteHatch.Core;

/// <summary>Russian plural categories used to pick the right counted phrase ("3 файла", "5 файлов").</summary>
public enum PluralForm
{
    One,
    Few,
    Many,
}

/// <summary>Russian plural rules: one for 1, few for 2–4, many for 0 and 5–20, 11–14 always many.</summary>
public static class RussianPlural
{
    public static PluralForm Form(int count)
    {
        var lastTwo = Math.Abs(count) % 100;
        var last = Math.Abs(count) % 10;

        if (lastTwo is >= 11 and <= 14)
        {
            return PluralForm.Many;
        }

        return last switch
        {
            1 => PluralForm.One,
            >= 2 and <= 4 => PluralForm.Few,
            _ => PluralForm.Many,
        };
    }
}
