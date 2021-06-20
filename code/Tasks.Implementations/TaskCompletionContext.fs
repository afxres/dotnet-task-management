namespace Mikodev.Tasks.TaskCompletionManagement

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

type TaskCompletionContext<'K, 'V>(key : 'K, stopwatch : Stopwatch, timeout : TimeSpan, token : CancellationToken) =
    let creation = stopwatch.Elapsed

    let completion = TaskCompletionSource<'V>(TaskCreationOptions.RunContinuationsAsynchronously)

    do
        assert (stopwatch.IsRunning)
        assert (timeout >= TimeSpan.Zero)

    member __.Completion = completion

    member __.Key = key

    member __.IsExpired = (stopwatch.Elapsed - creation) > timeout

    member __.IsCancelled = token.IsCancellationRequested
