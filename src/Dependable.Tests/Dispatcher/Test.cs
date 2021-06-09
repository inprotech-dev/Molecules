using System.Threading.Tasks;

namespace Dependable.Tests.Dispatcher
{
    public class Test
    {
        public Task<Activity> Run()
        {
            return Task.FromResult((Activity) null);
        }

        public Task<Activity> RunWithArguments(string argument)
        {
            return Task.FromResult((Activity) null);
        }
    }
}