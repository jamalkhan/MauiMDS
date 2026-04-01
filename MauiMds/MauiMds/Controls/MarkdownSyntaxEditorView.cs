using System.Collections.Generic;

namespace MauiMds.Controls;

public sealed class MarkdownSyntaxEditorView : ContentView, IEditorSurface
{
    // DEPRECATED: Syntax highlighting is intentionally disabled in the active editor path
    // because the current implementation is too slow for a good editing experience.
    // Keep this control around for clipboard/find/header actions until we revisit
    // a lighter-weight highlighting implementation.
    private static readonly TimeSpan HighlightDebounceDelay = TimeSpan.FromMilliseconds(120);
    private const int PlainTextFallbackCharacterThreshold = 24000;
    private const int PlainTextFallbackLineThreshold = 700;

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(MarkdownSyntaxEditorView),
        string.Empty,
        BindingMode.TwoWay,
        propertyChanged: OnTextPropertyChanged);

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(MarkdownSyntaxEditorView),
        string.Empty,
        propertyChanged: OnPlaceholderPropertyChanged);

    public static readonly BindableProperty IsSyntaxHighlightingEnabledProperty = BindableProperty.Create(
        nameof(IsSyntaxHighlightingEnabled),
        typeof(bool),
        typeof(MarkdownSyntaxEditorView),
        true,
        propertyChanged: OnSyntaxHighlightingChanged);

    private readonly MarkdownSyntaxHighlighter _highlighter = new();
    private readonly Editor _editor;
    private readonly Label _highlightLabel;
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private bool _isApplyingText;
    private bool _isUpdatingHistory;
    private CancellationTokenSource? _highlightCancellationSource;

    public MarkdownSyntaxEditorView()
    {
        _highlightLabel = new Label
        {
            FontFamily = "Courier New",
            FontSize = 15,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(12),
            InputTransparent = true
        };

        _editor = new Editor
        {
            AutoSize = EditorAutoSizeOption.Disabled,
            BackgroundColor = Colors.Transparent,
            FontFamily = "Courier New",
            FontSize = 15,
            Margin = new Thickness(12),
            TextColor = Colors.Transparent,
            CursorPosition = 0,
            SelectionLength = 0
        };
        _editor.TextChanged += OnEditorTextChanged;

        var grid = new Grid();
        grid.Children.Add(_highlightLabel);
        grid.Children.Add(_editor);

        Content = grid;
        RefreshHighlighting();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool IsSyntaxHighlightingEnabled
    {
        get => (bool)GetValue(IsSyntaxHighlightingEnabledProperty);
        set => SetValue(IsSyntaxHighlightingEnabledProperty, value);
    }

    public void FocusEditor()
    {
        _editor.Focus();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(Text ?? string.Empty);
        SetTextInternal(_undoStack.Pop(), recordHistory: false);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(Text ?? string.Empty);
        SetTextInternal(_redoStack.Pop(), recordHistory: false);
    }

    public async Task CopySelectionAsync()
    {
        var selectedText = GetSelectedText();
        if (!string.IsNullOrEmpty(selectedText))
        {
            await Clipboard.Default.SetTextAsync(selectedText);
        }
    }

    public async Task CutSelectionAsync()
    {
        var selectedText = GetSelectedText();
        if (string.IsNullOrEmpty(selectedText))
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(selectedText);
        ReplaceSelection(string.Empty);
    }

    public async Task PasteAsync()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ReplaceSelection(text);
    }

    public void ApplyHeaderPrefix(int level)
    {
        level = Math.Clamp(level, 1, 6);
        var prefix = new string('#', level) + " ";
        var text = Text ?? string.Empty;
        var cursor = _editor.CursorPosition;
        var lineStart = cursor <= 0 ? 0 : text.LastIndexOf('\n', Math.Max(0, cursor - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', lineStart);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;
        var line = text.Substring(lineStart, lineEnd - lineStart).TrimStart('#', ' ');

        var newLine = prefix + line;
        var updatedText = text[..lineStart] + newLine + text[lineEnd..];
        SetTextInternal(updatedText, recordHistory: true);
        _editor.CursorPosition = Math.Min(lineStart + newLine.Length, _editor.Text?.Length ?? 0);
        _editor.SelectionLength = 0;
    }

    public bool FindNext(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(Text))
        {
            return false;
        }

        var text = Text;
        var startIndex = Math.Min(text.Length, _editor.CursorPosition + Math.Max(0, _editor.SelectionLength));
        var index = text.IndexOf(query, startIndex, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && startIndex > 0)
        {
            index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }

        if (index < 0)
        {
            return false;
        }

        _editor.Focus();
        _editor.CursorPosition = index;
        _editor.SelectionLength = query.Length;
        return true;
    }

    private static void OnTextPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (MarkdownSyntaxEditorView)bindable;
        var newText = newValue as string ?? string.Empty;
        if (view._editor.Text != newText)
        {
            view.SetTextInternal(newText, recordHistory: false);
        }
        else if (view.IsSyntaxHighlightingEnabled)
        {
            view.ScheduleHighlightRefresh();
        }
    }

    private static void OnPlaceholderPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((MarkdownSyntaxEditorView)bindable)._editor.Placeholder = newValue as string;
    }

    private static void OnSyntaxHighlightingChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (MarkdownSyntaxEditorView)bindable;
        if (view.IsSyntaxHighlightingEnabled)
        {
            view.ScheduleHighlightRefresh();
        }
        else
        {
            view.RefreshHighlighting();
        }
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingText)
        {
            return;
        }

        if (!_isUpdatingHistory && e.OldTextValue != e.NewTextValue)
        {
            _undoStack.Push(e.OldTextValue ?? string.Empty);
            _redoStack.Clear();
        }

        Text = e.NewTextValue ?? string.Empty;
        if (IsSyntaxHighlightingEnabled)
        {
            ScheduleHighlightRefresh();
        }
    }

    private void SetTextInternal(string text, bool recordHistory)
    {
        if (recordHistory && !_isUpdatingHistory)
        {
            _undoStack.Push(Text ?? string.Empty);
            _redoStack.Clear();
        }
        else if (!recordHistory)
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        _isApplyingText = true;
        _isUpdatingHistory = !recordHistory;
        try
        {
            _editor.Text = text;
            if (!string.Equals(Text, text, StringComparison.Ordinal))
            {
                SetValue(TextProperty, text);
            }

            if (IsSyntaxHighlightingEnabled)
            {
                ScheduleHighlightRefresh();
            }
        }
        finally
        {
            _isApplyingText = false;
            _isUpdatingHistory = false;
        }
    }

    private void ReplaceSelection(string replacementText)
    {
        var text = Text ?? string.Empty;
        var cursor = Math.Clamp(_editor.CursorPosition, 0, text.Length);
        var selectionLength = Math.Clamp(_editor.SelectionLength, 0, text.Length - cursor);
        var updated = text[..cursor] + replacementText + text[(cursor + selectionLength)..];
        SetTextInternal(updated, recordHistory: true);
        _editor.CursorPosition = cursor + replacementText.Length;
        _editor.SelectionLength = 0;
        _editor.Focus();
    }

    private string GetSelectedText()
    {
        var text = Text ?? string.Empty;
        var cursor = Math.Clamp(_editor.CursorPosition, 0, text.Length);
        var selectionLength = Math.Clamp(_editor.SelectionLength, 0, text.Length - cursor);
        return selectionLength == 0 ? string.Empty : text.Substring(cursor, selectionLength);
    }

    private void ScheduleHighlightRefresh()
    {
        if (!IsSyntaxHighlightingEnabled)
        {
            RefreshHighlighting();
            return;
        }

        _highlightCancellationSource?.Cancel();
        _highlightCancellationSource?.Dispose();
        _highlightCancellationSource = new CancellationTokenSource();
        var token = _highlightCancellationSource.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(HighlightDebounceDelay, token);
                token.ThrowIfCancellationRequested();
                await MainThread.InvokeOnMainThreadAsync(RefreshHighlighting);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void RefreshHighlighting()
    {
        if (IsSyntaxHighlightingEnabled)
        {
            var text = Text ?? string.Empty;
            if (ShouldUsePlainTextFallback(text))
            {
                _highlightLabel.FormattedText = null;
                _highlightLabel.Text = text;
                _highlightLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#111111"), Color.FromArgb("#F6F0E8"));
            }
            else
            {
                _highlightLabel.Text = null;
                _highlightLabel.FormattedText = _highlighter.BuildFormattedText(text);
            }
            _editor.TextColor = Colors.Transparent;
        }
        else
        {
            _highlightLabel.FormattedText = null;
            _highlightLabel.Text = string.Empty;
            _editor.SetAppThemeColor(Editor.TextColorProperty, Color.FromArgb("#111111"), Color.FromArgb("#F6F0E8"));
        }
    }

    private static bool ShouldUsePlainTextFallback(string text)
    {
        if (text.Length >= PlainTextFallbackCharacterThreshold)
        {
            return true;
        }

        var lineCount = 1;
        foreach (var ch in text)
        {
            if (ch == '\n' && ++lineCount >= PlainTextFallbackLineThreshold)
            {
                return true;
            }
        }

        return false;
    }
}
