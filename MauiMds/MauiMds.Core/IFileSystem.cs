namespace MauiMds;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IEnumerable<string> GetFiles(string directoryPath);
    void DeleteFile(string path);
}
