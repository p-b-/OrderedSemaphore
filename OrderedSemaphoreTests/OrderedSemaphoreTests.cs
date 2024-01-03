using System.Diagnostics;
using ThreadSupport;

namespace ThreadSupportTests
{
    /// <summary>
    /// The WaitOne being tested here is actually executed in the ThreadTest class, which encapsulated the thread, the delay and the semaphore accessing.
    /// The results from this ThreadTest invocation are stored in a static class member, which is cleared by the initial called to ClearResults() in InitialiseTests.
    ///  This static way of storing test results will prevent the tests running simultaneously
    /// Abstracting out the ThreadTest setup would cause more issues than it fixes, as each test case adds diversity to what would be needed.
    /// </summary>
    /// <remarks>
    /// Note that as these tests are time-based, debugging during the tests will affect the results, and the tests may fail.  Test structure may need to be
    ///  altered if tests are prove to be non-deterministic.  It is important that the order of obtaining the semaphore matches the time stamp order of when 
    ///  it was requested, and the the closures, timeouts and exceptions match the state of the semaphore.  
    /// One simply improvement may be removing the 'release' output from the test results.
    /// </remarks>
    [TestClass]
    public class OrderedSemaphoreTests
    {
        [TestInitialize]
        public void InitialiseTest()
        {
            ThreadTest.ClearResults();
        }

        [TestMethod]
        public void WaitOne_MultipleAccessInfiniteWait_ShouldRunOkay()
        {
            List<ThreadTest> threads = ThreadTest.SetupTests(semaphoreConcurrency: 3, maxThreads: 10);

            AwaitAllThreads(threads);
            string results = ThreadTest.Results();
            Debug.WriteLine(results);

            // T0:O T1:O T2:O   Threads  0->2 obtain semaphore
            // T0:R T3:O        Thread 0 releases semaphore, Thread 3 obtains semaphore
            // T1:R T4:O        Thread 1 releases semaphore, Thread 4 obtains semaphore
            // T2:R T5:O        Thread 2 releases semaphore, Thread 5 obtains semaphore
            // T3:R T6:O        Thread 3 releases semaphore, Thread 6 obtains semaphore
            // T4:R T7:O        Thread 4 releases semaphore, Thread 7 obtains semaphore
            // T5:R T8:O        Thread 5 releases semaphore, Thread 8 obtains semaphore
            // T6:R T9:O        Thread 6 releases semaphore, Thread 9 obtains semaphore
            // T7:R             Thread 7 releases semaphore,
            // T8:R             Thread 8 releases semaphore
            // T9:R             Thread 9 releases semaphore
            string expectedResults = "T0:O T1:O T2:O T0:R T3:O T1:R T4:O T2:R T5:O T3:R T6:O T4:R T7:O T5:R T8:O T6:R T9:O T7:R T8:R T9:R";
            StringAssert.StartsWith(results, expectedResults);
        }

        [TestMethod]
        public void WaitOne_FiniteWait_ShouldRunOkay()
        {
            // Finite wait ensures that thread 3 will timeout before the semaphore is available for it
            List<ThreadTest> threads = ThreadTest.SetupTests(semaphoreConcurrency: 3, maxThreads: 4, finiteWait: 100, threadDelay:200);

            AwaitAllThreads(threads);
            string results = ThreadTest.Results();
            Debug.WriteLine(results);

            // T0:O T1:O T2:O   Threads 0->2 obtain semaphore
            // T3:T             Thread 3 times out attempting to obtain semaphore
            // T0:R             Thread 0 releases semaphore
            // T1:R             Thread 1 releases semaphore,
            // T2:R             Thread 2 releases semaphore
            string expectedResults = "T0:O T1:O T2:O T3:T T0:R T1:R T2:R";
            // Release order after timeout sometimes out-of-order.  Not testing for release order, but for timeout:
            expectedResults = "T0:O T1:O T2:O T3:T";
            StringAssert.StartsWith(results, expectedResults);
        }

        [TestMethod]
        public void WaitOne_SingularAccessInfiniteWait_ShouldRunOkay()
        {
            List<ThreadTest> threads = ThreadTest.SetupTests(semaphoreConcurrency: 1, maxThreads: 10);

            AwaitAllThreads(threads);
            string results = ThreadTest.Results();
            Debug.WriteLine(results);

            // T0:O T0:R        Thread 0 obtains semaphore, then releases it
            // T1:O T1:R        Thread 1 obtains semaphore, then releases it
            // T2:O T2:R        Thread 2 obtains semaphore, then releases it
            // T3:O T3:R        Thread 3 obtains semaphore, then releases it
            // T4:O T4:R        Thread 4 obtains semaphore, then releases it
            // T5:O T5:R        Thread 5 obtains semaphore, then releases it
            // T6:O T6:R        Thread 6 obtains semaphore, then releases it
            // T7:O T7:R        Thread 7 obtains semaphore, then releases it
            // T8:O T8:R        Thread 8 obtains semaphore, then releases it
            // T9:O T9:R        Thread 9 obtains semaphore, then releases it
            string expectedResults = "T0:O T0:R T1:O T1:R T2:O T2:R T3:O T3:R T4:O T4:R T5:O T5:R T6:O T6:R T7:O T7:R T8:O T8:R T9:O T9:R";
            StringAssert.StartsWith(results, expectedResults);
        }

        [TestMethod]
        public void WaitOne_LongerRunningThreads_ShouldRunOkay()
        {
            List<int> threadsToDelay = new List<int> { 3, 7 };
            List<ThreadTest> threads = ThreadTest.SetupTests(semaphoreConcurrency: 3, maxThreads: 10, delayThreads: threadsToDelay, delayThreadsBy:3000);

            AwaitAllThreads(threads);
            string results = ThreadTest.Results();
            Debug.WriteLine(results);

            // T0:O T1:O T2:O   Threads 0->2 obtain semaphore
            // T0:R T3:O        Thread 0 releases semaphore, thread 3 obtains semaphore
            // T1:R T4:O        Thread 1 releases semaphore, thread 4 obtains semaphore
            // T2:R T5:O        Thread 2 releases semaphore, thread 5 obtains semaphore
            // T4:R T6:O        Thread 4 releases semaphore, thread 6 obtains semaphore
            // T5:R T7:O        Thread 5 releases semaphore, thread 7 obtains semaphore
            // T6:R T8:O        Thread 6 releases semaphore, thread 8 obtains semaphore
            // T8:R T9:O        Thread 8 releases semaphore, thread 9 obtains semaphore
            // T9:R             Thread 9 releases semaphore
            // T3:R             Thread 3 releases semaphore  (thread had longer delay)
            // T7:R             Thread 7 releases semaphore (thread had longer delay)
            string expectedResults = "T0:O T1:O T2:O T0:R T3:O T1:R T4:O T2:R T5:O T4:R T6:O T5:R T7:O T6:R T8:O T8:R T9:O T9:R T3:R T7:R";
            StringAssert.StartsWith(results, expectedResults);
        }

        [TestMethod]
        public void WaitOne_CloseSemaphoreAtEndOfTest_ShouldRunOkay()
        {
            int simultaneousWaitCount = 3;
            // Rather than delay each thread by (threadIndex*50+50) milliseconds, each thread is delayed here by 1000ms.
            //  This is so that the threads haven't finished their test by the time the call to SetupTests has finished, and that the results are deterministic
            List<ThreadTest> threads = ThreadTest.SetupTests(semaphoreConcurrency: simultaneousWaitCount, maxThreads: simultaneousWaitCount+1, threadDelay:1000);

            // Ensure all initial threads have started and obtained semaphore before closing it, preventing the last thread from obtaining it
            SleepForMs(simultaneousWaitCount*50+50);
            ThreadTest.CloseSemaphoreUnderTest();

            AwaitAllThreads(threads);
            string results = ThreadTest.Results();

            Debug.WriteLine(results);

            // T0:O T1:O T2:O   Threads 0->2 obtain semaphore.  
            // CS               Semaphore closed
            // T3:X             Thread fails to get semaphore as it has been closed
            // T0:R T1:R T2:R   Threads 0->2 release semaphore
            string expectedResults = "T0:O T1:O T2:O CS T3:X T0:R T1:R T2:R";
            // Release order at end may change, but testing here for the thread failing to get semaphore
            expectedResults = "T0:O T1:O T2:O CS T3:X";

            StringAssert.StartsWith(results, expectedResults);
        }

        [TestMethod]
        public void WaitOne_CloseSemaphoreDuringTest_ShouldThrowException()
        {
            int simultaneousWaitCount = 3;
            List<ThreadTest> threads = ThreadTest.SetupTests(semaphoreConcurrency: simultaneousWaitCount, 
                maxThreads: simultaneousWaitCount + 2, threadDelay: 1000, onlyStartThreadsBelowIndex: simultaneousWaitCount + 1);

            // Ensure all initial threads have started and obtained semaphore before closing it, preventing the last thread from obtaining it
            SleepForMs(simultaneousWaitCount * 50 + 50);

            ThreadTest.CloseSemaphoreUnderTest();
            threads[threads.Count-1].StartTest();

            AwaitAllThreads(threads);
            string results = ThreadTest.Results();

            Debug.WriteLine(results);

            // T0:O T1:O T2:O   Threads 0->2 obtain semaphore.  
            // CS               Semaphore closed
            // T3:X             Thread fails to get semaphore as it has been closed
            // T4:E             Thread 4 throws an exception when trying to wait on a semaphore that has been closed
            string expectedResults = "T0:O T1:O T2:O CS T3:X T4:E";
            StringAssert.StartsWith(results, expectedResults);
        }

        internal void AwaitThreads(List<ThreadTest> threads, int awaitUptoIndeX)
        {
            while (true)
            {
                Thread.Sleep(100);
                bool allFinished = true;
                foreach (ThreadTest tt in threads)
                { 
                    if (tt.Index < awaitUptoIndeX && tt.Finished == false)
                    {
                        allFinished = false;
                        break;
                    }
                }
                if (allFinished)
                {
                    return;
                }
            }
        }

        internal void AwaitAllThreads(List<ThreadTest> threads)
        {
            while (true)
            {
                Thread.Sleep(100);
                bool allFinished = true;
                foreach (ThreadTest tt in threads)
                {
                    if (tt.Finished == false)
                    {
                        allFinished = false;
                        break;
                    }
                }
                if (allFinished)
                {
                    return;
                }
            }
        }

        internal void SleepForMs(int ms)
        {
            _ = new ManualResetEvent(false).WaitOne(ms);
        }
    }
}