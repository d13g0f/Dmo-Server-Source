using System;
using System.Threading;
using System.Threading.Tasks;

public class ConsistentAutoAttack
{
    private readonly Action _startAttack;
    private CancellationTokenSource _cts;

    public ConsistentAutoAttack(Action startAttack)
    {
        _startAttack = startAttack ?? throw new ArgumentNullException(nameof(startAttack));
    }

    public void StartLooping()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
            return; // Already running

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                _startAttack();
                await Task.Yield(); // Keeps it responsive to the task scheduler
            }
        }, token);
    }

    // Optional: Make StopLooping private or remove it
    // public void StopLooping() { /* Removed to prevent stopping */ }
}
