using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Plugins;
using DitaStudio.Presentation.Services;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

// Оформление текста: размер, цвет, маркер, выравнивание, размещение на странице, нумерация, правки рецензента.
public partial class InsertViewModel
{
    [RelayCommand]
    private void TogglePageBreakBeforeTitle()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "title")
        {
            _shell.StatusText = Loc.T("Msg_SelectATitleForExampleA");
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass("page-break-before");
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? Loc.T("Msg_PageBreakBeforeTheTitleIs")
            : Loc.T("Msg_PageBreakBeforeTheTitleIs2");
    }

    /// <summary>Выравнивание текущего блока: align-left (по умолчанию — класс снимается), -center, -right, -justify.</summary>
    [RelayCommand]
    private void SetAlignment(string? token)
    {
        var author = _docs.Current?.Author;
        if (author?.CurrentNode is null)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInAParagraph2");
            return;
        }

        var value = token is null or "align-left" ? null : token;
        if (author.SetCurrentBlockFormat(TextFormatting.AlignPrefix, value))
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = Loc.T("Msg_Alignment") + TextFormatting.Alignments.First(a => a.Token == (value ?? "align-left")).Label.ToLowerInvariant() + ".";
        }
    }

    /// <summary>Размеры шрифта для списка на панели: «Обычный» и размеры в пт.</summary>
    public IReadOnlyList<string> FontSizes => new[] { NormalSize }.Concat(TextFormatting.Sizes.Select(s => s.ToString())).Append(CustomSize).ToList();

    /// <summary>Пункт списка размеров: спросить число пунктов (дробные — через точку или запятую).</summary>
    public static string CustomSize => Loc.T("Msg_Other");

    public static string NormalSize => Loc.T("Msg_Normal");

    /// <summary>Размер шрифта выделения или дальнейшего набора: "10" (пт) или «Обычный»/null — снять.</summary>
    [RelayCommand]
    private async Task SetFontSize(string? size)
    {
        if (size == CustomSize)
        {
            var typed = await _ui.Dialogs.PromptTextAsync(Loc.T("Msg_CustomFontSize"), Loc.T("Msg_SizePt"),
                (_docs.Current?.Author.CurrentNode is { } node && TextFormatting.SizeOf(node) is { } current ? current : 11).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                Loc.T("Msg_From00To10", TextFormatting.MinCustomSize, TextFormatting.MaxCustomSize));
            if (typed is null)
            {
                return;
            }

            if (!double.TryParse(typed.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var custom) ||
                custom < TextFormatting.MinCustomSize || custom > TextFormatting.MaxCustomSize)
            {
                _shell.StatusText = Loc.T("Msg_TheSizeMustBeANumber", TextFormatting.MinCustomSize, TextFormatting.MaxCustomSize);
                return;
            }

            size = custom.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }

        var parsed = double.TryParse(size?.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var points);
        var token = parsed ? TextFormatting.SizeToken(points) : null;
        ApplyTextFormat(TextFormatting.SizePrefix, token, token is null ? Loc.T("Msg_FontSizeCleared") : Loc.T("Msg_FontSize00Pt", TextFormatting.ParseSizeToken(token)));
    }

    /// <summary>Цвет выделения или дальнейшего набора: color-red… или null — снять.</summary>
    [RelayCommand]
    private void SetTextColor(string? token)
    {
        var color = TextFormatting.Colors.FirstOrDefault(c => c.Token == token);
        ApplyTextFormat(TextFormatting.ColorPrefix, color.Token,
            color.Token is null ? Loc.T("Msg_TextColorCleared") : Loc.T("Msg_TextColor0", color.Label.ToLowerInvariant()));
    }

    /// <summary>
    /// Маркер, как «Цвет выделения текста» в Word: есть выделение — закрашивается сразу; нет — включается кисть этого цвета (дальше
    /// выделение мышью красит текст, пока не нажат <c>Esc</c> или тот же значок). Параметр: <c>mark-red</c>…, <c>null</c> — «Нет цвета»
    /// (с выделением снимает маркер, без — «ластик»), <c>custom</c> — выбрать свой цвет в окне.
    /// </summary>
    [RelayCommand]
    private async Task SetMarker(string? token)
    {
        var author = _docs.Current?.Author;
        if (author is null)
        {
            _shell.StatusText = Loc.T("Msg_OpenADocumentAndSelectText");
            return;
        }

        if (token == CustomMarker)
        {
            var current = author.MarkerPenToken is { } pen ? TextFormatting.ParseMarkToken(pen) : null;
            var picked = await _ui.Dialogs.PickColorAsync(Loc.T("Msg_MarkerColor"), current);
            if (picked is null || TextFormatting.MarkToken(picked) is not { } custom)
            {
                return;
            }

            token = custom;
        }

        if (author.HasTextSelection)
        {
            ApplyTextFormat(TextFormatting.MarkPrefix, token, token is null ? Loc.T("Msg_MarkerRemoved") : Loc.T("Msg_Marker0", MarkerLabel(token)));
            return;
        }

        // Выделения нет — кисть: тот же цвет второй раз выключает её.
        if (author.MarkerPenActive && author.MarkerPenToken == token)
        {
            author.StopMarkerPen();
            _shell.StatusText = Loc.T("Msg_MarkerModeOff");
            return;
        }

        if (author.StartMarkerPen(token))
        {
            _shell.StatusText = token is null
                ? Loc.T("Msg_MarkerModeEraserSelectTextWith")
                : Loc.T("Msg_MarkerMode0SelectTextWith", MarkerLabel(token));
        }
        else
        {
            _shell.StatusText = Loc.T("Msg_TheMarkerIsNotAvailableHere");
        }
    }

    /// <summary>Параметр команды маркера «Другой цвет…».</summary>
    public const string CustomMarker = "custom";

    private static string MarkerLabel(string token) =>
        TextFormatting.Marks.FirstOrDefault(m => m.Token == token) is { Label: not null } named
            ? named.Label.ToLowerInvariant()
            : TextFormatting.ParseMarkToken(token) ?? token;

    private void ApplyTextFormat(string prefix, string? token, string done)
    {
        var author = _docs.Current?.Author;
        if (author?.CurrentNode is null)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInTheText2");
            return;
        }

        if (author.ApplyTextFormat(prefix, token))
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = done;
        }
        else
        {
            _shell.StatusText = Loc.T("Msg_TextFormattingIsNotAvailableHere");
        }
    }

    /// <summary>
    /// Положение блока на отдельном листе PDF/DOCX: "place-bottom-right" и т. п. (<see cref="PagePlacement"/>),
    /// null — обычное, в тексте.
    /// </summary>
    [RelayCommand]
    private void SetPagePlacement(string? token)
    {
        var author = _docs.Current?.Author;
        if (author?.CurrentNode is null || PagePlacement.PlaceableFor(author.CurrentNode) is null)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInAParagraph3");
            return;
        }

        if (author.SetCurrentBlockFormat(PagePlacement.Prefix, token))
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = PagePlacement.LabelOf(token) is { } label
                ? Loc.T("Msg_TheBlockIsOnASeparate", label.ToLowerInvariant())
                : Loc.T("Msg_TheBlockIsInTheText");
        }
    }

    /// <summary>Нумерованный абзац (пункт): номер по заголовкам — 2.3.1 (outputclass numbered).</summary>
    [RelayCommand]
    private void ToggleNumberedParagraph()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "p")
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInAParagraph4");
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass(HeadingNumbering.NumberedClass);
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? Loc.T("Msg_TheParagraphIsNumberedWhenPublished")
            : Loc.T("Msg_TheParagraphIsNoLongerNumbered");
    }

    /// <summary>Заголовок «без номера»: не нумеруется и не попадает в оглавление (outputclass nonumber).</summary>
    [RelayCommand]
    private void ToggleUnnumberedTitle()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "title")
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInATopic");
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass(TocRules.NoNumberClass);
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? Loc.T("Msg_TitleWithoutNumberWhenPublishedIt")
            : Loc.T("Msg_TheTitleIsNumberedAgainAnd");
    }

    [RelayCommand]
    private void ToggleRevChanged()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInTheElement");
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentRev();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? Loc.T("Msg_TheElementIsMarkedAsChanged")
            : Loc.T("Msg_TheChangeMarkWasRemoved");
    }

    [RelayCommand]
    private void MarkTrackedInserted()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInTheElement2");
            return;
        }

        _docs.Current!.Author.MarkCurrentInserted();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = Loc.T("Msg_TheElementIsMarkedAsInserted");
    }

    [RelayCommand]
    private void MarkTrackedDeleted()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInTheElement3");
            return;
        }

        _docs.Current!.Author.MarkCurrentDeleted();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = Loc.T("Msg_TheElementIsMarkedAsDeleted");
    }

    [RelayCommand]
    private void AcceptTrackedChange()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || !TrackChanges.IsTracked(node))
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInAnElement2");
            return;
        }

        _docs.Current!.Author.AcceptCurrentTrackedChange();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = Loc.T("Msg_ChangeAccepted");
    }

    [RelayCommand]
    private void RejectTrackedChange()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || !TrackChanges.IsTracked(node))
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInAnElement2");
            return;
        }

        _docs.Current!.Author.RejectCurrentTrackedChange();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = Loc.T("Msg_ChangeRejected");
    }
}
