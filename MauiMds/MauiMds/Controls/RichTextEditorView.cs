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

#if WINDOWS
    private WebView? _webView;
    private int _webCursorPosition;
    private int _webSelectionLength;
    private bool _webViewReady;
#endif

    public VisualEditorView()
    {
        var toolbar = new HorizontalStackLayout { Spacing = 8 };
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
            HorizontalOptions = LayoutOptions.Fill,
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
        };

#if WINDOWS
        // On Windows the MAUI Editor renders as a plain TextBox with no rich-text support.
        // Replace it with a WebView that provides syntax-highlighted markdown editing.
        // The _editor field is kept as a hidden backing store for cursor/selection tracking
        // and so all format-transform methods can work unchanged.
        _webView = BuildWindowsWebView();
        editorBorder.Content = _webView;
#else
        editorBorder.Content = _editor;
#endif

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

    // ── Cursor/selection helpers ──────────────────────────────────────────────
    // On Windows, the WebView reports cursor position via URL messages.
    // On Mac, the MAUI Editor's native UITextView tracks it directly.

    private int CurrentCursor =>
#if WINDOWS
        _webCursorPosition;
#else
        _editor.CursorPosition;
#endif

    private int CurrentSelection =>
#if WINDOWS
        _webSelectionLength;
#else
        _editor.SelectionLength;
#endif

    // ── IEditorSurface ────────────────────────────────────────────────────────

    public void FocusEditor()
    {
#if WINDOWS
        _ = _webView?.EvaluateJavaScriptAsync("editorFocus()");
#else
        _editor.Focus();
#endif
    }

    public void ApplyParagraphStyle() => ApplyBlockTransform(RichTextBlockKind.Paragraph);

    public void Undo()
    {
        var result = _documentController.Undo(Text ?? string.Empty);
        if (result is null) return;
        ApplyExternalText(result, preserveSelection: true);
    }

    public void Redo()
    {
        var result = _documentController.Redo(Text ?? string.Empty);
        if (result is null) return;
        ApplyExternalText(result, preserveSelection: true);
    }

    public async Task CopySelectionAsync()
    {
        var selected = GetSelectedText();
        if (!string.IsNullOrEmpty(selected))
            await Clipboard.Default.SetTextAsync(selected);
    }

    public async Task CutSelectionAsync()
    {
        var selected = GetSelectedText();
        if (string.IsNullOrEmpty(selected)) return;
        await Clipboard.Default.SetTextAsync(selected);
        ReplaceSelection(string.Empty);
    }

    public async Task PasteAsync()
    {
        var pasted = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrEmpty(pasted))
            ReplaceSelection(pasted);
    }

    public void ApplyHeaderPrefix(int level)
    {
        var result = _documentController.ApplyHeaderPrefix(Text ?? string.Empty, CurrentCursor, CurrentSelection, level);
        if (result.Changed)
            ApplyUpdatedText(result.Text, result.CursorPosition, result.SelectionLength);
    }

    public void ApplyBulletStyle() => ApplyBlockTransform(RichTextBlockKind.Bullet);
    public void ApplyChecklistStyle() => ApplyBlockTransform(RichTextBlockKind.Task);
    public void ApplyQuoteStyle() => ApplyBlockTransform(RichTextBlockKind.Quote);
    public void ApplyCodeStyle() => ApplyBlockTransform(RichTextBlockKind.Code);
    public void ApplyBoldStyle() => ApplyInlineDelimiter("**");
    public void ApplyItalicStyle() => ApplyInlineDelimiter("_");

    private void ApplyInlineDelimiter(string delimiter)
    {
        var text = Text ?? string.Empty;
        var start = Math.Clamp(CurrentCursor, 0, text.Length);
        var length = Math.Clamp(CurrentSelection, 0, text.Length - start);

        if (length == 0)
        {
            var inserted = delimiter + delimiter;
            ApplyUpdatedText(text[..start] + inserted + text[start..], start + delimiter.Length, 0);
            return;
        }

        var selected = text.Substring(start, length);
        if (selected.StartsWith(delimiter, StringComparison.Ordinal) &&
            selected.EndsWith(delimiter, StringComparison.Ordinal) &&
            selected.Length > delimiter.Length * 2)
        {
            var inner = selected[delimiter.Length..(selected.Length - delimiter.Length)];
            ApplyUpdatedText(text[..start] + inner + text[(start + length)..], start, inner.Length);
        }
        else
        {
            var wrapped = delimiter + selected + delimiter;
            ApplyUpdatedText(text[..start] + wrapped + text[(start + length)..], start, wrapped.Length);
        }
    }

    public bool FindNext(string query)
    {
        var result = _documentController.FindNext(Text ?? string.Empty, CurrentCursor, CurrentSelection, query);
        if (!result.Found) return false;

        FocusEditor();
        SetSelection(result.CursorPosition, result.SelectionLength);
        return true;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is null)
            _macRichTextStyler.Detach();
    }

    // ── Text property change (ViewModel → View) ───────────────────────────────

    private static void OnTextPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
        => ((VisualEditorView)bindable).OnExternalTextChanged((string?)newValue ?? string.Empty);

    private void OnExternalTextChanged(string newText)
    {
        if (_isApplyingExternalText) return;

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

#if WINDOWS
        _ = SyncTextToWebViewAsync(newText);
#endif
        RefreshNativeStyling();
        UpdateToolbarState();
    }

    // ── Editor text changed (View → ViewModel) ────────────────────────────────

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingExternalText || _isRefreshingNativeStyle) return;

        var previous = e.OldTextValue ?? string.Empty;
        var current = e.NewTextValue ?? string.Empty;
        _documentController.RecordTextChange(previous, current);

        _isApplyingExternalText = true;
        try { SetValue(TextProperty, current); }
        finally { _isApplyingExternalText = false; }

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

    // ── Format helpers ────────────────────────────────────────────────────────

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
        var result = _documentController.ApplyBlockTransform(text, CurrentCursor, CurrentSelection, kind);
        if (!result.Changed) return;
        ApplyUpdatedText(result.Text, result.CursorPosition, result.SelectionLength);
    }

    private void ReplaceSelection(string replacementText)
    {
        var text = _editor.Text ?? string.Empty;
        var start = Math.Clamp(CurrentCursor, 0, text.Length);
        var length = Math.Clamp(CurrentSelection, 0, text.Length - start);
        var updated = text.Remove(start, length).Insert(start, replacementText);
        ApplyUpdatedText(updated, start + replacementText.Length, 0);
    }

    private void ApplyUpdatedText(string updatedText, int cursorPosition, int selectionLength)
    {
        ApplyExternalText(updatedText, preserveSelection: false);
        FocusEditor();
        SetSelection(Math.Clamp(cursorPosition, 0, updatedText.Length), selectionLength);
    }

    private void ApplyExternalText(string updatedText, bool preserveSelection)
    {
        var previousText = _editor.Text ?? string.Empty;
        var previousCursor = CurrentCursor;
        var previousSelection = CurrentSelection;

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

#if WINDOWS
        _ = SyncTextToWebViewAsync(updatedText);
#endif
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
        var start = Math.Clamp(CurrentCursor, 0, text.Length);
        var length = Math.Clamp(CurrentSelection, 0, text.Length - start);
        return length == 0 ? string.Empty : text.Substring(start, length);
    }

    private void UpdateToolbarState()
    {
        var kind = _documentController.DetermineCurrentBlockKind(_editor.Text ?? string.Empty, CurrentCursor);
        foreach (var (buttonKind, button) in _toolbarButtons)
        {
            var isActive = buttonKind == kind;
            button.BackgroundColor = isActive ? Color.FromArgb("#1D1D1B") : Color.FromArgb("#E8E0CF");
            button.TextColor = isActive ? Color.FromArgb("#F7F2E8") : Color.FromArgb("#1A1A1A");
        }
    }

    private void SetSelection(int cursorPosition, int selectionLength)
    {
#if WINDOWS
        _webCursorPosition = cursorPosition;
        _webSelectionLength = selectionLength;
        if (_webViewReady)
            _ = _webView?.EvaluateJavaScriptAsync($"editorSetCursor({cursorPosition},{selectionLength})");
#else
        _editor.CursorPosition = cursorPosition;
        _editor.SelectionLength = selectionLength;
        _macRichTextStyler.SyncSelection(cursorPosition, selectionLength);
#endif
        UpdateToolbarState();
        RefreshTypingAttributes();
    }

    private void OnEditorHandlerChanged(object? sender, EventArgs e) => RefreshNativeStyling();

    private void RefreshTypingAttributes()
    {
        _macRichTextStyler.RefreshTypingAttributes(
            _documentController.DetermineCurrentBlockKind(_editor.Text ?? string.Empty, CurrentCursor));
    }

    private void RefreshNativeStyling()
    {
        _isRefreshingNativeStyle = true;
        try
        {
            _macRichTextStyler.Attach(_editor);
            _macRichTextStyler.RefreshStyling(
                _editor.Text ?? string.Empty,
                CurrentCursor,
                _documentController.DetermineCurrentBlockKind(_editor.Text ?? string.Empty, CurrentCursor));
        }
        finally
        {
            _isRefreshingNativeStyle = false;
        }
    }

    // ── Windows WebView editor ────────────────────────────────────────────────
#if WINDOWS
    private WebView BuildWindowsWebView()
    {
        var wv = new WebView
        {
            Source = new HtmlWebViewSource { Html = BuildWindowsEditorHtml() },
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
        };
        wv.Navigating += OnWebViewNavigating;
        wv.Navigated += OnWebViewNavigated;
        return wv;
    }

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        _webViewReady = true;
        var text = _editor.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(text))
            _ = SyncTextToWebViewAsync(text);
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        // Text change: mauimds://tc/{cursor}/{selection}/{url-safe-base64-text}
        if (e.Url.StartsWith("mauimds://tc/"))
        {
            e.Cancel = true;
            var rest = e.Url["mauimds://tc/".Length..];
            var firstSlash = rest.IndexOf('/');
            var secondSlash = firstSlash >= 0 ? rest.IndexOf('/', firstSlash + 1) : -1;
            if (firstSlash < 0 || secondSlash < 0) return;

            int.TryParse(rest[..firstSlash], out _webCursorPosition);
            int.TryParse(rest[(firstSlash + 1)..secondSlash], out _webSelectionLength);

            var b64 = rest[(secondSlash + 1)..].Replace('-', '+').Replace('_', '/');
            var pad = (4 - b64.Length % 4) % 4;
            if (pad < 4) b64 += new string('=', pad);

            try
            {
                var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                HandleWebTextChange(text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VisualEditorView: malformed WebView text payload — {ex.Message}");
            }
        }
        // Cursor-only update: mauimds://k/{cursor}/{selection}
        else if (e.Url.StartsWith("mauimds://k/"))
        {
            e.Cancel = true;
            var parts = e.Url["mauimds://k/".Length..].Split('/');
            if (parts.Length >= 2)
            {
                int.TryParse(parts[0], out _webCursorPosition);
                int.TryParse(parts[1], out _webSelectionLength);
                UpdateToolbarState();
            }
        }
    }

    private void HandleWebTextChange(string text)
    {
        if (_isApplyingExternalText) return;

        var previous = _editor.Text ?? string.Empty;
        _documentController.RecordTextChange(previous, text);

        _isApplyingExternalText = true;
        try
        {
            _editor.Text = text;
            SetValue(TextProperty, text);
        }
        finally { _isApplyingExternalText = false; }

        UpdateToolbarState();
    }

    private async Task SyncTextToWebViewAsync(string text)
    {
        if (_webView is null || !_webViewReady) return;
        // Send as base64 to avoid JS string-escaping issues.
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
        await _webView.EvaluateJavaScriptAsync($"editorSetTextB64('{b64}')");
    }

    private static string BuildWindowsEditorHtml() => """
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><style>
        *{box-sizing:border-box;margin:0;padding:0}
        html,body{height:100%;overflow:hidden;background:transparent}
        .wrap{position:relative;height:100vh}
        .hl,.ta{
          position:absolute;top:0;left:0;right:0;bottom:0;
          padding:8px 4px;
          font:18px/1.6 'Segoe UI',sans-serif;
          white-space:pre-wrap;word-break:break-word;
          overflow-y:auto;tab-size:4
        }
        .hl{pointer-events:none;user-select:none}
        .ta{
          background:transparent;border:none;outline:none;resize:none;
          color:transparent;caret-color:#161616;
          -webkit-text-fill-color:transparent
        }
        .h1{font-size:1.8em;font-weight:700;color:#1a4fa0}
        .h2{font-size:1.5em;font-weight:700;color:#2155b0}
        .h3{font-size:1.2em;font-weight:700;color:#2d66c8}
        .qt{color:#776e5e;font-style:italic}
        .cf{font-family:'Cascadia Code',monospace;font-size:.85em;color:#7a7060}
        .cb{font-family:'Cascadia Code',monospace;font-size:.85em;color:#161616}
        .bm{color:#c0b898}
        .bt{font-weight:700;color:#161616}
        .im{color:#c0b898}
        .it{font-style:italic;color:#161616}
        .ic{font-family:'Cascadia Code',monospace;font-size:.9em;color:#8b4513}
        .pl{color:#161616}
        .bul-m{color:#a09070}
        </style></head>
        <body><div class="wrap">
          <div class="hl" id="hl"></div>
          <textarea class="ta" id="ta" spellcheck="true" autocorrect="off" autocapitalize="off"></textarea>
        </div>
        <script>
        (function(){
          'use strict';
          var ta=document.getElementById('ta'),hl=document.getElementById('hl');
          var inFence=false,sending=false;

          function esc(s){return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');}

          function highlightLine(line){
            if(line==='') return '<span class="pl">\n</span>';
            if(/^```/.test(line)){inFence=!inFence;return '<span class="cf">'+esc(line)+'</span>\n';}
            if(inFence) return '<span class="cb">'+esc(line)+'</span>\n';
            if(/^###\s/.test(line)) return '<span class="h3">'+esc(line)+'</span>\n';
            if(/^##\s/.test(line))  return '<span class="h2">'+esc(line)+'</span>\n';
            if(/^#\s/.test(line))   return '<span class="h1">'+esc(line)+'</span>\n';
            if(/^>\s/.test(line))   return '<span class="qt">'+esc(line)+'</span>\n';
            var out=esc(line);
            out=out.replace(/\*\*(.+?)\*\*/g,'<span class="bm">**</span><span class="bt">$1</span><span class="bm">**</span>');
            out=out.replace(/(?<![*_])_([^_\n]+?)_(?![*_])/g,'<span class="im">_</span><span class="it">$1</span><span class="im">_</span>');
            out=out.replace(/`([^`\n]+)`/g,'<span class="ic">`$1`</span>');
            if(/^[-*]\s/.test(line)) return '<span class="bul-m">'+out.substring(0,esc(line[0]).length+1)+'</span><span class="pl">'+out.substring(esc(line[0]).length+1)+'</span>\n';
            return '<span class="pl">'+out+'</span>\n';
          }

          function render(){
            inFence=false;
            hl.innerHTML=ta.value.split('\n').map(highlightLine).join('');
            hl.scrollTop=ta.scrollTop;
          }

          function b64url(s){
            return btoa(unescape(encodeURIComponent(s))).replace(/\+/g,'-').replace(/\//g,'_').replace(/=/g,'');
          }

          var textTimer,cursorTimer;
          ta.addEventListener('input',function(){
            render();
            if(sending) return;
            clearTimeout(textTimer);
            textTimer=setTimeout(function(){
              var c=ta.selectionStart,s=ta.selectionEnd-ta.selectionStart;
              window.location.href='mauimds://tc/'+c+'/'+s+'/'+b64url(ta.value);
            },80);
          });
          ta.addEventListener('scroll',function(){hl.scrollTop=ta.scrollTop;});
          function reportCursor(){
            clearTimeout(cursorTimer);
            cursorTimer=setTimeout(function(){
              window.location.href='mauimds://k/'+ta.selectionStart+'/'+(ta.selectionEnd-ta.selectionStart);
            },60);
          }
          ta.addEventListener('keyup',reportCursor);
          ta.addEventListener('click',reportCursor);
          ta.addEventListener('select',reportCursor);

          window.editorSetTextB64=function(b64){
            sending=true;
            ta.value=decodeURIComponent(escape(atob(b64)));
            render();
            sending=false;
          };
          window.editorSetText=function(t){
            sending=true;ta.value=t;render();sending=false;
          };
          window.editorSetCursor=function(pos,len){
            ta.focus();ta.setSelectionRange(pos,pos+(len||0));
          };
          window.editorFocus=function(){ta.focus();};

          render();
        })();
        </script></body></html>
        """;
#endif
}
