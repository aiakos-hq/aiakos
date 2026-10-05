using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Aiakos.Spec;

internal static class CanonicalJson
{
    private const string UnsupportedValueMessage = "unsupported canonical JSON value";

    internal static string Write(JsonElement value)
    {
        var builder = new StringBuilder();
        WriteValue(value, builder);
        return builder.ToString();
    }

    private static void WriteValue(JsonElement value, StringBuilder builder)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(value, builder);
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    WriteValue(item, builder);
                    first = false;
                }

                builder.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(ReadString(value), builder);
                break;
            case JsonValueKind.Number:
                WriteNumber(value, builder);
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            default:
                throw UnsupportedValue();
        }
    }

    private static void WriteObject(JsonElement value, StringBuilder builder)
    {
        var properties = new List<(string Name, JsonElement Value)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            string name;
            try
            {
                name = NormalizeAndValidate(property.Name);
            }
            catch (InvalidOperationException)
            {
                throw UnsupportedValue();
            }

            if (!names.Add(name))
            {
                throw UnsupportedValue();
            }

            properties.Add((name, property.Value));
        }

        properties.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        builder.Append('{');
        for (var index = 0; index < properties.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            WriteString(properties[index].Name, builder);
            builder.Append(':');
            WriteValue(properties[index].Value, builder);
        }

        builder.Append('}');
    }

    private static void WriteNumber(JsonElement value, StringBuilder builder)
    {
        var raw = value.GetRawText();
        var digitStart = raw[0] == '-' ? 1 : 0;
        if (digitStart == raw.Length || (raw[digitStart] == '0' && digitStart + 1 != raw.Length))
        {
            throw UnsupportedValue();
        }

        for (var index = digitStart; index < raw.Length; index++)
        {
            if (raw[index] is < '0' or > '9')
            {
                throw UnsupportedValue();
            }
        }

        if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            throw UnsupportedValue();
        }

        builder.Append(number.ToString(CultureInfo.InvariantCulture));
    }

    private static string ReadString(JsonElement value)
    {
        try
        {
            return NormalizeAndValidate(value.GetString()!);
        }
        catch (InvalidOperationException)
        {
            throw UnsupportedValue();
        }
    }

    private static string NormalizeAndValidate(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw UnsupportedValue();
                }

                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                throw UnsupportedValue();
            }
        }

        return value.Normalize(NormalizationForm.FormC);
    }

    private static void WriteString(string value, StringBuilder builder)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\t': builder.Append("\\t"); break;
                case '\n': builder.Append("\\n"); break;
                case '\f': builder.Append("\\f"); break;
                case '\r': builder.Append("\\r"); break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u00");
                        builder.Append(((int)character).ToString("x2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static ArgumentException UnsupportedValue() => new(UnsupportedValueMessage);
}
