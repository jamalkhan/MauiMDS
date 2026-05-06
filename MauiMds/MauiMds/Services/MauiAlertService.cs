namespace MauiMds;

internal sealed class MauiAlertService : IAlertService
{
    public async Task ShowAlertAsync(string title, string message, string cancel)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not null)
            await page.DisplayAlertAsync(title, message, cancel);
    }
}
