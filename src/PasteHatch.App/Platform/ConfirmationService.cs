using System;
using System.Windows.Forms;
using PasteHatch.Core;

namespace PasteHatch.Platform;

/// <summary>
/// Native Windows task dialog (<c>comctl32</c> TaskDialogIndirect) for the destructive Recycle Bin
/// confirmations and for error reporting. The destructive choice is a custom button and is never
/// the default; the Cancel button is both the default and the safe answer when the dialog is
/// closed with the title-bar X or Esc, per Microsoft UX guidance for destructive actions.
/// </summary>
public sealed class ConfirmationService : IConfirmationService
{
    private const string Caption = "PasteHatch";
    private readonly Func<IntPtr> _ownerHandleProvider;

    /// <param name="ownerHandleProvider">
    /// Returns the HWND of the main window so the dialog is modal to it. Called each time a
    /// dialog is shown; may return <see cref="IntPtr.Zero"/> before the window exists.
    /// </param>
    public ConfirmationService(Func<IntPtr> ownerHandleProvider)
    {
        ArgumentNullException.ThrowIfNull(ownerHandleProvider);
        _ownerHandleProvider = ownerHandleProvider;
    }

    /// <inheritdoc />
    public bool ConfirmRecycleBasket(string displayName, string directoryPath)
    {
        var text = Localization.Text.Format("Confirm_DeleteBasketText", displayName);
        return ConfirmRecycle(
            heading: Localization.Text.Get("Confirm_DeleteBasketHeading"),
            text: text,
            pathToReveal: directoryPath);
    }

    /// <inheritdoc />
    public bool ConfirmRecycleMaterial(string displayName)
    {
        var text = Localization.Text.Format("Confirm_DeleteFileText", displayName);
        return ConfirmRecycle(
            heading: Localization.Text.Get("Confirm_DeleteFileHeading"),
            text: text,
            pathToReveal: null);
    }

    /// <inheritdoc />
    public bool ConfirmRecycleFolder(string displayName)
    {
        var text = Localization.Text.Format("Confirm_DeleteFolderText", displayName);
        return ConfirmRecycle(
            heading: Localization.Text.Get("Confirm_DeleteFolderHeading"),
            text: text,
            pathToReveal: null);
    }

    /// <inheritdoc />
    public void ShowError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = Localization.Text.Get("Error_Heading"),
            Text = message,
            Icon = TaskDialogIcon.Error,
            Buttons = { TaskDialogButton.OK },
            DefaultButton = TaskDialogButton.OK,
        };
        Show(page);
    }

    private bool ConfirmRecycle(string heading, string text, string? pathToReveal)
    {
        var cancelButton = new TaskDialogButton(Localization.Text.Get("Action_Cancel"));
        var recycleButton = new TaskDialogButton(Localization.Text.Get("Button_Recycle"));

        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Warning,
            Buttons = { cancelButton, recycleButton },
            DefaultButton = cancelButton,
        };

        if (!string.IsNullOrWhiteSpace(pathToReveal))
        {
            page.Expander = new TaskDialogExpander(pathToReveal)
            {
                CollapsedButtonText = Localization.Text.Get("Expander_ShowPath"),
                ExpandedButtonText = Localization.Text.Get("Expander_HidePath"),
            };
        }

        var clickedButton = Show(page);

        // A null result means the dialog was dismissed without picking a button (X/Esc);
        // that must never be interpreted as consent to delete.
        return ReferenceEquals(clickedButton, recycleButton);
    }

    private TaskDialogButton? Show(TaskDialogPage page)
    {
        var ownerHandle = _ownerHandleProvider();
        return ownerHandle == IntPtr.Zero
            ? TaskDialog.ShowDialog(page)
            : TaskDialog.ShowDialog(ownerHandle, page);
    }
}
