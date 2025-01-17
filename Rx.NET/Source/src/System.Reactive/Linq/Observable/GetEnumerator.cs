﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information. 

#nullable disable

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Threading;

namespace System.Reactive.Linq.ObservableImpl
{
    internal sealed class GetEnumerator<TSource> : IEnumerator<TSource>, IObserver<TSource>
    {
        private readonly ConcurrentQueue<TSource> _queue;
        private TSource _current;
        private Exception _error;
        private bool _done;
        private bool _disposed;
        private IDisposable _subscription;

        private readonly SemaphoreSlim _gate;

        public GetEnumerator()
        {
            _queue = new ConcurrentQueue<TSource>();
            _gate = new SemaphoreSlim(0);
        }

        public IEnumerator<TSource> Run(IObservable<TSource> source)
        {
            //
            // [OK] Use of unsafe Subscribe: non-pretentious exact mirror with the dual GetEnumerator method.
            //
            Disposable.TrySetSingle(ref _subscription, source.Subscribe/*Unsafe*/(this));
            return this;
        }

        public void OnNext(TSource value)
        {
            _queue.Enqueue(value);
            _gate.Release();
        }

        public void OnError(Exception error)
        {
            _error = error;
            Disposable.Dispose(ref _subscription);
            _gate.Release();
        }

        public void OnCompleted()
        {
            _done = true;
            Disposable.Dispose(ref _subscription);
            _gate.Release();
        }

        public bool MoveNext()
        {
            _gate.Wait();

            if (_disposed)
            {
                throw new ObjectDisposedException("");
            }

            if (_queue.TryDequeue(out _current))
            {
                return true;
            }

            _error.ThrowIfNotNull();

            Debug.Assert(_done);

            _gate.Release(); // In the (rare) case the user calls MoveNext again we shouldn't block!
            return false;
        }

        public TSource Current => _current;

        object Collections.IEnumerator.Current => _current;

        public void Dispose()
        {
            Disposable.Dispose(ref _subscription);

            _disposed = true;
            _gate.Release();
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }
    }
}
