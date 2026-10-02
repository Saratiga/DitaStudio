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

/// <summary>Строит узел CALS-таблицы DITA по параметрам диалога «Вставить таблицу».</summary>
public static class TableNodeBuilder
{
    public static DitaNode Build(TableResult options)
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
}
