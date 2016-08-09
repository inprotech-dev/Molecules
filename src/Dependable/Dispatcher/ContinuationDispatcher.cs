using System;
using System.Collections.Generic;
using System.Linq;
using Dependable.Persistence;
using Dependable.Recovery;

namespace Dependable.Dispatcher
{
    public interface IContinuationDispatcher
    {
        Continuation[] Dispatch(Job job);
    }

    /// <summary>
    /// Used to evaluate to continuation of a given job and 
    /// dispatch the appropriate jobs.
    /// </summary>
    public class ContinuationDispatcher : IContinuationDispatcher
    {
        readonly IJobRouter _router;
        readonly IJobMutator _jobMutator;
        readonly IPersistenceStore _persistenceStore;
        readonly IRecoverableAction _recoverableAction;
        readonly IJobRootValidator _jobRootValidator;

        public ContinuationDispatcher(IJobRouter router,
            IJobMutator jobMutator,
            IPersistenceStore persistenceStore,
            IRecoverableAction recoverableAction,
            IJobRootValidator jobRootValidator)
        {
            if (router == null) throw new ArgumentNullException("router");
            if (jobMutator == null) throw new ArgumentNullException("JobMutator");
            if (persistenceStore == null) throw new ArgumentNullException("persistenceStore");
            if (recoverableAction == null) throw new ArgumentNullException("recoverableAction");

            _router = router;
            _jobMutator = jobMutator;
            _persistenceStore = persistenceStore;
            _recoverableAction = recoverableAction;
            _jobRootValidator = jobRootValidator;
        }

        public Continuation[] Dispatch(Job job)
        {
            if (job == null) throw new ArgumentNullException("job");

            var isTreeValid = _jobRootValidator.IsValid(job.RootId);
            var readyContinuations = job.Continuation.PendingContinuations(isTreeValid).ToArray();

            foreach (var continuation in readyContinuations)
                continuation.Status = continuation.CompensateForCancellation ? JobStatus.Cancelling : JobStatus.Ready;

            _jobMutator.Mutate<ContinuationDispatcher>(job, continuation: job.Continuation);

            DispatchCore(readyContinuations);

            return readyContinuations;
        }

        /// <summary>
        /// Finds all schedulable jobs that are in 
        /// Created state. Then for each of them, 
        /// attempts to update the status to Ready.
        /// Once successful, routes the job.
        /// </summary>
        void DispatchCore(IEnumerable<Continuation> readyContinuations)
        {
            var continuations = readyContinuations as Continuation[] ?? readyContinuations.ToArray();

            var schedulableJobs = (
                from @await in continuations
                let j = _persistenceStore.Load(@await.Id)
                where j.Status == JobStatus.Created
                select j)
                .ToArray();

            foreach (var job in schedulableJobs)
            {
                var jobReference = job;
                var cancellationJob = continuations.Single(_ => _.Id == job.Id).CompensateForCancellation;
                _recoverableAction.Run(
                    () =>
                        jobReference =
                            _jobMutator.Mutate<ContinuationDispatcher>(jobReference,
                                cancellationJob ? JobStatus.Cancelling : JobStatus.Ready),
                    then: () => _router.Route(jobReference));
            }
        }
    }
}