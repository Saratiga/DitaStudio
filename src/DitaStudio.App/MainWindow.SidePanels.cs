using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DitaStudio.App.Authoring;
using DitaStudio.App.Views;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.App;

// Правая панель: атрибуты выделенного элемента, палитра вставки, структура (дерево топика).
public partial class MainWindow
{
    private void OnEditorSelectionChanged()
    {
        BuildAttributePanel();
        BuildPalette();
        UpdateContextText();
    }

    private void UpdateContextText()
    {
        var node = Current?.Author.CurrentNode;
        ViewModel.ContextText = node is null ? string.Empty : node.Path;
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
            ViewModel.Insert.InsertElementCommand.Execute(name);
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
}
