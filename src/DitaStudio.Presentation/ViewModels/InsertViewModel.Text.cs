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
            _shell.StatusText = "Выделите заголовок (title) — например, заголовок раздела или топика.";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass("page-break-before");
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Разрыв страницы перед заголовком включён."
            : "Разрыв страницы перед заголовком выключен.";
    }

    /// <summary>Выравнивание текущего блока: align-left (по умолчанию — класс снимается), -center, -right, -justify.</summary>
    [RelayCommand]
    private void SetAlignment(string? token)
    {
        var author = _docs.Current?.Author;
        if (author?.CurrentNode is null)
        {
            _shell.StatusText = "Поставьте курсор в абзац, заголовок или ячейку.";
            return;
        }

        var value = token is null or "align-left" ? null : token;
        if (author.SetCurrentBlockFormat(TextFormatting.AlignPrefix, value))
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = "Выравнивание: " + TextFormatting.Alignments.First(a => a.Token == (value ?? "align-left")).Label.ToLowerInvariant() + ".";
        }
    }

    /// <summary>Размеры шрифта для списка на панели: «Обычный» и размеры в пт.</summary>
    public IReadOnlyList<string> FontSizes { get; } = new[] { NormalSize }.Concat(TextFormatting.Sizes.Select(s => s.ToString())).Append(CustomSize).ToList();

    /// <summary>Пункт списка размеров: спросить число пунктов (дробные — через точку или запятую).</summary>
    public const string CustomSize = "Другой…";

    public const string NormalSize = "Обычный";

    /// <summary>Размер шрифта выделения или дальнейшего набора: "10" (пт) или «Обычный»/null — снять.</summary>
    [RelayCommand]
    private async Task SetFontSize(string? size)
    {
        if (size == CustomSize)
        {
            var typed = await _ui.Dialogs.PromptTextAsync("Свой размер шрифта", "Размер, пт",
                (_docs.Current?.Author.CurrentNode is { } node && TextFormatting.SizeOf(node) is { } current ? current : 11).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                $"От {TextFormatting.MinCustomSize:0} до {TextFormatting.MaxCustomSize:0} пт; дробные значения — через точку или запятую (например, 13,5).");
            if (typed is null)
            {
                return;
            }

            if (!double.TryParse(typed.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var custom) ||
                custom < TextFormatting.MinCustomSize || custom > TextFormatting.MaxCustomSize)
            {
                _shell.StatusText = $"Размер должен быть числом от {TextFormatting.MinCustomSize:0} до {TextFormatting.MaxCustomSize:0} пт.";
                return;
            }

            size = custom.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }

        var parsed = double.TryParse(size?.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var points);
        var token = parsed ? TextFormatting.SizeToken(points) : null;
        ApplyTextFormat(TextFormatting.SizePrefix, token, token is null ? "Размер шрифта снят." : $"Размер шрифта {TextFormatting.ParseSizeToken(token):0.#} пт.");
    }

    /// <summary>Цвет выделения или дальнейшего набора: color-red… или null — снять.</summary>
    [RelayCommand]
    private void SetTextColor(string? token)
    {
        var color = TextFormatting.Colors.FirstOrDefault(c => c.Token == token);
        ApplyTextFormat(TextFormatting.ColorPrefix, color.Token,
            color.Token is null ? "Цвет текста снят." : $"Цвет текста: {color.Label.ToLowerInvariant()}.");
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
            _shell.StatusText = "Откройте документ и выделите текст.";
            return;
        }

        if (token == CustomMarker)
        {
            var current = author.MarkerPenToken is { } pen ? TextFormatting.ParseMarkToken(pen) : null;
            var picked = await _ui.Dialogs.PickColorAsync("Цвет маркера", current);
            if (picked is null || TextFormatting.MarkToken(picked) is not { } custom)
            {
                return;
            }

            token = custom;
        }

        if (author.HasTextSelection)
        {
            ApplyTextFormat(TextFormatting.MarkPrefix, token, token is null ? "Маркер снят." : $"Маркер: {MarkerLabel(token)}.");
            return;
        }

        // Выделения нет — кисть: тот же цвет второй раз выключает её.
        if (author.MarkerPenActive && author.MarkerPenToken == token)
        {
            author.StopMarkerPen();
            _shell.StatusText = "Маркер выключен.";
            return;
        }

        if (author.StartMarkerPen(token))
        {
            _shell.StatusText = token is null
                ? "Режим маркера: ластик — выделите текст мышью, чтобы снять маркер. Esc — выключить."
                : $"Режим маркера: {MarkerLabel(token)} — выделяйте текст мышью, он закрашивается. Esc — выключить.";
        }
        else
        {
            _shell.StatusText = "Здесь маркер недоступен.";
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
            _shell.StatusText = "Поставьте курсор в текст или выделите его.";
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
            _shell.StatusText = "Здесь оформление текста недоступно.";
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
            _shell.StatusText = "Поставьте курсор в абзац, рисунок, таблицу или заметку прямо в тексте топика (не в списке).";
            return;
        }

        if (author.SetCurrentBlockFormat(PagePlacement.Prefix, token))
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = PagePlacement.LabelOf(token) is { } label
                ? $"Блок на отдельном листе PDF и DOCX: {label.ToLowerInvariant()}."
                : "Блок снова идёт в тексте.";
        }
    }

    /// <summary>Нумерованный абзац (пункт): номер по заголовкам — 2.3.1 (outputclass numbered).</summary>
    [RelayCommand]
    private void ToggleNumberedParagraph()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "p")
        {
            _shell.StatusText = "Поставьте курсор в абзац.";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass(HeadingNumbering.NumberedClass);
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Абзац нумерованный: при публикации получит номер по заголовкам (например, 2.3.1)."
            : "Абзац больше не нумеруется.";
    }

    /// <summary>Заголовок «без номера»: не нумеруется и не попадает в оглавление (outputclass nonumber).</summary>
    [RelayCommand]
    private void ToggleUnnumberedTitle()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "title")
        {
            _shell.StatusText = "Поставьте курсор в заголовок топика или раздела.";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass(TocRules.NoNumberClass);
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Заголовок без номера: при публикации не нумеруется и не попадает в оглавление."
            : "Заголовок снова нумеруется и попадает в оглавление.";
    }

    [RelayCommand]
    private void ToggleRevChanged()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент, который нужно отметить как изменённый.";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentRev();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Элемент отмечен как изменённый (rev) — при публикации появится полоса на полях."
            : "Отметка об изменении снята.";
    }

    [RelayCommand]
    private void MarkTrackedInserted()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент, который нужно пометить как вставленный.";
            return;
        }

        _docs.Current!.Author.MarkCurrentInserted();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = "Элемент помечен как вставленный (track changes).";
    }

    [RelayCommand]
    private void MarkTrackedDeleted()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент, который нужно пометить как удалённый.";
            return;
        }

        _docs.Current!.Author.MarkCurrentDeleted();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = "Элемент помечен как удалённый (track changes) — скрыт из публикации, виден зачёркнутым в предпросмотре.";
    }

    [RelayCommand]
    private void AcceptTrackedChange()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || !TrackChanges.IsTracked(node))
        {
            _shell.StatusText = "Поставьте курсор в элемент с отслеживаемой правкой.";
            return;
        }

        _docs.Current!.Author.AcceptCurrentTrackedChange();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = "Правка принята.";
    }

    [RelayCommand]
    private void RejectTrackedChange()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || !TrackChanges.IsTracked(node))
        {
            _shell.StatusText = "Поставьте курсор в элемент с отслеживаемой правкой.";
            return;
        }

        _docs.Current!.Author.RejectCurrentTrackedChange();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = "Правка отклонена.";
    }
}
