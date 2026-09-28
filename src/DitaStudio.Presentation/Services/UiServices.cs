using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Presentation.Services;

/// <summary>Всё, что оболочка (WPF или Avalonia) даёт общим ViewModel'ям.</summary>
/// <param name="CreateDocumentView">Создаёт контрол вкладки для открытого документа.</param>
public sealed record UiServices(
    IDialogService Dialogs,
    IFilePicker Files,
    IUiPlatform Platform,
    IPdfPrinter PdfPrinter,
    Func<DitaProject, DitaDocument, IDocumentView> CreateDocumentView);
