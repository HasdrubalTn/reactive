﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information. 

#nullable disable

#if HAS_WPF
using System.Reactive.Disposables;
using System.Threading;

namespace System.Reactive.Concurrency
{
    /// <summary>
    /// Represents an object that schedules units of work on a <see cref="System.Windows.Threading.Dispatcher"/>.
    /// </summary>
    /// <remarks>
    /// This scheduler type is typically used indirectly through the <see cref="Linq.DispatcherObservable.ObserveOnDispatcher{TSource}(IObservable{TSource})"/> and <see cref="Linq.DispatcherObservable.SubscribeOnDispatcher{TSource}(IObservable{TSource})"/> methods that use the Dispatcher on the calling thread.
    /// </remarks>
    public class DispatcherScheduler : LocalScheduler, ISchedulerPeriodic
    {
        /// <summary>
        /// Gets the scheduler that schedules work on the current <see cref="System.Windows.Threading.Dispatcher"/>.
        /// </summary>
        [Obsolete(Constants_WindowsThreading.OBSOLETE_INSTANCE_PROPERTY)]
        public static DispatcherScheduler Instance => new DispatcherScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher);

        /// <summary>
        /// Gets the scheduler that schedules work on the <see cref="System.Windows.Threading.Dispatcher"/> for the current thread.
        /// </summary>
        public static DispatcherScheduler Current
        {
            get
            {
                var dispatcher = System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread);
                if (dispatcher == null)
                {
                    throw new InvalidOperationException(Strings_WindowsThreading.NO_DISPATCHER_CURRENT_THREAD);
                }

                return new DispatcherScheduler(dispatcher);
            }
        }

        /// <summary>
        /// Constructs a <see cref="DispatcherScheduler"/> that schedules units of work on the given <see cref="System.Windows.Threading.Dispatcher"/>.
        /// </summary>
        /// <param name="dispatcher"><see cref="DispatcherScheduler"/> to schedule work on.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <c>null</c>.</exception>
        public DispatcherScheduler(System.Windows.Threading.Dispatcher dispatcher)
        {
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Priority = System.Windows.Threading.DispatcherPriority.Normal;

        }

        /// <summary>
        /// Constructs a <see cref="DispatcherScheduler"/> that schedules units of work on the given <see cref="System.Windows.Threading.Dispatcher"/> at the given priority.
        /// </summary>
        /// <param name="dispatcher"><see cref="DispatcherScheduler"/> to schedule work on.</param>
        /// <param name="priority">Priority at which units of work are scheduled.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <c>null</c>.</exception>
        public DispatcherScheduler(System.Windows.Threading.Dispatcher dispatcher, System.Windows.Threading.DispatcherPriority priority)
        {
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Priority = priority;
        }

        /// <summary>
        /// Gets the <see cref="System.Windows.Threading.Dispatcher"/> associated with the <see cref="DispatcherScheduler"/>.
        /// </summary>
        public System.Windows.Threading.Dispatcher Dispatcher { get; }

        /// <summary>
        /// Gets the priority at which work items will be dispatched.
        /// </summary>
        public System.Windows.Threading.DispatcherPriority Priority { get; }

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

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (!d.IsDisposed)
                    {
                        d.Disposable = action(this, state);
                    }
                }),
                Priority
            );

            return d;
        }

        /// <summary>
        /// Schedules an action to be executed after <paramref name="dueTime"/> on the dispatcher, using a <see cref="System.Windows.Threading.DispatcherTimer"/> object.
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

            var timer = new System.Windows.Threading.DispatcherTimer(Priority, Dispatcher);

            timer.Tick += (s, e) =>
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
        /// Schedules a periodic piece of work on the dispatcher, using a <see cref="System.Windows.Threading.DispatcherTimer"/> object.
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
            if (period < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(period));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var timer = new System.Windows.Threading.DispatcherTimer(Priority, Dispatcher);

            var state1 = state;

            timer.Tick += (s, e) =>
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
