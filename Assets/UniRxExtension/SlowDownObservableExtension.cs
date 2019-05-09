using System;
using System.Collections.Generic;
using UniRx;
using UniRx.Operators;

namespace UniRxExtension
{
    public static partial class SlowDownObservableExtension
    {
        public static IObservable<TSource> SlowDown<TSource>(this IObservable<TSource> source, float intervalSecond = 0.10f)
        {
            return new SlowDownObservable<TSource>(source, intervalSecond);
        }
    }
}