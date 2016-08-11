using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dependable;
using Dependable.Dispatcher;
using Dependable.Extensions.Persistence.Sql;
using Dependable.Tracking;

namespace TestHost
{
    class Program
    {
        static IScheduler _scheduler;

        static void Main()
        {
            DependableJobsTable.Create(ConfigurationManager.ConnectionStrings["Default"].ConnectionString);

            _scheduler = new DependableConfiguration()
                .SetDefaultRetryCount(2)
                .SetDefaultRetryDelay(TimeSpan.FromSeconds(1))
                .SetRetryTimerInterval(TimeSpan.FromSeconds(1))
                .UseSqlPersistenceProvider("Default", "TestHost")
                .UseConsoleEventLogger(EventType.JobStatusChanged | EventType.JobSuspended)
                .Activity<Greet>(c => c.WithMaxQueueLength(3).WithMaxWorkers(3))
                .CreateScheduler();

            _scheduler.Start();

            Console.WriteLine("Dependable Examples");
            Console.WriteLine("1. Single Activity");
            Console.WriteLine("2: Activity Sequence");
            Console.WriteLine("3. Parallel Activity");
            Console.WriteLine("4. Logger Filter");
            Console.WriteLine("5. Exception Filter");
            Console.WriteLine("6. Job cancellation");

            var option = Convert.ToInt32(Console.ReadLine());

            switch (option)
            {
                case 1:
                    _scheduler.Schedule(
                        Activity.Run<Greet>(g => g.Run("alice", "cooper")).Then<Greet>(g => g.Run("bob", "jane")));
                    _scheduler.Schedule(Activity.Run<Greet>(g => g.Run("bob", "jane")));
                    _scheduler.Schedule(Activity.Run<Greet>(g => g.Run("kyle", "simpson")));
                    _scheduler.Schedule(Activity.Run<Greet>(g => g.Run("andrew", "matthews")));
                    break;
                case 2:
                    var sequence = Activity
                        .Sequence(
                            Activity.Run<Greet>(g => g.Run("a", "b")),
                            Activity.Run<Greet>(g => g.Run("c", "d")))
                        .ExceptionFilter<LoggingFilter>((c, f) => f.Log(c, "hey"))
                        .AnyFailed<Greet>(g => g.Run("e", "f"))
                        .Cancelled<GreetCancelled>(d => d.Run())
                        .ThenContinue()
                        .Then<Greet>(g => g.Run("g", "h"));
                    _scheduler.Schedule(sequence);
                    break;
                case 3:
                    _scheduler.Schedule(Activity.Parallel(
                        Activity.Run<Greet>(g => g.Run("a", "b")).Then<Greet>(g => g.Run("e", "f")),
                        Activity.Run<Greet>(g => g.Run("g", "h")).Then<Greet>(g => g.Run("i", "j"))
                        ));
                    break;
                case 4:
                    _scheduler.Schedule(
                        Activity
                            .Run<Greet>(g => g.Run("buddhike", "de silva"))
                            .ExceptionFilter<LoggingFilter>((c, f) => f.Log(c, "something was wrong")));
                    break;
                case 5:
                    _scheduler.Schedule(
                        Activity.Run<Greet>(g => g.Run("c", "d"))
                            .ExceptionFilter<LoggingFilter>((c, f) => f.Log(c, "ouch"))
                            .Failed<Greet>(g => g.Run("a", "b")));
                    break;
                case 6:
                    Console.WriteLine("Enter 'y' to initate job cancellation at any time");
                    Console.WriteLine("Press any key to start the schedule");
                    Console.ReadLine();
                    var guid = _scheduler.Schedule(Activity.Run<DueSchedule>(a => a.Run()).Cancelled(Activity.Run<CancelSchedule>(d=>d.Run())));
                    if ("Y" == Console.ReadLine().ToUpper())
                    {
                        _scheduler.Stop(guid);
                    }
                    break;
            }

            Console.ReadLine();
            Console.WriteLine("Press enter to cleanup completed jobs");
            Console.ReadLine();

            DependableJobsTable.Clean(ConfigurationManager.ConnectionStrings["Default"].ConnectionString, "TestHost");
        }
    }

    public class Person
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }

    public class GreetEx
    {
        // ReSharper disable once CSharpWarnings::CS1998
        public async Task Run(Person person)
        {
            Console.WriteLine("Hello {0} {1}", person.FirstName, person.LastName);
        }
    }

    public class Greet
    {
        // ReSharper disable once CSharpWarnings::CS1998
        public async Task Run(string firstName, string lastName)
        {
            Console.WriteLine("hello {0} {1}", firstName, lastName);
            if(firstName == "c")
                throw new Exception("Failed!");
        }
    }

    public class GreetMany
    {
        // ReSharper disable once CSharpWarnings::CS1998
        public async Task<Activity> Run(IEnumerable<string> names)
        {
            return Activity.Sequence(Enumerable.Empty<Activity>());
        }
    }

    public class GreetCancelled
    {
        // ReSharper disable once CSharpWarnings::CS1998
        public async Task Run()
        {
            Console.WriteLine("Bye! Party over!");
        }
    }

    public class DueSchedule
    {
        // ReSharper disable once CSharpWarnings::CS1998
        public async Task<Activity> Run()
        {
            Console.WriteLine("DueSchedule");
            Thread.Sleep(5000);

            return
                Activity.Sequence(Activity.Run<ApplicationList>(a => a.Download()),
                                  Activity.Run<ApplicationList>(a => a.DownloadEachItem()));
            //.Cancelled(Activity.Run<CancelApplicationList>(g => g.Run()));
        }
    }

    public class CancelApplicationList
    {
        // ReSharper disable once CSharpWarnings::CS1998
        public async Task Run()
        {
            Console.WriteLine("CancelApplicationList");
        }
    }


    public class CancelSchedule
    {
        // ReSharper disable once CSharpWarnings::CS1998
        public async Task Run()
        {
            Console.WriteLine("CancelSchedule");
        }
    }

    public class ApplicationList
    {
        // ReSharper disable once CSharpWarnings::CS1998
        public async Task Download()
        {
            Thread.Sleep(10000);
            Console.WriteLine("ApplicationList - Download");
        }

        // ReSharper disable once CSharpWarnings::CS1998
        public async Task<Activity> DownloadEachItem()
        {
            Console.WriteLine("ApplicationList - Download Each Item");
            return Activity.Parallel(Activity.Run<ApplicationDetails>(a => a.Download()).Cancelled(Activity.Run<ApplicationDetails.CancelDownload>(notify => notify.Run())),
                Activity.Run<ApplicationDetails>(a => a.Convert()),
                Activity.Run<ApplicationDetails>(a => a.Notify()));
             //.Cancelled(Activity.Run<ApplicationDetails>(notify => notify.CancelTask()));
        }
    }

    public class ApplicationDetails
    {
        // ReSharper disable once CSharpWarnings::CS1998
        public async Task Download()
        {
            Thread.Sleep(5000);
            Console.WriteLine("ApplicationDetails Download");
            
        }

        // ReSharper disable once CSharpWarnings::CS1998
        public async Task Convert()
        {
            
            Thread.Sleep(5000);
            Console.WriteLine("ApplicationDetails Convert");
            
        }

        // ReSharper disable once CSharpWarnings::CS1998
        public async Task Notify()
        {
            Console.WriteLine("NOWWWWWWW");
            Thread.Sleep(5000);
            Console.WriteLine("ApplicationDetails Notify");
            throw new Exception("A");
        }

        public async Task CancelTask()
        {
            Console.WriteLine("ApplicationDetails CancelTask");
        }

        public class CancelDownload
        {
            // ReSharper disable once CSharpWarnings::CS1998
            public async Task Run()
            {
                Console.WriteLine("ApplicationDetails CancelDownload");
            }
        }
    }

    public class LoggingFilter
    {
        public void Log(ExceptionContext context, string message)
        {
            Console.WriteLine(context.ActivityType);
        }
    }
}