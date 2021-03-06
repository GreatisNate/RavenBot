﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RavenBot.Core
{
    public class Kernel : IKernel
    {
        private readonly object mutex = new object();
        private readonly object timeoutMutex = new object();
        private readonly List<TimeoutCallbackHandle> timeouts = new List<TimeoutCallbackHandle>();

        private Thread kernelThread;
        private bool started;

        public ITimeoutHandle SetTimeout(Action action, int timeoutMilliseconds)
        {
            lock (timeoutMutex)
            {
                var timeout = new TimeoutCallbackHandle(action, timeoutMilliseconds);
                this.timeouts.Add(timeout);
                return timeout;
            }
        }

        public void ClearTimeout(ITimeoutHandle handle)
        {
            lock (timeoutMutex)
            {
                this.timeouts.Remove((TimeoutCallbackHandle)handle);
            }
        }

        public void Start()
        {
            lock (mutex)
            {
                if (this.started)
                {
                    return;
                }

                this.started = true;
                this.kernelThread = new Thread(KernelProcess)
                {
                    IsBackground = true
                };
                this.kernelThread.Start();
            }
        }

        public void Stop()
        {
            lock (mutex)
            {
                this.started = false;
                this.kernelThread.Join();
            }
        }

        public bool Started => started;

        private void KernelProcess()
        {
            do
            {
                lock (mutex)
                {
                    if (!this.started)
                    {
                        return;
                    }
                }

                var timeout = 100;
                try
                {
                    lock (timeoutMutex)
                    {
                        var item = this.timeouts.OrderBy(x => x.Timeout).FirstOrDefault();
                        if (item != null)
                        {
                            if (DateTime.Now >= item.Timeout)
                            {
                                ClearTimeout(item);
                                item.Action?.Invoke();
                                continue;
                            }

                            timeout = Math.Min(timeout, (int)(item.Timeout - DateTime.Now).TotalMilliseconds);
                        }
                    }
                }
                catch
                {
                    // ignored, we can't have the kernel die due to an exception
                }
                System.Threading.Thread.Sleep(timeout);
            } while (true);
        }

        private class TimeoutCallbackHandle : ITimeoutHandle
        {
            public TimeoutCallbackHandle(Action action, int timeout)
            {
                this.Registered = DateTime.Now;
                this.Timeout = this.Registered.AddMilliseconds(timeout);
                this.Action = action;
            }

            public DateTime Registered { get; }
            public DateTime Timeout { get; }
            public Action Action { get; }
            public Guid Id { get; } = Guid.NewGuid();
        }
    }
}