using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dependable.Dispatcher
{
    public interface IMethodBinder
    {
        Task<JobResult> Run(object instance, Job job);
    }

    public class MethodBinder : IMethodBinder
    {
        static readonly IEnumerable<Type> ValidReturnTypes = new[] {typeof (Task), typeof (Task<Activity>)};

        public async Task<JobResult> Run(object instance, Job job)
        {
            var type = instance.GetType();
            var runMethod = type.GetMethod(job.Method);

            if (runMethod == null || !ValidReturnTypes.Contains(runMethod.ReturnType))
            {
                throw new InvalidOperationException(
                    string.Format("Type {0} does not have a matching Run method.", type.FullName));
            }

            var requiresCancellationToken =
                runMethod.GetCustomAttributes(typeof (RequiresCancellationTokenAttribute), false)
                    .Cast<RequiresCancellationTokenAttribute>()
                    .SingleOrDefault();

            var result = requiresCancellationToken == null
                ? runMethod.Invoke(instance, job.Arguments)
                : runMethod.Invoke(instance, ExpandArgsWithCancellationToken(job));

            if (result == null)
                return null;

            if (runMethod.ReturnType == typeof (Task))
            {
                await (Task) result;
                return new JobResult();
            }

            return new JobResult(await (Task<Activity>) result);
        }

        static object[] ExpandArgsWithCancellationToken(Job job)
        {
            return (new List<object>(job.Arguments ?? new object[0]) {new CancellationToken(job.RootId)}).ToArray();
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public class RequiresCancellationTokenAttribute : Attribute
    {
    }

    public class CancellationToken
    {
        public Guid Value { get; private set; }

        public CancellationToken(Guid token)
        {
            Value = token;
        }
    }
}