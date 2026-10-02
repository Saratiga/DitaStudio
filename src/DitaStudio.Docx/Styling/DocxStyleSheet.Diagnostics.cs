using System.Text.RegularExpressions;
using DitaStudio.Core.Model;
using DitaStudio.Core.Localization;

namespace DitaStudio.Docx.Styling;

// Предупреждения разбора CSS.
public sealed partial class DocxStyleSheet
{
    internal sealed class Diagnostics
    {
        private readonly List<string> _messages = new();
        private readonly SortedSet<string> _selectors = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _properties = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _values = new(StringComparer.Ordinal);
        private bool _finalized;

        public void Add(string message) => _messages.Add(message);

        public void UnsupportedSelector(string selector) => _selectors.Add(selector);

        public void UnsupportedProperty(string property) => _properties.Add(property);

        public void UnsupportedValue(string property, string value) => _values.Add($"{property}: {value}");

        public IReadOnlyList<string> Messages
        {
            get
            {
                if (!_finalized)
                {
                    _finalized = true;
                    if (_selectors.Count > 0)
                    {
                        _messages.Add(Loc.T("Core_CSSDOCXTheseSelectorsAreNot") + Sample(_selectors) +
                                      Loc.T("Core_InDOCXTheTagsAndClasses") +
                                      Loc.T("Core_SelectorsByDITAElementsNameClass") +
                                      ":last-child, :nth-child(), :nth-of-type(), :not(), :empty, ::before, ::after.");
                    }

                    if (_properties.Count > 0)
                    {
                        _messages.Add(Loc.T("Core_CSSDOCXThesePropertiesAreNot") + Sample(_properties) + ".");
                    }

                    if (_values.Count > 0)
                    {
                        _messages.Add(Loc.T("Core_CSSDOCXTheseValuesWereNot") + Sample(_values) + ".");
                    }
                }

                return _messages;
            }
        }

        private static string Sample(IReadOnlyCollection<string> items)
        {
            const int limit = 8;
            var shown = string.Join(", ", items.Take(limit));
            return items.Count > limit ? Loc.T("Core_0And1More", shown, items.Count - limit) : shown;
        }
    }
}
