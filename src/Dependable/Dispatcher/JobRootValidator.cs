using System;
using Dependable.Persistence;

namespace Dependable.Dispatcher
{
    public interface IJobRootValidator
    {
        bool IsValid(Guid id);
    }

    internal class JobRootValidator : IJobRootValidator
    {
        readonly IPersistenceStore _persistenceStore;

        public JobRootValidator(IPersistenceStore persistenceStore)
        {
            if (persistenceStore == null) throw new ArgumentNullException("persistenceStore");
            _persistenceStore = persistenceStore;
        }

        public bool IsValid(Guid id)
        {
            var currentJob = _persistenceStore.Load(id);
            return !(currentJob.Status == JobStatus.Cancelled || currentJob.Status == JobStatus.CancellationInitiated);
        }
    }
}
