using System;
using System.Reactive.Concurrency;

namespace Websocket.Client
{
    public static class ObservablesEx
    {
        public static IObservable<T> ThrottleFirst<T>(this IObservable<T> source, TimeSpan timespan,
            IScheduler timeSource)
        {
            return new ThrottleFirstObservable<T>(source, timespan, timeSource);
        }

        public static IObservable<T> ThrottleFirst<T>(this IObservable<T> source, TimeSpan timespan)
        {
            return new ThrottleFirstObservable<T>(source, timespan, DefaultScheduler.Instance);
        }
    }
}