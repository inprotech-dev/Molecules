using System;
using System.Collections.Generic;
using System.Linq;
using Dependable.Persistence;
using Dependable.Recovery;
using Dependable.Tracking;

namespace Dependable.Dispatcher
{
    public interface IJobQueueFactory
    {
        QueueConfiguration Create();
    }

    public class JobQueueFactory : IJobQueueFactory 
    {
        readonly IPersistenceStore _persistenceStore;
        readonly IDependableConfiguration _configuration;
        readonly IEventStream _eventStream;
        readonly IRecoverableAction _recoverableAction;
        readonly IJobMutator _jobMutator;

        public JobQueueFactory(
            IPersistenceStore persistenceStore, 
            IDependableConfiguration configuration,
            IEventStream eventStream,
            IRecoverableAction recoverableAction,
            IJobMutator jobMutator)
        {
            if (persistenceStore == null) throw new ArgumentNullException("persistenceStore");
            if (configuration == null) throw new ArgumentNullException("configuration");
            if (eventStream == null) throw new ArgumentNullException("eventStream");
            if (recoverableAction == null) throw new ArgumentNullException("recoverableAction");
            if (jobMutator == null) throw new ArgumentNullException("jobMutator");

            _persistenceStore = persistenceStore;
            _configuration = configuration;
            _eventStream = eventStream;
            _recoverableAction = recoverableAction;
            _jobMutator = jobMutator;
        }

        public QueueConfiguration Create()
        {
            var readyItems = _persistenceStore.LoadBy(JobStatus.Ready).ToArray();
            var cancellingItems = _persistenceStore.LoadBy(JobStatus.Cancelling).ToArray();

            var runningItems = _persistenceStore.LoadBy(JobStatus.Running).ToArray();
            var failedItems = _persistenceStore.LoadBy(JobStatus.Failed).ToArray();
            var waitingForChildren = _persistenceStore.LoadBy(JobStatus.WaitingForChildren).ToArray();

            //Exclude job root while picking the runnable items
            var cancellationInitiatedItems = _persistenceStore.LoadBy(JobStatus.CancellationInitiated)
                                                              .Where(_=>_.ParentId != null);

            var partiallyCompletedItems =  _persistenceStore.LoadBy(JobStatus.ReadyToComplete)
                                                            .Concat(_persistenceStore.LoadBy(JobStatus.ReadyToPoison))
                                                            .Concat(cancellationInitiatedItems)
                                                            .ToArray();

            var all = partiallyCompletedItems.Concat(readyItems)
                                             .Concat(cancellingItems)
                                             .Concat(failedItems)
                                             .Concat(waitingForChildren)
                                             .Concat(runningItems)
                                             .ToArray();

            var totalSuspendedItemsInSpecificQueues = 0;
            var activitySpecificQueues = new Dictionary<Type, IJobQueue>();

            foreach (var activityConfiguration in _configuration.ActivityConfiguration)
            {
                var suspendedCount = _persistenceStore.CountSuspended(activityConfiguration.Type);
                var filtered = Filter(all, activityConfiguration.Type);
                all = filtered.Item2;
                totalSuspendedItemsInSpecificQueues += suspendedCount;

                activitySpecificQueues[activityConfiguration.Type] =
                    new JobQueue(filtered.Item1, suspendedCount, activityConfiguration,
                        _configuration.ActivityConfiguration, _persistenceStore, _eventStream, _recoverableAction, _jobMutator);

                PublishQueueInitializedEvent(filtered.Item1.Length, suspendedCount, activityConfiguration.Type);
            }

            var suspendedCountForDefaultQueue = _persistenceStore.CountSuspended(null) -
                                                totalSuspendedItemsInSpecificQueues;

            var defaultQueue = new JobQueue(all, suspendedCountForDefaultQueue,
                _configuration.DefaultActivityConfiguration, _configuration.ActivityConfiguration, _persistenceStore,
                _eventStream, _recoverableAction, _jobMutator);

            PublishQueueInitializedEvent(all.Length, suspendedCountForDefaultQueue);

            return new QueueConfiguration(defaultQueue, activitySpecificQueues);
        }

        void PublishQueueInitializedEvent(int activityCount, int suspendedCount, Type activityType = null)
        {
            _eventStream.Publish<JobQueueFactory>(
                EventType.Activity,
                EventProperty.ActivityName("JobQueueInitialized"),
                EventProperty.Named("ActivityType", activityType == null ? "All" : activityType.FullName),
                EventProperty.Named("ActivityCount", activityCount),
                EventProperty.Named("SuspendedCount", suspendedCount));
    
        }

        static Tuple<Job[], Job[]> Filter(Job[] recoverableJobs, Type activityType = null)
        {
            var consumableItems = recoverableJobs.Where(j => j.Type == (activityType ?? j.Type)).ToArray();
            return Tuple.Create(consumableItems, recoverableJobs.Except(consumableItems).ToArray());
        }
    }
}