namespace MauiMds;

/// <summary>
/// Single source of truth for all Color values in the app.
/// Replaces the ~128 scattered Color.FromArgb("...") call sites.
/// Colors are grouped by the UI surface that owns them.
/// </summary>
public static class AppColors
{
    // ── Base text ─────────────────────────────────────────────────────────────
    // Default prose text used by the markdown viewer and the base label factory.
    public static readonly Color TextLight = Color.FromArgb("#161616");
    public static readonly Color TextDark  = Color.FromArgb("#F3EDE2");

    // Default text used by the syntax editor and its highlighter.
    public static readonly Color EditorTextLight = Color.FromArgb("#111111");
    public static readonly Color EditorTextDark  = Color.FromArgb("#F6F0E8");

    // Monospace / code block text (viewer rendering).
    public static readonly Color MonoTextLight = Color.FromArgb("#1E1E1E");
    public static readonly Color MonoTextDark  = Color.FromArgb("#F5F1E8");

    // ── Themed border (default container) ────────────────────────────────────
    // Used by CreateThemedBorder, table cells, and image placeholders.
    public static readonly Color BorderBgLight     = Color.FromArgb("#F8F3E8");
    public static readonly Color BorderBgDark      = Color.FromArgb("#2A2B2D");
    public static readonly Color BorderStrokeLight = Color.FromArgb("#D8CEBB");
    public static readonly Color BorderStrokeDark  = Color.FromArgb("#4A4B50");

    // ── Horizontal rule ───────────────────────────────────────────────────────
    public static readonly Color HRuleLight = Color.FromArgb("#CDBFA7");
    public static readonly Color HRuleDark  = BorderStrokeDark;

    // ── Block quote ───────────────────────────────────────────────────────────
    public static readonly Color BlockQuoteBgLight     = Color.FromArgb("#EFE7D8");
    public static readonly Color BlockQuoteBgDark      = Color.FromArgb("#343432");
    public static readonly Color BlockQuoteAccentLight = Color.FromArgb("#A08E71");
    public static readonly Color BlockQuoteAccentDark  = Color.FromArgb("#C8B79D");

    // ── Code block ────────────────────────────────────────────────────────────
    public static readonly Color CodeBgLight        = Color.FromArgb("#EAE3D6");
    public static readonly Color CodeBgDark         = Color.FromArgb("#1E1F21");
    public static readonly Color CodeBorderLight    = Color.FromArgb("#CFC3AE");
    public static readonly Color CodeBorderDark     = Color.FromArgb("#4A4C52");
    // Label shown above a fenced code block indicating language (e.g. "csharp").
    public static readonly Color CodeLangLight      = Color.FromArgb("#7B735F");
    public static readonly Color CodeLangDark       = Color.FromArgb("#CDBEA3");
    // Inline code in the viewer (background wash behind the code span).
    public static readonly Color InlineCodeBg        = Color.FromArgb("#E8E1D3");
    // 16% opacity black overlay used for inline code in the syntax editor.
    public static readonly Color InlineCodeOverlay   = Color.FromArgb("#2A000000");

    // ── Front matter ─────────────────────────────────────────────────────────
    public static readonly Color FrontMatterTitleLight   = Color.FromArgb("#7B5A2A");
    public static readonly Color FrontMatterTitleDark    = Color.FromArgb("#E6C88A");
    public static readonly Color FrontMatterContentLight = Color.FromArgb("#2C261E");
    public static readonly Color FrontMatterContentDark  = Color.FromArgb("#F0E7D9");
    public static readonly Color FrontMatterBgLight      = Color.FromArgb("#F0E5D2");
    public static readonly Color FrontMatterBgDark       = Color.FromArgb("#352F29");
    public static readonly Color FrontMatterBorderLight  = Color.FromArgb("#CCB28A");
    public static readonly Color FrontMatterBorderDark   = Color.FromArgb("#675843");

    // ── Render-failure / error block ─────────────────────────────────────────
    public static readonly Color ErrorBgLight     = Color.FromArgb("#FBE0DD");
    public static readonly Color ErrorBgDark      = Color.FromArgb("#432524");
    public static readonly Color ErrorBorderLight = Color.FromArgb("#D27A72");
    public static readonly Color ErrorBorderDark  = Color.FromArgb("#B65A54");

    // ── Inline formatting (viewer) ────────────────────────────────────────────
    public static readonly Color HighlightBgLight = Color.FromArgb("#FFF176");
    public static readonly Color HighlightBgDark  = Color.FromArgb("#665B00");
    public static readonly Color SuperscriptLight = Color.FromArgb("#5C6BC0");
    public static readonly Color SuperscriptDark  = Color.FromArgb("#9FA8DA");
    // Footnote reference number in prose (light mode only; dark uses EditorTextDark).
    public static readonly Color FootnoteRefColor = Color.FromArgb("#8D5A2B");
    public static readonly Color LinkLight        = Color.FromArgb("#2B6CB0");
    public static readonly Color LinkDark         = Color.FromArgb("#7DB6FF");

    // ── Syntax highlighting (editor overlay) ──────────────────────────────────
    public static readonly Color SynHeaderMarkerLight = Color.FromArgb("#7A3E9D");
    public static readonly Color SynHeaderMarkerDark  = Color.FromArgb("#C792EA");
    public static readonly Color SynBlockQuoteLight   = Color.FromArgb("#7C6B58");
    public static readonly Color SynBlockQuoteDark    = Color.FromArgb("#B9A98F");
    public static readonly Color SynListMarkerLight   = Color.FromArgb("#B35C1E");
    public static readonly Color SynListMarkerDark    = Color.FromArgb("#F0A65E");
    public static readonly Color SynLinkLight         = LinkLight;
    public static readonly Color SynLinkDark          = LinkDark;
    public static readonly Color SynImageLight        = Color.FromArgb("#0F766E");
    public static readonly Color SynImageDark         = Color.FromArgb("#5EEAD4");
    public static readonly Color SynInlineCodeLight   = Color.FromArgb("#8C4A16");
    public static readonly Color SynInlineCodeDark    = Color.FromArgb("#FFD08A");
    public static readonly Color SynEmphasisLight     = Color.FromArgb("#A61E4D");
    public static readonly Color SynEmphasisDark      = Color.FromArgb("#FF8FB1");
    public static readonly Color SynFootnoteLight     = FootnoteRefColor;
    public static readonly Color SynFootnoteDark      = Color.FromArgb("#F2B880");
    public static readonly Color SynCodeFenceLight    = Color.FromArgb("#6B7280");
    public static readonly Color SynCodeFenceDark     = Color.FromArgb("#9CA3AF");
    public static readonly Color SynFmDelimiterLight  = Color.FromArgb("#8B6F47");
    public static readonly Color SynFmDelimiterDark   = Color.FromArgb("#E8C68A");
    public static readonly Color SynFmKeyLight        = Color.FromArgb("#7C3AED");
    public static readonly Color SynFmKeyDark         = Color.FromArgb("#C4B5FD");
    public static readonly Color SynFmValueLight      = Color.FromArgb("#0F766E");
    public static readonly Color SynFmValueDark       = Color.FromArgb("#99F6E4");

    // ── Code token colors (viewer fenced code block) ──────────────────────────
    public static readonly Color CodeKeywordLight = Color.FromArgb("#8B3F96");
    public static readonly Color CodeKeywordDark  = Color.FromArgb("#C792EA");
    public static readonly Color CodeStringLight  = Color.FromArgb("#2F855A");
    public static readonly Color CodeStringDark   = Color.FromArgb("#9ECE6A");
    public static readonly Color CodeCommentLight = Color.FromArgb("#718096");
    public static readonly Color CodeCommentDark  = Color.FromArgb("#637777");
    public static readonly Color CodeNumberLight  = Color.FromArgb("#B7791F");
    public static readonly Color CodeNumberDark   = Color.FromArgb("#FF9E3B");
    public static readonly Color CodeTypeLight    = LinkLight;
    public static readonly Color CodeTypeDark     = LinkDark;
    public static readonly Color CodeDefaultLight = MonoTextLight;
    public static readonly Color CodeDefaultDark  = Color.FromArgb("#A9B1D6");

    // ── Editor toolbar (RichTextEditorView) ───────────────────────────────────
    public static readonly Color ToolbarBg             = Color.FromArgb("#F4F0E6");
    public static readonly Color ToolbarBorder         = Color.FromArgb("#D8D0C2");
    public static readonly Color ToolbarLabel          = Color.FromArgb("#6F6250");
    public static readonly Color ToolbarBtnBg          = Color.FromArgb("#E8E0CF");
    public static readonly Color ToolbarBtnText        = Color.FromArgb("#1A1A1A");
    public static readonly Color ToolbarBtnActiveBg    = Color.FromArgb("#1D1D1B");
    public static readonly Color ToolbarBtnActiveText  = Color.FromArgb("#F7F2E8");
    public static readonly Color EditorPrimaryText     = TextLight;

    // ── Workspace icons (WorkspaceItemIconView) ───────────────────────────────
    public static readonly Color IconDocBg          = Color.FromArgb("#FFFDFC");
    public static readonly Color IconDocStroke      = Color.FromArgb("#8D867C");
    public static readonly Color IconFoldCornerBg   = Color.FromArgb("#F3ECE2");
    public static readonly Color IconFolderBg       = Color.FromArgb("#D8C08C");
    public static readonly Color IconFolderStroke   = Color.FromArgb("#8B7347");
    public static readonly Color IconFolderTabBg    = Color.FromArgb("#E4CD9D");
    public static readonly Color IconRedFolderBg    = Color.FromArgb("#D44040");
    public static readonly Color IconRedFolderStroke = Color.FromArgb("#952222");
    public static readonly Color IconRedFolderTabBg = Color.FromArgb("#E86060");
    public static readonly Color IconAudioBg        = Color.FromArgb("#3A7DD4");
    public static readonly Color IconAudioStroke    = Color.FromArgb("#1A5CA0");
    public static readonly Color IconTranscribedBg  = Color.FromArgb("#3A9B62");
    public static readonly Color IconTranscribedStroke = Color.FromArgb("#1E7340");
    public static readonly Color IconQueuedBg       = Color.FromArgb("#7BBFEA");
    public static readonly Color IconQueuedStroke   = Color.FromArgb("#4A8FBF");
    public static readonly Color IconHashOverlay    = Color.FromArgb("#C9651A");

    // ── Recording group view ──────────────────────────────────────────────────
    public static readonly Color RecordingBtnBgLight   = Color.FromArgb("#DDD3BF");
    public static readonly Color RecordingBtnBgDark    = Color.FromArgb("#3A3835");
    public static readonly Color RecordingBtnTextLight = Color.FromArgb("#5A4E42");
    public static readonly Color RecordingBtnTextDark  = Color.FromArgb("#C8B89A");
    public static readonly Color AudioChipBgLight      = Color.FromArgb("#E8E0CF");
    public static readonly Color AudioChipBgDark       = Color.FromArgb("#3A3835");
    public static readonly Color AudioChipTextLight    = Color.FromArgb("#1A1A1A");
    public static readonly Color AudioChipTextDark     = Color.FromArgb("#F3EDE2");
    public static readonly Color PlayBtnActive         = Color.FromArgb("#CC4444");
    public static readonly Color PlayBtnBgLight        = Color.FromArgb("#5A4E42");
    public static readonly Color PlayBtnBgDark         = Color.FromArgb("#4A4340");

    // ── Snackbar / notification palette ──────────────────────────────────────
    public static readonly Color SnackDebugBgLight     = Color.FromArgb("#EEE8DD");
    public static readonly Color SnackDebugBgDark      = Color.FromArgb("#313234");
    public static readonly Color SnackDebugAccentLight = Color.FromArgb("#756B5B");
    public static readonly Color SnackDebugAccentDark  = Color.FromArgb("#9B9388");
    public static readonly Color SnackDebugTextLight   = Color.FromArgb("#1C1A17");
    public static readonly Color SnackDebugTextDark    = Color.FromArgb("#EEE5D9");
    public static readonly Color SnackDebugSubLight    = Color.FromArgb("#645C53");
    public static readonly Color SnackDebugSubDark     = Color.FromArgb("#B8B1A6");

    public static readonly Color SnackInfoBgLight      = Color.FromArgb("#F7F2E8");
    public static readonly Color SnackInfoBgDark       = Color.FromArgb("#2D2E30");
    public static readonly Color SnackInfoAccentLight  = Color.FromArgb("#8D7F67");
    public static readonly Color SnackInfoAccentDark   = Color.FromArgb("#CBBEA6");
    public static readonly Color SnackInfoTextLight    = TextLight;
    public static readonly Color SnackInfoTextDark     = TextDark;
    public static readonly Color SnackInfoSubLight     = Color.FromArgb("#5F584F");
    public static readonly Color SnackInfoSubDark      = Color.FromArgb("#BEB7AC");

    public static readonly Color SnackWarnBgLight      = Color.FromArgb("#FFF3C9");
    public static readonly Color SnackWarnBgDark       = Color.FromArgb("#43381B");
    public static readonly Color SnackWarnAccentLight  = Color.FromArgb("#C79000");
    public static readonly Color SnackWarnAccentDark   = Color.FromArgb("#FFD45C");
    public static readonly Color SnackWarnTextLight    = Color.FromArgb("#342600");
    public static readonly Color SnackWarnTextDark     = Color.FromArgb("#FFF4D2");
    public static readonly Color SnackWarnSubLight     = Color.FromArgb("#705B1F");
    public static readonly Color SnackWarnSubDark      = Color.FromArgb("#E8D39D");

    public static readonly Color SnackErrorBgLight     = ErrorBgLight;
    public static readonly Color SnackErrorBgDark      = ErrorBgDark;
    public static readonly Color SnackErrorAccentLight = Color.FromArgb("#B42318");
    public static readonly Color SnackErrorAccentDark  = Color.FromArgb("#FF8A7A");
    public static readonly Color SnackErrorTextLight   = Color.FromArgb("#3F0D07");
    public static readonly Color SnackErrorTextDark    = Color.FromArgb("#FFE7E4");
    public static readonly Color SnackErrorSubLight    = Color.FromArgb("#7D2E28");
    public static readonly Color SnackErrorSubDark     = Color.FromArgb("#F1B5AE");

    // ── Admonition blocks ─────────────────────────────────────────────────────
    // Each tuple: (BgLight, BgDark, BorderLight, BorderDark, HeaderLight, HeaderDark)
    public static readonly (Color BgLight, Color BgDark, Color BorderLight, Color BorderDark, Color HeaderLight, Color HeaderDark)
        AdmonitionNote = (
            Color.FromArgb("#EFF6FF"), Color.FromArgb("#1A2A3F"),
            Color.FromArgb("#3B82F6"), Color.FromArgb("#60A5FA"),
            Color.FromArgb("#1D4ED8"), Color.FromArgb("#93C5FD"));

    public static readonly (Color BgLight, Color BgDark, Color BorderLight, Color BorderDark, Color HeaderLight, Color HeaderDark)
        AdmonitionTip = (
            Color.FromArgb("#F0FDF4"), Color.FromArgb("#1A2E22"),
            Color.FromArgb("#22C55E"), Color.FromArgb("#4ADE80"),
            Color.FromArgb("#15803D"), Color.FromArgb("#86EFAC"));

    public static readonly (Color BgLight, Color BgDark, Color BorderLight, Color BorderDark, Color HeaderLight, Color HeaderDark)
        AdmonitionWarning = (
            Color.FromArgb("#FFFBEB"), Color.FromArgb("#2E2410"),
            Color.FromArgb("#F59E0B"), Color.FromArgb("#FCD34D"),
            Color.FromArgb("#B45309"), Color.FromArgb("#FDE68A"));

    public static readonly (Color BgLight, Color BgDark, Color BorderLight, Color BorderDark, Color HeaderLight, Color HeaderDark)
        AdmonitionImportant = (
            Color.FromArgb("#F5F3FF"), Color.FromArgb("#1E1533"),
            Color.FromArgb("#8B5CF6"), Color.FromArgb("#A78BFA"),
            Color.FromArgb("#6D28D9"), Color.FromArgb("#C4B5FD"));

    public static readonly (Color BgLight, Color BgDark, Color BorderLight, Color BorderDark, Color HeaderLight, Color HeaderDark)
        AdmonitionCaution = (
            Color.FromArgb("#FEF2F2"), Color.FromArgb("#2A1010"),
            Color.FromArgb("#EF4444"), Color.FromArgb("#F87171"),
            Color.FromArgb("#B91C1C"), Color.FromArgb("#FCA5A5"));

    public static readonly (Color BgLight, Color BgDark, Color BorderLight, Color BorderDark, Color HeaderLight, Color HeaderDark)
        AdmonitionQuestion = (
            Color.FromArgb("#ECFDF5"), Color.FromArgb("#102A22"),
            Color.FromArgb("#10B981"), Color.FromArgb("#34D399"),
            Color.FromArgb("#047857"), Color.FromArgb("#6EE7B7"));

    public static readonly (Color BgLight, Color BgDark, Color BorderLight, Color BorderDark, Color HeaderLight, Color HeaderDark)
        AdmonitionBug = (
            Color.FromArgb("#FFF7ED"), Color.FromArgb("#2A1800"),
            Color.FromArgb("#F97316"), Color.FromArgb("#FB923C"),
            Color.FromArgb("#C2410C"), Color.FromArgb("#FED7AA"));

    public static readonly (Color BgLight, Color BgDark, Color BorderLight, Color BorderDark, Color HeaderLight, Color HeaderDark)
        AdmonitionDefault = (
            Color.FromArgb("#F8FAFC"), Color.FromArgb("#1C1C24"),
            Color.FromArgb("#64748B"), Color.FromArgb("#94A3B8"),
            Color.FromArgb("#334155"), Color.FromArgb("#CBD5E1"));
}
