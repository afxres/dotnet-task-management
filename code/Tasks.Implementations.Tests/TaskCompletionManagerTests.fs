module TaskCompletionManagerTests

open Mikodev.Tasks.Abstractions
open Mikodev.Tasks.TaskCompletionManagement
open System
open System.Threading
open System.Threading.Tasks
open Xunit

[<Fact>]
let ``Create New Then Timeout`` () : Task<unit> =
    let manager = new TaskCompletionManager<string, obj>() :> ITaskCompletionManager<string, obj>
    let factory = new Func<_, _>(fun _ -> Guid.NewGuid().ToString())
    let (alpha, _) = manager.CreateNew(TimeSpan.FromSeconds(1.0), factory, token = CancellationToken.None)
    let (bravo, _) = manager.CreateNew(TimeSpan.FromSeconds(3.0), factory, token = CancellationToken.None)

    Async.StartAsTask (async {
        do! Async.Sleep 2000
        Assert.True(alpha.IsFaulted)
        do! Assert.ThrowsAsync<TimeoutException>(fun () -> alpha :> Task) |> Async.AwaitTask |> Async.Ignore
        Assert.False(bravo.IsCompleted)

        do! Async.Sleep 2000
        Assert.True(bravo.IsFaulted)
        do! Assert.ThrowsAsync<TimeoutException>(fun () -> bravo :> Task) |> Async.AwaitTask |> Async.Ignore
    })

[<Fact>]
let ``Create New Then Set Result`` () =
    let random = Random()
    let manager = new TaskCompletionManager<int, double>() :> ITaskCompletionManager<int, double>
    let (alpha, key) = manager.CreateNew(TimeSpan.FromSeconds(1.0), (fun _ -> random.Next()), token = CancellationToken.None)
    Assert.False(alpha.IsCompleted)
    Assert.True(manager.SetResult(key, double key))
    Assert.True(alpha.IsCompleted)
    Assert.Equal(double key, alpha.Result)
    Assert.False(manager.SetResult(key, nan))
    ()

[<Fact>]
let ``Create New Then Set Exception`` () =
    let manager = new TaskCompletionManager<string, obj>() :> ITaskCompletionManager<string, obj>
    let (alpha, key) = manager.CreateNew(TimeSpan.FromMinutes(1.0), (fun _ -> Guid.NewGuid().ToString()), token = CancellationToken.None)
    let message = Guid.NewGuid().ToString()
    Assert.True(manager.SetException(key, new Exception(message)))
    Async.StartAsTask (async {
        let! error = Assert.ThrowsAsync<Exception>(fun () -> alpha :> Task) |> Async.AwaitTask
        Assert.Equal(message, error.Message)
    })

[<Fact>]
let ``Create New Then Set Cancel`` () =
    let manager = new TaskCompletionManager<string, obj>() :> ITaskCompletionManager<string, obj>
    let (alpha, key) = manager.CreateNew(TimeSpan.FromMinutes(1.0), (fun _ -> Guid.NewGuid().ToString()), token = CancellationToken.None)
    Assert.False(alpha.IsCompleted)
    Assert.True(manager.SetCancel key)
    Async.StartAsTask (async {
        do! Assert.ThrowsAsync<TaskCanceledException>(fun () -> alpha :> Task) |> Async.AwaitTask |> Async.Ignore
        Assert.True(alpha.IsCanceled)
    })

[<Fact>]
let ``Create New Then Cancel Manually`` () =
    let manager = new TaskCompletionManager<string, byte array>() :> ITaskCompletionManager<string, byte array>
    let cancellation = new CancellationTokenSource()
    let (alpha, _) = manager.CreateNew(TimeSpan.FromMinutes(1.0), (fun _ -> Guid.NewGuid().ToString()), token = cancellation.Token)
    Assert.False(alpha.IsCompleted)
    cancellation.Cancel()
    cancellation.Dispose()
    Async.StartAsTask (async {
        do! Async.Sleep 1000
        do! Assert.ThrowsAsync<TaskCanceledException>(fun () -> alpha :> Task) |> Async.AwaitTask |> Async.Ignore
        Assert.True(alpha.IsCompleted)
    })

[<Fact>]
let ``Create New Then Dispose`` () =
    let manager = new TaskCompletionManager<string, Guid>() :> ITaskCompletionManager<string, Guid>
    let factory = new Func<_, _>(fun _ -> Guid.NewGuid().ToString())
    let (alpha, _) = manager.CreateNew(TimeSpan.FromMinutes(1.0), factory, token = CancellationToken.None)
    let (bravo, _) = manager.CreateNew(TimeSpan.FromMinutes(1.0), factory, token = CancellationToken.None)
    Assert.False(alpha.IsCompleted)
    Assert.False(bravo.IsCompleted)
    (manager :?> IDisposable).Dispose()
    Async.StartAsTask (async {
        do! Async.Sleep 1000
        Assert.True(alpha.IsCanceled)
        Assert.True(bravo.IsCanceled)
    })

[<Theory>]
[<InlineData(-1.0)>]
[<InlineData(-2.0)>]
let ``Create New With Invalid Argument`` (milliseconds : double) =
    let manager = new TaskCompletionManager<string, obj>() :> ITaskCompletionManager<string, obj>
    let timeout = TimeSpan.FromMilliseconds(milliseconds)
    Assert.Throws<ArgumentOutOfRangeException>(fun () -> manager.CreateNew(timeout, (fun _ -> Guid.NewGuid().ToString()), token = CancellationToken.None) |> ignore) |> ignore
    ()

[<Fact>]
let ``Create New With Null Argument`` () =
    let manager = new TaskCompletionManager<string, obj>() :> ITaskCompletionManager<string, obj>
    Assert.Throws<ArgumentNullException>(fun () -> manager.CreateNew(TimeSpan.Zero, null, token = CancellationToken.None) |> ignore) |> ignore
    ()

[<Theory>]
[<InlineData("alpha")>]
[<InlineData("β")>]
let ``Create`` key =
    let manager = new TaskCompletionManager<string, obj>() :> ITaskCompletionManager<string, obj>
    let (alpha, created) = manager.Create(TimeSpan.FromSeconds(1.0), key, token = CancellationToken.None)
    Assert.NotNull(alpha)
    Assert.False(alpha.IsCompleted)
    Assert.True(created)
    ()

[<Theory>]
[<InlineData("charlie")>]
[<InlineData("Δ")>]
let ``Create Or Get`` key =
    let manager = new TaskCompletionManager<string, obj>() :> ITaskCompletionManager<string, obj>
    let (alpha, createdAlpha) = manager.Create(TimeSpan.FromSeconds(1.0), key, token = CancellationToken.None)
    let (bravo, createdBravo) = manager.Create(TimeSpan.FromSeconds(1.0), key, token = CancellationToken.None)
    Assert.True(createdAlpha)
    Assert.False(createdBravo)
    Assert.Equal(alpha, bravo)
    Assert.False(alpha.IsCompleted)
    ()

[<Theory>]
[<InlineData(-1.0)>]
[<InlineData(-2.0)>]
let ``Create With Invalid Argument`` (milliseconds : double) =
    let manager = new TaskCompletionManager<string, obj>() :> ITaskCompletionManager<string, obj>
    let timeout = TimeSpan.FromMilliseconds milliseconds
    Assert.Throws<ArgumentOutOfRangeException>(fun () -> manager.Create(timeout, String.Empty, token = CancellationToken.None) |> ignore) |> ignore
    ()

[<Fact>]
let ``Operate After Dispose`` () =
    let manager = new TaskCompletionManager<string, obj>() :> ITaskCompletionManager<string, obj>
    (manager :?> IDisposable).Dispose()
    let error = Assert.Throws<ObjectDisposedException>(fun () -> manager.CreateNew(TimeSpan.Zero, (fun _ -> Guid.NewGuid().ToString()), token = CancellationToken.None) |> ignore)
    Assert.Equal(manager.GetType().Name, error.ObjectName)
    // 'set xxx' methods should return false
    ()

[<Theory>]
[<InlineData(0.0)>]
[<InlineData(-1.0)>]
[<InlineData(1001.0)>]
let ``New Instance With Invalid Argument`` (milliseconds : double) =
    let interval = TimeSpan.FromMilliseconds milliseconds
    Assert.Throws<ArgumentOutOfRangeException>(fun () -> new TaskCompletionManager<string, string>(interval) |> ignore) |> ignore
    ()
