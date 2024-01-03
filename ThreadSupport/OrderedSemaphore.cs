using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

namespace ThreadSupport
{
    public class OrderedSemaphore : IDisposable
    {
        public bool ClosedOrClosing => _closed || _closing;
        // Locks _currentCount, _maxCount, _orderedThreadWaitIds, _threadWaitIdsToTriggers and write access to _closing
        private object _semaphoreLock;

        private int _currentCount;
        private int _maxCount;
        private List<Guid> _orderedThreadWaitIds;
        private Dictionary<Guid, ManualResetEvent> _threadWaitIdsToTriggers;
        private bool _closing;

        private bool _closed;
        // Used to track how many threads received and acknowledges a close signal.  This is updated using an interlocked method
        private int _closeSignalReceivedCount;

        private bool _isDisposed;

        public OrderedSemaphore(int initialCount, int maxCount)
        {
            this._semaphoreLock = new object();
            this._currentCount = initialCount;
            this._maxCount = maxCount;
            this._closing = false;
            this._closed = false;

            this._orderedThreadWaitIds = new List<Guid>();
            this._threadWaitIdsToTriggers = new Dictionary<Guid, ManualResetEvent>();
        }

        /// <summary>
        /// Blocks the current thread until the semaphore becomes available to the caller.
        /// </summary>
        /// <returns>True if the semaphore was obtained.</returns>
        public bool WaitOne()
        {
            return WaitOne(Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Blocks the current thread until the semaphore becomes available to the caller, or the timeout is reached
        /// </summary>
        /// <param name="timeout">Timeout representing the number of milliseconds to wait for the semaphore</param>
        /// <returns>True if the semaphore was obtained.</returns>
        public bool WaitOne(TimeSpan timeout)
        {
            return WaitOne((int)Math.Floor(timeout.TotalMilliseconds));
        }

        /// <summary>
        /// Blocks the current thread until the semaphore becomes available to the caller, or the timeout is reached
        /// </summary>
        /// <param name="millisecondsTimeout">Timeout representing the number of milliseconds to wait for the semaphore, or -1 for indefinite</param>
        /// <returns>True if the semaphore was obtained.</returns>
        public bool WaitOne(int millisecondsTimeout)
        {
            if (_closed || _closing)
            {
                throw new Exception("Could not await on closed or closing ordered semaphore");
            }
            bool triggered = false;
            bool signalReceivedDueToSemaphoreClose = false;

            using (ManualResetEvent manualResetEvent = new ManualResetEvent(false))
            {
                Guid threadWaitId = Guid.NewGuid();

                lock (this._semaphoreLock)
                {
                    int availability = this._maxCount - this._currentCount;
                    if (availability > 0 && this._orderedThreadWaitIds.Count < availability)
                    {
                        this._currentCount++;
                        Debug.WriteLine($"Obtained semaphore. Previous obtain count was {this._currentCount - 1}, now it is {this._currentCount}");
                        return true;
                    }
                    this._threadWaitIdsToTriggers[threadWaitId] = manualResetEvent;
                    this._orderedThreadWaitIds.Add(threadWaitId);
                }
                bool signalReceivedDueToClosureOrTimeout;
                try
                {
                    triggered = manualResetEvent.WaitOne(millisecondsTimeout);
                    if (this._closing)
                    {
                        Debug.WriteLine($"Semaphore signalled due to closing");

                        // Wait was triggered because semaphore is closing
                        triggered = false;
                        signalReceivedDueToClosureOrTimeout = true;
                        signalReceivedDueToSemaphoreClose = true;
                    }
                    else
                    {
                        signalReceivedDueToClosureOrTimeout = !triggered;
                    }
                }
                catch (ThreadAbortException)
                {
                    signalReceivedDueToClosureOrTimeout = true;
                }
                if (signalReceivedDueToClosureOrTimeout)
                {
                    // Tidy up semaphore state
                    RemoveThreadWaitIdFromSemaphore(threadWaitId);
                }
            }

            // When closing the semaphore, an atomic count is maintained of the number of threads that have received the signal
            if (signalReceivedDueToSemaphoreClose)
            {
                Interlocked.Increment(ref this._closeSignalReceivedCount);
            }

            return triggered;
        }

        /// <summary>
        /// Exits the semaphore the specified number of times and returns the previous count
        /// </summary>
        /// <param name="releaseCount">The number of times to exit the semaphore</param>
        /// <returns>The semaphore count before release was called</returns>
        public int Release(int releaseCount)
        {
            int prereleaseCount = -1;
            lock (this._semaphoreLock)
            {
                prereleaseCount = this._currentCount;
                this._currentCount -= releaseCount;
                Debug.WriteLine($"Released semaphore from {prereleaseCount} to {prereleaseCount - 1}");
            }

            if (!this._closing && !this._closed)
            {
                // A release received during close-down does not signal other waiting threads
                SignalWaitingThreads();
            }
            else
            {
                Debug.WriteLine($"Released semaphore whilst closed or closing");
            }

            return prereleaseCount;
        }

        /// <summary>
        /// Exits the semaphore
        /// </summary>
        /// <returns>The semaphore count before release was called</returns>
        public int Release()
        {
            return Release(1);
        }

        /// <summary>
        /// Close semaphore, signalling all threads. Waits to ensure all signal threaded have received the signal, up to the specified period, unless the doNotWait flag is true.
        /// </summary>
        /// <param name="millisecondsToWaitForClosures">Milliseconds to wait for all threads to close. Minimum is 100ms.</param>
        /// <param name="doNotWait">When true, does not wait to ensure all waiting threads received the signal</param>
        /// <returns>true if all waiting threads were closed</returns>
        private bool Close(int millisecondsToWaitForClosures, bool doNotWait)
        {
            int signaledCount = SendCloseSignalToAllWaitingThreads();

            if (doNotWait)
            {
                this._closed = true;
                return (this._closeSignalReceivedCount >= signaledCount);
            }
            int loopDelayMs = 100;
            int waitLoopMax = millisecondsToWaitForClosures / loopDelayMs;

            for (int waitForAllClosedCount = 0; waitForAllClosedCount < waitLoopMax; waitForAllClosedCount++)
            {
                _ = new ManualResetEvent(false).WaitOne(loopDelayMs);
                if (this._closeSignalReceivedCount >= signaledCount)
                {
                    this._closed = true;
                    return true;
                }
            }
            this._closed = true;
            return false;
        }

        /// <summary>
        /// Close semaphore, signalling all threads. Waits to ensure all signal threaded have received the signal, up to the specified period.
        /// </summary>
        /// <param name="millisecondsToWaitForClosures">Milliseconds to wait for all threads to close. Minimum is 100ms. A value of 0 indicates not to wait.</param>
        /// <returns>true if all waiting threads were closed</returns>
        public bool Close(int millisecondsToWaitForClosures)
        {
            int signaledCount = SendCloseSignalToAllWaitingThreads();
            if (millisecondsToWaitForClosures < 100)
            {
                millisecondsToWaitForClosures = 100;
            }
            return Close(millisecondsToWaitForClosures: millisecondsToWaitForClosures, doNotWait: false);
        }

        /// <summary>
        /// Close all waiting threads, return without awaiting for them to signal they received the close request
        /// </summary>
        public void Close()
        {
            Close(millisecondsToWaitForClosures: 0, doNotWait: true);
        }

        private void SignalWaitingThreads()
        {
            lock (this._semaphoreLock)
            {
                int threadWaitCount = this._orderedThreadWaitIds.Count;
                int availability = this._maxCount - this._currentCount;
                while (threadWaitCount > 0 && availability > 0)
                {
                    Guid waitIdToTrigger = this._orderedThreadWaitIds[0];
                    this._orderedThreadWaitIds.RemoveAt(0);
                    threadWaitCount--;

                    ManualResetEvent? manualResetEvent = null;
                    if (this._threadWaitIdsToTriggers.ContainsKey(waitIdToTrigger))
                    {
                        manualResetEvent = this._threadWaitIdsToTriggers[waitIdToTrigger];
                        this._threadWaitIdsToTriggers.Remove(waitIdToTrigger);

                        try
                        {
                            if (manualResetEvent.Set())
                            {
                                availability--;
                                this._currentCount++;
                                Debug.WriteLine($"Signalling semaphore availability, from {this._currentCount - 1} to {this._currentCount}");
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // Do nothing, this wait was removed by the calling thread
                        }
                    }
                }
            }
        }

        /// <summary>
        /// This is called when the semaphore is closing
        /// </summary>
        /// <returns>Count of threads signalled to close</returns>
        private int SendCloseSignalToAllWaitingThreads()
        {
            // Close signal is sent via the manual reset event that all waiting threads are waiting on. The difference is
            //  that with the _closing flag set, that the waiting threads will exit rather than assume control of the semaphore.
            int count = 0;
            lock (this._semaphoreLock)
            {
                this._closing = true;
                this._closeSignalReceivedCount = 0;

                foreach (ManualResetEvent waitHandle in _threadWaitIdsToTriggers.Values)
                {
                    try
                    {
                        waitHandle.Set();
                        count++;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Do nothing, this wait was removed by the calling thread
                    }
                }
            }
            return count;
        }

        private void RemoveThreadWaitIdFromSemaphore(Guid waitIdToRemove)
        {
            lock (this._semaphoreLock)
            {
                if (this._threadWaitIdsToTriggers.ContainsKey(waitIdToRemove))
                {
                    this._threadWaitIdsToTriggers.Remove(waitIdToRemove);
                }
                this._orderedThreadWaitIds.Remove(waitIdToRemove);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this._isDisposed)
            {
                this._isDisposed = true;

                if (!this._closing && !this._closed)
                {
                    Close(500);
                }
            }
        }

        ~OrderedSemaphore()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
        }

        /// <summary>
        /// Return true if this object has already been disposed
        /// </summary>
        public bool IsDisposed
        {
            get
            {
                return this._isDisposed;
            }
        }
    }
}
