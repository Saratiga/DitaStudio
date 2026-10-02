using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

/// <summary>Перевод таблицы соответствий (reltable) карты в строки диалога и обратно.</summary>
public static class RelTableConverter
{
    public static List<List<RelTableCell>> Parse(DitaProject project, DitaNode? reltable, string mapPath)
    {
        var rows = new List<List<RelTableCell>>();
        if (reltable is null)
        {
            return rows;
        }

        foreach (var relrow in reltable.ElementChildren().Where(n => n.Name == "relrow"))
        {
            var row = new List<RelTableCell>();
            foreach (var relcell in relrow.ElementChildren().Where(n => n.Name == "relcell"))
            {
                var href = relcell.FirstElement("topicref")?.GetAttribute("href");
                var cell = new RelTableCell();
                if (!string.IsNullOrWhiteSpace(href))
                {
                    var reference = RefResolver.Parse(mapPath, href!);
                    if (reference.Path is not null)
                    {
                        cell.File = project.Files.FirstOrDefault(
                            f => string.Equals(f.FullPath, reference.Path, StringComparison.OrdinalIgnoreCase));
                        cell.TopicId = reference.TopicId;
                    }
                }

                row.Add(cell);
            }

            rows.Add(row);
        }

        return rows;
    }

    public static DitaNode Build(List<List<RelTableCell>> rows, string mapPath)
    {
        var reltable = DitaNode.Element("reltable");
        foreach (var row in rows)
        {
            var relrow = DitaNode.Element("relrow");
            foreach (var cell in row)
            {
                var relcell = DitaNode.Element("relcell");
                if (cell.File is not null)
                {
                    var href = RefResolver.MakeRelative(mapPath, cell.File.FullPath);
                    if (!string.IsNullOrEmpty(cell.TopicId))
                    {
                        href += "#" + cell.TopicId;
                    }

                    var topicref = DitaNode.Element("topicref");
                    topicref.SetAttribute("href", href);
                    relcell.Add(topicref);
                }

                relrow.Add(relcell);
            }

            reltable.Add(relrow);
        }

        return reltable;
    }
}
