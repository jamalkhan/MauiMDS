using MauiMds.Models;

namespace MauiMds.Tests.Features.Editor;

[TestClass]
public sealed class WorkspaceTreeItemTests
{
    [TestMethod]
    public void FileItem_HasExpectedDefaults()
    {
        var item = new WorkspaceTreeItem("/workspace/doc.mds", isDirectory: false, depth: 0);

        Assert.IsFalse(item.IsDirectory);
        Assert.IsTrue(item.CanDelete);
        Assert.IsTrue(item.CanRename);
        Assert.IsFalse(item.IsPendingDelete);
        Assert.IsFalse(item.IsRenaming);
        Assert.IsFalse(item.IsSelected);
    }

    [TestMethod]
    public void DirectoryItem_CanNotDeleteOrRename()
    {
        var item = new WorkspaceTreeItem("/workspace/folder", isDirectory: true, depth: 0);

        Assert.IsTrue(item.IsDirectory);
        Assert.IsFalse(item.CanDelete);
        Assert.IsFalse(item.CanRename);
    }

    [TestMethod]
    public void Name_IsLastSegmentOfFullPath()
    {
        var item = new WorkspaceTreeItem("/workspace/notes/meeting.mds", isDirectory: false, depth: 1);

        Assert.AreEqual("meeting.mds", item.Name);
    }

    [TestMethod]
    public void IndentWidth_ScalesWithDepth()
    {
        var root = new WorkspaceTreeItem("/workspace/folder", isDirectory: true, depth: 0);
        var child = new WorkspaceTreeItem("/workspace/folder/sub", isDirectory: true, depth: 1);
        var grandchild = new WorkspaceTreeItem("/workspace/folder/sub/doc.mds", isDirectory: false, depth: 2);

        Assert.AreEqual(0, root.IndentWidth);
        Assert.IsTrue(child.IndentWidth > 0);
        Assert.IsTrue(grandchild.IndentWidth > child.IndentWidth);
    }

    [TestMethod]
    public void IsRenaming_CanRename_IsFalseWhenPendingDelete()
    {
        var item = new WorkspaceTreeItem("/workspace/doc.mds", isDirectory: false, depth: 0);

        item.IsPendingDelete = true;

        Assert.IsFalse(item.CanRename, "pending-delete items cannot be renamed");
    }

    [TestMethod]
    public void RenameText_DefaultsToName()
    {
        var item = new WorkspaceTreeItem("/workspace/doc.mds", isDirectory: false, depth: 0);

        Assert.AreEqual("doc.mds", item.RenameText);
    }

    [TestMethod]
    public void ResetRenameText_RestoresNameAfterEdit()
    {
        var item = new WorkspaceTreeItem("/workspace/doc.mds", isDirectory: false, depth: 0);
        item.RenameText = "modified.mds";

        item.ResetRenameText();

        Assert.AreEqual("doc.mds", item.RenameText);
    }

    [TestMethod]
    public void PropertyChanged_FiresForIsSelected()
    {
        var item = new WorkspaceTreeItem("/workspace/doc.mds", isDirectory: false, depth: 0);
        var fired = new List<string?>();
        item.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        item.IsSelected = true;

        CollectionAssert.Contains(fired, nameof(WorkspaceTreeItem.IsSelected));
    }

    [TestMethod]
    public void PropertyChanged_FiresForIsRenaming()
    {
        var item = new WorkspaceTreeItem("/workspace/doc.mds", isDirectory: false, depth: 0);
        var fired = new List<string?>();
        item.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        item.IsRenaming = true;

        CollectionAssert.Contains(fired, nameof(WorkspaceTreeItem.IsRenaming));
    }

    [TestMethod]
    public void ExpandGlyph_ChangesWithExpansionState()
    {
        var item = new WorkspaceTreeItem("/workspace/folder", isDirectory: true, depth: 0);
        var collapsedGlyph = item.ExpandGlyph;

        item.IsExpanded = !item.IsExpanded;

        Assert.AreNotEqual(collapsedGlyph, item.ExpandGlyph);
    }

    [TestMethod]
    public void HasChildren_TrueWhenChildAdded()
    {
        var parent = new WorkspaceTreeItem("/workspace", isDirectory: true, depth: 0);
        var child = new WorkspaceTreeItem("/workspace/doc.mds", isDirectory: false, depth: 1, parent: parent);

        parent.Children.Add(child);

        Assert.IsTrue(parent.HasChildren);
        Assert.AreSame(parent, child.Parent);
    }
}
