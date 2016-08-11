using System;
using System.Collections.Generic;
using System.Linq;
using Dependable.Persistence;

namespace Dependable.Dispatcher
{
    public interface IEndTransition
    {
        Job Transit(Job job, JobStatus endStatus);
    }

    public class EndTransition : IEndTransition
    {
        readonly IPersistenceStore _jobRepository;
        readonly IJobMutator _jobMutator;
        readonly IContinuationDispatcher _continuationDispatcher;
        private readonly IJobRootValidator _jobRootValidator;

        static readonly Dictionary<JobStatus, JobStatus> PendingEndStatus =
            new Dictionary<JobStatus, JobStatus>
            {
                {JobStatus.Completed, JobStatus.ReadyToComplete},
                {JobStatus.Poisoned, JobStatus.ReadyToPoison},
                 {JobStatus.Cancelled, JobStatus.CancellationInitiated}
            };

        static readonly IEnumerable<JobStatus> EndStatuses = new[]
        {JobStatus.Completed, JobStatus.Poisoned, JobStatus.Cancelled};

        public EndTransition(IPersistenceStore jobRepository,
            IJobMutator jobMutator,
            IContinuationDispatcher continuationDispatcher, 
            IJobRootValidator jobRootValidator)
        {
            if (jobRepository == null) throw new ArgumentNullException("jobRepository");
            if (jobMutator == null) throw new ArgumentNullException("jobMutator");
            if (continuationDispatcher == null) throw new ArgumentNullException("continuationDispatcher");
            if (jobRootValidator == null) throw new ArgumentNullException("jobRootValidator");

            _jobRepository = jobRepository;
            _jobMutator = jobMutator;
            _continuationDispatcher = continuationDispatcher;
            _jobRootValidator = jobRootValidator;
        }

        public Job Transit(Job job, JobStatus endStatus)
        {
            if (job == null) throw new ArgumentNullException("job");

            if (!EndStatuses.Contains(endStatus))
                throw new ArgumentOutOfRangeException("endStatus", endStatus, "Not a valid end status.");
            
            return job.ParentId == null ? 
                _jobMutator.Mutate<EndTransition>(job, status: job.Status == JobStatus.CancellationInitiated ? JobStatus.Cancelled: endStatus) : EndTree(job, endStatus);
        }

        Job EndTree(Job job, JobStatus endStatus)
        {
            // If this is the root, mutate to completed status.
            if (job.ParentId == null)
            {
                //If Cancellation is Initiated - Endstate is Cancelled. Otherwise its Completed
                endStatus = job.Status == JobStatus.CancellationInitiated ? JobStatus.Cancelled : JobStatus.Completed;
                return _jobMutator.Mutate<EndTransition>(job, status: endStatus);
            }
            
            
            job = _jobMutator.Mutate<EndTransition>(job, PendingEndStatus[endStatus]);

            /*
             * First, load the parent, find the await record for this job and 
             * update its status to end status.
             */
// ReSharper disable once PossibleInvalidOperationException
            var parent = _jobRepository.Load(job.ParentId.Value);
            var @await = parent.Continuation.Find(job, _jobRootValidator.IsValid(parent.RootId));
            @await.Status = endStatus;

            /*
             * After we set the await's status, invoke ContinuationDispatcher to 
             * ensure any pending await for parent is dispatched.
             * If ContinuationDispatcher returns any awaits, that means parent job is not
             * ready for completion. 
             */
          
            var pendingAwaits = _continuationDispatcher.Dispatch(parent);

            if (!pendingAwaits.Any())
                EndTree(parent, endStatus == JobStatus.Cancelled? JobStatus.Cancelled  : JobStatus.Completed );
            
            return _jobMutator.Mutate<EndTransition>(job, endStatus);
        }
    }
}