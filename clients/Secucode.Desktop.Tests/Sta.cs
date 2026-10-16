using System.Runtime.ExceptionServices;

namespace Secucode.Desktop.Tests;

// WPF objects need an STA thread; xUnit runs tests on MTA threads.
public static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
