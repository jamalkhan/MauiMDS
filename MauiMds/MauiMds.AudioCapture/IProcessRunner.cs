namespace MauiMds.AudioCapture;

public interface IProcessRunner
{
    Task<(int ExitCode, string Stderr)> RunAsync(string fileName, string arguments);
}
