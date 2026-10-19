namespace PasteHatch.Core;

/// <summary>An open basket: one folder under the baskets root plus its display name.</summary>
public sealed record BasketTab(string Uid, string DisplayName, string DirectoryPath);
