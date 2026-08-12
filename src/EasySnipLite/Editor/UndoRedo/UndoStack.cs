namespace EasySnipLite.Editor.UndoRedo;

/// <summary>可撤销/重做的命令：Execute 应用新状态，Undo 恢复旧状态。</summary>
public interface IUndoableCommand
{
    void Execute();
    void Undo();
}

/// <summary>把对象加入目标集合的命令（Redo 会再次 Add）。</summary>
public sealed class AddObjectCommand<T>(ICollection<T> target, T item) : IUndoableCommand
{
    public void Execute() => target.Add(item);
    public void Undo() => target.Remove(item);
}

/// <summary>从目标列表移除对象的命令，撤销时恢复到原索引（钳制到当前长度）。</summary>
public sealed class DeleteObjectCommand<T>(IList<T> target, T item) : IUndoableCommand
{
    private int _index;

    public void Execute()
    {
        _index = target.IndexOf(item);
        target.Remove(item);
    }

    public void Undo() => target.Insert(Math.Min(_index, target.Count), item);
}

/// <summary>任意属性变换命令：执行应用新值，撤销恢复旧值。</summary>
public sealed class TransformCommand(Action apply, Action revert) : IUndoableCommand
{
    public void Execute() => apply();
    public void Undo() => revert();
}

/// <summary>命令式撤销栈：Push 执行并入栈（清空 redo），Undo/Redo 在双栈间转移，超容量丢弃最旧。</summary>
public sealed class UndoStack
{
    private readonly int _capacity;
    private readonly List<IUndoableCommand> _undo = [];
    private readonly List<IUndoableCommand> _redo = [];

    public UndoStack(int capacity = 50)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;

    public void Push(IUndoableCommand command)
    {
        command.Execute();
        _undo.Add(command);
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();
        _redo.Add(command);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Execute();
        _undo.Add(command);
    }

    /// <summary>清空全部撤销/重做历史（标注整体清空时用）。</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
