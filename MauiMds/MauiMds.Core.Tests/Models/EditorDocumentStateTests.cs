using MauiMds.Models;

namespace MauiMds.Core.Tests.Models;

[TestClass]
public sealed class EditorDocumentStateTests
{
    [TestMethod]
    public void DefaultState_HasExpectedDefaultValues()
    {
        var state = new EditorDocumentState();

        Assert.AreEqual(string.Empty, state.FilePath);
        Assert.AreEqual("Untitled.mds", state.FileName);
        Assert.AreEqual(string.Empty, state.Content);
        Assert.AreEqual(string.Empty, state.OriginalContent);
        Assert.IsTrue(state.IsUntitled);
        Assert.IsFalse(state.IsDirty);
        Assert.IsFalse(state.IsReadOnly);
        Assert.IsNull(state.FileSizeBytes);
        Assert.IsNull(state.LastModified);
        Assert.AreEqual("utf-8", state.EncodingName);
    }

    [TestMethod]
    public void DefaultState_NewLineMatchesEnvironment()
    {
        var state = new EditorDocumentState();

        Assert.AreEqual(Environment.NewLine, state.NewLine);
    }

    [TestMethod]
    public void State_CanBeMarkedDirty()
    {
        var state = new EditorDocumentState { IsDirty = true };

        Assert.IsTrue(state.IsDirty);
    }

    [TestMethod]
    public void State_CanBeMarkedReadOnly()
    {
        var state = new EditorDocumentState { IsReadOnly = true };

        Assert.IsTrue(state.IsReadOnly);
    }

    [TestMethod]
    public void State_CanHoldFileMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new EditorDocumentState
        {
            FilePath = "/tmp/notes.mds",
            FileName = "notes.mds",
            FileSizeBytes = 1024,
            LastModified = now,
            IsUntitled = false
        };

        Assert.AreEqual("/tmp/notes.mds", state.FilePath);
        Assert.AreEqual("notes.mds", state.FileName);
        Assert.AreEqual(1024, state.FileSizeBytes);
        Assert.AreEqual(now, state.LastModified);
        Assert.IsFalse(state.IsUntitled);
    }

    [TestMethod]
    public void State_EncodingNameCanBeChanged()
    {
        var state = new EditorDocumentState { EncodingName = "utf-16" };

        Assert.AreEqual("utf-16", state.EncodingName);
    }
}
