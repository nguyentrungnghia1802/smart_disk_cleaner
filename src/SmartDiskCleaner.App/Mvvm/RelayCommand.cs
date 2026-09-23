namespace SmartDiskCleaner.App.Mvvm;

using System.Windows.Input;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action execute) : this(_ => execute(), null) { }
    public RelayCommand(Action execute, Func<bool> canExecute) : this(_ => execute(), _ => canExecute()) { }
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public void NotifyCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute) : this(_ => execute(), null) { }
    public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute) : this(_ => execute(), _ => canExecute()) { }
    public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (_isExecuting != value)
            {
                _isExecuting = value;
                NotifyCanExecuteChanged();
            }
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        IsExecuting = true;
        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public void NotifyCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
