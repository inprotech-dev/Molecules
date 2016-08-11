﻿using System;
using System.Threading.Tasks;
using Dependable.Dependencies;
using Dependable.Recovery;
using Dependable.Tracking;
using Dependable.Utilities;

namespace Dependable.Dispatcher
{
    public interface IDispatcher
    {
        Task Dispatch(Job job, ActivityConfiguration configuration);
    }

    /// <summary>
    /// Dispatches the corresponding Job implemented by users.
    /// </summary>
    public class Dispatcher : IDispatcher
    {
        readonly IJobCoordinator _jobCoordinator;
        readonly IDependencyResolver _dependencyResolver;
        readonly IErrorHandlingPolicy _errorHandlingPolicy;
        readonly IMethodBinder _methodBinder;
        readonly IEventStream _eventStream;
        readonly IRecoverableAction _recoverableAction;

        readonly IStatusChanger _statusChanger;
        readonly IContinuationLiveness _continuationLiveness;
        readonly IExceptionFilterDispatcher _exceptionFilterDispatcher;
        readonly IJobRootValidator _jobRootValidator;

        public Dispatcher(IDependencyResolver dependencyResolver,
            IJobCoordinator jobCoordinator, IErrorHandlingPolicy errorHandlingPolicy,
            IMethodBinder methodBinder,
            IEventStream eventStream,
            IRecoverableAction recoverableAction,
            IStatusChanger statusChanger,
            IContinuationLiveness continuationLiveness,
            IExceptionFilterDispatcher exceptionFilterDispatcher,
            IJobRootValidator jobRootValidator)
        {
            if (jobCoordinator == null) throw new ArgumentNullException("jobCoordinator");
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            if (errorHandlingPolicy == null) throw new ArgumentNullException("errorHandlingPolicy");
            if (methodBinder == null) throw new ArgumentNullException("methodBinder");
            if (eventStream == null) throw new ArgumentNullException("eventStream");
            if (recoverableAction == null) throw new ArgumentNullException("recoverableAction");
            if (statusChanger == null) throw new ArgumentNullException("statusChanger");
            if (continuationLiveness == null) throw new ArgumentNullException("continuationLiveness");
            if (exceptionFilterDispatcher == null) throw new ArgumentNullException("exceptionFilterDispatcher");
            if (jobRootValidator == null) throw new ArgumentNullException("jobRootValidator");

            _jobCoordinator = jobCoordinator;
            _dependencyResolver = dependencyResolver;
            _errorHandlingPolicy = errorHandlingPolicy;
            _methodBinder = methodBinder;
            _eventStream = eventStream;
            _recoverableAction = recoverableAction;
            _statusChanger = statusChanger;
            _continuationLiveness = continuationLiveness;
            _exceptionFilterDispatcher = exceptionFilterDispatcher;
            _jobRootValidator = jobRootValidator;
        }

        public async Task Dispatch(Job job, ActivityConfiguration configuration)
        {
            if (job == null) throw new ArgumentNullException("job");
            if (configuration == null) throw new ArgumentNullException("configuration");

            switch (job.Status)
            {
                case JobStatus.Ready:
                case JobStatus.Running:
                case JobStatus.Failed:
                    await Run(job);
                    break;
                case JobStatus.WaitingForChildren:
                    _jobCoordinator.Run(job, () => _continuationLiveness.Verify(job.Id));
                    break;
                case JobStatus.ReadyToComplete:
                    _jobCoordinator.Run(job, () => _statusChanger.Change(job, JobStatus.Completed));
                    break;
                case JobStatus.ReadyToPoison:
                    _jobCoordinator.Run(job, () => _statusChanger.Change(job, JobStatus.Poisoned));
                    break;
                case JobStatus.Cancelling:
                    await Run(job, true);
                    break;
                default:
                    _eventStream.Publish<Dispatcher>(EventType.JobAbandoned,
                        EventProperty.Named("Reason", "UnexpectedStatus"), EventProperty.JobSnapshot(job));
                    break;
            }
        }

        async Task Run(Job job, bool force = false)
        {
            using (var scope = _dependencyResolver.BeginScope())
            {
                var context = new JobContext(job);
                JobResult result = null;

                try
                {
                    if (!force && !_jobRootValidator.IsValid(job.RootId))
                    {
                        _statusChanger.Change(job, JobStatus.Cancelled);
                        _eventStream.Publish<JobCoordinator>(EventType.JobCancelled,
                            EventProperty.Named("Reason", "Cancelled"), EventProperty.JobSnapshot(job));
                        return;
                    }

                    job = _statusChanger.Change(job, JobStatus.Running);

                    var instance = scope.GetService(job.Type);

                    result = await _methodBinder.Run(instance, job);

                    if (result == null)
                    {
                        _eventStream.Publish<Dispatcher>(EventType.JobAbandoned,
                            EventProperty.Named("Reason", "ReturnedNullTask"), EventProperty.JobSnapshot(job));
                    }
                }
                catch (Exception e)
                {
                    if (e.IsFatal())
                        throw;
       
                    if (!force)
                    {
                        _exceptionFilterDispatcher.Dispatch(job, e, context, scope);
                        _eventStream.Publish<Dispatcher>(e, EventProperty.JobSnapshot(job));
                        _errorHandlingPolicy.RetryOrPoison(job, e, context);
                    }
                }

                if (result != null)
                    CompleteJob(job, result);
            }
        }

        void CompleteJob(Job job, JobResult result)
        {
            if (result.Activity == null || result.Activity.IsEmptyGroup())
                _jobCoordinator.Run(job, () => _statusChanger.Change(job, JobStatus.Completed));
            else
            {
                _recoverableAction.Run(() =>
                    _statusChanger.Change(job, JobStatus.WaitingForChildren, activity: result.Activity));
            }
        }
    }
}