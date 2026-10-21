namespace PasteHatch.Core.Tests;

internal sealed class FakeConfirmationService : IConfirmationService
{
    public bool ConfirmBasketResult { get; set; } = true;

    public bool ConfirmMaterialResult { get; set; } = true;

    public bool ConfirmFolderResult { get; set; } = true;

    public List<(string DisplayName, string DirectoryPath)> BasketConfirmations { get; } = [];

    public List<string> MaterialConfirmations { get; } = [];

    public List<string> FolderConfirmations { get; } = [];

    public List<string> Errors { get; } = [];

    public bool ConfirmRecycleBasket(string displayName, string directoryPath)
    {
        BasketConfirmations.Add((displayName, directoryPath));
        return ConfirmBasketResult;
    }

    public bool ConfirmRecycleMaterial(string displayName)
    {
        MaterialConfirmations.Add(displayName);
        return ConfirmMaterialResult;
    }

    public bool ConfirmRecycleFolder(string displayName)
    {
        FolderConfirmations.Add(displayName);
        return ConfirmFolderResult;
    }

    public void ShowError(string message) => Errors.Add(message);
}
