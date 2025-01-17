﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information. 

#nullable disable

#if WINDOWS
using System.Reactive.Disposables;
using System.Runtime.ExceptionServices;
using System.Threading;
using Windows.System;
using Windows.UI.Core;
#if HAS_OS_XAML
using Windows.UI.Xaml;
#endif
namespace System.Reactive.Concurrency
{
    /// <summary>
    /// Represents an object that schedules units of work on a <see cref="CoreDispatcher"/>.
    /// </summary>
    /// <remarks>
    /// This scheduler type is typically used indirectly through the <see cref="Linq.DispatcherObservable.ObserveOnDispatcher{TSource}(IObservable{TSource})"/> and <see cref="Linq.DispatcherObservable.SubscribeOnDispatcher{TSource}(IObservable{TSource})"/> methods that use the current Dispatcher.
    /// </remarks>
    [CLSCompliant(false)]
    public sealed class CoreDispatcherScheduler : LocalScheduler, ISchedulerPeriodic
    {
        /// <summary>
        /// Constructs a <see cref="CoreDispatcherScheduler"/> that schedules units of work on the given <see cref="CoreDispatcher"/>.
        /// </summary>
        /// <param name="dispatcher">Dispatcher to schedule work on.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <c>null</c>.</exception>
        public CoreDispatcherScheduler(CoreDispatcher dispatcher)
        {
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Priority = CoreDispatcherPriority.Normal;           
        }

        /// <summary>
        /// Constructs a <see cref="CoreDispatcherScheduler"/> that schedules units of work on the given <see cref="CoreDispatcher"/> with the given priority.
        /// </summary>
        /// <param name="dispatcher">Dispatcher to schedule work on.</param>
        /// <param name="priority">Priority for scheduled units of work.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <c>null</c>.</exception>
        public CoreDispatcherScheduler(CoreDispatcher dispatcher, CoreDispatcherPriority priority)
        {
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Priority = priority;
        }

        /// <summary>
        /// Gets the scheduler that schedules work on the <see cref="CoreDispatcher"/> associated with the current Window.
        /// </summary>
        public static CoreDispatcherScheduler Current
        {
            get
            {
                var window = CoreWindow.GetForCurrentThread();
                if (window == null)
                {
                    throw new InvalidOperationException(Strings_WindowsThreading.NO_WINDOW_CURRENT);
                }

                return new CoreDispatcherScheduler(window.Dispatcher);
            }
        }

        /// <summary>
        /// Gets the <see cref="CoreDispatcher"/> associated with the <see cref="CoreDispatcherScheduler"/>.
        /// </summary>
        public CoreDispatcher Dispatcher { get; }

        private DispatcherQueue? _dispatcherQueue;

        /// <summary>
        /// Gets the priority at which work is scheduled.
        /// </summary>
        public CoreDispatcherPriority Priority { get; }

        /// <summary>
        /// Schedules an action to be executed on the dispatcher.
        /// </summary>
        /// <typeparam name="TState">The type of the state passed to the scheduled action.</typeparam>
        /// <param name="state">State passed to the action to be executed.</param>
        /// <param name="action">Action to be executed.</param>
        /// <returns>The disposable object used to cancel the scheduled action (best effort).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is <c>null</c>.</exception>
        public override IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var d = new SingleAssignmentDisposable();

            var res = Dispatcher.RunAsync(Priority, () =>
            {
                if (!d.IsDisposed)
                {
                    try
                    {
                        d.Disposable = action(this, state);
                    }
                    catch (Exception ex)
                    {
                        //
                        // Work-around for the behavior of throwing from RunAsync not propagating
                        // the exception to the Application.UnhandledException event (as of W8RP)
                        // as our users have come to expect from previous XAML stacks using Rx.
                        //
                        // If we wouldn't do this, there'd be an observable behavioral difference
                        // between scheduling with TimeSpan.Zero or using this overload.
                        //
                        // For scheduler implementation guidance rules, see TaskPoolScheduler.cs
                        // in System.Reactive.PlatformServices\Reactive\Concurrency.
                        //
                        
                        var timer = CreateDispatcherQueue().CreateTimer();
                        timer.Interval = TimeSpan.Zero;

                        timer.Tick += (o, e) =>
                        {
                            timer.Stop();
                            ExceptionDispatchInfo.Capture(ex).Throw();
                        };

                        timer.Start();
                    }
                }
            });

            return StableCompositeDisposable.Create(
                d,
                res.AsDisposable()
            );
        }

        private DispatcherQueue CreateDispatcherQueue()
        {
            if(_dispatcherQueue != null)
            {
                return _dispatcherQueue;
            }

            if(Dispatcher.HasThreadAccess)
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                return _dispatcherQueue;
            }

            // We're on a different thread, get it from the right one
            Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            }).GetAwaiter().GetResult(); // This is a synchronous call and we need the result to proceed

            return _dispatcherQueue!;
        }

        /// <summary>
        /// Schedules an action to be executed after <paramref name="dueTime"/> on the dispatcher, using a <see cref="DispatcherQueueTimer"/> object.
        /// </summary>
        /// <typeparam name="TState">The type of the state passed to the scheduled action.</typeparam>
        /// <param name="state">State passed to the action to be executed.</param>
        /// <param name="action">Action to be executed.</param>
        /// <param name="dueTime">Relative time after which to execute the action.</param>
        /// <returns>The disposable object used to cancel the scheduled action (best effort).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is <c>null</c>.</exception>
        public override IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var dt = Scheduler.Normalize(dueTime);
            if (dt.Ticks == 0)
            {
                return Schedule(state, action);
            }

            return ScheduleSlow(state, dt, action);
        }

        private IDisposable ScheduleSlow<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
        {
            var d = new MultipleAssignmentDisposable();

            var timer = CreateDispatcherQueue().CreateTimer();

            timer.Tick += (o, e) =>
            {
                var t = Interlocked.Exchange(ref timer, null);
                if (t != null)
                {
                    try
                    {
                        d.Disposable = action(this, state);
                    }
                    finally
                    {
                        t.Stop();
                        action = null;
                    }
                }
            };

            timer.Interval = dueTime;
            timer.Start();

            d.Disposable = Disposable.Create(() =>
            {
                var t = Interlocked.Exchange(ref timer, null);
                if (t != null)
                {
                    t.Stop();
                    action = static (s, t) => Disposable.Empty;
                }
            });

            return d;
        }

        /// <summary>
        /// Schedules a periodic piece of work on the dispatcher, using a <see cref="DispatcherQueueTimer"/> object.
        /// </summary>
        /// <typeparam name="TState">The type of the state passed to the scheduled action.</typeparam>
        /// <param name="state">Initial state passed to the action upon the first iteration.</param>
        /// <param name="period">Period for running the work periodically.</param>
        /// <param name="action">Action to be executed, potentially updating the state.</param>
        /// <returns>The disposable object used to cancel the scheduled recurring action (best effort).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="period"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
        public IDisposable SchedulePeriodic<TState>(TState state, TimeSpan period, Func<TState, TState> action)
        {
            //
            // According to MSDN documentation, the default is TimeSpan.Zero, so that's definitely valid.
            // Empirical observation - negative values seem to be normalized to TimeSpan.Zero, but let's not go there.
            //
            if (period < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(period));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var timer = CreateDispatcherQueue().CreateTimer();

            var state1 = state;

            timer.Tick += (o, e) =>
            {
                state1 = action(state1);
            };

            timer.Interval = period;
            timer.Start();

            return Disposable.Create(() =>
            {
                var t = Interlocked.Exchange(ref timer, null);
                if (t != null)
                {
                    t.Stop();
                    action = static _ => _;
                }
            });
        }
    }
}
#endif
