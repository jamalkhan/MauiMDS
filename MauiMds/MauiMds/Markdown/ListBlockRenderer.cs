using MauiMds.Models;

namespace MauiMds.Markdown;

public sealed class ListBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType)
    {
        return blockType is BlockType.BulletListItem or BlockType.OrderedListItem or BlockType.TaskListItem;
    }

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        return block.Type switch
        {
            BlockType.BulletListItem => CreateListItemView("•", block.Content, block.ListLevel, context),
            BlockType.OrderedListItem => CreateListItemView($"{block.OrderedNumber}.", block.Content, block.ListLevel, context),
            BlockType.TaskListItem => CreateTaskListView(block, context),
            _ => MarkdownViewFactory.CreateRichTextLabel(block.Content, 17, FontAttributes.None, new Thickness(0), context.InlineFormatter)
        };
    }

    private static View CreateTaskListView(MarkdownBlock block, MarkdownRenderContext context)
    {
        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            Margin = new Thickness(18 + (block.ListLevel * 22), 0, 0, 6)
        };

        var checkBox = new CheckBox
        {
            IsChecked = block.IsChecked,
            VerticalOptions = LayoutOptions.Start
        };

        var contentLabel = MarkdownViewFactory.CreateRichTextLabel(block.Content, 17, FontAttributes.None, new Thickness(0), context.InlineFormatter);

        layout.Children.Add(checkBox);
        layout.Children.Add(contentLabel);
        Grid.SetColumn(contentLabel, 1);
        return layout;
    }

    private static View CreateListItemView(string marker, string content, int listLevel, MarkdownRenderContext context)
    {
        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            Margin = new Thickness(18 + (listLevel * 22), 0, 0, 4)
        };

        var markerLabel = MarkdownViewFactory.CreateRichTextLabel(marker, 17, FontAttributes.Bold, new Thickness(0), context.InlineFormatter);
        var contentLabel = MarkdownViewFactory.CreateRichTextLabel(content, 17, FontAttributes.None, new Thickness(0), context.InlineFormatter);

        layout.Children.Add(markerLabel);
        layout.Children.Add(contentLabel);
        Grid.SetColumn(contentLabel, 1);
        return layout;
    }
}
