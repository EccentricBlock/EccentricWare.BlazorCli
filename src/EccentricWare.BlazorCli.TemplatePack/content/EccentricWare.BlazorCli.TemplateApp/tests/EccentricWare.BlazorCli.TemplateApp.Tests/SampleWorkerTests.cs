using EccentricWare.BlazorCli.TemplateApp.Application;
using EccentricWare.BlazorCli.TemplateApp.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EccentricWare.BlazorCli.TemplateApp.Tests;

public sealed class SampleWorkerTests
{
    private sealed class RecordingService : IApplicationStatusService
    {
        public int Calls;
        public bool Throw;
        public readonly TaskCompletionSource FirstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ApplicationStatusDto GetStatus(AppMode mode)
        {
            Interlocked.Increment(ref Calls);
            FirstCall.TrySetResult();
            if (Throw) throw new InvalidOperationException("Simulated worker dependency failure");
            return new("Test", "9.9.9", "run", "healthy");
        }
    }

    [Fact]
    public async Task WorkerStartsAndStopsCooperatively()
    {
        var service = new RecordingService();
        using var worker = new SampleWorker(NullLogger<SampleWorker>.Instance, service);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(timeout.Token);
        // BackgroundService does not promise that ExecuteAsync has begun when StartAsync returns;
        // stopping first made this test fail under load. Wait until the worker has really started.
        await service.FirstCall.Task.WaitAsync(timeout.Token);
        await worker.StopAsync(timeout.Token);
        Assert.Equal(1, Volatile.Read(ref service.Calls));
        Assert.True(worker.ExecuteTask?.IsCompleted);
    }

    [Fact]
    public async Task CancellationBeforeFirstTickIsSafe()
    {
        var service = new RecordingService();
        using var worker = new SampleWorker(NullLogger<SampleWorker>.Instance, service);
        using var cancellation = new CancellationTokenSource();
        await worker.StartAsync(cancellation.Token);
        cancellation.Cancel();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token);
        Assert.True(worker.ExecuteTask?.IsCompleted);
    }

    [Fact]
    public async Task DependencyFailureFaultsWorkerTask()
    {
        var service = new RecordingService { Throw = true };
        using var worker = new SampleWorker(NullLogger<SampleWorker>.Instance, service);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // BackgroundService.StartAsync may itself observe a synchronously faulted task.
        try { await worker.StartAsync(timeout.Token); }
        catch (InvalidOperationException) { /* expected */ }
        Assert.NotNull(worker.ExecuteTask);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await worker.ExecuteTask!);
    }
}
