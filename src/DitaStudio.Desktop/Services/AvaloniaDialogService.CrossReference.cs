using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Desktop.Services;

// Окно «Перекрёстная ссылка» — как в Word: «Тип ссылки» и «Вставить ссылку на» сверху, ниже — документ и список целей.
public sealed partial class AvaloniaDialogService
{
    /// <summary>Строка выпадающего списка: значение и подпись.</summary>
    private sealed record Choice<T>(T Value, string Title)
    {
        public override string ToString() => Title;
    }

    private static readonly XrefTargetKind[] XrefKinds =
    {
        XrefTargetKind.NumberedItem, XrefTargetKind.Section, XrefTargetKind.Topic, XrefTargetKind.Footnote,
        XrefTargetKind.Figure, XrefTargetKind.Table
    };

    private static string KindTitle(XrefTargetKind kind) => kind switch
    {
        XrefTargetKind.NumberedItem => Loc.T("Xref_KindNumbered"),
        XrefTargetKind.Section => Loc.T("Xref_KindSection"),
        XrefTargetKind.Topic => Loc.T("Xref_KindTopic"),
        XrefTargetKind.Footnote => Loc.T("Xref_KindFootnote"),
        XrefTargetKind.Figure => Loc.T("Xref_KindFigure"),
        _ => Loc.T("Xref_KindTable")
    };

    /// <summary>Виды текста ссылки для типа цели — первым идёт вид по умолчанию.</summary>
    internal static IReadOnlyList<(XrefForm Form, string Title)> XrefForms(XrefTargetKind kind) => kind switch
    {
        XrefTargetKind.Figure or XrefTargetKind.Table => new[]
        {
            (XrefForm.Default, Loc.T("Xref_FormLabel")), (XrefForm.Full, Loc.T("Xref_FormFull")),
            (XrefForm.Number, Loc.T("Xref_FormNumber")), (XrefForm.Text, Loc.T("Xref_FormCaptionText"))
        },
        XrefTargetKind.NumberedItem => new[]
        {
            (XrefForm.Default, Loc.T("Xref_FormItemNumber")), (XrefForm.Text, Loc.T("Xref_FormItemText")), (XrefForm.Full, Loc.T("Xref_FormNumberAndText"))
        },
        XrefTargetKind.Footnote => new[] { (XrefForm.Default, Loc.T("Xref_FormFootnoteNumber")), (XrefForm.Text, Loc.T("Xref_FormFootnoteText")) },
        _ => new[]
        {
            (XrefForm.Default, Loc.T("Xref_FormHeadingText")), (XrefForm.Number, Loc.T("Xref_FormHeadingNumber")), (XrefForm.Full, Loc.T("Xref_FormNumberAndText"))
        }
    };

    public async Task<CrossReferenceChoice?> InsertCrossReferenceAsync(DitaProject project, string currentFile, Func<string, DitaDocument?> documentFor)
    {
        var labels = Labels.For(Loc.Instance.Language);
        // Последний тип — «Сторонний ресурс» (null): сайт, программа или файл вне проекта.
        var kinds = XrefKinds.Select(k => new Choice<XrefTargetKind?>(k, KindTitle(k))).ToList();
        kinds.Add(new Choice<XrefTargetKind?>(null, Loc.T("Xref_KindExternal")));
        var kind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = kinds };
        var form = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(kind, Loc.T("Xref_KindLabel"));
        AutomationProperties.SetName(form, Loc.T("Xref_FormLabel0"));

        // Документ: этот — первым, затем остальные топики проекта.
        var files = new List<Choice<string>> { new(currentFile, Loc.T("Xref_ThisDocument")) };
        files.AddRange(project.Files
            .Where(f => f.Kind == DitaDocumentKind.Topic && !string.Equals(f.FullPath, currentFile, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.RelativePath, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new Choice<string>(f.FullPath, f.RelativePath)));
        var file = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = files, SelectedIndex = 0 };
        AutomationProperties.SetName(file, Loc.T("Xref_Document"));

        var targets = new ListBox { MinHeight = 160 };
        AutomationProperties.SetName(targets, Loc.T("Xref_Targets"));
        var empty = Muted(Wrapped(string.Empty, 0));
        var text = Input();
        AutomationProperties.SetName(text, Loc.T("Xref_OwnText"));

        // Сторонний ресурс: адрес сайта или путь к программе/файлу (кнопка «Обзор…»).
        var address = Input();
        address.Watermark = Loc.T("Xref_ExternalWatermark");
        AutomationProperties.SetName(address, Loc.T("Xref_ExternalAddress"));
        var browse = new Button { Content = Loc.T("Xref_ExternalBrowse"), Padding = new Thickness(12, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        browse.Click += async (_, _) =>
        {
            if (await _files.OpenFileAsync(Loc.T("Xref_ExternalBrowseTitle"), new[] { FileFilter.All }, project.RootPath) is { } picked)
            {
                address.Text = picked;
            }
        };
        var addressRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        addressRow.Children.Add(browse);
        addressRow.Children.Add(address);
        var external = new StackPanel
        {
            IsVisible = false,
            Children = { Label(Loc.T("Xref_ExternalAddress")), addressRow, Muted(Wrapped(Loc.T("Xref_ExternalHint"), 4)) }
        };
        var internalTop = new StackPanel();

        bool IsExternal() => kind.SelectedItem is Choice<XrefTargetKind?> { Value: null };

        void Refill()
        {
            var isExternal = IsExternal();
            external.IsVisible = isExternal;
            internalTop.IsVisible = !isExternal;
            targets.IsVisible = !isExternal;
            form.IsEnabled = !isExternal;
            if (isExternal)
            {
                form.ItemsSource = new[] { new Choice<XrefForm>(XrefForm.Default, Loc.T("Xref_FormExternal")) };
                form.SelectedIndex = 0;
                return;
            }

            if (kind.SelectedItem is not Choice<XrefTargetKind?> { Value: { } kindValue } || file.SelectedItem is not Choice<string> f)
            {
                return;
            }

            var k = new Choice<XrefTargetKind>(kindValue, KindTitle(kindValue));

            var formTitle = (form.SelectedItem as Choice<XrefForm>)?.Value;
            var forms = XrefForms(k.Value).Select(x => new Choice<XrefForm>(x.Form, x.Title)).ToList();
            form.ItemsSource = forms;
            form.SelectedItem = forms.FirstOrDefault(x => x.Value == formTitle) ?? forms[0];

            var document = documentFor(f.Value);
            var found = document is null ? new List<XrefTarget>() : XrefTargets.In(document, k.Value).ToList();
            targets.ItemsSource = found.Select(t => new Choice<XrefTarget>(t, t.Display(labels))).ToList();
            targets.SelectedIndex = found.Count > 0 ? 0 : -1;
            empty.Text = found.Count == 0 ? Loc.T("Xref_NoTargets", KindTitle(k.Value).ToLowerInvariant()) : string.Empty;
            empty.IsVisible = found.Count == 0;
        }

        kind.SelectionChanged += (_, _) => Refill();
        file.SelectionChanged += (_, _) => Refill();
        kind.SelectedIndex = 0;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var left = new StackPanel { Children = { Label(Loc.T("Xref_KindLabel")), kind } };
        var right = new StackPanel { Children = { Label(Loc.T("Xref_FormLabel0")), form } };
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);

        CrossReferenceChoice? result = null;
        var root = new DockPanel { Margin = new Thickness(16) };
        var window = Shell(Loc.T("Menu_CrossReference").TrimEnd('…'), root, 600, 560, autoHeight: false);
        var buttons = Buttons(window, () =>
        {
            if (IsExternal())
            {
                if (!string.IsNullOrWhiteSpace(address.Text))
                {
                    result = CrossReferenceChoice.External(address.Text.Trim(), (text.Text ?? string.Empty).Trim());
                }
            }
            else if (targets.SelectedItem is Choice<XrefTarget> chosen && file.SelectedItem is Choice<string> f)
            {
                result = new CrossReferenceChoice(f.Value, chosen.Value, (form.SelectedItem as Choice<XrefForm>)?.Value ?? XrefForm.Default,
                    (text.Text ?? string.Empty).Trim());
            }
        }, Loc.T("Author_Paste"));
        AcceptOnDoubleTap(targets, buttons);

        internalTop.Children.Add(Label(Loc.T("Xref_Document")));
        internalTop.Children.Add(file);
        internalTop.Children.Add(Label(Loc.T("Xref_Targets")));
        internalTop.Children.Add(empty);
        var top = new StackPanel { Children = { grid, internalTop, external } };
        var bottom = new StackPanel { Children = { Label(Loc.T("Xref_OwnText")), text, buttons } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(targets);

        window.Opened += (_, _) => kind.Focus();
        return await ShowAsync(window) ? result : null;
    }
}
