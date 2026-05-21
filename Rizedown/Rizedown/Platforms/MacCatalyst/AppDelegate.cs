using Foundation;
using Rizedown.ViewModels;
using Microsoft.Extensions.Logging;
using ObjCRuntime;
using UIKit;

namespace Rizedown;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // Mac Catalyst routes NSApplicationDelegate.applicationShouldTerminate: to the UIApplicationDelegate.
    // The return type is NSApplicationTerminateReply: 0 = Cancel, 1 = Now, 2 = Later.
    [Export("applicationShouldTerminate:")]
    public nuint ApplicationShouldTerminate(UIApplication application)
    {
        const nuint terminateNow = 1;
        const nuint terminateLater = 2;

        var vm = IPlatformApplication.Current?.Services.GetService<MainViewModel>();
        if (vm?.Recording.IsRecording != true)
        {
            return terminateNow;
        }

        _ = PromptStopRecordingAndQuitAsync(vm);
        return terminateLater;
    }

    private static async Task PromptStopRecordingAndQuitAsync(MainViewModel vm)
    {
        var logger = IPlatformApplication.Current?.Services
            .GetService<ILoggerFactory>()?.CreateLogger<AppDelegate>();

        try
        {
            var shouldQuit = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var windows = Microsoft.Maui.Controls.Application.Current?.Windows;
                var page = windows?.Count > 0 ? windows[0].Page : null;
                if (page is null) return true;

                return await page.DisplayAlertAsync(
                    "Recording in Progress",
                    "A recording is currently active. Stop the recording and quit?",
                    "Stop & Quit",
                    "Cancel");
            });

            if (shouldQuit)
            {
                await vm.StopRecordingAsync();
            }

            ReplyToShouldTerminate(shouldQuit);
        }
        catch (Exception ex)
        {
            // Safety net — allow termination so the app cannot get stuck.
            logger?.LogError(ex, "AppDelegate: error during quit prompt; forcing termination.");
            ReplyToShouldTerminate(true);
        }
    }

    private static void ReplyToShouldTerminate(bool shouldTerminate)
    {
        // NSApplicationTerminateReply is not bound in .NET Mac Catalyst, so we use
        // NSObject.PerformSelector to forward the reply message to NSApplication.
        var uiAppHandle = UIApplication.SharedApplication.Handle;
        var selector = new Selector("replyToApplicationShouldTerminate:");
        Runtime.GetNSObject(uiAppHandle)?.PerformSelector(selector, new NSNumber(shouldTerminate), 0);
    }
}
