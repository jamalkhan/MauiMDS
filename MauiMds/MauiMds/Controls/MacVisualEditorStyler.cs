using Microsoft.Maui.Controls;
#if MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;
#endif

namespace MauiMds.Controls;

public sealed class MacVisualEditorStyler
{
#if MACCATALYST
    private UITextView? _nativeTextView;

    private static readonly System.Text.RegularExpressions.Regex BoldRunPattern =
        new(@"\*\*(.+?)\*\*", System.Text.RegularExpressions.RegexOptions.None);

    private static readonly System.Text.RegularExpressions.Regex ItalicRunPattern =
        new(@"(?<![*_])_([^_\n]+?)_(?![*_])", System.Text.RegularExpressions.RegexOptions.None);
#endif

    public void Attach(Editor editor)
    {
#if MACCATALYST
        _nativeTextView ??= editor.Handler?.PlatformView as UITextView;
        if (_nativeTextView is null)
        {
            return;
        }

        _nativeTextView.BackgroundColor = UIColor.Clear;
        _nativeTextView.TextContainerInset = new UIEdgeInsets(4, 0, 12, 0);
        _nativeTextView.TextContainer.LineFragmentPadding = 0;
        // Must be false: true lets macOS intercept Cmd+B/I directly on the UITextView,
        // applying bold/italic as attributed-string attributes without changing the plain-text
        // content — so the markdown source is never updated and the change is lost on the next
        // RefreshStyling call.  With false, Cmd+B/I fall through to our menu-bar accelerators
        // which properly insert ** / _ into the markdown source.
        _nativeTextView.AllowsEditingTextAttributes = false;
#endif
    }

    public void Detach()
    {
#if MACCATALYST
        _nativeTextView = null;
#endif
    }

    public void SyncSelection(int cursorPosition, int selectionLength)
    {
#if MACCATALYST
        if (_nativeTextView is not null)
        {
            _nativeTextView.SelectedRange = new NSRange(cursorPosition, selectionLength);
        }
#endif
    }

    public void RefreshStyling(string text, int cursorPosition, RichTextBlockKind currentBlockKind)
    {
#if MACCATALYST
        if (_nativeTextView is null)
        {
            return;
        }

        var selectedRange = _nativeTextView.SelectedRange;
        _nativeTextView.AttributedText = BuildAttributedMarkdown(text);
        _nativeTextView.SelectedRange = new NSRange(
            Math.Min((nint)text.Length, selectedRange.Location),
            Math.Min((nint)Math.Max(0, text.Length - selectedRange.Location), selectedRange.Length));
        _nativeTextView.TypingAttributes2 = BuildTypingAttributes(currentBlockKind);
#endif
    }

    public void RefreshTypingAttributes(RichTextBlockKind currentBlockKind)
    {
#if MACCATALYST
        if (_nativeTextView is not null)
        {
            _nativeTextView.TypingAttributes2 = BuildTypingAttributes(currentBlockKind);
        }
#endif
    }

#if MACCATALYST
    private static NSMutableAttributedString BuildAttributedMarkdown(string text)
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

    private static void ApplyStyledLine(NSMutableAttributedString attributed, string rawLine, int location)
    {
        if (string.IsNullOrEmpty(rawLine))
        {
            return;
        }

        var leadingWhitespace = VisualEditorDocumentController.GetLeadingWhitespace(rawLine);
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

        var taskMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"^([-*]\s\[[ xX]\]\s)");
        if (taskMatch.Success)
        {
            var contentStart = offset + taskMatch.Length;
            var contentLen = Math.Max(0, trimmed.Length - taskMatch.Length);
            ApplyHiddenMarker(attributed, new NSRange(offset, taskMatch.Length), 15);
            ApplyParagraphIndent(attributed, new NSRange(location, rawLine.Length), firstLineIndent: 0, headIndent: 34);
            ApplyTextStyle(attributed, new NSRange(contentStart, contentLen), 17, false);
            ApplyInlineStyleRuns(attributed, trimmed.Substring(taskMatch.Length), contentStart, 17);
            return;
        }

        var bulletMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"^([-*]\s)");
        if (bulletMatch.Success)
        {
            var contentStart = offset + bulletMatch.Length;
            var contentLen = Math.Max(0, trimmed.Length - bulletMatch.Length);
            ApplyMutedMarker(attributed, new NSRange(offset, bulletMatch.Length), 16);
            ApplyParagraphIndent(attributed, new NSRange(location, rawLine.Length), firstLineIndent: 0, headIndent: 26);
            ApplyTextStyle(attributed, new NSRange(contentStart, contentLen), 17, false);
            ApplyInlineStyleRuns(attributed, trimmed.Substring(bulletMatch.Length), contentStart, 17);
            return;
        }

        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            var contentStart = offset + 2;
            var contentLen = Math.Max(0, trimmed.Length - 2);
            ApplyHiddenMarker(attributed, new NSRange(offset, 2), 16);
            ApplyParagraphIndent(attributed, new NSRange(location, rawLine.Length), firstLineIndent: 18, headIndent: 18);
            ApplyTextStyle(attributed, new NSRange(contentStart, contentLen), 17, false);
            ApplyInlineStyleRuns(attributed, trimmed.Substring(2), contentStart, 17);
            return;
        }

        ApplyParagraphIndent(attributed, new NSRange(location, rawLine.Length), firstLineIndent: 0, headIndent: 0);
        ApplyTextStyle(attributed, new NSRange(location, rawLine.Length), 18, false);
        ApplyInlineStyleRuns(attributed, rawLine, location, 18);
    }

    private static void ApplyInlineStyleRuns(NSMutableAttributedString attributed, string content, int startOffset, nfloat baseFontSize)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        foreach (System.Text.RegularExpressions.Match match in BoldRunPattern.Matches(content))
        {
            var innerLength = match.Groups[1].Length;
            if (innerLength == 0)
            {
                continue;
            }

            ApplyHiddenMarker(attributed, new NSRange(startOffset + match.Index, 2), 1);
            attributed.AddAttributes(new UIStringAttributes
            {
                Font = UIFont.BoldSystemFontOfSize(baseFontSize),
                ForegroundColor = UIColor.FromRGB(22, 22, 22)
            }, new NSRange(startOffset + match.Index + 2, innerLength));
            ApplyHiddenMarker(attributed, new NSRange(startOffset + match.Index + 2 + innerLength, 2), 1);
        }

        foreach (System.Text.RegularExpressions.Match match in ItalicRunPattern.Matches(content))
        {
            var innerLength = match.Groups[1].Length;
            if (innerLength == 0)
            {
                continue;
            }

            ApplyHiddenMarker(attributed, new NSRange(startOffset + match.Index, 1), 1);
            attributed.AddAttributes(new UIStringAttributes
            {
                Font = UIFont.ItalicSystemFontOfSize(baseFontSize),
                ForegroundColor = UIColor.FromRGB(22, 22, 22)
            }, new NSRange(startOffset + match.Index + 1, innerLength));
            ApplyHiddenMarker(attributed, new NSRange(startOffset + match.Index + 1 + innerLength, 1), 1);
        }
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

    private static NSDictionary BuildTypingAttributes(RichTextBlockKind kind)
    {
        var font = kind switch
        {
            RichTextBlockKind.Header1 => UIFont.BoldSystemFontOfSize(32),
            RichTextBlockKind.Header2 => UIFont.BoldSystemFontOfSize(26),
            RichTextBlockKind.Header3 => UIFont.BoldSystemFontOfSize(22),
            RichTextBlockKind.Code => GetMonospaceFont(15, bold: false),
            _ => UIFont.SystemFontOfSize(kind == RichTextBlockKind.Paragraph ? 18 : 17)
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
}
