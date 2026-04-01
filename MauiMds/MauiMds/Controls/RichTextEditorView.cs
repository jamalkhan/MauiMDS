using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using MauiMds.Models;
using Microsoft.Maui.Controls.Shapes;
#if MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;
#endif

namespace MauiMds.Controls;

public sealed class RichTextEditorView : ContentView, IEditorSurface
{
    public static readonly BindableProperty BlocksProperty = BindableProperty.Create(
        nameof(Blocks),
        typeof(IReadOnlyList<MarkdownBlock>),
        typeof(RichTextEditorView),
        default(IReadOnlyList<MarkdownBlock>));

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(RichTextEditorView),
        string.Empty,
        BindingMode.TwoWay,
        propertyChanged: OnTextPropertyChanged);

    public static readonly BindableProperty FallbackToMarkdownCommandProperty = BindableProperty.Create(
        nameof(FallbackToMarkdownCommand),
        typeof(ICommand),
        typeof(RichTextEditorView));

    private readonly Editor _editor;
    private readonly Dictionary<RichBlockKind, Button> _toolbarButtons = [];
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private bool _isApplyingExternalText;
    private bool _isRefreshingNativeStyle;

#if MACCATALYST
    private UITextView? _nativeTextView;
#endif

    public RichTextEditorView()
    {
        var toolbar = new HorizontalStackLayout
        {
            Spacing = 8
        };
        AddToolbarButton(toolbar, RichBlockKind.Paragraph, "Paragraph", () => ApplyBlockTransform(RichBlockKind.Paragraph));
        AddToolbarButton(toolbar, RichBlockKind.Header1, "H1", () => ApplyBlockTransform(RichBlockKind.Header1));
        AddToolbarButton(toolbar, RichBlockKind.Header2, "H2", () => ApplyBlockTransform(RichBlockKind.Header2));
        AddToolbarButton(toolbar, RichBlockKind.Header3, "H3", () => ApplyBlockTransform(RichBlockKind.Header3));
        AddToolbarButton(toolbar, RichBlockKind.Bullet, "Bullet", () => ApplyBlockTransform(RichBlockKind.Bullet));
        AddToolbarButton(toolbar, RichBlockKind.Task, "Checklist", () => ApplyBlockTransform(RichBlockKind.Task));
        AddToolbarButton(toolbar, RichBlockKind.Quote, "Quote", () => ApplyBlockTransform(RichBlockKind.Quote));
        AddToolbarButton(toolbar, RichBlockKind.Code, "Code", () => ApplyBlockTransform(RichBlockKind.Code));

        var fallbackButton = new Button
        {
            Text = "Markdown",
            Padding = new Thickness(12, 7),
            CornerRadius = 12,
            FontSize = 12,
            BackgroundColor = Color.FromArgb("#5E584E"),
            TextColor = Colors.White
        };
        fallbackButton.Clicked += (_, _) =>
        {
            if (FallbackToMarkdownCommand?.CanExecute(null) == true)
            {
                FallbackToMarkdownCommand.Execute(null);
            }
        };

        var toolbarBorder = new Border
        {
            BackgroundColor = Color.FromArgb("#F4F0E6"),
            Stroke = Color.FromArgb("#D8D0C2"),
            StrokeThickness = 1,
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 0, 0, 12),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 12,
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            new Label
                            {
                                Text = "Format the current block or selection",
                                FontSize = 12,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#6F6250")
                            },
                            toolbar
                        }
                    },
                    fallbackButton
                }
            }
        };
        Grid.SetColumn(fallbackButton, 1);

        _editor = new Editor
        {
            AutoSize = EditorAutoSizeOption.Disabled,
            FontSize = 18,
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#161616"),
            Placeholder = "Write Markdown here...",
            Margin = new Thickness(0),
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        _editor.TextChanged += OnEditorTextChanged;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        _editor.Focused += (_, _) => UpdateToolbarState();
        _editor.HandlerChanged += OnEditorHandlerChanged;

        var editorBorder = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
            Content = _editor
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        layout.Children.Add(toolbarBorder);
        layout.Children.Add(editorBorder);
        Grid.SetRow(editorBorder, 1);

        Content = layout;

        UpdateToolbarState();
    }

    public IReadOnlyList<MarkdownBlock>? Blocks
    {
        get => (IReadOnlyList<MarkdownBlock>?)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? FallbackToMarkdownCommand
    {
        get => (ICommand?)GetValue(FallbackToMarkdownCommandProperty);
        set => SetValue(FallbackToMarkdownCommandProperty, value);
    }

    public void FocusEditor() => _editor.Focus();

    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(Text ?? string.Empty);
        ApplyExternalText(_undoStack.Pop(), preserveSelection: true);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(Text ?? string.Empty);
        ApplyExternalText(_redoStack.Pop(), preserveSelection: true);
    }

    public async Task CopySelectionAsync()
    {
        var selected = GetSelectedText();
        if (!string.IsNullOrEmpty(selected))
        {
            await Clipboard.Default.SetTextAsync(selected);
        }
    }

    public async Task CutSelectionAsync()
    {
        var selected = GetSelectedText();
        if (string.IsNullOrEmpty(selected))
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(selected);
        ReplaceSelection(string.Empty);
    }

    public async Task PasteAsync()
    {
        var pasted = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrEmpty(pasted))
        {
            ReplaceSelection(pasted);
        }
    }

    public void ApplyHeaderPrefix(int level)
    {
        ApplyBlockTransform(level switch
        {
            1 => RichBlockKind.Header1,
            2 => RichBlockKind.Header2,
            _ => RichBlockKind.Header3
        });
    }

    public bool FindNext(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var text = _editor.Text ?? string.Empty;
        var start = Math.Clamp(_editor.CursorPosition + _editor.SelectionLength, 0, text.Length);
        var index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && start > 0)
        {
            index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }

        if (index < 0)
        {
            return false;
        }

        _editor.Focus();
        SetSelection(index, query.Length);
        return true;
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is null)
        {
#if MACCATALYST
            DetachNativeTextView();
#endif
        }
    }

    private static void OnTextPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((RichTextEditorView)bindable).OnExternalTextChanged((string?)newValue ?? string.Empty);
    }

    private void OnExternalTextChanged(string newText)
    {
        if (_isApplyingExternalText)
        {
            return;
        }

        if (string.Equals(_editor.Text, newText, StringComparison.Ordinal))
        {
            RefreshNativeStyling();
            UpdateToolbarState();
            return;
        }

        _isApplyingExternalText = true;
        try
        {
            _editor.Text = newText;
        }
        finally
        {
            _isApplyingExternalText = false;
        }

        RefreshNativeStyling();
        UpdateToolbarState();
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingExternalText || _isRefreshingNativeStyle)
        {
            return;
        }

        var previous = e.OldTextValue ?? string.Empty;
        var current = e.NewTextValue ?? string.Empty;
        if (!string.Equals(previous, current, StringComparison.Ordinal))
        {
            _undoStack.Push(previous);
            _redoStack.Clear();
        }

        _isApplyingExternalText = true;
        try
        {
            SetValue(TextProperty, current);
        }
        finally
        {
            _isApplyingExternalText = false;
        }

        RefreshNativeStyling();
        UpdateToolbarState();
    }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Editor.CursorPosition) or nameof(Editor.SelectionLength))
        {
            UpdateToolbarState();
            RefreshTypingAttributes();
        }
    }

    private void AddToolbarButton(HorizontalStackLayout toolbar, RichBlockKind kind, string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            Padding = new Thickness(12, 7),
            CornerRadius = 12,
            FontSize = 12,
            BackgroundColor = Color.FromArgb("#E8E0CF"),
            TextColor = Color.FromArgb("#1A1A1A")
        };
        button.Clicked += (_, _) => action();
        _toolbarButtons[kind] = button;
        toolbar.Children.Add(button);
    }

    private void ApplyBlockTransform(RichBlockKind kind)
    {
        var text = _editor.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var start = Math.Clamp(_editor.CursorPosition, 0, text.Length);
        var length = Math.Clamp(_editor.SelectionLength, 0, text.Length - start);

        if (length > 0)
        {
            ApplySelectionTransform(kind, text, start, length);
            return;
        }

        ApplyCurrentBlockTransform(kind, text, start);
    }

    private void ApplySelectionTransform(RichBlockKind kind, string text, int start, int length)
    {
        var selected = text.Substring(start, length);
        var normalizedSelection = StripKnownMarkdownPrefix(selected.Trim());
        if (string.IsNullOrWhiteSpace(normalizedSelection))
        {
            return;
        }

        var before = text[..start].TrimEnd();
        var after = text[(start + length)..].TrimStart();
        var transformed = FormatBlock(kind, normalizedSelection, 1);

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(before))
        {
            builder.Append(before);
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.Append(transformed);

        if (!string.IsNullOrWhiteSpace(after))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(after);
        }

        var updated = builder.ToString();
        ApplyUpdatedText(updated, updated.IndexOf(transformed, StringComparison.Ordinal), transformed.Length);
    }

    private void ApplyCurrentBlockTransform(RichBlockKind kind, string text, int cursor)
    {
        var (lineStart, lineLength) = GetCurrentLineRange(text, cursor);
        var line = text.Substring(lineStart, lineLength);
        var indent = GetLeadingWhitespace(line);
        var stripped = StripKnownMarkdownPrefix(line.Trim());
        var updatedLine = $"{indent}{FormatBlock(kind, stripped, 1)}";
        var updatedText = text.Remove(lineStart, lineLength).Insert(lineStart, updatedLine);
        ApplyUpdatedText(updatedText, Math.Min(lineStart + updatedLine.Length, updatedText.Length), 0);
    }

    private void ReplaceSelection(string replacementText)
    {
        var text = _editor.Text ?? string.Empty;
        var start = Math.Clamp(_editor.CursorPosition, 0, text.Length);
        var length = Math.Clamp(_editor.SelectionLength, 0, text.Length - start);
        var updated = text.Remove(start, length).Insert(start, replacementText);
        ApplyUpdatedText(updated, start + replacementText.Length, 0);
    }

    private void ApplyUpdatedText(string updatedText, int cursorPosition, int selectionLength)
    {
        ApplyExternalText(updatedText, preserveSelection: false);
        _editor.Focus();
        SetSelection(Math.Clamp(cursorPosition, 0, updatedText.Length), selectionLength);
    }

    private void ApplyExternalText(string updatedText, bool preserveSelection)
    {
        var previousText = _editor.Text ?? string.Empty;
        var previousCursor = _editor.CursorPosition;
        var previousSelection = _editor.SelectionLength;

        if (!string.Equals(previousText, updatedText, StringComparison.Ordinal))
        {
            _undoStack.Push(previousText);
            _redoStack.Clear();
        }

        _isApplyingExternalText = true;
        try
        {
            _editor.Text = updatedText;
            SetValue(TextProperty, updatedText);
        }
        finally
        {
            _isApplyingExternalText = false;
        }

        RefreshNativeStyling();
        UpdateToolbarState();

        if (preserveSelection)
        {
            SetSelection(
                Math.Clamp(previousCursor, 0, updatedText.Length),
                Math.Clamp(previousSelection, 0, Math.Max(0, updatedText.Length - Math.Clamp(previousCursor, 0, updatedText.Length))));
        }
    }

    private string GetSelectedText()
    {
        var text = _editor.Text ?? string.Empty;
        var start = Math.Clamp(_editor.CursorPosition, 0, text.Length);
        var length = Math.Clamp(_editor.SelectionLength, 0, text.Length - start);
        return length == 0 ? string.Empty : text.Substring(start, length);
    }

    private void UpdateToolbarState()
    {
        var kind = DetermineCurrentBlockKind();
        foreach (var (buttonKind, button) in _toolbarButtons)
        {
            var isActive = buttonKind == kind;
            button.BackgroundColor = isActive ? Color.FromArgb("#1D1D1B") : Color.FromArgb("#E8E0CF");
            button.TextColor = isActive ? Color.FromArgb("#F7F2E8") : Color.FromArgb("#1A1A1A");
        }
    }

    private RichBlockKind DetermineCurrentBlockKind()
    {
        var text = _editor.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return RichBlockKind.Paragraph;
        }

        var cursor = Math.Clamp(_editor.CursorPosition, 0, text.Length);
        if (IsInsideCodeFence(text, cursor))
        {
            return RichBlockKind.Code;
        }

        var (lineStart, lineLength) = GetCurrentLineRange(text, cursor);
        var line = text.Substring(lineStart, lineLength).TrimStart();
        if (line.StartsWith("### ", StringComparison.Ordinal))
        {
            return RichBlockKind.Header3;
        }

        if (line.StartsWith("## ", StringComparison.Ordinal))
        {
            return RichBlockKind.Header2;
        }

        if (line.StartsWith("# ", StringComparison.Ordinal))
        {
            return RichBlockKind.Header1;
        }

        if (Regex.IsMatch(line, @"^[-*]\s\[[ xX]\]\s"))
        {
            return RichBlockKind.Task;
        }

        if (line.StartsWith("> ", StringComparison.Ordinal))
        {
            return RichBlockKind.Quote;
        }

        if (Regex.IsMatch(line, @"^[-*]\s"))
        {
            return RichBlockKind.Bullet;
        }

        return RichBlockKind.Paragraph;
    }

    private static (int Start, int Length) GetCurrentLineRange(string text, int cursor)
    {
        var lineStart = cursor;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        var lineEnd = cursor;
        while (lineEnd < text.Length && text[lineEnd] != '\n')
        {
            lineEnd++;
        }

        return (lineStart, lineEnd - lineStart);
    }

    private static string GetLeadingWhitespace(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]) && line[index] != '\n' && line[index] != '\r')
        {
            index++;
        }

        return index == 0 ? string.Empty : line[..index];
    }

    private static string StripKnownMarkdownPrefix(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
        {
            return trimmed[4..].Trim();
        }

        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            return trimmed[3..].Trim();
        }

        if (trimmed.StartsWith("# ", StringComparison.Ordinal))
        {
            return trimmed[2..].Trim();
        }

        if (Regex.IsMatch(trimmed, @"^[-*]\s\[[ xX]\]\s"))
        {
            return Regex.Replace(trimmed, @"^[-*]\s\[[ xX]\]\s", string.Empty).Trim();
        }

        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            return trimmed[2..].Trim();
        }

        if (Regex.IsMatch(trimmed, @"^[-*]\s"))
        {
            return Regex.Replace(trimmed, @"^[-*]\s", string.Empty).Trim();
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal) && trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return trimmed.Trim('`').Trim();
        }

        return trimmed;
    }

    private static string FormatBlock(RichBlockKind kind, string text, int listLevel)
    {
        return kind switch
        {
            RichBlockKind.Header1 => $"# {text}",
            RichBlockKind.Header2 => $"## {text}",
            RichBlockKind.Header3 => $"### {text}",
            RichBlockKind.Bullet => $"{new string(' ', Math.Max(0, listLevel - 1) * 2)}- {text}",
            RichBlockKind.Task => $"{new string(' ', Math.Max(0, listLevel - 1) * 2)}- [ ] {text}",
            RichBlockKind.Quote => $"> {text}",
            RichBlockKind.Code => $"```{Environment.NewLine}{text}{Environment.NewLine}```",
            _ => text
        };
    }

    private static bool IsInsideCodeFence(string text, int cursor)
    {
        var segment = text[..Math.Clamp(cursor, 0, text.Length)];
        var fenceCount = Regex.Matches(segment, @"^```", RegexOptions.Multiline).Count;
        return fenceCount % 2 == 1;
    }

    private void SetSelection(int cursorPosition, int selectionLength)
    {
        _editor.CursorPosition = cursorPosition;
        _editor.SelectionLength = selectionLength;
#if MACCATALYST
        if (_nativeTextView is not null)
        {
            _nativeTextView.SelectedRange = new NSRange(cursorPosition, selectionLength);
        }
#endif
        UpdateToolbarState();
        RefreshTypingAttributes();
    }

    private void OnEditorHandlerChanged(object? sender, EventArgs e)
    {
        RefreshNativeStyling();
    }

    private void RefreshTypingAttributes()
    {
#if MACCATALYST
        if (_nativeTextView is null)
        {
            return;
        }

        _nativeTextView.TypingAttributes2 = BuildTypingAttributes(DetermineCurrentBlockKind());
#endif
    }

    private void RefreshNativeStyling()
    {
#if MACCATALYST
        AttachNativeTextView();
        if (_nativeTextView is null)
        {
            return;
        }

        var text = _editor.Text ?? string.Empty;
        var selectedRange = _nativeTextView.SelectedRange;
        _isRefreshingNativeStyle = true;
        try
        {
            _nativeTextView.AttributedText = BuildAttributedMarkdown(text);
            _nativeTextView.SelectedRange = new NSRange(
                Math.Min((nint)text.Length, selectedRange.Location),
                Math.Min((nint)Math.Max(0, text.Length - selectedRange.Location), selectedRange.Length));
            _nativeTextView.TypingAttributes2 = BuildTypingAttributes(DetermineCurrentBlockKind());
        }
        finally
        {
            _isRefreshingNativeStyle = false;
        }
#endif
    }

#if MACCATALYST
    private void AttachNativeTextView()
    {
        if (_nativeTextView is not null)
        {
            return;
        }

        _nativeTextView = _editor.Handler?.PlatformView as UITextView;
        if (_nativeTextView is null)
        {
            return;
        }

        _nativeTextView.BackgroundColor = UIColor.Clear;
        _nativeTextView.TextContainerInset = new UIEdgeInsets(4, 0, 12, 0);
        _nativeTextView.TextContainer.LineFragmentPadding = 0;
        _nativeTextView.AllowsEditingTextAttributes = true;
        RefreshTypingAttributes();
    }

    private void DetachNativeTextView()
    {
        _nativeTextView = null;
    }

    private NSMutableAttributedString BuildAttributedMarkdown(string text)
    {
        var attributed = new NSMutableAttributedString(text, new UIStringAttributes
        {
            Font = UIFont.SystemFontOfSize(18),
            ForegroundColor = UIColor.FromRGB(22, 22, 22)
        });

        var lines = SplitLinesPreservingTerminators(text);
        var location = 0;
        var inCodeFence = false;

        foreach (var line in lines)
        {
            var rawLine = line.TrimEnd('\r', '\n');
            var contentLength = rawLine.Length;
            var lineRange = new NSRange(location, contentLength);

            if (rawLine.StartsWith("```", StringComparison.Ordinal))
            {
                ApplyCodeFenceLineStyle(attributed, lineRange);
                inCodeFence = !inCodeFence;
            }
            else if (inCodeFence)
            {
                ApplyCodeContentLineStyle(attributed, lineRange);
            }
            else
            {
                ApplyStyledLine(attributed, rawLine, location);
            }

            location += line.Length;
        }

        return attributed;
    }

    private void ApplyStyledLine(NSMutableAttributedString attributed, string rawLine, int location)
    {
        if (string.IsNullOrEmpty(rawLine))
        {
            return;
        }

        var leadingWhitespace = GetLeadingWhitespace(rawLine);
        var trimmed = rawLine.TrimStart();
        var offset = location + leadingWhitespace.Length;

        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
        {
            ApplyHeaderStyle(attributed, offset, trimmed, 3);
            return;
        }

        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            ApplyHeaderStyle(attributed, offset, trimmed, 2);
            return;
        }

        if (trimmed.StartsWith("# ", StringComparison.Ordinal))
        {
            ApplyHeaderStyle(attributed, offset, trimmed, 1);
            return;
        }

        var taskMatch = Regex.Match(trimmed, @"^([-*]\s\[[ xX]\]\s)");
        if (taskMatch.Success)
        {
            ApplyHiddenMarker(attributed, new NSRange(offset, taskMatch.Length), 15);
            ApplyParagraphIndent(attributed, new NSRange(location, rawLine.Length), firstLineIndent: 0, headIndent: 34);
            ApplyTextStyle(attributed, new NSRange(offset + taskMatch.Length, Math.Max(0, trimmed.Length - taskMatch.Length)), 17, false);
            return;
        }

        var bulletMatch = Regex.Match(trimmed, @"^([-*]\s)");
        if (bulletMatch.Success)
        {
            ApplyMutedMarker(attributed, new NSRange(offset, bulletMatch.Length), 16);
            ApplyParagraphIndent(attributed, new NSRange(location, rawLine.Length), firstLineIndent: 0, headIndent: 26);
            ApplyTextStyle(attributed, new NSRange(offset + bulletMatch.Length, Math.Max(0, trimmed.Length - bulletMatch.Length)), 17, false);
            return;
        }

        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            ApplyHiddenMarker(attributed, new NSRange(offset, 2), 16);
            ApplyParagraphIndent(attributed, new NSRange(location, rawLine.Length), firstLineIndent: 18, headIndent: 18);
            ApplyTextStyle(attributed, new NSRange(offset + 2, Math.Max(0, trimmed.Length - 2)), 17, false);
            return;
        }

        ApplyParagraphIndent(attributed, new NSRange(location, rawLine.Length), firstLineIndent: 0, headIndent: 0);
        ApplyTextStyle(attributed, new NSRange(location, rawLine.Length), 18, false);
    }

    private static void ApplyHeaderStyle(NSMutableAttributedString attributed, int start, string line, int level)
    {
        var markerLength = level + 1;
        var fontSize = level switch
        {
            1 => 32,
            2 => 26,
            _ => 22
        };

        ApplyHiddenMarker(attributed, new NSRange(start, markerLength), 14);
        ApplyParagraphIndent(attributed, new NSRange(start, line.Length), firstLineIndent: 0, headIndent: 0);
        ApplyTextStyle(attributed, new NSRange(start + markerLength, Math.Max(0, line.Length - markerLength)), fontSize, true);
    }

    private static void ApplyCodeFenceLineStyle(NSMutableAttributedString attributed, NSRange range)
    {
        attributed.AddAttributes(new UIStringAttributes
        {
            Font = GetMonospaceFont(14, bold: true),
            ForegroundColor = UIColor.FromRGB(123, 115, 95)
        }, range);
    }

    private static void ApplyCodeContentLineStyle(NSMutableAttributedString attributed, NSRange range)
    {
        attributed.AddAttributes(new UIStringAttributes
        {
            Font = GetMonospaceFont(15, bold: false),
            ForegroundColor = UIColor.FromRGB(22, 22, 22)
        }, range);
    }

    private static void ApplyMutedMarker(NSMutableAttributedString attributed, NSRange range, nfloat fontSize)
    {
        attributed.AddAttributes(new UIStringAttributes
        {
            Font = UIFont.SystemFontOfSize(fontSize),
            ForegroundColor = UIColor.FromRGBA(130, 121, 107, 180)
        }, range);
    }

    private static void ApplyHiddenMarker(NSMutableAttributedString attributed, NSRange range, nfloat fontSize)
    {
        attributed.AddAttributes(new UIStringAttributes
        {
            Font = UIFont.SystemFontOfSize((nfloat)Math.Max(1, (double)fontSize)),
            ForegroundColor = UIColor.Clear
        }, range);
    }

    private static void ApplyTextStyle(NSMutableAttributedString attributed, NSRange range, nfloat fontSize, bool bold)
    {
        attributed.AddAttributes(new UIStringAttributes
        {
            Font = bold ? UIFont.BoldSystemFontOfSize(fontSize) : UIFont.SystemFontOfSize(fontSize),
            ForegroundColor = UIColor.FromRGB(22, 22, 22)
        }, range);
    }

    private static void ApplyParagraphIndent(NSMutableAttributedString attributed, NSRange range, nfloat firstLineIndent, nfloat headIndent)
    {
        var paragraphStyle = new NSMutableParagraphStyle
        {
            FirstLineHeadIndent = firstLineIndent,
            HeadIndent = headIndent,
            ParagraphSpacing = 8,
            LineSpacing = 1
        };

        attributed.AddAttributes(new UIStringAttributes
        {
            ParagraphStyle = paragraphStyle
        }, range);
    }

    private static NSDictionary BuildTypingAttributes(RichBlockKind kind)
    {
        var font = kind switch
        {
            RichBlockKind.Header1 => UIFont.BoldSystemFontOfSize(32),
            RichBlockKind.Header2 => UIFont.BoldSystemFontOfSize(26),
            RichBlockKind.Header3 => UIFont.BoldSystemFontOfSize(22),
            RichBlockKind.Code => GetMonospaceFont(15, bold: false),
            _ => UIFont.SystemFontOfSize(kind == RichBlockKind.Paragraph ? 18 : 17)
        };

        return new UIStringAttributes
        {
            Font = font,
            ForegroundColor = UIColor.FromRGB(22, 22, 22)
        }.Dictionary;
    }

    private static List<string> SplitLinesPreservingTerminators(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }
        else if (text.Length == 0)
        {
            lines.Add(string.Empty);
        }

        return lines;
    }

    private static UIFont GetMonospaceFont(nfloat size, bool bold)
    {
        var familyName = bold ? "Menlo-Bold" : "Menlo-Regular";
        return UIFont.FromName(familyName, size)
               ?? UIFont.FromName("Courier", size)
               ?? UIFont.SystemFontOfSize(size);
    }
#endif

    private enum RichBlockKind
    {
        Paragraph,
        Header1,
        Header2,
        Header3,
        Bullet,
        Task,
        Quote,
        Code
    }
}
