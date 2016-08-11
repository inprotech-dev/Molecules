﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dependable.Dependencies;
using Dependable.Diagnostics;
using Dependable.Dispatcher;
using Dependable.Persistence;
using Dependable.Recovery;
using Dependable.Tracking;

namespace Dependable
{
    public interface IDependableConfiguration
    {
        ActivityConfiguration For(Type type);

        ActivityConfiguration DefaultActivityConfiguration { get; }

        IEnumerable<ActivityConfiguration> ActivityConfiguration { get; }

        TimeSpan RetryTimerInterval { get; }
    }

    public class DependableConfiguration : IDependableConfiguration
    {
        TimeSpan _defaultRetryTimerInterval = Defaults.RetryTimerInterval;
        IExceptionLogger _exceptionLogger = new NullExceptionLogger();
        IDependencyResolver _dependencyResolver = new DefaultDependencyResolver();
        IPersistenceProvider _persistenceProvider = new InMemoryPersistenceProvider();

        readonly ICollection<IEventSink> _eventSinks = new Collection<IEventSink>();

        readonly ActivityConfiguration _defaultActivityConfiguration = new ActivityConfiguration();

        readonly Dictionary<Type, ActivityConfiguration> _activityConfiguration =
            new Dictionary<Type, ActivityConfiguration>();

        public DependableConfiguration UseExceptionLogger(IExceptionLogger exceptionLogger)
        {
            if (exceptionLogger == null) throw new ArgumentNullException("exceptionLogger");
            _exceptionLogger = exceptionLogger;
            return this;
        }

        public DependableConfiguration UseDependencyResolver(IDependencyResolver dependencyResolver)
        {
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            _dependencyResolver = dependencyResolver;
            return this;
        }

        public DependableConfiguration UsePersistenceProvider(IPersistenceProvider provider)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            _persistenceProvider = provider;
            return this;
        }

        public DependableConfiguration UseEventSink(IEventSink eventSink)
        {
            if (eventSink == null) throw new ArgumentNullException("eventSink");
            _eventSinks.Add(eventSink);
            return this;
        }

        public DependableConfiguration Activity<T>(Action<ActivityConfiguration> configurator)
        {
            var configuration = new ActivityConfiguration(typeof (T), _defaultActivityConfiguration);

            configurator(configuration);

            _activityConfiguration[typeof (T)] = configuration;

            return this;
        }

        public DependableConfiguration SetRetryTimerInterval(TimeSpan interval)
        {
            _defaultRetryTimerInterval = interval;
            return this;
        }

        public DependableConfiguration SetDefaultRetryCount(int count)
        {
            _defaultActivityConfiguration.WithRetryCount(count);
            return this;
        }

        public DependableConfiguration SetDefaultRetryDelay(TimeSpan delay)
        {
            _defaultActivityConfiguration.WithRetryDelay(delay);
            return this;
        }

        public DependableConfiguration SetDefaultMaxQueueLength(int length)
        {
            _defaultActivityConfiguration.WithMaxQueueLength(length);
            return this;
        }

        public DependableConfiguration SetDefaultMaxWorkers(int count)
        {
            _defaultActivityConfiguration.WithMaxWorkers(count);
            return this;
        }

        public IScheduler CreateScheduler()
        {
            Func<DateTime> now = () => DateTime.Now;

            var eventStream = new EventStream(_eventSinks, _exceptionLogger, now);
            var recoverableAction = new RecoverableAction(this, eventStream);
            var delegatingPersistenceStore = new DelegatingPersistenceStore(_persistenceProvider);
            var jobMutation = new JobMutator(eventStream, delegatingPersistenceStore);

            var queueConfiguration = new JobQueueFactory(
                delegatingPersistenceStore, this, eventStream, recoverableAction, jobMutation).Create();

            var router = new JobRouter(queueConfiguration);
            var methodBinder = new MethodBinder();
            var jobRootValidator = new JobRootValidator(delegatingPersistenceStore);

            var continuationDispatcher = new ContinuationDispatcher(router, jobMutation,
                delegatingPersistenceStore, recoverableAction, jobRootValidator);
            var activityToContinuationConverter = new ActivityToContinuationConverter(now);

            var runningTransition = new RunningTransition(jobMutation);
            var failedTransition = new FailedTransition(this, jobMutation, now);
            var continuationLiveness = new ContinuationLiveness(delegatingPersistenceStore, continuationDispatcher);

            var endTransition = new EndTransition(delegatingPersistenceStore, jobMutation, continuationDispatcher, jobRootValidator);

            var coordinator = new JobCoordinator(eventStream, recoverableAction);

            var waitingForChildrenTransition = new WaitingForChildrenTransition(
                delegatingPersistenceStore,
                continuationDispatcher, 
                activityToContinuationConverter, 
                recoverableAction, 
                jobMutation);

            var changeState = new StatusChanger(eventStream, runningTransition, failedTransition,
                endTransition, waitingForChildrenTransition, jobMutation);

            var failedJobQueue = new FailedJobQueue(this, delegatingPersistenceStore, now, eventStream, router);

            var errorHandlingPolicy = new ErrorHandlingPolicy(this, coordinator, changeState,
                failedJobQueue, recoverableAction);

            var exceptionFilterDispatcher = new ExceptionFilterDispatcher(eventStream);

            var jobDispatcher = new Dispatcher.Dispatcher(_dependencyResolver,
                coordinator,
                errorHandlingPolicy,
                methodBinder,
                eventStream,
                recoverableAction,
                changeState,
                continuationLiveness,
                exceptionFilterDispatcher, 
                jobRootValidator);

            var jobPumps =
                queueConfiguration
                    .ActivitySpecificQueues
                    .Values
                    .Select(q => new JobPump(jobDispatcher, eventStream, q))
                    .ToList();

            jobPumps.Add(new JobPump(jobDispatcher, eventStream, queueConfiguration.Default));

            return new Scheduler(
                queueConfiguration,
                this,
                delegatingPersistenceStore,
                now,
                failedJobQueue,
                recoverableAction,
                router,
                activityToContinuationConverter,
                jobPumps,
                jobMutation);
        }

        TimeSpan IDependableConfiguration.RetryTimerInterval
        {
            get { return _defaultRetryTimerInterval; }
        }

        ActivityConfiguration IDependableConfiguration.For(Type type)
        {
            ActivityConfiguration configuration;

            return _activityConfiguration.TryGetValue(type, out configuration)
                ? configuration
                : _defaultActivityConfiguration;
        }

        ActivityConfiguration IDependableConfiguration.DefaultActivityConfiguration
        {
            get { return _defaultActivityConfiguration; }
        }

        IEnumerable<ActivityConfiguration> IDependableConfiguration.ActivityConfiguration
        {
            get { return _activityConfiguration.Values; }
        }
    }
}