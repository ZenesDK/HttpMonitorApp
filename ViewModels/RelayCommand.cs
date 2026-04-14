#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HttpMonitorApp.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
    
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;
    
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    
    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);
    
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;
            
        _isExecuting = true;
        RaiseCanExecuteChanged();
        
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            // Логируем ошибку, но не даем команде зависнуть
            System.Diagnostics.Debug.WriteLine($"Command error: {ex.Message}");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }
    
    public event EventHandler? CanExecuteChanged;
    
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}