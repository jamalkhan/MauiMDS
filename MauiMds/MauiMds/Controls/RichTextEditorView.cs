using System.Windows.Input;
using MauiMds.Models;
using Microsoft.Maui.Controls.Shapes;

namespace MauiMds.Controls;

public sealed class VisualEditorView : ContentView, IEditorSurface
{
    public static readonly BindableProperty BlocksProperty = BindableProperty.Create(
        nameof(Blocks),
        typeof(IReadOnlyList<MarkdownBlock>),
        typeof(VisualEditorView),
        default(IReadOnlyList<MarkdownBlock>));

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(VisualEditorView),
        string.Empty,
        BindingMode.TwoWay,
        propertyChanged: OnTextPropertyChanged);

    private readonly Editor _editor;
    private readonly Dictionary<RichTextBlockKind, Button> _toolbarButtons = [];
    private readonly VisualEditorDocumentController _documentController = new();
    private readonly MacVisualEditorStyler _macRichTextStyler = new();
    private bool _isApplyingExternalText;
    private bool _isRefreshingNativeStyle;

    public VisualEditorView()
    {
        var toolbar = new HorizontalStackLayout
        {
            Spacing = 8
        };
        AddToolbarButton(toolbar, RichTextBlockKind.Paragraph, "Paragraph", () => ApplyBlockTransform(RichTextBlockKind.Paragraph));
        AddToolbarButton(toolbar, RichTextBlockKind.Header1, "H1", () => ApplyBlockTransform(RichTextBlockKind.Header1));
        AddToolbarButton(toolbar, RichTextBlockKind.Header2, "H2", () => ApplyBlockTransform(RichTextBlockKind.Header2));
        AddToolbarButton(toolbar, RichTextBlockKind.Header3, "H3", () => ApplyBlockTransform(RichTextBlockKind.Header3));
        AddToolbarButton(toolbar, RichTextBlockKind.Bullet, "Bullet", () => ApplyBlockTransform(RichTextBlockKind.Bullet));
        AddToolbarButton(toolbar, RichTextBlockKind.Task, "Checklist", () => ApplyBlockTransform(RichTextBlockKind.Task));
        AddToolbarButton(toolbar, RichTextBlockKind.Quote, "Quote", () => ApplyBlockTransform(RichTextBlockKind.Quote));
        AddToolbarButton(toolbar, RichTextBlockKind.Code, "Code", () => ApplyBlockTransform(RichTextBlockKind.Code));

        var toolbarBorder = new Border
        {
            BackgroundColor = Color.FromArgb("#F4F0E6"),
            Stroke = Color.FromArgb("#D8D0C2"),
            StrokeThickness = 1,
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 0, 0, 12),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = new VerticalStackLayout
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
            }
        };

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

    public void FocusEditor() => _editor.Focus();

    public void ApplyParagraphStyle()
    {
        ApplyBlockTransform(RichTextBlockKind.Paragraph);
    }

    public void Undo()
    {
        var result = _documentController.Undo(Text ?? string.Empty);
        if (result is null)
        {
            return;
        }

        ApplyExternalText(result, preserveSelection: true);
    }

    public void Redo()
    {
        var result = _documentController.Redo(Text ?? string.Empty);
        if (result is null)
        {
            return;
        }

        ApplyExternalText(result, preserveSelection: true);
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
        var result = _documentController.ApplyHeaderPrefix(Text ?? string.Empty, _editor.CursorPosition, _editor.SelectionLength, level);
        if (result.Changed)
        {
            ApplyUpdatedText(result.Text, result.CursorPosition, result.SelectionLength);
        }
    }

    public void ApplyBulletStyle()
    {
        ApplyBlockTransform(RichTextBlockKind.Bullet);
    }

    public void ApplyChecklistStyle()
    {
        ApplyBlockTransform(RichTextBlockKind.Task);
    }

    public void ApplyQuoteStyle()
    {
        ApplyBlockTransform(RichTextBlockKind.Quote);
    }

    public void ApplyCodeStyle()
    {
        ApplyBlockTransform(RichTextBlockKind.Code);
    }

    public bool FindNext(string query)
    {
        var result = _documentController.FindNext(Text ?? string.Empty, _editor.CursorPosition, _editor.SelectionLength, query);
        if (!result.Found)
        {
            return false;
        }

        _editor.Focus();
        SetSelection(result.CursorPosition, result.SelectionLength);
        return true;
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is null)
        {
            _macRichTextStyler.Detach();
        }
    }

    private static void OnTextPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((VisualEditorView)bindable).OnExternalTextChanged((string?)newValue ?? string.Empty);
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
        _documentController.RecordTextChange(previous, current);

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

    private void AddToolbarButton(HorizontalStackLayout toolbar, RichTextBlockKind kind, string text, Action action)
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

    private void ApplyBlockTransform(RichTextBlockKind kind)
    {
        var text = _editor.Text ?? string.Empty;
        var result = _documentController.ApplyBlockTransform(text, _editor.CursorPosition, _editor.SelectionLength, kind);
        if (!result.Changed)
        {
            return;
        }
        ApplyUpdatedText(result.Text, result.CursorPosition, result.SelectionLength);
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

        _documentController.RecordTextChange(previousText, updatedText);

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
        var kind = _documentController.DetermineCurrentBlockKind(_editor.Text ?? string.Empty, _editor.CursorPosition);
        foreach (var (buttonKind, button) in _toolbarButtons)
        {
            var isActive = buttonKind == kind;
            button.BackgroundColor = isActive ? Color.FromArgb("#1D1D1B") : Color.FromArgb("#E8E0CF");
            button.TextColor = isActive ? Color.FromArgb("#F7F2E8") : Color.FromArgb("#1A1A1A");
        }
    }

    private void SetSelection(int cursorPosition, int selectionLength)
    {
        _editor.CursorPosition = cursorPosition;
        _editor.SelectionLength = selectionLength;
        _macRichTextStyler.SyncSelection(cursorPosition, selectionLength);
        UpdateToolbarState();
        RefreshTypingAttributes();
    }

    private void OnEditorHandlerChanged(object? sender, EventArgs e)
    {
        RefreshNativeStyling();
    }

    private void RefreshTypingAttributes()
    {
        _macRichTextStyler.RefreshTypingAttributes(_documentController.DetermineCurrentBlockKind(_editor.Text ?? string.Empty, _editor.CursorPosition));
    }

    private void RefreshNativeStyling()
    {
        _isRefreshingNativeStyle = true;
        try
        {
            _macRichTextStyler.Attach(_editor);
            _macRichTextStyler.RefreshStyling(_editor.Text ?? string.Empty, _editor.CursorPosition, _documentController.DetermineCurrentBlockKind(_editor.Text ?? string.Empty, _editor.CursorPosition));
        }
        finally
        {
            _isRefreshingNativeStyle = false;
        }
    }
}
