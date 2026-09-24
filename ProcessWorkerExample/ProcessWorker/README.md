# Process Worker

Keyed producer/consumer work queue built on [System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).

## Core Concepts

| Concept | Description |
|---------|-------------|
| **IProcessWorkerProvider** | Static keyed registry. `GetOrCreate(key, config)` returns an existing worker or creates a new one. |
| **IProcessWorkerProducer** | Submits a delegate to the channel via `EnqueueAsync` and returns its `ProcessWorkerInfo`. |
| **IProcessWorkerConsumer** | Runs a `SemaphoreSlim`-bounded worker loop reading from the channel. Each item runs in its own async DI scope. |
| **ProcessWorkerInfo** | `ProcessId`, the item's `StoppingToken`, and `Completion` to await the result. |

## Usage

```csharp
var worker = provider.GetOrCreate("pdf-export", new ProcessWorkerConfiguration { Concurrency = 3 });

// Enqueue work; the delegate receives the item's scoped IServiceProvider and its stopping token
var info = await worker.Producer.EnqueueAsync(async (services, cancellationToken) =>
{
    var exporter = services.GetRequiredService<MyPdfExporter>();
    await exporter.ExportAsync(cancellationToken);
});

// Await completion; faults with the delegate's exception, or is canceled
await info.Completion;
```

## Process Status

`Queued` --> `Running` --> `Done` / `Canceled` / `Failed` / `Fatal`

`CancellationRequested` covers the time between `CancelWorkItemAsync` and the actual cancellation.

**Fatal** is a terminal state for the entire worker, not just a single item. When the consumer encounters a fatal error, it flushes the channel: all outstanding work items transition to `Fatal` and their `Completion` faults.

## DI Registration

```csharp
services.RegisterProcessWorker();
```

## Layout

Standard `Common/` `Model/` `Service/` `Startup/` split, one implementation per interface with matching names. `IProcessWorkerProvider` is the only type registered in DI (singleton); workers, producers, and consumers are created by the provider, never resolved.
