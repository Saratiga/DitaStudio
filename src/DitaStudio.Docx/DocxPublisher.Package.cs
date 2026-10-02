using System.Globalization;
using System.Text.RegularExpressions;
using DitaStudio.Core.IO;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

// Части пакета: закладки, нумерация, настройки, свойства документа, стили.
public sealed partial class DocxPublisher
{
    private static string BookmarkKey(string path, string? topicId) => Path.GetFullPath(path) + "#" + (topicId ?? string.Empty);

    /// <summary>Строит закладку на каждый топик публикации. Ссылки внутри проекта нередко
    /// указывают на топик через его СОБСТВЕННЫЙ id (например, "install.dita#install") даже когда
    /// файл однотопиковый и MapItem.TargetTopicId для него не задан (не нужен для разрешения
    /// неоднозначности) — поэтому каждый топик регистрируется под обоими ключами: с
    /// TargetTopicId из карты и (если отличается) с id корневого элемента документа.</summary>
    private Dictionary<string, string> AssignBookmarks(IEnumerable<MapItem> topics)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in topics)
        {
            if (item.TargetPath is null)
            {
                continue;
            }

            var key = BookmarkKey(item.TargetPath, item.TargetTopicId);
            if (result.ContainsKey(key))
            {
                continue;
            }

            var seed = DocxRenderer.SafeBookmarkName(
                Path.GetFileNameWithoutExtension(item.TargetPath) + (item.TargetTopicId is null ? string.Empty : "_" + item.TargetTopicId));
            var name = seed;
            var counter = 2;
            while (!used.Add(name))
            {
                name = seed + "_" + counter++;
            }

            result[key] = name;

            var rootId = _project.TryGetDocument(item.TargetPath)?.Root.GetAttribute("id");
            if (!string.IsNullOrEmpty(rootId))
            {
                var rootKey = BookmarkKey(item.TargetPath, rootId);
                result.TryAdd(rootKey, name);
            }
        }

        return result;
    }

    // ================================================================ нумерация

    private static NumberingDefinitionsPart AddNumbering(MainDocumentPart mainPart, DocxStyleSheet styles, DocxLayout layout)
    {
        var part = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new W.Numbering();

        // Маркеры и цвет маркеров — из CSS (ul, ul ul, li::marker…), по умолчанию disc/circle/square.
        numbering.Append(DocxNumbering.Bullet(DocxRenderer.BulletAbstractNumId, styles.BulletMarker, styles.BulletColor));
        numbering.Append(DocxNumbering.Decimal(DocxRenderer.DecimalAbstractNumId, styles.OrderedColor));
        if (layout.NumberHeadings)
        {
            // все abstractNum должны идти раньше любого num
            numbering.Append(HeadingAbstractNum(layout.NumberingDepth));
            numbering.Append(new W.NumberingInstance(new W.AbstractNumId { Val = HeadingAbstractNumId }) { NumberID = HeadingNumId });
        }

        part.Numbering = numbering;
        return part;
    }

    /// <summary>Многоуровневая нумерация заголовков «1», «1.1», «1.1.1» (как в ГОСТ 2.105),
    /// привязанная к стилям Heading1…HeadingN — номера видны и в оглавлении.</summary>
    private static W.AbstractNum HeadingAbstractNum(int depth)
    {
        var abstractNum = new W.AbstractNum { AbstractNumberId = HeadingAbstractNumId };
        abstractNum.Append(new W.MultiLevelType { Val = W.MultiLevelValues.Multilevel });
        for (var i = 0; i < 9; i++)
        {
            // Все уровни десятичные: глубже настройки номера получают только нумерованные абзацы
            // (стили заголовков к этим уровням не привязаны).
            var numbered = true;
            var text = string.Join(".", Enumerable.Range(1, i + 1).Select(n => "%" + n));
            var level = new W.Level
            {
                LevelIndex = i,
                StartNumberingValue = new W.StartNumberingValue { Val = 1 },
                NumberingFormat = new W.NumberingFormat { Val = numbered ? W.NumberFormatValues.Decimal : W.NumberFormatValues.None },
                LevelSuffix = new W.LevelSuffix { Val = W.LevelSuffixValues.Space },
                LevelText = new W.LevelText { Val = numbered ? text : string.Empty },
                LevelJustification = new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                PreviousParagraphProperties = new W.PreviousParagraphProperties(new W.Indentation { Left = "0", FirstLine = "0" })
            };

            if (i < depth && i < 6)
            {
                level.ParagraphStyleIdInLevel = new W.ParagraphStyleIdInLevel { Val = DocxStyleCatalog.Heading(i + 1) };
            }

            abstractNum.Append(level);
        }

        return abstractNum;
    }

    // ============================================================ стили и настройки

    private static void AddSettings(WordprocessingDocument document, DocxLayout layout)
    {
        var settingsPart = document.MainDocumentPart!.AddNewPart<DocumentSettingsPart>();
        var settings = new W.Settings();

        // Порядок элементов settings задан схемой: mirrorMargins … autoHyphenation … updateFields.
        if (layout.MirrorMargins)
        {
            settings.Append(new W.MirrorMargins());
        }

        if (layout.AutoHyphenation)
        {
            settings.Append(new W.AutoHyphenation());
        }

        settings.Append(new W.UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings = settings;
    }

    private static void SetDocumentProperties(WordprocessingDocument document, string title, DocxLayout layout)
    {
        var properties = document.PackageProperties;
        properties.Title = title;
        properties.Language = layout.Language;
        if (layout.Author.Length > 0)
        {
            properties.Creator = layout.Author;
        }
    }

    private static void AddStyles(MainDocumentPart mainPart, DocxStyleSheet sheet, DocxLayout layout)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new W.Styles();

        styles.Append(new W.DocDefaults(
            new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                new W.RunFonts
                {
                    Ascii = DocxDefaults.FontFamily, HighAnsi = DocxDefaults.FontFamily,
                    ComplexScript = DocxDefaults.FontFamily, EastAsia = DocxDefaults.FontFamily
                },
                new W.FontSize { Val = ((int)(DocxDefaults.FontSizePt * 2)).ToString() },
                new W.FontSizeComplexScript { Val = ((int)(DocxDefaults.FontSizePt * 2)).ToString() },
                new W.Languages { Val = layout.Language }))));

        foreach (var def in DocxStyleCatalog.Styles)
        {
            var props = sheet.Styles.TryGetValue(def.Id, out var resolved) ? resolved.Clone() : def.Defaults.Clone();
            W.NumberingProperties? numbering = null;
            var isHeading = def.OutlineLevel is not null;

            if (isHeading)
            {
                var level = def.OutlineLevel!.Value + 1;
                if (level == 1 && layout.PageBreakBeforeTopLevel)
                {
                    props.PageBreakBefore = true;
                }

                if (layout.NumberHeadings && level <= layout.NumberingDepth)
                {
                    numbering = new W.NumberingProperties(
                        new W.NumberingLevelReference { Val = level - 1 },
                        new W.NumberingId { Val = HeadingNumId });
                }
            }

            // Заголовок «без номера»: как обычный, но без нумерации и без уровня структуры —
            // поэтому и поле TOC в Word его не соберёт.
            var plainHeading = def.Id.StartsWith(DocxStyleCatalog.HeadingPlainPrefix, StringComparison.Ordinal);
            if (plainHeading && layout.NumberHeadings)
            {
                numbering = new W.NumberingProperties(new W.NumberingId { Val = 0 });
            }

            var primary = isHeading || plainHeading || def.Id is DocxStyleCatalog.Normal or DocxStyleCatalog.Title or DocxStyleCatalog.Subtitle;
            styles.Append(DocxStyleWriter.Create(def.Id, def.Name, def.Kind, def.BasedOn, props,
                primary, next: isHeading || plainHeading ? DocxStyleCatalog.Normal : null, numbering,
                plainHeading ? BodyTextOutlineLevel : def.OutlineLevel));
        }

        stylesPart.Styles = styles;
    }
}
