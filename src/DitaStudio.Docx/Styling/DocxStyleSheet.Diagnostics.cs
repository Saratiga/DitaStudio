using System.Text.RegularExpressions;
using DitaStudio.Core.Model;

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
                        _messages.Add("CSS → DOCX: селекторы не поддерживаются, правила пропущены: " + Sample(_selectors) +
                                      ". В DOCX работают теги и классы HTML-публикации (p, h1, .note, .shortdesc…), классы outputclass и " +
                                      "селекторы по элементам DITA: имя, .класс, #id, [атрибут], потомок, ребёнок, соседи, :first-child, " +
                                      ":last-child, :nth-child(), :nth-of-type(), :not(), :empty, ::before, ::after.");
                    }

                    if (_properties.Count > 0)
                    {
                        _messages.Add("CSS → DOCX: свойства не переносятся в Word: " + Sample(_properties) + ".");
                    }

                    if (_values.Count > 0)
                    {
                        _messages.Add("CSS → DOCX: значения не распознаны: " + Sample(_values) + ".");
                    }
                }

                return _messages;
            }
        }

        private static string Sample(IReadOnlyCollection<string> items)
        {
            const int limit = 8;
            var shown = string.Join(", ", items.Take(limit));
            return items.Count > limit ? $"{shown} и ещё {items.Count - limit}" : shown;
        }
    }
}
