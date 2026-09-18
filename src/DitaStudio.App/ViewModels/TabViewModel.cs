using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.App.Views;

namespace DitaStudio.App.ViewModels;

// Одна вкладка открытого документа. Заголовок вкладки (текст + точка
// несохранённости) считается одной строкой, как раньше BuildTabHeader.
public partial class TabViewModel : ObservableObject
{
    private readonly DocumentsViewModel _owner;

    public DocumentPane Pane { get; }

    // Не readonly: обновляется при переносе/переименовании файла
    // (MainWindow.Project.cs, OnProjectFileMove) — тот же смысл, что раньше
    // TabItem.Tag.
    public string FullPath { get; set; }

    [ObservableProperty]
    private string title;

    public TabViewModel(DocumentsViewModel owner, DocumentPane pane, string fullPath)
    {
        _owner = owner;
        Pane = pane;
        FullPath = fullPath;
        title = TitleFor(pane);
    }

    public void RefreshTitle() => Title = TitleFor(Pane);

    private static string TitleFor(DocumentPane pane) =>
        (pane.IsDirty ? "• " : string.Empty) +
        (pane.FilePath is null ? pane.Title : Path.GetFileName(pane.FilePath));

    [RelayCommand]
    private void Close() => _owner.CloseTab(this);
}
