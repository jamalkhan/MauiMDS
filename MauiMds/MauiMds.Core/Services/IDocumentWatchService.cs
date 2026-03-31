namespace MauiMds.Services;

public interface IDocumentWatchService : IDisposable
{
    event EventHandler<string>? DocumentChanged;
    void Watch(string? filePath);
    void Stop();
}
