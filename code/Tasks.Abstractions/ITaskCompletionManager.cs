using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mikodev.Tasks.Abstractions
{
    public interface ITaskCompletionManager<TKey, TValue>
    {
        Task<TValue> Create(TimeSpan timeout, TKey key, out bool created, CancellationToken token);

        Task<TValue> CreateNew(TimeSpan timeout, Func<int, TKey> keyFactory, out TKey key, CancellationToken token);

        bool SetResult(TKey key, TValue result);

        bool SetException(TKey key, Exception exception);

        bool SetCancel(TKey key);
    }
}
