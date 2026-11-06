using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using PasteHatch.Core;
using PasteHatch.Ui;

namespace PasteHatch;

public sealed partial class MainWindow
{
    private readonly PreviewLoader _preview = new();

    private void ShowPreview(EntryNode? node)
    {
        _preview.Cancel();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewText.Text = string.Empty;
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Text = Localization.Text.Get("Preview_Placeholder");
        PreviewPlaceholder.Visibility = Visibility.Visible;

        if (node is null)
        {
            PreviewTitle.Text = string.Empty;
            PreviewMeta.Text = string.Empty;
            return;
        }

        PreviewTitle.Text = node.Name;
        PreviewMeta.Text = node.IsFolder
            ? Localization.Text.Get("Kind_Folder")
            : $"{node.TypeLabel} · {node.SizeText} · {node.Entry.Modified.LocalDateTime.ToString("g", Localization.Culture)}";

        if (node.IsFolder)
        {
            PreviewPlaceholder.Text = Localization.Text.Get(
                FileTree.SelectedNode?.IsExpanded == true
                    ? "Preview_FolderExpanded"
                    : "Preview_FolderCollapsed");
            return;
        }

        if (node.Entry.Kind is not (MaterialKind.Image or MaterialKind.Text or MaterialKind.Link))
        {
            PreviewPlaceholder.Text = Localization.Text.Get("Preview_Unsupported");
            return;
        }

        _ = LoadPreviewAsync(node);
    }

    private async Task LoadPreviewAsync(EntryNode node)
    {
        try
        {
            var content = await _preview.LoadAsync(node.FullPath, node.Entry.Kind);
            if (content is null)
            {
                return;
            }

            if (content.Image is not null)
            {
                var scale = PreviewImage.XamlRoot?.RasterizationScale ?? 1;
                PreviewImage.MaxWidth = content.Image.PixelWidth / scale;
                PreviewImage.MaxHeight = content.Image.PixelHeight / scale;
                PreviewImage.Source = content.Image;
                PreviewImage.Visibility = Visibility.Visible;
            }
            else
            {
                PreviewText.Text = content.Text ?? string.Empty;
                PreviewText.Visibility = Visibility.Visible;
            }

            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            PreviewPlaceholder.Text = Localization.Text.Format(
                node.Entry.Kind == MaterialKind.Image ? "Preview_ImageError" : "Preview_ReadError", ex.Message);
        }
    }
}
