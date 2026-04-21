using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public sealed class ImageBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.Image;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var image = new Image
        {
            Aspect = Aspect.AspectFit,
            MaximumHeightRequest = 480,
            Margin = new Thickness(0)
        };

        var source = context.InlineFormatter.ResolveImageSource(block.ImageSource, context.SourceFilePath);
        if (source is not null)
        {
            image.Source = source;
        }

        var stack = new VerticalStackLayout
        {
            Spacing = 6,
            Children = { image }
        };

        var captionText = !string.IsNullOrWhiteSpace(block.ImageTitle)
            ? block.ImageTitle
            : block.ImageAltText;

        if (!string.IsNullOrWhiteSpace(captionText))
        {
            stack.Children.Add(MarkdownViewFactory.CreateRichTextLabel(captionText, 12, FontAttributes.Italic, new Thickness(0), context.InlineFormatter));
        }

        return MarkdownViewFactory.CreateThemedBorder(stack, new Thickness(14), new Thickness(0, 6, 0, 12));
    }
}
