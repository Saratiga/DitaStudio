using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Одна вкладка открытого документа. Заголовок вкладки (текст + точка
// несохранённости) считается одной строкой, как раньше BuildTabHeader.
public partial class TabViewModel : ObservableObject
{
    private readonly DocumentsViewModel _owner;

    public IDocumentView Pane { get; }

    // Не readonly: обновляется при переносе/переименовании файла
    // (MainWindow.Project.cs, OnProjectFileMove) — тот же смысл, что раньше
    // TabItem.Tag.
    public string FullPath { get; set; }

    [ObservableProperty]
    private string title;

    public TabViewModel(DocumentsViewModel owner, IDocumentView pane, string fullPath)
    {
        _owner = owner;
        Pane = pane;
        FullPath = fullPath;
        title = TitleFor(pane);
    }

    public void RefreshTitle() => Title = TitleFor(Pane);

    private static string TitleFor(IDocumentView pane) =>
        (pane.IsDirty ? "• " : string.Empty) +
        (pane.FilePath is null ? pane.Title : Path.GetFileName(pane.FilePath));

    [RelayCommand]
    private Task Close() => _owner.CloseTabAsync(this);
}
