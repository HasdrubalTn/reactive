﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information. 

using System.Threading.Tasks;

namespace System.Reactive.Linq
{
    public partial class AsyncObservable
    {
        public static IAsyncObservable<TSource> SingleOrDefaultAsync<TSource>(this IAsyncObservable<TSource> source) => SingleOrDefault(source);
        public static IAsyncObservable<TSource> SingleOrDefaultAsync<TSource>(this IAsyncObservable<TSource> source, Func<TSource, bool> predicate) => SingleOrDefault(source, predicate);
        public static IAsyncObservable<TSource> SingleOrDefaultAsync<TSource>(this IAsyncObservable<TSource> source, Func<TSource, ValueTask<bool>> predicate) => SingleOrDefault(source, predicate);

        public static IAsyncObservable<TSource> SingleOrDefault<TSource>(this IAsyncObservable<TSource> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return Create<TSource>(observer => source.SubscribeSafeAsync(AsyncObserver.SingleOrDefault(observer)));
        }

        public static IAsyncObservable<TSource> SingleOrDefault<TSource>(this IAsyncObservable<TSource> source, Func<TSource, bool> predicate)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return Create<TSource>(observer => source.SubscribeSafeAsync(AsyncObserver.SingleOrDefault(observer, predicate)));
        }

        public static IAsyncObservable<TSource> SingleOrDefault<TSource>(this IAsyncObservable<TSource> source, Func<TSource, ValueTask<bool>> predicate)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return Create<TSource>(observer => source.SubscribeSafeAsync(AsyncObserver.SingleOrDefault(observer, predicate)));
        }
    }

    public partial class AsyncObserver
    {
        public static IAsyncObserver<TSource> SingleOrDefault<TSource>(IAsyncObserver<TSource> observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            var hasValue = false;
            var value = default(TSource);

            return Create<TSource>(
                async x =>
                {
                    if (hasValue)
                    {
                        await observer.OnErrorAsync(new InvalidOperationException("The sequence contains more than one element.")).ConfigureAwait(false);
                        return;
                    }

                    hasValue = true;
                    value = x;
                },
                observer.OnErrorAsync,
                async () =>
                {
                    await observer.OnNextAsync(hasValue ? value : default).ConfigureAwait(false);
                    await observer.OnCompletedAsync().ConfigureAwait(false);
                }
            );
        }

        public static IAsyncObserver<TSource> SingleOrDefault<TSource>(IAsyncObserver<TSource> observer, Func<TSource, bool> predicate)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return Where(SingleOrDefault(observer), predicate);
        }

        public static IAsyncObserver<TSource> SingleOrDefault<TSource>(IAsyncObserver<TSource> observer, Func<TSource, ValueTask<bool>> predicate)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return Where(SingleOrDefault(observer), predicate);
        }
    }
}
