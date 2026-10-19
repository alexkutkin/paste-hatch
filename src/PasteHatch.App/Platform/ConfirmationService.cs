using System;
using System.Windows.Forms;
using PasteHatch.Core;

namespace PasteHatch.Platform;

/// <summary>
/// Native Windows task dialog (<c>comctl32</c> TaskDialogIndirect) for the destructive Recycle Bin
/// confirmations and for error reporting. The destructive choice is a custom button and is never
/// the default; "Отмена" is both the default and the safe answer when the dialog is closed with
/// the title-bar X or Esc, per Microsoft UX guidance for destructive actions.
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
        var text = $"Корзина «{displayName}» и все её файлы будут отправлены в Корзину Windows, откуда их можно восстановить.";
        return ConfirmRecycle(heading: "Удалить корзину?", text: text, pathToReveal: directoryPath);
    }

    /// <inheritdoc />
    public bool ConfirmRecycleMaterial(string displayName)
    {
        var text = $"Файл «{displayName}» будет отправлен в Корзину Windows.";
        return ConfirmRecycle(heading: "Удалить файл?", text: text, pathToReveal: null);
    }

    /// <inheritdoc />
    public void ShowError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var page = new TaskDialogPage
        {
            Caption = Caption,
            Heading = "Ошибка",
            Text = message,
            Icon = TaskDialogIcon.Error,
            Buttons = { TaskDialogButton.OK },
            DefaultButton = TaskDialogButton.OK,
        };
        Show(page);
    }

    private bool ConfirmRecycle(string heading, string text, string? pathToReveal)
    {
        var cancelButton = new TaskDialogButton("Отмена");
        var recycleButton = new TaskDialogButton("В Корзину Windows");

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
                CollapsedButtonText = "Показать путь",
                ExpandedButtonText = "Скрыть путь",
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
