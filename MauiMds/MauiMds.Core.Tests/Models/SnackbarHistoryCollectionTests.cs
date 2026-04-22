using System.Collections.Specialized;
using MauiMds.Models;

namespace MauiMds.Core.Tests.Models;

[TestClass]
public sealed class SnackbarHistoryCollectionTests
{
    private static SnackbarMessage MakeMessage(string text, SnackbarMessageLevel level = SnackbarMessageLevel.Info) =>
        new()
        {
            Level = level,
            Category = "Test",
            Message = text,
            Timestamp = DateTimeOffset.UtcNow
        };

    [TestMethod]
    public void ReplaceAll_ReplacesExistingItems()
    {
        var collection = new SnackbarHistoryCollection();
        collection.Add(MakeMessage("old message"));

        var newMessages = new[] { MakeMessage("new one"), MakeMessage("new two") };
        collection.ReplaceAll(newMessages);

        Assert.AreEqual(2, collection.Count);
        Assert.AreEqual("new one", collection[0].Message);
        Assert.AreEqual("new two", collection[1].Message);
    }

    [TestMethod]
    public void ReplaceAll_WithEmptyList_ClearsCollection()
    {
        var collection = new SnackbarHistoryCollection();
        collection.Add(MakeMessage("existing"));

        collection.ReplaceAll([]);

        Assert.AreEqual(0, collection.Count);
    }

    [TestMethod]
    public void ReplaceAll_RaisesCollectionChangedEvent()
    {
        var collection = new SnackbarHistoryCollection();
        collection.Add(MakeMessage("old"));
        var eventFired = false;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                eventFired = true;
            }
        };

        collection.ReplaceAll([MakeMessage("new")]);

        Assert.IsTrue(eventFired, "Expected CollectionChanged Reset event");
    }

    [TestMethod]
    public void SnackbarMessage_DisplayMessage_ExcludesRedundantExceptionText()
    {
        var message = new SnackbarMessage
        {
            Level = SnackbarMessageLevel.Error,
            Category = "Test",
            Message = "File not found: path.txt",
            Timestamp = DateTimeOffset.UtcNow,
            ExceptionMessage = "path.txt"
        };

        StringAssert.Contains(message.DisplayMessage, "File not found");
    }

    [TestMethod]
    public void SnackbarMessage_DisplayMessage_AppendsDistinctExceptionText()
    {
        var message = new SnackbarMessage
        {
            Level = SnackbarMessageLevel.Error,
            Category = "Test",
            Message = "An error occurred",
            Timestamp = DateTimeOffset.UtcNow,
            ExceptionMessage = "Access denied"
        };

        StringAssert.Contains(message.DisplayMessage, "An error occurred");
        StringAssert.Contains(message.DisplayMessage, "Access denied");
    }

    [TestMethod]
    public void SnackbarMessage_FromLogLevel_MapsCorrectly()
    {
        Assert.AreEqual(SnackbarMessageLevel.Info, SnackbarMessage.FromLogLevel(Microsoft.Extensions.Logging.LogLevel.Information));
        Assert.AreEqual(SnackbarMessageLevel.Warning, SnackbarMessage.FromLogLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
        Assert.AreEqual(SnackbarMessageLevel.Error, SnackbarMessage.FromLogLevel(Microsoft.Extensions.Logging.LogLevel.Error));
        Assert.AreEqual(SnackbarMessageLevel.Error, SnackbarMessage.FromLogLevel(Microsoft.Extensions.Logging.LogLevel.Critical));
        Assert.AreEqual(SnackbarMessageLevel.Debug, SnackbarMessage.FromLogLevel(Microsoft.Extensions.Logging.LogLevel.Debug));
    }

    [TestMethod]
    public void SnackbarMessage_FromLogLevel_TraceIsDebug()
    {
        Assert.AreEqual(SnackbarMessageLevel.Debug, SnackbarMessage.FromLogLevel(Microsoft.Extensions.Logging.LogLevel.Trace));
    }

    [TestMethod]
    public void SnackbarMessage_FromLogLevel_NoneDefaultsToInfo()
    {
        Assert.AreEqual(SnackbarMessageLevel.Info, SnackbarMessage.FromLogLevel(Microsoft.Extensions.Logging.LogLevel.None));
    }

    [TestMethod]
    public void SnackbarMessage_LevelLabel_AllLevels()
    {
        Assert.AreEqual("Debug", new SnackbarMessage { Level = SnackbarMessageLevel.Debug, Category = "T", Message = "m", Timestamp = DateTimeOffset.UtcNow }.LevelLabel);
        Assert.AreEqual("Info", new SnackbarMessage { Level = SnackbarMessageLevel.Info, Category = "T", Message = "m", Timestamp = DateTimeOffset.UtcNow }.LevelLabel);
        Assert.AreEqual("Warning", new SnackbarMessage { Level = SnackbarMessageLevel.Warning, Category = "T", Message = "m", Timestamp = DateTimeOffset.UtcNow }.LevelLabel);
        Assert.AreEqual("Error", new SnackbarMessage { Level = SnackbarMessageLevel.Error, Category = "T", Message = "m", Timestamp = DateTimeOffset.UtcNow }.LevelLabel);
    }

    [TestMethod]
    public void SnackbarMessage_ExceptionDetails_CanBeSet()
    {
        var message = new SnackbarMessage
        {
            Level = SnackbarMessageLevel.Error,
            Category = "Test",
            Message = "Something failed",
            Timestamp = DateTimeOffset.UtcNow,
            ExceptionDetails = "Stack trace line 1\nStack trace line 2"
        };

        Assert.IsNotNull(message.ExceptionDetails);
        StringAssert.Contains(message.ExceptionDetails, "Stack trace");
    }
}
