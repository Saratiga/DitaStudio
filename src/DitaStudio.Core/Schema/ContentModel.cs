namespace DitaStudio.Core.Schema;

/// <summary>Узел контент-модели (аналог модели содержимого в DTD).</summary>
public abstract class ContentModel
{
    public sealed class Empty : ContentModel
    {
        public static readonly Empty Instance = new();

        public override string ToString() => "EMPTY";
    }

    public sealed class Any : ContentModel
    {
        public static readonly Any Instance = new();

        public override string ToString() => "ANY";
    }

    /// <summary>Ссылка на элемент по имени.</summary>
    public sealed class Name : ContentModel
    {
        public Name(string value) => Value = value;

        public string Value { get; }

        public override string ToString() => Value;
    }

    /// <summary>#PCDATA — текстовое содержимое.</summary>
    public sealed class Pcdata : ContentModel
    {
        public static readonly Pcdata Instance = new();

        public override string ToString() => "#PCDATA";
    }

    public sealed class Sequence : ContentModel
    {
        public Sequence(IReadOnlyList<ContentModel> items) => Items = items;

        public IReadOnlyList<ContentModel> Items { get; }

        public override string ToString() => "(" + string.Join(", ", Items) + ")";
    }

    public sealed class Choice : ContentModel
    {
        public Choice(IReadOnlyList<ContentModel> items) => Items = items;

        public IReadOnlyList<ContentModel> Items { get; }

        public override string ToString() => "(" + string.Join(" | ", Items) + ")";
    }

    /// <summary>Повторение: ? * +</summary>
    public sealed class Repeat : ContentModel
    {
        public Repeat(ContentModel item, int min, int max)
        {
            Item = item;
            Min = min;
            Max = max;
        }

        public ContentModel Item { get; }

        public int Min { get; }

        /// <summary>-1 — без ограничения.</summary>
        public int Max { get; }

        public override string ToString()
        {
            var suffix = (Min, Max) switch
            {
                (0, 1) => "?",
                (0, -1) => "*",
                (1, -1) => "+",
                _ => $"{{{Min},{Max}}}"
            };
            return Item + suffix;
        }
    }

    /// <summary>Собирает все имена элементов, встречающиеся в модели.</summary>
    public IEnumerable<string> CollectNames()
    {
        var acc = new List<string>();
        Collect(this, acc);
        return acc;

        static void Collect(ContentModel m, List<string> acc)
        {
            switch (m)
            {
                case Name n:
                    acc.Add(n.Value);
                    break;
                case Sequence s:
                    foreach (var i in s.Items)
                    {
                        Collect(i, acc);
                    }

                    break;
                case Choice c:
                    foreach (var i in c.Items)
                    {
                        Collect(i, acc);
                    }

                    break;
                case Repeat r:
                    Collect(r.Item, acc);
                    break;
            }
        }
    }

    public bool AllowsText()
    {
        switch (this)
        {
            case Pcdata:
                return true;
            case Any:
                return true;
            case Sequence s:
                return s.Items.Any(i => i.AllowsText());
            case Choice c:
                return c.Items.Any(i => i.AllowsText());
            case Repeat r:
                return r.Item.AllowsText();
            default:
                return false;
        }
    }
}
