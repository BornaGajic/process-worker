namespace ProcessWorker.Model;

public delegate Task<TState> ProcessWorkerStatusChangeCallback<TState>(Guid processId, ProcessStatus newStatus) 
    where TState : ProcessWorkerState;
