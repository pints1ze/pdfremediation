namespace PdfRemediation.Infrastructure.Pdf;

/// <summary>
/// One operator in a decoded content stream with its operands.
/// <see cref="Start"/> is where its first operand starts (or the operator, if it has none);
/// <see cref="End"/> is just past the operator. Both are byte offsets into the decoded stream.
/// </summary>
internal sealed record ContentOperation(string Operator, int Start, int End, IReadOnlyList<string> Operands);

/// <summary>
/// Splits a decoded content stream into operations at exact byte boundaries.
/// It only finds boundaries; it does not interpret operands beyond their raw text.
/// </summary>
internal static class ContentStreamLexer
{
    public static IReadOnlyList<ContentOperation> Lex(ReadOnlySpan<byte> content)
    {
        var operations = new List<ContentOperation>();
        var operands = new List<string>();
        int? operandsStart = null;
        var depth = 0;
        var position = 0;

        while (true)
        {
            SkipWhitespaceAndComments(content, ref position);
            if (position >= content.Length)
            {
                break;
            }

            var start = position;
            var b = content[position];
            switch (b)
            {
                case (byte)'(':
                    SkipLiteralString(content, ref position);
                    break;
                case (byte)'<' when position + 1 < content.Length && content[position + 1] == '<':
                    position += 2;
                    depth++;
                    break;
                case (byte)'>' when position + 1 < content.Length && content[position + 1] == '>':
                    position += 2;
                    depth--;
                    break;
                case (byte)'<':
                    SkipHexString(content, ref position);
                    break;
                case (byte)'[':
                    position++;
                    depth++;
                    break;
                case (byte)']':
                    position++;
                    depth--;
                    break;
                case (byte)'/':
                    position++;
                    SkipRegular(content, ref position);
                    break;
                default:
                    if (IsDelimiter(b))
                    {
                        throw new PdfProcessingException("CONTENT_STREAM_UNREADABLE", $"Unexpected '{(char)b}' at byte {position} of a content stream.");
                    }

                    SkipRegular(content, ref position);
                    break;
            }

            var token = System.Text.Encoding.Latin1.GetString(content[start..position]);
            var isOperator = depth == 0 && IsOperatorToken(b, token);
            if (!isOperator)
            {
                operandsStart ??= start;
                operands.Add(token);
                continue;
            }

            if (token == "BI")
            {
                position = SkipInlineImage(content, position);
            }

            operations.Add(new ContentOperation(token, operandsStart ?? start, position, operands.ToArray()));
            operands.Clear();
            operandsStart = null;
        }

        return operations;
    }

    private static bool IsOperatorToken(byte first, string token)
    {
        if (first is (byte)'(' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'/')
        {
            return false;
        }

        if (token is "true" or "false" or "null")
        {
            return false;
        }

        var c = token[0];
        return !(char.IsAsciiDigit(c) || c is '+' or '-' or '.');
    }

    /// <summary>Moves past an inline image's dictionary and data, returning the offset just after its EI operator.</summary>
    private static int SkipInlineImage(ReadOnlySpan<byte> content, int position)
    {
        var id = content[position..].IndexOf("ID"u8);
        if (id < 0)
        {
            throw new PdfProcessingException("CONTENT_STREAM_UNREADABLE", "An inline image has no ID operator.");
        }

        var data = position + id + 2;
        for (var i = data + 1; i + 1 < content.Length; i++)
        {
            if (content[i] == 'E' && content[i + 1] == 'I' && IsWhitespace(content[i - 1])
                && (i + 2 == content.Length || IsWhitespace(content[i + 2]) || IsDelimiter(content[i + 2])))
            {
                return i + 2;
            }
        }

        throw new PdfProcessingException("CONTENT_STREAM_UNREADABLE", "An inline image has no EI operator.");
    }

    private static void SkipWhitespaceAndComments(ReadOnlySpan<byte> content, ref int position)
    {
        while (position < content.Length)
        {
            if (IsWhitespace(content[position]))
            {
                position++;
            }
            else if (content[position] == '%')
            {
                while (position < content.Length && content[position] is not ((byte)'\r' or (byte)'\n'))
                {
                    position++;
                }
            }
            else
            {
                return;
            }
        }
    }

    private static void SkipLiteralString(ReadOnlySpan<byte> content, ref int position)
    {
        var nesting = 0;
        while (position < content.Length)
        {
            var b = content[position++];
            if (b == '\\')
            {
                position++;
            }
            else if (b == '(')
            {
                nesting++;
            }
            else if (b == ')' && --nesting == 0)
            {
                return;
            }
        }

        throw new PdfProcessingException("CONTENT_STREAM_UNREADABLE", "A literal string in a content stream is not closed.");
    }

    private static void SkipHexString(ReadOnlySpan<byte> content, ref int position)
    {
        var close = content[position..].IndexOf((byte)'>');
        if (close < 0)
        {
            throw new PdfProcessingException("CONTENT_STREAM_UNREADABLE", "A hex string in a content stream is not closed.");
        }

        position += close + 1;
    }

    private static void SkipRegular(ReadOnlySpan<byte> content, ref int position)
    {
        while (position < content.Length && !IsWhitespace(content[position]) && !IsDelimiter(content[position]))
        {
            position++;
        }
    }

    private static bool IsWhitespace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool IsDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
}
