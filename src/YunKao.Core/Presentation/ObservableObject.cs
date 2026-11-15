using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace YunKao.Core.Presentation;

/// <summary>
/// 最小的属性通知基类。界面层通过 x:Bind 绑定，不依赖第三方 MVVM 包。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// 同步命令。CanExecute 由调用方通过 <see cref="NotifyCanExecuteChanged"/> 刷新。
/// </summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute(parameter);
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
