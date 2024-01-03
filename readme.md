## OrderedSemaphore

The objective of the ordered semaphore is to serialise access to a limited number of resources whilst maintaining the order of the accesses.

The code is provided in the form of a library, with a test project showing how to use it. However to prevent projects from having to add more external dependencies, please just copy the code directly into your project.

### Useage

#### Construction

Construct an ordered semaphore by calling:
```
OrderedSemaphore(int initialCount, int maxCount)
```
```initialCount``` is the number of times the semaphore should consider itself obtained, on construction. Typically this is 0.
```maxCount``` is the maximum number of times that this semaphore can be obtained simultaneously.

#### Closing semaphore

Any thread with access to the semaphore object can close it, by calling one of the methods:

```public void Close()```
```public bool Close(int millisecondsToWaitForClosures)```

The first signals all waiting threads that the semaphore is closing, and returns immediately.
The second signals all waiting threads that the semaphore is closing, and waits the specific number of milliseconds for the threads to acknowledge the closure. It returns true if all waiting threads acknowledges the closure, and false otherwise.

Note, any thread that has obtained the semaphore before closure will not be informed. However, on attempting to call WaitOne on a closed semaphore, they will receive an exception.


#### Obtaining the semaphore

The ordered semaphore is obtained by calling one of the following:
```public bool WaitOne()```
```public bool WaitOne(TimeSpan timeout)```
```public bool WaitOne(int millisecondsTimeout)```

The first, with no parameter, waits indefinitely. The second and third wait for the timespan given as the only parameter.

Each of these returns true if the semaphore was successfully obtained, and false if it was not.

If the method returned false, the property ```ClosedOrClosing``` can be accessed on the semaphore object. If it is true, the semaphore was not obtained due to the semaphore being closed by a thread. If it is false, then timeout occurred.


#### Obtaining recursively

The ordered semaphore can be obtained multiple times by the same thread, each time it will have to wait for the semaphore to be released by a different thread. Obviously care must be taken to ensure that the same thread does not obtain the semaphore completely, as this will cause the call to ```WaitOne()``` to hangup indefinitely - if this is a danger then use a timeout when attempting to gain the semaphore recursively.

#### Releasing the semaphore

The semapohore can be released by calling one of the following:
```public int Release()```
```public int Release(int releaseCount)```

The first releases the semaphore once, allowing another thread to obtain it.
The second will release the semaphore the specified number of times. If the semaphore was obtained multiple times by the calling thread, it can release them all here.

In both cases, the returned integer is the semaphore count before release was called.

### Inner workings

When WaitOne() is called by a thread, a ManualResetEvent is first created - this is the event that will be triggered when either the semaphore is being closed, or the semaphore has gained availability.

Before waiting on this ManualResetEvent, a check is made on semaphore availability - if there is then the current count of used semaphore resources is increased and true is returned.

If there is no immediate availability, then the event is waited on.  When the event is triggered, a check is made to see if the semaphore is closing or not.  If it is not, then true is returned, otherwise false is returned.

Note, returning false may mean that the semaphore has been closed, or that the WaitOne() has timed out.

When a thread is ready to return its use of the semaphore, it calls Release().  This decrements the semaphore usage counter, and triggers the ManualResetEvent on the first waiting thread.

### Tests

Tests are provided as part of the solutin in the OrderedSemaphoreTests project.  Note that due to timing issues in the tests, it is actually possible for them to fail.  There are some notes at the start of the OrderedSemaphoreTests.cs file that document the process and provide some guidance on writing better tests if they are failing on your platform.