using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DitaStudio.App.Authoring;
using DitaStudio.App.Views;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Templates;
using DitaStudio.Core.Validation;
using Microsoft.Win32;

namespace DitaStudio.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, DocumentPane> _panes = new(StringComparer.OrdinalIgnoreCase);
    private DitaProject? _project;
    private MapTree? _mapTree;
    private Dialogs.ConditionsResult? _conditions;
    private string? _lastOutputDirectory;

    public MainWindow()
    {
        InitializeComponent();
        RegisterShortcuts();
        ThemeMenuItem.IsChecked = ThemeManager.Current == ThemeManager.Theme.Dark;
        UpdateStatus("Откройте папку с проектом DITA: Файл → Открыть папку проекта.");
    }

    private DocumentPane? Current => DocumentTabs.SelectedItem is TabItem { Content: DocumentPane pane } ? pane : null;

    private void RegisterShortcuts()
    {
        void Bind(Key key, ModifierKeys modifiers, Action action)
        {
            var command = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(command, (_, _) => action()));
            InputBindings.Add(new KeyBinding(command, key, modifiers));
        }

        Bind(Key.S, ModifierKeys.Control, () => SaveCurrent());
        Bind(Key.S, ModifierKeys.Control | ModifierKeys.Shift, SaveAll);
        Bind(Key.N, ModifierKeys.Control, NewDocument);
        Bind(Key.O, ModifierKeys.Control | ModifierKeys.Shift, OpenProject);
        Bind(Key.W, ModifierKeys.Control, CloseCurrentTab);
        Bind(Key.F5, ModifierKeys.None, () => PublishSite());
        Bind(Key.F7, ModifierKeys.None, ValidateProject);
        Bind(Key.F1, ModifierKeys.None, ShowElementHelp);
        Bind(Key.E, ModifierKeys.Control, FocusPalette);
        Bind(Key.F, ModifierKeys.Control | ModifierKeys.Shift, FocusSearch);
        Bind(Key.Z, ModifierKeys.Control | ModifierKeys.Alt, () => Current?.PerformUndo());
        Bind(Key.Y, ModifierKeys.Control | ModifierKeys.Alt, () => Current?.PerformRedo());
        Bind(Key.Up, ModifierKeys.Control | ModifierKeys.Shift, () => MoveElement(true));
        Bind(Key.Down, ModifierKeys.Control | ModifierKeys.Shift, () => MoveElement(false));
    }

    private void UpdateStatus(string text) => StatusText.Text = text;

    // ======================================================================
    //  Проект
    // ======================================================================

    private void OnOpenProject(object sender, RoutedEventArgs e) => OpenProject();

    private void OpenProject()
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку с проектом DITA" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LoadProject(dialog.FolderName);
    }

    private void LoadProject(string path)
    {
        _project = new DitaProject(path);
        _panes.Clear();
        DocumentTabs.Items.Clear();

        try
        {
            _project.Scan();
        }
        catch (Exception ex)
        {
            Dialogs.Message("Проект", $"Не удалось прочитать папку: {ex.Message}");
            return;
        }

        Title = $"DITA Studio — {_project.Name}";
        BuildProjectTree();
        BuildMapSelector();
        BuildKeysList();
        UpdateStatus($"Проект открыт: {_project.Files.Count} файлов, {_project.Keys.Count} ключей.");
    }

    private void OnRescanProject(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        _project.Scan();
        BuildProjectTree();
        BuildMapSelector();
        BuildKeysList();
        UpdateStatus($"Проект обновлён: {_project.Files.Count} файлов.");
    }

    private void BuildProjectTree()
    {
        ProjectTree.Items.Clear();
        if (_project is null)
        {
            return;
        }

        var rootItem = new TreeViewItem { Header = _project.Name, IsExpanded = true, Tag = _project.RootPath };
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = rootItem
        };

        foreach (var file in _project.Files)
        {
            var directory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
            var parent = EnsureFolder(folders, rootItem, directory);
            parent.Items.Add(new TreeViewItem
            {
                Header = BuildFileHeader(file),
                Tag = file,
                ToolTip = file.RelativePath
            });
        }

        ProjectTree.Items.Add(rootItem);
    }

    private TreeViewItem EnsureFolder(Dictionary<string, TreeViewItem> folders, TreeViewItem root, string relativeDirectory)
    {
        if (string.IsNullOrEmpty(relativeDirectory))
        {
            return root;
        }

        if (folders.TryGetValue(relativeDirectory, out var existing))
        {
            return existing;
        }

        var parentPath = Path.GetDirectoryName(relativeDirectory) ?? string.Empty;
        var parent = EnsureFolder(folders, root, parentPath);

        var item = new TreeViewItem
        {
            Header = Path.GetFileName(relativeDirectory),
            IsExpanded = true,
            Tag = _project is null ? relativeDirectory : Path.Combine(_project.RootPath, relativeDirectory)
        };

        parent.Items.Add(item);
        folders[relativeDirectory] = item;
        return item;
    }

    private static object BuildFileHeader(ProjectFile file)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = file.Kind switch
            {
                DitaDocumentKind.Map => "🗺",
                DitaDocumentKind.Topic => "📄",
                _ => "•"
            },
            Margin = new Thickness(0, 0, 6, 0)
        });
        panel.Children.Add(new TextBlock { Text = file.FileName });
        panel.Children.Add(new TextBlock
        {
            Text = "  " + file.Title,
            Foreground = ThemeManager.Brush("TagBrush"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 150
        });
        return panel;
    }

    private void OnProjectTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectTree.SelectedItem is TreeViewItem { Tag: ProjectFile file })
        {
            OpenDocument(file.FullPath);
        }
    }

    private void BuildKeysList()
    {
        KeysList.ItemsSource = _project?.Keys.Values.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
    }

    private void OnKeyDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (KeysList.SelectedItem is KeyDefinition key && key.ResolvedPath is not null && File.Exists(key.ResolvedPath))
        {
            OpenDocument(key.ResolvedPath);
        }
    }

    // ======================================================================
    //  Документы
    // ======================================================================

    private DocumentPane? OpenDocument(string path)
    {
        if (_project is null)
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        if (_panes.TryGetValue(full, out var existing))
        {
            SelectPane(existing);
            return existing;
        }

        DitaDocument document;
        try
        {
            document = _project.GetDocument(full);
        }
        catch (Exception ex)
        {
            Dialogs.Message("Открытие файла", $"Не удалось разобрать {Path.GetFileName(full)}:\n\n{ex.Message}");
            return null;
        }

        var pane = new DocumentPane(_project, document);
        pane.DirtyChanged += (_, _) => UpdateTabHeaders();
        pane.SelectionChanged += (_, _) => OnEditorSelectionChanged();

        var tab = new TabItem { Content = pane, Tag = full };
        tab.Header = BuildTabHeader(pane, tab);
        DocumentTabs.Items.Add(tab);
        DocumentTabs.SelectedItem = tab;
        _panes[full] = pane;

        UpdateStatus($"Открыт {Path.GetFileName(full)}");
        return pane;
    }

    private object BuildTabHeader(DocumentPane pane, TabItem tab)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = (pane.IsDirty ? "• " : string.Empty) +
                   (pane.FilePath is null ? pane.Title : Path.GetFileName(pane.FilePath)),
            VerticalAlignment = VerticalAlignment.Center
        });

        var close = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(8, 0, 0, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        close.Click += (_, _) => CloseTab(tab);
        panel.Children.Add(close);
        return panel;
    }

    private void UpdateTabHeaders()
    {
        foreach (var item in DocumentTabs.Items.OfType<TabItem>())
        {
            if (item.Content is DocumentPane pane)
            {
                item.Header = BuildTabHeader(pane, item);
            }
        }
    }

    private void SelectPane(DocumentPane pane)
    {
        foreach (var item in DocumentTabs.Items.OfType<TabItem>())
        {
            if (ReferenceEquals(item.Content, pane))
            {
                DocumentTabs.SelectedItem = item;
                return;
            }
        }
    }

    private void OnDocumentTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, DocumentTabs))
        {
            return;
        }

        OnEditorSelectionChanged();
        BuildOutline();
    }

    private void OnCloseTab(object sender, RoutedEventArgs e) => CloseCurrentTab();

    private void CloseCurrentTab()
    {
        if (DocumentTabs.SelectedItem is TabItem tab)
        {
            CloseTab(tab);
        }
    }

    private void CloseTab(TabItem tab)
    {
        if (tab.Content is not DocumentPane pane)
        {
            return;
        }

        pane.CommitPendingEdits();
        if (pane.IsDirty)
        {
            var answer = MessageBox.Show(
                $"Сохранить изменения в «{pane.Title}»?",
                "DITA Studio",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.Cancel)
            {
                return;
            }

            if (answer == MessageBoxResult.Yes && !pane.Save(out var error))
            {
                Dialogs.Message("Сохранение", error ?? "Не удалось сохранить файл.");
                return;
            }
        }

        if (tab.Tag is string path)
        {
            _panes.Remove(path);
        }

        DocumentTabs.Items.Remove(tab);
    }

    private void OnSave(object sender, RoutedEventArgs e) => SaveCurrent();

    private bool SaveCurrent()
    {
        var pane = Current;
        if (pane is null)
        {
            return false;
        }

        if (!pane.Save(out var error))
        {
            Dialogs.Message("Сохранение", error ?? "Не удалось сохранить файл.");
            return false;
        }

        _project?.RebuildKeySpace();
        BuildKeysList();
        UpdateTabHeaders();
        UpdateStatus($"Сохранено: {Path.GetFileName(pane.FilePath ?? pane.Title)}");
        return true;
    }

    private void OnSaveAll(object sender, RoutedEventArgs e) => SaveAll();

    private void SaveAll()
    {
        var saved = 0;
        foreach (var pane in _panes.Values.ToList())
        {
            if (!pane.IsDirty)
            {
                continue;
            }

            if (pane.Save(out var error))
            {
                saved++;
            }
            else
            {
                Dialogs.Message("Сохранение", error ?? "Не удалось сохранить файл.");
            }
        }

        _project?.RebuildKeySpace();
        BuildKeysList();
        UpdateTabHeaders();
        UpdateStatus($"Сохранено файлов: {saved}");
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var dirty = _panes.Values.Count(p => p.IsDirty);
        if (dirty > 0)
        {
            var answer = MessageBox.Show(
                $"Не сохранено документов: {dirty}. Сохранить перед выходом?",
                "DITA Studio",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (answer == MessageBoxResult.Yes)
            {
                SaveAll();
            }
        }

        base.OnClosing(e);
    }

    private void OnNewDocument(object sender, RoutedEventArgs e) => NewDocument();

    private void NewDocument()
    {
        if (_project is null)
        {
            Dialogs.Message("Создание документа", "Сначала откройте папку проекта.");
            return;
        }

        var folders = new List<string> { _project.RootPath };
        folders.AddRange(Directory.EnumerateDirectories(_project.RootPath, "*", SearchOption.AllDirectories)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase));

        var selected = ProjectTree.SelectedItem is TreeViewItem { Tag: string folder } && Directory.Exists(folder)
            ? folder
            : ProjectTree.SelectedItem is TreeViewItem { Tag: ProjectFile file }
                ? Path.GetDirectoryName(file.FullPath)
                : null;

        var result = Dialogs.NewDocument(_project.RootPath, folders, selected);
        if (result is null)
        {
            return;
        }

        var path = Path.Combine(result.Folder, result.FileName);
        if (File.Exists(path) && !Dialogs.Confirm("Создание документа", $"Файл {result.FileName} уже существует. Перезаписать?"))
        {
            return;
        }

        var document = DocumentTemplates.Create(result.Template.Key, result.Title);
        document.FilePath = path;

        try
        {
            document.Save(path);
        }
        catch (Exception ex)
        {
            Dialogs.Message("Создание документа", ex.Message);
            return;
        }

        _project.Register(document);
        _project.AddFile(path);
        _project.RebuildKeySpace();
        BuildProjectTree();
        BuildMapSelector();
        BuildKeysList();
        OpenDocument(path);
    }

    // ======================================================================
    //  Правая панель: атрибуты, палитра, структура
    // ======================================================================

    private void OnEditorSelectionChanged()
    {
        BuildAttributePanel();
        BuildPalette();
        UpdateContextText();
    }

    private void UpdateContextText()
    {
        var node = Current?.Author.CurrentNode;
        ContextText.Text = node is null ? string.Empty : node.Path;
    }

    private void BuildAttributePanel()
    {
        AttributePanel.Children.Clear();
        var pane = Current;
        var node = pane?.Author.CurrentNode;

        if (pane is null || node is null)
        {
            AttributeContext.Text = "Элемент не выбран";
            return;
        }

        var def = DitaCatalog.Default.Get(node.Name);
        AttributeContext.Text = $"<{node.Name}>  {def?.Description ?? string.Empty}";

        var shown = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in node.Attributes.ToList())
        {
            shown.Add(attribute.Name);
            AttributePanel.Children.Add(BuildAttributeRow(pane, node, attribute.Name, attribute.Value, def));
        }

        if (def is null)
        {
            return;
        }

        var missing = def.Attributes.Keys
            .Where(a => !shown.Contains(a))
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        var adder = new ComboBox
        {
            ItemsSource = missing,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(4, 3, 4, 3)
        };

        var addPanel = new StackPanel();
        addPanel.Children.Add(new TextBlock
        {
            Text = "Добавить атрибут",
            FontSize = 11,
            Foreground = ThemeManager.Brush("TextMuted"),
            Margin = new Thickness(0, 14, 0, 2)
        });
        adder.Margin = new Thickness(0);
        addPanel.Children.Add(adder);

        adder.SelectionChanged += (_, _) =>
        {
            if (adder.SelectedItem is not string name)
            {
                return;
            }

            pane.PushUndo($"Атрибут @{name}");
            var attributeDef = def.Attributes[name];
            node.SetAttribute(name, attributeDef.DefaultValue ?? (attributeDef.Values.Count > 0 ? attributeDef.Values[0] : string.Empty));
            pane.Document.IsDirty = true;
            UpdateTabHeaders();
            BuildAttributePanel();
        };

        AttributePanel.Children.Add(addPanel);
    }

    private FrameworkElement BuildAttributeRow(DocumentPane pane, DitaNode node, string name, string value, ElementDef? def)
    {
        var container = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var header = new DockPanel();
        var label = new TextBlock
        {
            Text = "@" + name,
            FontFamily = InlineStyles.Mono,
            FontSize = 11.5,
            Foreground = ThemeManager.Brush("AttributeNameBrush")
        };

        var remove = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(4, 0, 4, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            ToolTip = "Удалить атрибут"
        };
        remove.Click += (_, _) =>
        {
            pane.PushUndo($"Удаление @{name}");
            node.RemoveAttribute(name);
            pane.Document.IsDirty = true;
            UpdateTabHeaders();
            BuildAttributePanel();
        };

        DockPanel.SetDock(remove, Dock.Right);
        header.Children.Add(remove);
        header.Children.Add(label);
        container.Children.Add(header);

        var attributeDef = def?.Attributes.GetValueOrDefault(name);

        if (attributeDef is { Type: AttrType.Enumeration } && attributeDef.Values.Count > 0)
        {
            var combo = new ComboBox
            {
                ItemsSource = attributeDef.Values,
                IsEditable = true,
                Text = value,
                Padding = new Thickness(4, 3, 4, 3)
            };

            combo.LostFocus += (_, _) => Apply(combo.Text);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string selected)
                {
                    Apply(selected);
                }
            };

            container.Children.Add(combo);
        }
        else
        {
            var box = new TextBox { Text = value, Padding = new Thickness(4, 3, 4, 3) };
            box.LostFocus += (_, _) => Apply(box.Text);
            box.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    Apply(box.Text);
                }
            };
            container.Children.Add(box);
        }

        if (!string.IsNullOrWhiteSpace(attributeDef?.Description))
        {
            container.Children.Add(new TextBlock
            {
                Text = attributeDef!.Description,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeManager.Brush("TagBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        return container;

        void Apply(string newValue)
        {
            if (node.GetAttribute(name) == newValue)
            {
                return;
            }

            pane.PushUndo($"Изменение @{name}");
            node.SetAttribute(name, newValue);
            pane.Document.IsDirty = true;
            UpdateTabHeaders();
            UpdateStatus($"@{name} = {newValue}");
        }
    }

    private void BuildPalette()
    {
        PaletteList.Items.Clear();
        var pane = Current;
        var node = pane?.Author.CurrentNode;

        if (pane is null || node is null)
        {
            PaletteHint.Text = "Поставьте курсор в текст, чтобы увидеть допустимые элементы.";
            return;
        }

        var parent = node.Parent;
        var candidates = new List<(ElementDef Def, string Where)>();

        if (parent is not null)
        {
            var index = EditCommands.ElementIndexOf(parent, node) + 1;
            foreach (var def in DitaCatalog.Default.InsertableAt(parent, index))
            {
                candidates.Add((def, $"после <{node.Name}>"));
            }
        }

        var childNames = DitaCatalog.ChildNames(node);
        foreach (var def in DitaCatalog.Default.InsertableAt(node, childNames.Count))
        {
            if (candidates.Any(c => c.Def.Name == def.Name))
            {
                continue;
            }

            candidates.Add((def, $"внутрь <{node.Name}>"));
        }

        var filter = PaletteFilter.Text.Trim();
        if (filter.Length > 0)
        {
            candidates = candidates
                .Where(c => c.Def.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            c.Def.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        PaletteHint.Text = $"Допустимо рядом с <{node.Name}>: {candidates.Count} элементов. Двойной щелчок — вставить.";

        foreach (var (def, where) in candidates.OrderBy(c => c.Def.Name, StringComparer.Ordinal))
        {
            var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            panel.Children.Add(new TextBlock
            {
                Text = def.Name,
                FontFamily = InlineStyles.Mono,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            });
            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(def.Description) ? where : $"{def.Description} · {where}",
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeManager.Brush("TagBrush")
            });

            PaletteList.Items.Add(new ListBoxItem { Content = panel, Tag = def.Name });
        }
    }

    private void OnPaletteFilterChanged(object sender, TextChangedEventArgs e) => BuildPalette();

    private void OnPaletteInsert(object sender, MouseButtonEventArgs e)
    {
        if (PaletteList.SelectedItem is ListBoxItem { Tag: string name })
        {
            InsertElement(name);
        }
    }

    private void FocusPalette()
    {
        RightTabs.SelectedIndex = 1;
        PaletteFilter.Focus();
    }

    private void OnFocusPalette(object sender, RoutedEventArgs e) => FocusPalette();

    private void BuildOutline()
    {
        OutlineTree.Items.Clear();
        var document = Current?.Document;
        if (document is null)
        {
            return;
        }

        OutlineTree.Items.Add(BuildOutlineItem(document.Root, 0));
    }

    private TreeViewItem BuildOutlineItem(DitaNode node, int depth)
    {
        var text = node.InnerText.Trim();
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = node.Name,
            FontFamily = InlineStyles.Mono,
            FontSize = 11,
            Foreground = ThemeManager.Brush("Accent")
        });

        if (text.Length > 0)
        {
            header.Children.Add(new TextBlock
            {
                Text = "  " + (text.Length > 42 ? text[..42] + "…" : text),
                FontSize = 11,
                Foreground = ThemeManager.Brush("TextMuted")
            });
        }

        var item = new TreeViewItem { Header = header, Tag = node, IsExpanded = depth < 3 };

        foreach (var child in node.ElementChildren())
        {
            item.Items.Add(BuildOutlineItem(child, depth + 1));
        }

        return item;
    }

    private void OnOutlineSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: DitaNode node })
        {
            var editor = Current?.Author.EditorFor(node);
            editor?.Focus();
            UpdateStatus(node.Path);
        }
    }

    // ======================================================================
    //  Вставка и форматирование
    // ======================================================================

    private void InsertElement(string name)
    {
        var pane = Current;
        if (pane is null)
        {
            return;
        }

        pane.Mode = EditorMode.Author;
        if (!pane.Author.InsertElement(name))
        {
            UpdateStatus($"Элемент <{name}> здесь недопустим.");
            return;
        }

        UpdateTabHeaders();
        BuildOutline();
        UpdateStatus($"Вставлен <{name}>");
    }

    private void OnInsertParagraph(object sender, RoutedEventArgs e) => InsertElement("p");

    private void OnInsertUl(object sender, RoutedEventArgs e) => InsertElement("ul");

    private void OnInsertOl(object sender, RoutedEventArgs e) => InsertElement("ol");

    private void OnInsertNote(object sender, RoutedEventArgs e) => InsertElement("note");

    private void OnInsertCodeblock(object sender, RoutedEventArgs e) => InsertElement("codeblock");

    private void OnInsertTable(object sender, RoutedEventArgs e)
    {
        var pane = Current;
        var node = pane?.Author.CurrentNode;
        if (pane is null || node?.Parent is null)
        {
            return;
        }

        var options = Dialogs.InsertTable();
        if (options is null)
        {
            return;
        }

        pane.PushUndo("Вставка таблицы");
        var table = BuildTableNode(options);

        var parent = node.Parent;
        var index = EditCommands.ElementIndexOf(parent, node) + 1;
        if (!DitaCatalog.Default.CanInsert(parent, "table", index))
        {
            UpdateStatus("Таблицу здесь вставить нельзя.");
            return;
        }

        parent.Insert(EditCommands.ChildIndexForElementIndex(parent, index), table);
        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        UpdateTabHeaders();
        BuildOutline();
    }

    private static DitaNode BuildTableNode(Dialogs.TableResult options)
    {
        var table = DitaNode.Element("table");
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            var title = DitaNode.Element("title");
            title.SetText(options.Title);
            table.Add(title);
        }

        var tgroup = DitaNode.Element("tgroup");
        tgroup.SetAttribute("cols", options.Columns.ToString());
        table.Add(tgroup);

        for (var c = 1; c <= options.Columns; c++)
        {
            var colspec = DitaNode.Element("colspec");
            colspec.SetAttribute("colname", "c" + c);
            colspec.SetAttribute("colnum", c.ToString());
            colspec.SetAttribute("colwidth", "1*");
            tgroup.Add(colspec);
        }

        if (options.Header)
        {
            var thead = DitaNode.Element("thead");
            var row = DitaNode.Element("row");
            for (var c = 1; c <= options.Columns; c++)
            {
                var entry = DitaNode.Element("entry");
                entry.SetAttribute("colname", "c" + c);
                row.Add(entry);
            }

            thead.Add(row);
            tgroup.Add(thead);
        }

        var tbody = DitaNode.Element("tbody");
        for (var r = 0; r < options.Rows; r++)
        {
            var row = DitaNode.Element("row");
            for (var c = 1; c <= options.Columns; c++)
            {
                var entry = DitaNode.Element("entry");
                entry.SetAttribute("colname", "c" + c);
                row.Add(entry);
            }

            tbody.Add(row);
        }

        tgroup.Add(tbody);
        return table;
    }

    private void OnInsertImage(object sender, RoutedEventArgs e)
    {
        var pane = Current;
        if (pane is null || pane.FilePath is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.svg;*.bmp|Все файлы|*.*",
            InitialDirectory = Path.GetDirectoryName(pane.FilePath)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var href = RefResolver.MakeRelative(pane.FilePath, dialog.FileName);
        var image = DitaNode.Element("image");
        image.SetAttribute("href", href);
        image.SetAttribute("placement", "break");

        var alt = DitaNode.Element("alt");
        alt.SetText(Path.GetFileNameWithoutExtension(dialog.FileName));
        image.Add(alt);

        if (!pane.Author.InsertInlineNode(image))
        {
            UpdateStatus("Поставьте курсор в абзац, куда вставить изображение.");
            return;
        }

        UpdateTabHeaders();
    }

    private void OnInsertXref(object sender, RoutedEventArgs e)
    {
        var pane = Current;
        if (_project is null || pane is null || pane.FilePath is null)
        {
            return;
        }

        var result = Dialogs.InsertXref(_project, pane.FilePath);
        if (result is null)
        {
            return;
        }

        var href = RefResolver.MakeRelative(pane.FilePath, result.File.FullPath);
        if (!string.IsNullOrEmpty(result.TopicId))
        {
            href += "#" + result.TopicId;
        }

        var xref = DitaNode.Element("xref");
        xref.SetAttribute("href", href);
        xref.SetAttribute("format", "dita");
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            xref.SetText(result.Text);
        }

        if (!pane.Author.InsertInlineNode(xref))
        {
            UpdateStatus("Поставьте курсор в текст, куда вставить ссылку.");
            return;
        }

        UpdateTabHeaders();
    }

    private void Format(string element)
    {
        var pane = Current;
        if (pane is null)
        {
            return;
        }

        if (!pane.Author.WrapCurrentInline(element))
        {
            UpdateStatus("Выделите текст в режиме «Автор».");
            return;
        }

        UpdateTabHeaders();
    }

    private void OnFormatBold(object sender, RoutedEventArgs e) => Format("b");

    private void OnFormatItalic(object sender, RoutedEventArgs e) => Format("i");

    private void OnFormatCode(object sender, RoutedEventArgs e) => Format("codeph");

    private void OnFormatUicontrol(object sender, RoutedEventArgs e) => Format("uicontrol");

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveElement(true);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveElement(false);

    private void MoveElement(bool up)
    {
        if (Current?.Author.MoveCurrent(up) == true)
        {
            UpdateTabHeaders();
            BuildOutline();
        }
    }

    private void OnDeleteElement(object sender, RoutedEventArgs e)
    {
        var node = Current?.Author.CurrentNode;
        if (node is null)
        {
            return;
        }

        if (!Dialogs.Confirm("Удаление", $"Удалить элемент <{node.Name}> вместе с содержимым?"))
        {
            return;
        }

        if (Current?.Author.DeleteCurrent() == true)
        {
            UpdateTabHeaders();
            BuildOutline();
        }
    }

    private void OnToggleTags(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
        {
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.Author.ShowElementTags = item.IsChecked;
            pane.Author.Rebuild();
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        Current?.PerformUndo();
        UpdateTabHeaders();
        BuildOutline();
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        Current?.PerformRedo();
        UpdateTabHeaders();
        BuildOutline();
    }

    // ======================================================================
    //  Проверка и поиск
    // ======================================================================

    private void OnValidateProject(object sender, RoutedEventArgs e) => ValidateProject();

    private void ValidateProject()
    {
        if (_project is null)
        {
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var issues = _project.ValidateAll();
        ShowIssues(issues);
        UpdateStatus($"Проверка проекта: ошибок {issues.Count(i => i.Severity == IssueSeverity.Error)}, " +
                     $"предупреждений {issues.Count(i => i.Severity == IssueSeverity.Warning)}.");
    }

    private void OnValidateDocument(object sender, RoutedEventArgs e)
    {
        var pane = Current;
        if (pane is null || _project is null)
        {
            return;
        }

        var error = pane.CommitPendingEdits();
        if (error is not null)
        {
            Dialogs.Message("Проверка", $"Документ не разбирается как XML:\n\n{error}");
            return;
        }

        var issues = new List<ValidationIssue>();
        issues.AddRange(new DitaValidator().Validate(pane.Document));
        issues.AddRange(RefResolver.ValidateReferences(_project, pane.Document));
        ShowIssues(issues);
        UpdateStatus($"Проверка документа: {issues.Count} замечаний.");
    }

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues)
    {
        IssuesList.ItemsSource = issues
            .OrderByDescending(i => i.Severity)
            .ToList();
        BottomTabs.SelectedIndex = 0;
    }

    private void OnIssueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssuesList.SelectedItem is not ValidationIssue issue)
        {
            return;
        }

        if (issue.FilePath is not null && File.Exists(issue.FilePath))
        {
            var pane = OpenDocument(issue.FilePath);
            if (pane is not null && issue.Node is not null)
            {
                var editor = pane.Author.EditorFor(issue.Node);
                if (editor is not null)
                {
                    editor.Focus();
                }
                else
                {
                    pane.Mode = EditorMode.Source;
                    if (issue.Line > 0)
                    {
                        pane.Source.GoToLine(issue.Line);
                    }
                }
            }
        }

        UpdateStatus(issue.ToString());
    }

    private void FocusSearch()
    {
        BottomTabs.SelectedIndex = 1;
        SearchBox.Focus();
    }

    private void OnFocusSearch(object sender, RoutedEventArgs e) => FocusSearch();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
        }
    }

    private void OnSearch(object sender, RoutedEventArgs e) => RunSearch();

    private void RunSearch()
    {
        if (_project is null)
        {
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var hits = _project.Search(SearchBox.Text, false, SearchElements.IsChecked == true);
        SearchResults.ItemsSource = hits;
        UpdateStatus($"Найдено совпадений: {hits.Count}");
    }

    private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchResults.SelectedItem is not DitaProject.SearchHit hit)
        {
            return;
        }

        var pane = OpenDocument(hit.File.FullPath);
        var editor = pane?.Author.EditorFor(hit.Node);
        editor?.Focus();
    }

    // ======================================================================
    //  Карта
    // ======================================================================

    private void BuildMapSelector()
    {
        if (_project is null)
        {
            return;
        }

        var maps = _project.Maps.ToList();
        MapSelector.ItemsSource = maps;
        if (maps.Count > 0)
        {
            MapSelector.SelectedIndex = 0;
        }
        else
        {
            MapTreeView.Items.Clear();
        }
    }

    private void OnMapSelected(object sender, SelectionChangedEventArgs e) => BuildMapTree();

    private void BuildMapTree()
    {
        MapTreeView.Items.Clear();
        if (_project is null || MapSelector.SelectedItem is not ProjectFile map)
        {
            return;
        }

        try
        {
            _mapTree = MapTree.Build(_project, map.FullPath);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Карта не читается: {ex.Message}");
            return;
        }

        var rootItem = BuildMapItem(_mapTree.Root, true);
        MapTreeView.Items.Add(rootItem);
    }

    private TreeViewItem BuildMapItem(MapItem item, bool isRoot)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };

        header.Children.Add(new TextBlock
        {
            Text = isRoot ? "🗺" : item.IsResourceOnly ? "🔑" : item.TargetPath is null ? "▸" : "📄",
            Margin = new Thickness(0, 0, 6, 0)
        });

        header.Children.Add(new TextBlock
        {
            Text = item.Title,
            Foreground = item.IsBroken
                ? ThemeManager.Brush("Danger")
                : ThemeManager.Brush("TextPrimary")
        });

        header.Children.Add(new TextBlock
        {
            Text = "  " + item.ElementName,
            FontFamily = InlineStyles.Mono,
            FontSize = 10,
            Foreground = ThemeManager.Brush("TagBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var node = new TreeViewItem { Header = header, Tag = item, IsExpanded = true };
        foreach (var child in item.Children)
        {
            node.Items.Add(BuildMapItem(child, false));
        }

        return node;
    }

    private MapItem? SelectedMapItem => MapTreeView.SelectedItem is TreeViewItem { Tag: MapItem item } ? item : null;

    private void OnMapTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var item = SelectedMapItem;
        if (item is not null)
        {
            UpdateStatus($"{item.ElementName}: {item.Title}{(item.IsBroken ? " — файл не найден" : string.Empty)}");
        }
    }

    private void OnMapTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = SelectedMapItem;
        if (item?.TargetPath is not null && File.Exists(item.TargetPath))
        {
            OpenDocument(item.TargetPath);
        }
        else if (MapSelector.SelectedItem is ProjectFile map)
        {
            OpenDocument(map.FullPath);
        }
    }

    private DocumentPane? OpenMapPane()
    {
        if (MapSelector.SelectedItem is not ProjectFile map)
        {
            return null;
        }

        return OpenDocument(map.FullPath);
    }

    private void OnMapAddTopicref(object sender, RoutedEventArgs e)
    {
        if (_project is null || MapSelector.SelectedItem is not ProjectFile map)
        {
            return;
        }

        var result = Dialogs.InsertXref(_project, map.FullPath);
        if (result is null)
        {
            return;
        }

        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        pane.PushUndo("Добавление ссылки в карту");

        var topicref = DitaNode.Element("topicref");
        topicref.SetAttribute("href", RefResolver.MakeRelative(map.FullPath, result.File.FullPath));

        var target = SelectedMapItem?.Node ?? pane.Document.Root;
        if (ReferenceEquals(target, pane.Document.Root) || target.Name is "map" or "bookmap")
        {
            target.Add(topicref);
        }
        else
        {
            target.Parent?.Insert(target.IndexInParent + 1, topicref);
        }

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        UpdateTabHeaders();
        BuildMapTree();
    }

    private void OnMapAddTopichead(object sender, RoutedEventArgs e)
    {
        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        pane.PushUndo("Добавление раздела в карту");

        var topichead = DitaNode.Element("topichead");
        var meta = DitaNode.Element("topicmeta");
        var navtitle = DitaNode.Element("navtitle");
        navtitle.SetText("Новый раздел");
        meta.Add(navtitle);
        topichead.Add(meta);

        var target = SelectedMapItem?.Node ?? pane.Document.Root;
        if (ReferenceEquals(target, pane.Document.Root) || target.Name is "map" or "bookmap")
        {
            target.Add(topichead);
        }
        else
        {
            target.Parent?.Insert(target.IndexInParent + 1, topichead);
        }

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        UpdateTabHeaders();
        BuildMapTree();
    }

    private void MapStructureOperation(Func<DitaNode, bool> operation, string description)
    {
        var item = SelectedMapItem;
        if (item is null)
        {
            return;
        }

        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        pane.PushUndo(description);
        if (!operation(item.Node))
        {
            UpdateStatus("Операция здесь недоступна.");
            return;
        }

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        UpdateTabHeaders();
        BuildMapTree();
    }

    private void OnMapMoveUp(object sender, RoutedEventArgs e) =>
        MapStructureOperation(EditCommands.MoveUp, "Перемещение в карте");

    private void OnMapMoveDown(object sender, RoutedEventArgs e) =>
        MapStructureOperation(EditCommands.MoveDown, "Перемещение в карте");

    private void OnMapDelete(object sender, RoutedEventArgs e)
    {
        var item = SelectedMapItem;
        if (item is null || !Dialogs.Confirm("Карта", $"Убрать «{item.Title}» из карты?"))
        {
            return;
        }

        MapStructureOperation(EditCommands.Delete, "Удаление из карты");
    }

    private void OnMapIndent(object sender, RoutedEventArgs e) =>
        MapStructureOperation(node =>
        {
            var previous = EditCommands.PreviousElement(node);
            if (previous is null || previous.Name is "title" or "topicmeta")
            {
                return false;
            }

            node.RemoveSelf();
            previous.Add(node);
            return true;
        }, "Вложение в карте");

    private void OnMapOutdent(object sender, RoutedEventArgs e) =>
        MapStructureOperation(node =>
        {
            var parent = node.Parent;
            var grand = parent?.Parent;
            if (parent is null || grand is null)
            {
                return false;
            }

            var index = grand.IndexOf(parent) + 1;
            node.RemoveSelf();
            grand.Insert(index, node);
            return true;
        }, "Вынос из вложения");

    // ======================================================================
    //  Публикация
    // ======================================================================

    private void OnPublishSite(object sender, RoutedEventArgs e) => PublishSite();

    private void PublishSite() => Publish(singleFile: false, exportPdf: false);

    private void OnPublishSingle(object sender, RoutedEventArgs e) => Publish(singleFile: true, exportPdf: false);

    private void OnPublishPdf(object sender, RoutedEventArgs e) => Publish(singleFile: true, exportPdf: true);

    private void OnPublishConditions(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var result = Dialogs.PublishConditions(_project, _conditions);
        if (result is not null)
        {
            _conditions = result;
            UpdateStatus($"Условия сборки обновлены: исключено значений {_conditions.Exclude.Sum(x => x.Value.Count)}.");
        }
    }

    private void Publish(bool singleFile, bool exportPdf)
    {
        if (_project is null || MapSelector.SelectedItem is not ProjectFile map)
        {
            Dialogs.Message("Публикация", "Выберите карту на вкладке «Карта».");
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
            if (pane.IsDirty)
            {
                pane.Save(out _);
            }
        }

        _project.RebuildKeySpace();

        var dialog = new OpenFolderDialog
        {
            Title = "Куда сохранить публикацию",
            DefaultDirectory = _lastOutputDirectory ?? Path.Combine(_project.RootPath, "out")
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _lastOutputDirectory = dialog.FolderName;

        var options = new PublishOptions
        {
            OutputDirectory = dialog.FolderName,
            SingleFile = singleFile,
            ShowDraftComments = _conditions?.ShowDraftComments ?? false,
            Language = "ru"
        };

        if (_conditions is not null)
        {
            foreach (var (attribute, values) in _conditions.Exclude)
            {
                options.ExcludeConditions[attribute] = values;
            }
        }

        BottomTabs.SelectedIndex = 2;
        BuildLog.Text = $"Сборка карты {map.RelativePath}…\n";

        try
        {
            var publisher = new HtmlPublisher(_project);
            var result = publisher.Publish(map.FullPath, options);

            BuildLog.AppendText($"Файлов записано: {result.Files.Count}\n");
            foreach (var warning in result.Warnings)
            {
                BuildLog.AppendText("Предупреждение: " + warning + "\n");
            }

            BuildLog.AppendText("Результат: " + result.EntryFile + "\n");

            if (exportPdf)
            {
                var pdfPath = Path.ChangeExtension(result.EntryFile, ".pdf");
                BuildLog.AppendText("Печать в PDF…\n");
                var error = PdfExporter.ExportToPdf(result.EntryFile, pdfPath);
                if (error is null)
                {
                    BuildLog.AppendText("PDF готов: " + pdfPath + "\n");
                    UpdateStatus("PDF собран: " + pdfPath);
                    OpenInShell(pdfPath);
                    return;
                }

                BuildLog.AppendText("PDF: " + error + "\n");
                Dialogs.Message("Экспорт в PDF", error);
            }

            UpdateStatus("Публикация готова: " + result.EntryFile);
            OpenInShell(result.EntryFile);
        }
        catch (Exception ex)
        {
            BuildLog.AppendText("Ошибка: " + ex.Message + "\n");
            Dialogs.Message("Публикация", ex.Message);
        }
    }

    private static void OpenInShell(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // если открыть нечем — файл всё равно собран
        }
    }

    // ======================================================================
    //  Справка
    // ======================================================================

    private void OnElementHelp(object sender, RoutedEventArgs e) => ShowElementHelp();

    private void ShowElementHelp()
    {
        var node = Current?.Author.CurrentNode;
        if (node is null)
        {
            UpdateStatus("Поставьте курсор в элемент.");
            return;
        }

        var def = DitaCatalog.Default.Get(node.Name);
        if (def is null)
        {
            Dialogs.Message("Справка", $"Элемент <{node.Name}> отсутствует в словаре DITA 1.3.");
            return;
        }

        var allowed = def.Automaton.AllowedNames;
        var attributes = def.Attributes.Keys.OrderBy(a => a, StringComparer.Ordinal);

        Dialogs.Message($"<{def.Name}>",
            $"{def.Description}\n\n" +
            $"Модуль: {def.Domain}\n" +
            $"@class: {def.ClassAttr}\n\n" +
            $"Содержимое: {def.ModelText}\n\n" +
            $"Допустимые дочерние элементы ({allowed.Count}): {string.Join(", ", allowed.Take(40))}" +
            (allowed.Count > 40 ? "…" : string.Empty) +
            $"\n\nАтрибуты: {string.Join(", ", attributes)}");
    }

    private void OnAbout(object sender, RoutedEventArgs e) => Dialogs.About();

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();

        // Раскрашенные в коде части (дерево проекта, карта, структура, атрибуты) не следят
        // за DynamicResource сами по себе — перестраиваем их, чтобы цвета подхватились сразу.
        BuildProjectTree();
        BuildMapTree();
        BuildOutline();
        BuildAttributePanel();
        BuildPalette();
    }
}
