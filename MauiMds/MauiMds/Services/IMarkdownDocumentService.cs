using MauiMds.Models;

namespace MauiMds.Services;

public interface IMarkdownDocumentService
{
    Task<MarkdownDocument?> LoadInitialDocumentAsync();
    Task<MarkdownDocument?> PickDocumentAsync();
}
