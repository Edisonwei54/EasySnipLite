using EasySnipLite.Editor.UndoRedo;

namespace EasySnipLite.Tests;

/// <summary>M3 命令式撤销栈纯逻辑：Push/Undo/Redo/容量上限。</summary>
public class UndoStackTests
{
    private static (List<string> list, UndoStack stack) NewStack(int capacity = 50)
    {
        var list = new List<string>();
        return (list, new UndoStack(capacity));
    }

    // ---- Push / Execute ----

    [Fact]
    public void Push_ExecutesCommand_AddsItemToTarget()
    {
        var (list, stack) = NewStack();

        stack.Push(new AddObjectCommand<string>(list, "A"));

        Assert.Contains("A", list);
    }

    [Fact]
    public void Push_SecondCommand_UndoStackGrows()
    {
        var (list, stack) = NewStack();
        stack.Push(new AddObjectCommand<string>(list, "A"));
        stack.Push(new AddObjectCommand<string>(list, "B"));

        Assert.True(stack.CanUndo);
    }

    // ---- Undo / Redo ----

    [Fact]
    public void Undo_RemovesItem_CanUndoBecomesFalse()
    {
        var (list, stack) = NewStack();
        stack.Push(new AddObjectCommand<string>(list, "A"));

        stack.Undo();

        Assert.Empty(list);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void UndoThenRedo_ReaddsItem()
    {
        var (list, stack) = NewStack();
        stack.Push(new AddObjectCommand<string>(list, "A"));
        stack.Undo();

        stack.Redo();

        Assert.Contains("A", list);
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void Push_AfterUndo_ClearsRedoStack()
    {
        var (list, stack) = NewStack();
        stack.Push(new AddObjectCommand<string>(list, "A"));
        stack.Undo();

        stack.Push(new AddObjectCommand<string>(list, "B"));

        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_OnEmptyStack_DoesNotThrow()
    {
        var (_, stack) = NewStack();

        stack.Undo();

        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void Redo_OnEmptyStack_DoesNotThrow()
    {
        var (_, stack) = NewStack();

        stack.Redo();

        Assert.False(stack.CanRedo);
    }

    // ---- 容量上限 ----

    [Fact]
    public void Push_OverCapacity_DropsOldestUndo()
    {
        var (list, stack) = NewStack(capacity: 2);
        stack.Push(new AddObjectCommand<string>(list, "A"));
        stack.Push(new AddObjectCommand<string>(list, "B"));
        stack.Push(new AddObjectCommand<string>(list, "C"));

        stack.Undo(); // 撤销 C
        stack.Undo(); // 撤销 B

        Assert.False(stack.CanUndo); // A 的命令已被丢弃，无可再撤销
        Assert.Equal(new[] { "A" }, list); // A 从未被撤销 → 仍保留；B、C 已被撤销移除
    }

    [Fact]
    public void Constructor_ZeroCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UndoStack(0));
    }

    // ---- Delete 命令 ----

    [Fact]
    public void DeleteCommand_Execute_RemovesItem()
    {
        var list = new List<string> { "A", "B", "C" };
        var stack = new UndoStack();
        var cmd = new DeleteObjectCommand<string>(list, "B");

        stack.Push(cmd);

        Assert.Equal(new[] { "A", "C" }, list);
    }

    [Fact]
    public void DeleteCommand_Undo_RestoresItemAtOriginalIndex()
    {
        var list = new List<string> { "A", "B", "C" };
        var stack = new UndoStack();
        stack.Push(new DeleteObjectCommand<string>(list, "B"));

        stack.Undo();

        Assert.Equal(new[] { "A", "B", "C" }, list);
    }

    // ---- Transform 命令 ----

    [Fact]
    public void TransformCommand_ExecuteAndUndo_AppliesAndReverts()
    {
        double value = 1;
        var stack = new UndoStack();
        var cmd = new TransformCommand(() => value = 2, () => value = 1);

        stack.Push(cmd);
        Assert.Equal(2, value);

        stack.Undo();
        Assert.Equal(1, value);

        stack.Redo();
        Assert.Equal(2, value);
    }

    // ---- Clear（issue #20 标注清空：Esc 一级清空标注后撤销栈同步清空） ----

    [Fact]
    public void Clear_RemovesUndoAndRedo()
    {
        var stack = new UndoStack();
        stack.Push(new TransformCommand(() => { }, () => { }));
        stack.Undo(); // 制造 redo 内容
        stack.Push(new TransformCommand(() => { }, () => { }));

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.UndoCount);
    }

    [Fact]
    public void Clear_EmptyStack_NoThrow()
    {
        var stack = new UndoStack();
        stack.Clear();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }
}
