namespace PasteHatch.Core;

/// <summary>
/// Native confirmation and error dialogs. The brief requires a confirmation before a basket
/// or material is sent to the Windows Recycle Bin; Microsoft UX guidance asks for a modal
/// task dialog with the safest response as the default, which is what implementations use.
/// </summary>
public interface IConfirmationService
{
    /// <summary>Asks whether the basket folder and all its files may be sent to the Recycle Bin.</summary>
    bool ConfirmRecycleBasket(string displayName, string directoryPath);

    /// <summary>Asks whether a single material file may be sent to the Recycle Bin.</summary>
    bool ConfirmRecycleMaterial(string displayName);

    /// <summary>Shows a modal error dialog for a failed operation the user should know about.</summary>
    void ShowError(string message);
}
