using DitaStudio.Core.Model;
using DitaStudio.Core.Validation;
using DitaStudio.Core.Localization;

namespace DitaStudio.Core.Project;

public sealed partial class DitaProject
{
    // ------------------------------------------------------------- проверка

    /// <summary>plugins — дополнительные правила стиля из плагинов (см. IValidationRulePlugin);
    /// падение одного плагина на одном файле не прерывает проверку остальных.</summary>
    public IReadOnlyList<ValidationIssue> ValidateAll(IReadOnlyList<IValidationRulePlugin>? plugins = null)
    {
        var validator = new DitaValidator(Catalog);
        var issues = new List<ValidationIssue>();

        foreach (var file in _files)
        {
            DitaDocument doc;
            try
            {
                doc = GetDocument(file.FullPath);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    Loc.T("Core_TheFileCannotBeParsedAs", ex.Message),
                    null,
                    file.FullPath));
                continue;
            }

            issues.AddRange(validator.Validate(doc));
            issues.AddRange(RefResolver.ValidateReferences(this, doc));

            if (plugins is null)
            {
                continue;
            }

            foreach (var plugin in plugins)
            {
                try
                {
                    issues.AddRange(plugin.Check(doc));
                }
                catch (Exception ex)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Warning, Loc.T("Msg_ThePlugin0FailedDuringValidation", plugin.Name, ex.Message), null, file.FullPath));
                }
            }
        }

        return issues;
    }
}
