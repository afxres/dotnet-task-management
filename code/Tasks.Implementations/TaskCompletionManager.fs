namespace Mikodev.Tasks.TaskCompletionManagement

open Mikodev.Tasks.Abstractions
open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading

type TaskCompletionManager<'K, 'V when 'K : equality>(scanInterval : TimeSpan) =
    // scan task loop interval (milliseconds)
    let interval = int scanInterval.TotalMilliseconds

    do
        if interval < 1 || interval > 1000 then
            raise (ArgumentOutOfRangeException())

    let cancellation = new CancellationTokenSource()

    let cancellationToken = cancellation.Token

    let locker = obj()

    let stopwatch = Stopwatch()

    let contexts = Dictionary<'K, TaskCompletionContext<'K, 'V>>()

    let update key action = lock locker (fun () ->
        match contexts.TryGetValue key with
        | true, value ->
            action value.Completion
            // ensure completed before removing
            assert value.Completion.Task.IsCompleted
            contexts.Remove key |> ignore
            true
        | _ -> false)

    let loop () = async {
        // scan and remove
        while not cancellationToken.IsCancellationRequested do
            do! Async.Sleep interval
            let list = lock locker (fun () ->
                let values = contexts.Values |> Seq.filter (fun x -> x.IsCancelled || x.IsExpired) |> Seq.toList
                for i in values do
                    contexts.Remove i.Key |> ignore
                values)
            for i in list do
                let completion = i.Completion
                if i.IsCancelled then
                    completion.SetCanceled()
                else
                    completion.SetException(TimeoutException())

        // cleanup after disposed
        // cancel all uncompleted tasks
        lock locker (fun () ->
            for i in contexts.Values do
                i.Completion.SetCanceled()
            contexts.Clear())
    }

    let verify () =
        if cancellationToken.IsCancellationRequested then
            raise (ObjectDisposedException(typeof<TaskCompletionManager<'K, 'V>>.Name))

    let verifyTimeout timeout =
        if timeout < TimeSpan.Zero then
            raise (ArgumentOutOfRangeException())

    let verifyNotNull a =
        if a = null then
            raise (ArgumentNullException())

    let insert key timeout token =
        let context = TaskCompletionContext(key, stopwatch, timeout, token)
        contexts.Add(key, context)
        context.Completion.Task

    do
        stopwatch.Start()
        Async.Start (loop())

    new() = new TaskCompletionManager<_, _>(TimeSpan.FromMilliseconds(100.0))

    interface ITaskCompletionManager<'K, 'V> with
        member __.Create(timeout, key, created, token) =
            verifyTimeout timeout
            let struct (k, result) = lock locker (fun () ->
                verify()
                match contexts.TryGetValue key with
                | true, context ->
                    struct (false, context.Completion.Task)
                | _ ->
                    struct (true, insert key timeout token))
            created <- k
            result

        member __.CreateNew(timeout, factory, key, token) =
            verifyNotNull factory
            verifyTimeout timeout
            let struct (k, result) = lock locker (fun () ->
                verify()
                // find a valid key
                let key = Seq.initInfinite id |> Seq.pick (fun x -> let key = factory.Invoke x in if contexts.ContainsKey key then None else Some key)
                struct (key, insert key timeout token))
            key <- k
            result

        member __.SetResult(key, value) = update key (fun x -> x.SetResult value)

        member __.SetException(key, error) = update key (fun x -> x.SetException error)

        member __.SetCancel key = update key (fun x -> x.SetCanceled())

    interface IDisposable with
        member __.Dispose() =
            if not cancellationToken.IsCancellationRequested then
                cancellation.Cancel()
                cancellation.Dispose()
            ()
