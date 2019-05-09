using UniRx;
using UniRx.Operators;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UniRxExtension
{
    public class SlowDownObservable<T> : OperatorObservableBase<T>
    {
        readonly IObservable<T> source;
        readonly float intervalSeconds;

        public SlowDownObservable(IObservable<T> source, float intervalSeconds)
            : base(source.IsRequiredSubscribeOnCurrentThread())
        {
            this.source = source;
            this.intervalSeconds = intervalSeconds;
        }

        protected override IDisposable SubscribeCore(IObserver<T> observer, IDisposable cancel)
        {
            return new SlowDown(this, observer, cancel).Run();
        }

        class SlowDown : OperatorObserverBase<T, T>
        {
            readonly SlowDownObservable<T> parent;
            readonly object gate = new object();
            bool calledCompleted = false;
            bool hasError = false;
            Exception error;
            private float intervalSeconds;
            private Queue<T> messageQueue;
            private SerialDisposable cancelable;

            public SlowDown(SlowDownObservable<T> parent, IObserver<T> observer, IDisposable cancel) : base(observer, cancel)
            {
                this.parent = parent;
                this.intervalSeconds = parent.intervalSeconds;
            }

            public IDisposable Run()
            {
                cancelable = new SerialDisposable();
                var subscription = parent.source.Subscribe(this);

                messageQueue = new Queue<T>(10);
                MainThreadDispatcher.SendStartCoroutine(TakeCoroutine());

                return StableCompositeDisposable.Create(cancelable, subscription);
            }

            IEnumerator TakeCoroutine()
            {
                var wait = new WaitForSeconds(intervalSeconds);

                while (!cancelable.IsDisposed)
                {
                    yield return wait;

                    if (messageQueue.Count > 0 && !hasError)
                    {
                        var data = messageQueue.Dequeue();
                        observer.OnNext(data);
                    }

                    if (hasError)
                    {
                        if (!cancelable.IsDisposed)
                        {
                            cancelable.Dispose();
                            try { observer.OnError(error); }
                            finally { Dispose(); }
                        }
                    }
                    else if (calledCompleted && messageQueue.Count <= 0)
                    {
                        if (!cancelable.IsDisposed)
                        {
                            cancelable.Dispose();
                            try { observer.OnCompleted(); }
                            finally { Dispose(); }
                        }
                    }
                }
            }

            public override void OnNext(T value)
            {
                if (cancelable.IsDisposed) return;

                lock (gate)
                {
                    messageQueue.Enqueue(value);
                }
            }

            public override void OnError(Exception error)
            {
                lock (gate)
                {
                    hasError = true;
                    this.error = error;
                }
            }

            public override void OnCompleted()
            {
                lock (gate)
                {
                    calledCompleted = true;
                }
            }
        }
    }
}