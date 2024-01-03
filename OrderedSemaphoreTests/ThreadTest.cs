using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThreadSupport;

namespace ThreadSupportTests
{
    internal class ThreadTest
    {
        static object s_resultLock = new object();
        static StringBuilder s_results = new StringBuilder();
        static OrderedSemaphore s_semaphoreUnderTest;
        internal int Index { get; private set; }

        internal bool Started { get; private set; } = false;
        internal bool Finished { get; private set; } = false;
        internal int? FiniteWait { get; set; } = null;
        int _startDelayInMs;
        int _waitInMs;

        OrderedSemaphore _semaphore;

        Thread? _thread;
        internal ThreadTest(int index, OrderedSemaphore semaphore, int startDelayInMs, int waitInMs)
        {
            Index = index;
            _thread = null;
            _startDelayInMs = startDelayInMs;
            _waitInMs = waitInMs;
            _semaphore = semaphore;
        }

        internal void StartTest()
        {
            _thread = new Thread(RunThread);
            _thread.Start();
        }

        internal void RunThread()
        {
            if (_startDelayInMs > 0)
            {
                _ = new ManualResetEvent(false).WaitOne(_startDelayInMs);
            }
            Debug.WriteLine($"Thread {Index} starting");
            Started = true;

            try
            {
                bool semaphoreObtained;
                if (FiniteWait == null)
                {
                    semaphoreObtained = _semaphore.WaitOne();
                }
                else
                {
                    semaphoreObtained = _semaphore.WaitOne(FiniteWait.Value);
                }

                if (semaphoreObtained)
                {
                    AddToResults($"T{Index}:O ");

                    Debug.WriteLine($" T{Index} Obtained semaphore, sleeping for {_waitInMs} ms");
                    _ = new ManualResetEvent(false).WaitOne(_waitInMs);
                    AddToResults($"T{Index}:R ");
                    Debug.WriteLine($" T{Index} Releasing semaphore");
                    _semaphore.Release();
                }
                else if (_semaphore.ClosedOrClosing)
                {
                    AddToResults($"T{Index}:X ");
                } 
                else
                {
                    // Timeout
                    AddToResults($"T{Index}:T ");
                }
            }
            catch(Exception ex)
            {
                StringAssert.Contains(ex.Message, "Could not await on closed or closing ordered semaphore");
                AddToResults($"T{Index}:E ");
            }

            Finished = true;
        }

        internal static List<ThreadTest> SetupTests(int semaphoreConcurrency, int maxThreads, int finiteWait = 0,
            List<int>? delayThreads = null, int delayThreadsBy = 0,
            int threadDelay = 0,
            int onlyStartThreadsBelowIndex = 0)
        {
            List<ThreadTest> threads = new List<ThreadTest>();
            s_semaphoreUnderTest = new OrderedSemaphore(0, semaphoreConcurrency);
            for (int i = 0; i < maxThreads; i++)
            {
                int delay = threadDelay;
                if (threadDelay == 0)
                {
                    delay = i * 50 + 50;
                }
                if (delayThreads != null && delayThreads.Count > 0 && delayThreadsBy > 0)
                {
                    if (delayThreads.Contains(i))
                    {
                        delay = delayThreadsBy;
                    }
                }
                ThreadTest tt = new ThreadTest(i, s_semaphoreUnderTest, i*10, delay);
                if (finiteWait > 0)
                {
                    tt.FiniteWait = finiteWait;
                }

                threads.Add(tt);
                if (onlyStartThreadsBelowIndex == 0 || i < onlyStartThreadsBelowIndex)
                {
                    tt.StartTest();
                    while (tt.Started == false) ;
                }
            }

            return threads;
        }

        internal static void CloseSemaphoreUnderTest()
        {
            ThreadTest.AddToResults("CS ");
            s_semaphoreUnderTest.Close();
        }

        internal static void AddToResults(string add)
        {
            lock(s_resultLock)
            {
                s_results.Append(add);
            }
        }

        internal static void ClearResults()
        {
            lock (s_resultLock)
            {
                s_results = new StringBuilder();
            }
        }

        internal static string Results()
        {
            lock(s_resultLock)
            {
                return s_results.ToString();
            }
        }
    }
}
