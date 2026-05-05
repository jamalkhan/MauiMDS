using MauiMds;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Markdown;

public sealed class MarkdownInlineFormatter
{
    private static readonly Regex CodeTokenRegex = new(
        "(\"[^\"]*\"|'[^']*'|//.*?$|#.*?$|\\b\\d+\\b|\\b[A-Za-z_][A-Za-z0-9_]*\\b)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, bool> _requiresFormattedTextCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<InlineToken>> _inlineTokenCache = new(StringComparer.Ordinal);
    private readonly Dictionary<CodeCacheKey, IReadOnlyList<CodeToken>> _codeTokenCache = [];
    private ILogger<MarkdownInlineFormatter>? _logger;

    public Action<string>? AnchorNavigationCallback { get; set; }

    public void AttachLogger(ILogger<MarkdownInlineFormatter>? logger)
    {
        _logger = logger;
    }

    public bool RequiresFormattedText(string source)
    {
        lock (_cacheLock)
        {
            if (_requiresFormattedTextCache.TryGetValue(source, out var cached))
            {
                _logger?.LogTrace("Inline formatter reused formatted-text requirement cache. Length: {Length}, RequiresFormattedText: {RequiresFormattedText}", source.Length, cached);
                return cached;
            }
        }

        var requiresFormattedText = !string.IsNullOrEmpty(source) &&
                                    (source.Contains('\\', StringComparison.Ordinal) ||
                                     source.Contains('*', StringComparison.Ordinal) ||
                                     source.Contains('`', StringComparison.Ordinal) ||
                                     source.Contains('[', StringComparison.Ordinal) ||
                                     source.Contains('~', StringComparison.Ordinal) ||
                                     source.Contains('=', StringComparison.Ordinal) ||
                                     source.Contains('^', StringComparison.Ordinal) ||
                                     source.Contains('_', StringComparison.Ordinal) ||
                                     source.Contains('<', StringComparison.Ordinal) ||
                                     source.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                                     source.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                                     source.Contains('@', StringComparison.Ordinal));

        lock (_cacheLock)
        {
            _requiresFormattedTextCache[source] = requiresFormattedText;
        }

        _logger?.LogTrace("Inline formatter computed formatted-text requirement. Length: {Length}, RequiresFormattedText: {RequiresFormattedText}", source.Length, requiresFormattedText);

        return requiresFormattedText;
    }

    public FormattedString BuildFormattedText(string source, double fontSize)
    {
        var tokens = GetInlineTokens(source);
        var formatted = new FormattedString();

        foreach (var token in tokens)
        {
            formatted.Spans.Add(CreateInlineSpan(token, fontSize));
        }

        return formatted;
    }

    public FormattedString BuildCodeFormattedText(string code, string language)
    {
        var tokens = GetCodeTokens(code, language);
        var formatted = new FormattedString();

        foreach (var token in tokens)
        {
            formatted.Spans.Add(CreateCodeSpan(token.Text, token.TokenType));
        }

        return formatted;
    }

    public ImageSource? ResolveImageSource(string source, string sourceFilePath)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri))
        {
            return ImageSource.FromUri(absoluteUri);
        }

        if (!string.IsNullOrWhiteSpace(sourceFilePath))
        {
            var fileDirectory = System.IO.Path.GetDirectoryName(sourceFilePath);
            if (!string.IsNullOrWhiteSpace(fileDirectory))
            {
                var candidatePath = System.IO.Path.Combine(fileDirectory, source);
                if (File.Exists(candidatePath))
                {
                    return ImageSource.FromFile(candidatePath);
                }
            }
        }

        return File.Exists(source) ? ImageSource.FromFile(source) : null;
    }

    private IReadOnlyList<InlineToken> GetInlineTokens(string source)
    {
        lock (_cacheLock)
        {
            if (_inlineTokenCache.TryGetValue(source, out var cached))
            {
                _logger?.LogTrace("Inline formatter inline-token cache hit. Length: {Length}, TokenCount: {TokenCount}", source.Length, cached.Count);
                return cached;
            }
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tokens = MarkdownInlineTokenizer.Tokenize(source);

        lock (_cacheLock)
        {
            _inlineTokenCache[source] = tokens;
        }

        _logger?.LogDebug("Inline formatter inline-token cache miss. Length: {Length}, TokenCount: {TokenCount}, ElapsedMs: {ElapsedMs}", source.Length, tokens.Count, stopwatch.ElapsedMilliseconds);

        return tokens;
    }

    private IReadOnlyList<CodeToken> GetCodeTokens(string code, string language)
    {
        var key = new CodeCacheKey(code, language);

        lock (_cacheLock)
        {
            if (_codeTokenCache.TryGetValue(key, out var cached))
            {
                _logger?.LogTrace("Inline formatter code-token cache hit. Length: {Length}, Language: {Language}, TokenCount: {TokenCount}", code.Length, language, cached.Count);
                return cached;
            }
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tokens = TokenizeCode(code, language);

        lock (_cacheLock)
        {
            _codeTokenCache[key] = tokens;
        }

        _logger?.LogDebug("Inline formatter code-token cache miss. Length: {Length}, Language: {Language}, TokenCount: {TokenCount}, ElapsedMs: {ElapsedMs}", code.Length, language, tokens.Count, stopwatch.ElapsedMilliseconds);

        return tokens;
    }

    private List<CodeToken> TokenizeCode(string code, string language)
    {
        var tokens = new List<CodeToken>();
        var keywords = GetCodeKeywords(language);

        foreach (var line in code.Split(Environment.NewLine))
        {
            // Handle full-line comments for languages that use # or //
            var trimmedLine = line.TrimStart();
            if (trimmedLine.StartsWith("//", StringComparison.Ordinal) ||
                (IsHashCommentLanguage(language) && trimmedLine.StartsWith('#')))
            {
                tokens.Add(new CodeToken(line, "comment"));
                tokens.Add(new CodeToken(Environment.NewLine, null));
                continue;
            }

            var lineIndex = 0;

            foreach (Match match in CodeTokenRegex.Matches(line))
            {
                if (match.Index > lineIndex)
                {
                    tokens.Add(new CodeToken(line.Substring(lineIndex, match.Index - lineIndex), null));
                }

                var val = match.Value;
                string? tokenType;

                if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                {
                    tokenType = "string";
                }
                else if (val.StartsWith("//", StringComparison.Ordinal) || val.StartsWith('#'))
                {
                    tokenType = "comment";
                }
                else if (keywords.Contains(val))
                {
                    tokenType = "keyword";
                }
                else
                {
                    tokenType = ClassifyCodeToken(val);
                }

                tokens.Add(new CodeToken(val, tokenType));
                lineIndex = match.Index + match.Length;
            }

            if (lineIndex < line.Length)
            {
                tokens.Add(new CodeToken(line[lineIndex..], null));
            }

            tokens.Add(new CodeToken(Environment.NewLine, null));
        }

        return tokens;
    }

    private static bool IsHashCommentLanguage(string language)
    {
        var norm = language.Trim().ToLowerInvariant();
        return norm is "bash" or "sh" or "python" or "py" or "ruby" or "rb" or "yaml" or "yml";
    }

    private Span CreateInlineSpan(InlineToken token, double fontSize)
    {
        var span = token.Style switch
        {
            InlineTokenStyle.Link => CreateLinkSpan(token.Text, token.Target ?? token.Text, fontSize),
            InlineTokenStyle.InlineCode => CreateInlineCodeSpan(token.Text, fontSize),
            _ => CreateSpan(token.Text, fontSize)
        };

        switch (token.Style)
        {
            case InlineTokenStyle.Bold:
                span.FontAttributes = FontAttributes.Bold;
                break;
            case InlineTokenStyle.Italic:
                span.FontAttributes = FontAttributes.Italic;
                break;
            case InlineTokenStyle.Strikethrough:
                span.TextDecorations = TextDecorations.Strikethrough;
                break;
            case InlineTokenStyle.Underline:
                span.TextDecorations = TextDecorations.Underline;
                break;
            case InlineTokenStyle.Highlight:
                span.SetAppThemeColor(Span.BackgroundColorProperty, AppColors.HighlightBgLight, AppColors.HighlightBgDark);
                break;
            case InlineTokenStyle.Superscript:
                span.FontSize = Math.Max(10, fontSize - 4);
                span.SetAppThemeColor(Span.TextColorProperty, AppColors.SuperscriptLight, AppColors.SuperscriptDark);
                break;
            case InlineTokenStyle.Subscript:
                span.FontSize = Math.Max(10, fontSize - 4);
                span.SetAppThemeColor(Span.TextColorProperty, AppColors.SuperscriptLight, AppColors.SuperscriptDark);
                break;
            case InlineTokenStyle.FootnoteReference:
                span.FontSize = Math.Max(10, fontSize - 4);
                span.FontAttributes = FontAttributes.Bold;
                span.TextColor = AppColors.FootnoteRefColor;
                break;
        }

        return span;
    }

    private Span CreateSpan(string text, double fontSize)
    {
        var span = new Span
        {
            Text = text,
            FontSize = fontSize
        };
        span.SetAppThemeColor(Span.TextColorProperty, AppColors.TextLight, AppColors.TextDark);
        return span;
    }

    private Span CreateInlineCodeSpan(string text, double fontSize)
    {
        var span = CreateSpan(text, fontSize - 1);
        span.FontFamily = "Courier New";
        span.BackgroundColor = AppColors.InlineCodeBg;
        return span;
    }

    private Span CreateLinkSpan(string label, string url, double fontSize)
    {
        var span = CreateSpan(label, fontSize);
        span.TextColor = AppColors.LinkLight;
        span.TextDecorations = TextDecorations.Underline;

        // Anchor links (#heading) are handled via AnchorNavigationCallback
        if (url.StartsWith('#') && AnchorNavigationCallback is not null)
        {
            var anchor = url;
            var callback = AnchorNavigationCallback;
            span.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => callback(anchor))
            });
            return span;
        }

        if (DeviceInfo.Platform == DevicePlatform.MacCatalyst)
        {
            return span;
        }

        span.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await Launcher.OpenAsync(url))
        });
        return span;
    }

    private static HashSet<string> GetCodeKeywords(string language)
    {
        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "csharp" or "cs" => ["abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while"],
            "javascript" or "js" => ["break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "export", "extends", "finally", "for", "function", "if", "import", "in", "instanceof", "let", "new", "null", "of", "return", "static", "super", "switch", "this", "throw", "true", "false", "try", "typeof", "undefined", "var", "void", "while", "with", "yield", "async", "await", "from"],
            "typescript" or "ts" => ["abstract", "any", "as", "async", "await", "boolean", "break", "case", "catch", "class", "const", "continue", "declare", "default", "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for", "from", "function", "if", "implements", "import", "in", "instanceof", "interface", "keyof", "let", "namespace", "never", "new", "null", "number", "of", "override", "private", "protected", "public", "readonly", "return", "static", "string", "super", "switch", "this", "throw", "true", "try", "type", "typeof", "undefined", "unknown", "var", "void", "while", "yield"],
            "python" or "py" => ["and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None", "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while", "with", "yield"],
            "json" => [],
            "sql" => ["add", "all", "alter", "and", "as", "asc", "between", "by", "case", "column", "constraint", "create", "cross", "database", "delete", "desc", "distinct", "drop", "else", "end", "exists", "foreign", "from", "full", "group", "having", "in", "index", "inner", "insert", "into", "is", "join", "key", "left", "like", "limit", "not", "null", "on", "or", "order", "outer", "primary", "references", "right", "rownum", "select", "set", "table", "then", "top", "truncate", "union", "unique", "update", "values", "view", "when", "where"],
            "bash" or "sh" => ["break", "case", "continue", "do", "done", "echo", "elif", "else", "esac", "eval", "exec", "exit", "export", "fi", "for", "function", "if", "in", "local", "read", "readonly", "return", "select", "set", "shift", "source", "then", "unset", "until", "while"],
            "xml" or "html" => [],
            "rust" => ["as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct", "super", "trait", "true", "type", "union", "unsafe", "use", "where", "while"],
            "go" => ["break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for", "func", "go", "goto", "if", "import", "interface", "map", "package", "range", "return", "select", "struct", "switch", "type", "var"],
            "java" or "kotlin" => ["abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default", "do", "double", "else", "enum", "extends", "false", "final", "finally", "float", "for", "goto", "if", "implements", "import", "instanceof", "int", "interface", "long", "native", "new", "null", "package", "private", "protected", "public", "return", "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw", "throws", "transient", "true", "try", "void", "volatile", "while"],
            _ => ["true", "false", "null"]
        };
    }

    private Span CreateCodeSpan(string text, string? tokenType)
    {
        var span = new Span
        {
            Text = text,
            FontFamily = "Courier New",
            FontSize = 15
        };

        switch (tokenType)
        {
            case "keyword":
                span.SetAppThemeColor(Span.TextColorProperty, AppColors.CodeKeywordLight, AppColors.CodeKeywordDark);
                span.FontAttributes = FontAttributes.Bold;
                break;
            case "string":
                span.SetAppThemeColor(Span.TextColorProperty, AppColors.CodeStringLight, AppColors.CodeStringDark);
                break;
            case "comment":
                span.SetAppThemeColor(Span.TextColorProperty, AppColors.CodeCommentLight, AppColors.CodeCommentDark);
                span.FontAttributes = FontAttributes.Italic;
                break;
            case "number":
                span.SetAppThemeColor(Span.TextColorProperty, AppColors.CodeNumberLight, AppColors.CodeNumberDark);
                break;
            case "type":
                span.SetAppThemeColor(Span.TextColorProperty, AppColors.CodeTypeLight, AppColors.CodeTypeDark);
                break;
            default:
                span.SetAppThemeColor(Span.TextColorProperty, AppColors.CodeDefaultLight, AppColors.CodeDefaultDark);
                break;
        }

        return span;
    }

    private static string? ClassifyCodeToken(string value)
    {
        if (value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith('#'))
        {
            return "comment";
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return "string";
        }

        if (double.TryParse(value, out _))
        {
            return "number";
        }

        // Heuristic: PascalCase identifiers are likely types
        if (value.Length > 1 && char.IsUpper(value[0]) && value.Any(char.IsLower))
        {
            return "type";
        }

        return null;
    }

    private readonly record struct CodeToken(string Text, string? TokenType);
    private readonly record struct CodeCacheKey(string Code, string Language);
}
