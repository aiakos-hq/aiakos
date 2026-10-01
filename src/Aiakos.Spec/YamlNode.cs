using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Aiakos.Spec;

internal sealed class YamlNode
{
    public YamlNode(YamlNodeKind kind, Mark mark)
    {
        Kind = kind;
        Mark = mark;
    }

    public YamlNodeKind Kind { get; }
    public Mark Mark { get; }
    public string? Value { get; set; }
    public bool IsNull { get; set; }
    public bool IsTaggedOrAlias { get; set; }
    public List<YamlEntry> Entries { get; } = [];
    public List<YamlNode> Items { get; } = [];
}

internal sealed record YamlEntry(YamlNode Key, YamlNode Value);

internal enum YamlNodeKind
{
    Scalar,
    Mapping,
    Sequence
}

internal sealed class YamlEventTreeParser
{
    private readonly IParser parser;
    private readonly Action<string, Mark, string> report;
    private readonly string source;

    public YamlEventTreeParser(IParser parser, string source, Action<string, Mark, string> report)
    {
        this.parser = parser;
        this.source = source;
        this.report = report;
    }

    public YamlNode? Parse()
    {
        parser.Consume<StreamStart>();
        if (parser.Current is StreamEnd)
        {
            return null;
        }

        if (parser.Current is not DocumentStart)
        {
            throw new YamlException(parser.Current?.Start ?? Mark.Empty, parser.Current?.End ?? Mark.Empty, "Expected a document start.");
        }

        parser.Consume<DocumentStart>();
        var root = parser.Current is DocumentEnd ? null : ParseNode();
        parser.Consume<DocumentEnd>();
        if (parser.Current is not StreamEnd)
        {
            var mark = parser.Current?.Start ?? Mark.Empty;
            throw new YamlException(mark, mark, "Only one YAML document is supported.");
        }

        return root;
    }

    private YamlNode ParseNode()
    {
        var current = parser.Current ?? throw new YamlException("Unexpected end of YAML stream.");
        if (current is Scalar scalar)
        {
            parser.MoveNext();
            ReportMetadata(scalar.Anchor, scalar.Tag, scalar.Start);
            var isNull = scalar.Style == ScalarStyle.Plain && (scalar.Value.Length == 0 || scalar.Value is "~" or "null");
            return new YamlNode(YamlNodeKind.Scalar, scalar.Start)
            {
                Value = scalar.Value,
                IsNull = isNull,
                IsTaggedOrAlias = !scalar.Tag.IsEmpty
            };
        }

        if (current is AnchorAlias alias)
        {
            parser.MoveNext();
            report("AIK1003", alias.Start, "anchors and aliases are not supported");
            return new YamlNode(YamlNodeKind.Scalar, alias.Start) { IsTaggedOrAlias = true };
        }

        if (current is MappingStart mappingStart)
        {
            parser.MoveNext();
            ReportMetadata(mappingStart.Anchor, mappingStart.Tag, mappingStart.Start);
            var mapping = new YamlNode(YamlNodeKind.Mapping, mappingStart.Start)
            {
                IsTaggedOrAlias = !mappingStart.Tag.IsEmpty
            };
            var keys = new HashSet<string>(StringComparer.Ordinal);
            while (parser.Current is not MappingEnd)
            {
                var key = ParseNode();
                var value = ParseNode();
                if (key.Kind == YamlNodeKind.Scalar && key.Value is { } keyValue)
                {
                    if (keyValue == "<<")
                    {
                        report("AIK1003", key.Mark, "merge keys ('<<') are not supported");
                    }

                    if (!keys.Add(keyValue))
                    {
                        report("AIK1003", key.Mark, $"duplicate key '{keyValue}'");
                    }
                }

                mapping.Entries.Add(new YamlEntry(key, value));
            }

            parser.Consume<MappingEnd>();
            return mapping;
        }

        if (current is SequenceStart sequenceStart)
        {
            parser.MoveNext();
            ReportMetadata(sequenceStart.Anchor, sequenceStart.Tag, sequenceStart.Start);
            var sequence = new YamlNode(YamlNodeKind.Sequence, sequenceStart.Start)
            {
                IsTaggedOrAlias = !sequenceStart.Tag.IsEmpty
            };
            while (parser.Current is not SequenceEnd)
            {
                sequence.Items.Add(ParseNode());
            }

            parser.Consume<SequenceEnd>();
            return sequence;
        }

        throw new YamlException(current.Start, current.End, $"Unexpected YAML event '{current.GetType().Name}'.");
    }

    private void ReportMetadata(AnchorName anchor, TagName tag, Mark mark)
    {
        if (!anchor.IsEmpty)
        {
            report("AIK1003", mark, "anchors and aliases are not supported");
        }

        if (!tag.IsEmpty)
        {
            report("AIK1003", mark, $"tag '{ReadTag(mark) ?? tag.Value}' is not supported");
        }
    }

    private string? ReadTag(Mark mark)
    {
        var lines = source.Split('\n');
        if (mark.Line < 1 || mark.Line > lines.Length || mark.Column < 1)
        {
            return null;
        }

        var line = lines[(int)mark.Line - 1];
        var start = (int)mark.Column - 1;
        if (start >= line.Length || line[start] != '!')
        {
            return null;
        }

        var end = start + 1;
        while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] is not ',' and not '[' and not ']' and not '{' and not '}')
        {
            end++;
        }

        return line[start..end];
    }
}
