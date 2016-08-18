using System.Collections.Generic;
using System.Linq;

namespace Dependable.Dispatcher
{
    public static class ContinuationExtensions
    {
        public static IEnumerable<Continuation> PendingContinuations(this Continuation item, bool isValid = true)
        {
            if (item == null) return Enumerable.Empty<Continuation>();

            if (item.Type == ContinuationType.Single)
            {
                if (item.Status == JobStatus.Created || item.Status == JobStatus.Ready || item.Status == JobStatus.Cancelling)
                    return new[] {item};

                if (item.Status == JobStatus.Poisoned)
                {
                    var failedHandler = item.OnAllFailed.PendingContinuations(isValid).ToArray();

                    if (!failedHandler.Any() && item.CanContinue())
                        return item.Next.PendingContinuations(isValid);

                    return failedHandler;
                }

                if (item.Status == JobStatus.Completed || item.Status == JobStatus.Cancelled || item.Status == JobStatus.CancellationInitiated)
                {
                    var pendingContinuations = item.Next.PendingContinuations(isValid).ToArray();
                    if (pendingContinuations.Any())
                        return pendingContinuations;
                }
            }
            if (item.Type == ContinuationType.Parallel)
            {
                var children = item.Children
                                    .SelectMany(a => a.PendingContinuations(isValid))
                                    .ToArray();
                if (children.Any()) return children;
            }

            if (item.Type == ContinuationType.Sequence)
            {
                foreach (var child in item.Children)
                {
                    var children = child.PendingContinuations(isValid).ToArray();
                    if (children.Any()) return children;
                    if (!child.CanContinue()) break;
                }
            }

            var anyFailureHandlers = Enumerable.Empty<Continuation>();
            var allFailureHandlers = Enumerable.Empty<Continuation>();

            var failedChildren = item.Children.Where(c => !c.CanContinue()).ToArray();

            if (failedChildren.Any()) anyFailureHandlers = item.OnAnyFailed.PendingContinuations(isValid);
            if (failedChildren.Length == item.Children.Count())
                allFailureHandlers = item.OnAllFailed.PendingContinuations(isValid);

            var resultArray = anyFailureHandlers.Concat(allFailureHandlers).ToArray();

            if (resultArray.Any())
                return resultArray;

            if (item.CanContinue())
            {
                var nextContinuations = item.Next.PendingContinuations(isValid).ToArray();
                if (nextContinuations.Any())
                    return nextContinuations;
            }

            if (!isValid)
            {
                var pendingCancellations = PendingCancellations(item).ToArray();
                if (pendingCancellations.Any())
                    return pendingCancellations;
            }

            return Enumerable.Empty<Continuation>();
        }

        //This is to ensure the descendents cancellations are considered
        public static IEnumerable<Continuation> PendingCancellations(this Continuation item)
        {
            if (item.IsCancelled())
            {
                var cancelledHandlers = item.OnCancelled.PendingContinuations(false).ToArray();
                if (cancelledHandlers.Any())
                    return cancelledHandlers;

                foreach (var child in item.Children.Where(_ => _.IsCancelled()))
                {
                    var children = child.PendingCancellations().ToArray();
                    if (children.Any()) return children;
                }
            }
            return Enumerable.Empty<Continuation>();
        }

        public static bool CanContinue(this Continuation item, bool isFailureHandler = false)
        {
            if (item.Children.Any()) // Is this a group?
            {
                var noOfSuccessfulChildren = item.Children.Count(c => c.CanContinue());

                if (noOfSuccessfulChildren == item.Children.Count())
                    return true;

                // We come here if we've got at least one failing child.
                // First make sure we can continue if we run our failure handlers.
                if (!item.ContinueAfterHandlingFailure) return false;

                // Have they all failed?
                if (noOfSuccessfulChildren == 0)
                {
                    return (item.OnAllFailed == null || item.OnAllFailed.CanContinue(true)) &&
                           (item.OnAnyFailed == null || item.OnAnyFailed.CanContinue(true));
                }

                return item.OnAnyFailed == null || item.OnAnyFailed.CanContinue(true);
            }

            // If this item is already completed we can continue if there's no next item or
            // next item can also continue.
            if (item.Status == JobStatus.Completed || item.Status == JobStatus.Cancelled)
            {
                return !isFailureHandler || item.Next == null || item.Next.CanContinue();
            }

            if (item.Status == JobStatus.Poisoned)
            {
                // We cannot continue if await does not handle failures                    
                return item.ContinueAfterHandlingFailure &&
                       (item.OnAllFailed == null || item.OnAllFailed.CanContinue(true));
            }

            return false;
        }

        public static bool IsCancelled(this Continuation item)
        {
            if (item.Status == JobStatus.Cancelled)
                return true;

            if (item.Children.Any())
            {
                return item.Children.Any(c => c.IsCancelled());
            }

            return false;
        }

        public static Continuation Find(this Continuation @await, Job child, bool isValid = true)
        {
            if (@await == null) return null;

            if (!@await.Children.Any() && @await.Id == child.Id)
                return @await;

            var result = @await.OnAnyFailed.Find(child);
            result = result ?? @await.OnAllFailed.Find(child);
            result = result ?? @await.Next.Find(child);

            if (!isValid)
                result = result ?? @await.OnCancelled.Find(child);

            if (result != null)
                return result;

            return @await
                .Children
                .Select(c => c.Find(child, isValid))
                .FirstOrDefault(match => match != null);
        }
    }
}