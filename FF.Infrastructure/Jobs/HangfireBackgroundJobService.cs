using FF.Application.Interfaces.Jobs;
using Hangfire;
using System.Linq.Expressions;

namespace FF.Infrastructure.Jobs;

public class HangfireBackgroundJobService : IBackgroundJobService
{
    public string Enqueue(Expression<Action> methodCall)
        => BackgroundJob.Enqueue(methodCall);

    public string Enqueue<T>(Expression<Action<T>> methodCall)
        => BackgroundJob.Enqueue(methodCall);

    public void AddOrUpdateRecurring(string jobId, Expression<Action> methodCall, string cronExpression)
        => RecurringJob.AddOrUpdate(jobId, methodCall, cronExpression);

    public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression)
        => RecurringJob.AddOrUpdate(jobId, methodCall, cronExpression);
}