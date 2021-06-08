using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dependable;
using Dependable.Dispatcher;
using Dependable.Extensions.Persistence.Sql;
using Dependable.Tracking;
using Microsoft.Extensions.Configuration;

namespace TestHost
{
    internal class Program
    {
        static IScheduler _scheduler;
        static IConfiguration _configuration;

        static void Main()
        {
            ResolveConfiguration();

            var connectionString = GetConnectionString();

            DependableJobsTable.Create(connectionString);

            _scheduler = new DependableConfiguration()
                .SetDefaultRetryCount(2)
                .SetDefaultRetryDelay(TimeSpan.FromSeconds(1))
                .SetRetryTimerInterval(TimeSpan.FromSeconds(1))
                .UseSqlPersistenceProvider(connectionString, "TestHost")
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
                    Console.WriteLine("Enter 'y' to initiate job cancellation at any time");
                    Console.WriteLine("Press any key to start the schedule");
                    Console.ReadLine();

                    var stopToken = Guid.NewGuid();

                    _scheduler.Schedule(
                        Activity.Run<DueSchedule>(a => a.Run()).Cancelled(Activity.Run<CancelSchedule>(d => d.Run())),
                        stopToken);

                    if (ConsoleKey.Y == Console.ReadKey().Key) _scheduler.Stop(stopToken);
                    break;
            }

            Console.ReadLine();
            Console.WriteLine("Press enter to cleanup completed jobs");
            Console.ReadLine();

            DependableJobsTable.Clean(connectionString, "TestHost");
        }

        static void ResolveConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            _configuration = builder.Build();
        }

        static string GetConnectionString(string name = "Default")
        {
            return _configuration.GetConnectionString(name);
        }
    }

    public class Person
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }

    public class GreetEx
    {
        public Task Run(Person person)
        {
            Console.WriteLine("Hello {0} {1}", person.FirstName, person.LastName);

            return Task.CompletedTask;
        }
    }

    public class Greet
    {
        public Task Run(string firstName, string lastName)
        {
            Console.WriteLine("hello {0} {1}", firstName, lastName);
            if (firstName == "c")
                throw new Exception("Failed!");

            return Task.CompletedTask;
        }
    }

    public class GreetMany
    {
        public Task<Activity> Run(IEnumerable<string> names)
        {
            return Task.FromResult((Activity) Activity.Sequence(Enumerable.Empty<Activity>()));
        }
    }

    public class GreetCancelled
    {
        public Task Run()
        {
            Console.WriteLine("Bye! Party over!");

            return Task.CompletedTask;
        }
    }

    public class DueSchedule
    {
        public Task<Activity> Run()
        {
            Console.WriteLine("DueSchedule");
            Thread.Sleep(5000);

            return Task.FromResult((Activity)
                Activity.Sequence(Activity.Run<ApplicationList>(a => a.Download()),
                    Activity.Run<ApplicationList>(a => a.DownloadEachItem())));
            //.Cancelled(Activity.Run<CancelApplicationList>(g => g.Run()));
        }
    }

    public class CancelApplicationList
    {
        public Task Run()
        {
            Console.WriteLine("CancelApplicationList");

            return Task.CompletedTask;
        }
    }


    public class CancelSchedule
    {
        public Task Run()
        {
            Console.WriteLine("CancelSchedule");

            return Task.CompletedTask;
        }
    }

    public class ApplicationList
    {
        public Task Download()
        {
            Thread.Sleep(10000);
            Console.WriteLine("ApplicationList - Download");

            return Task.CompletedTask;
        }

        public Task<Activity> DownloadEachItem()
        {
            Console.WriteLine("ApplicationList - Download Each Item");
            return Task.FromResult((Activity)
                Activity.Parallel(
                    Activity.Run<ApplicationDetails>(a => a.Download())
                        .Cancelled(Activity.Run<ApplicationDetails.CancelDownload>(notify => notify.Run())),
                    Activity.Run<ApplicationDetails>(a => a.Convert()),
                    Activity.Run<ApplicationDetails>(a => a.Notify())));
            //.Cancelled(Activity.Run<ApplicationDetails>(notify => notify.CancelTask()));
        }
    }

    public class ApplicationDetails
    {
        public Task Download()
        {
            Thread.Sleep(5000);
            Console.WriteLine("ApplicationDetails Download");

            return Task.CompletedTask;
        }

        public Task Convert()
        {
            Thread.Sleep(5000);
            Console.WriteLine("ApplicationDetails Convert");

            return Task.CompletedTask;
        }

        public Task Notify()
        {
            Console.WriteLine("NOWWWWWWW");
            Thread.Sleep(5000);
            Console.WriteLine("ApplicationDetails Notify");
            throw new Exception("A");
        }

        public Task CancelTask()
        {
            Console.WriteLine("ApplicationDetails CancelTask");

            return Task.CompletedTask;
        }

        public class CancelDownload
        {
            public Task Run()
            {
                Console.WriteLine("ApplicationDetails CancelDownload");

                return Task.CompletedTask;
            }
        }
    }

    public class LoggingFilter
    {
        public void Log(ExceptionContext context, string message)
        {
            Console.WriteLine("LoggingFilter.Log: {0} {1}", context.ActivityType, message);
        }
    }
}