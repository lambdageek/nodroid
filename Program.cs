using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace nodroid;

public class nodroid {
    public static void Main ()
    {
	CancellationTokenSource cts = new ();
	int cancelCount = 0;
	
	//Console.CancelKeyPress += (s, e) =>
        //{
        //    Console.WriteLine("Canceling...");
        //    cts.Cancel();
	//    cancelCount++;
	//    if (cancelCount < 2)
	//	e.Cancel = true;
        //};
	using (var looper = new Looper (cts.Token))
	{
	    var oldContext = SynchronizationContext.Current;
	    try {
		SynchronizationContext.SetSynchronizationContext (looper.SynchronizationContext);
		Console.Error.WriteLine ("new context installed");
		Task? t = AsyncMain ();//looper.QueueWork(AsyncMain);
		t.ContinueWith(delegate { cts.Cancel(); }, TaskScheduler.Default);
		while (true) {
		    Console.Error.WriteLine ("waiting for work");
		    if (!looper.RunOne())
			break;
		}
		t.GetAwaiter().GetResult();
	    } finally {
		SynchronizationContext.SetSynchronizationContext (oldContext);
	    }
	}
    }

    public static async Task AsyncMain ()
    {
	CallSyncWithAWait();
	Console.WriteLine ($"one {Thread.CurrentThread.ManagedThreadId}");
	await Task.Yield();
	Console.WriteLine ($"two {Thread.CurrentThread.ManagedThreadId}");
	await Task.Delay(500);
	Console.WriteLine ($"three {Thread.CurrentThread.ManagedThreadId}");
	await Task.Delay(500);
	Console.WriteLine ($"four {Thread.CurrentThread.ManagedThreadId}");
    }

    public static void CallSyncWithAWait()
    {
	Console.WriteLine ("sync method waiting");
	AsyncyThing().Wait();
	Console.WriteLine ("sync method done waiting");
    }

    public static async Task AsyncyThing() {
	Console.WriteLine ("zero");
	await Task.Yield();
	Console.WriteLine ("Foo");
	await Task.Delay(1);
	Console.WriteLine ("Bar");
    }
}


public class Looper : IDisposable
{
    private readonly BlockingCollection<WorkItem> queue;
    private readonly SyncContext context;
    private readonly CancellationToken ct;
    private TaskScheduler? scheduler;
    private TaskFactory? factory;
    public Looper(CancellationToken ct)
    {
	queue = new ();
	context = new (this);
	this.ct = ct;
    }

    public struct WorkItem
    {
	public SendOrPostCallback func;
	public object? state;
    }

    public void Dispose()
    {
	queue.Dispose();
    }

    private TaskFactory GetFactory()
    {
	if (factory != null)
	    return factory;
	if (scheduler == null)
	    scheduler = TaskScheduler.FromCurrentSynchronizationContext(); // SynchronizationContext.Current should be == this
	factory = new TaskFactory (ct, TaskCreationOptions.None, TaskContinuationOptions.None, scheduler);
	return factory;
    }

    public class SyncContext : SynchronizationContext
    {
	private readonly Looper looper;

	public SyncContext(Looper looper)
	{
	    this.looper = looper;
	}

	public override SynchronizationContext CreateCopy() => new SyncContext (looper);

	public override void Post(SendOrPostCallback cb, object? state)
	{
	    looper.Enqueue (new WorkItem {func = cb, state = state});
	}


	public override void Send(SendOrPostCallback cb, object? state)
	{
	    Console.Error.WriteLine ("don't call Looper.SyncContext.Send");
	    throw new NotSupportedException ("don't call Looper.SyncContext.Send");
	}

    }

    public bool HasWork => queue.Count > 0;

    public SynchronizationContext SynchronizationContext => context;


    private void Enqueue (WorkItem it)
    {
	Console.Error.WriteLine ($"enqueued work {Thread.CurrentThread.ManagedThreadId}");
	queue.Add (it);
    }

    public Task QueueWork (Func<Task> f) {
	return GetFactory().StartNew (f);
    }

    public bool RunOne ()
    {
	WorkItem it;
	try {
	    Console.Error.WriteLine ("taking work item");
	    it = queue.Take(ct);
	} catch (OperationCanceledException) {
	    Console.Error.WriteLine ("canceled");
	    return false;
	}
	Console.Error.WriteLine ("running work item");
	it.func (it.state);
	Console.Error.WriteLine ("one iteration done");
	return true;
    }
}
