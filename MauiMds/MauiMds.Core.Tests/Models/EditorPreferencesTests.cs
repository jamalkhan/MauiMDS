using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Core.Tests.Models;

[TestClass]
public sealed class EditorPreferencesTests
{
    [TestMethod]
    public void Defaults_AutoSaveEnabled_IsTrue()
    {
        Assert.IsTrue(new EditorPreferences().AutoSaveEnabled);
    }

    [TestMethod]
    public void Defaults_AutoSaveDelaySeconds_IsThirty()
    {
        Assert.AreEqual(30, new EditorPreferences().AutoSaveDelaySeconds);
    }

    [TestMethod]
    public void Defaults_MaxLogFileSizeMb_IsTwo()
    {
        Assert.AreEqual(2, new EditorPreferences().MaxLogFileSizeMb);
    }

    [TestMethod]
    public void Defaults_InitialViewerRenderLineCount_IsTwenty()
    {
        Assert.AreEqual(20, new EditorPreferences().InitialViewerRenderLineCount);
    }

    [TestMethod]
    public void Defaults_Use24HourTime_IsFalse()
    {
        Assert.IsFalse(new EditorPreferences().Use24HourTime);
    }

    [TestMethod]
    public void Defaults_FileLogLevel_IsInformation()
    {
        Assert.AreEqual(LogLevel.Information, new EditorPreferences().FileLogLevel);
    }

    [TestMethod]
    public void Init_CanOverrideAllDefaults()
    {
        var prefs = new EditorPreferences
        {
            AutoSaveEnabled = false,
            AutoSaveDelaySeconds = 60,
            MaxLogFileSizeMb = 10,
            InitialViewerRenderLineCount = 50,
            Use24HourTime = true,
            FileLogLevel = LogLevel.Debug
        };

        Assert.IsFalse(prefs.AutoSaveEnabled);
        Assert.AreEqual(60, prefs.AutoSaveDelaySeconds);
        Assert.AreEqual(10, prefs.MaxLogFileSizeMb);
        Assert.AreEqual(50, prefs.InitialViewerRenderLineCount);
        Assert.IsTrue(prefs.Use24HourTime);
        Assert.AreEqual(LogLevel.Debug, prefs.FileLogLevel);
    }
}
