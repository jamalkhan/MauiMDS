namespace MauiMds;

public sealed class RealFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public IEnumerable<string> GetFiles(string directoryPath) => Directory.GetFiles(directoryPath);
    public void DeleteFile(string path) => File.Delete(path);
}
