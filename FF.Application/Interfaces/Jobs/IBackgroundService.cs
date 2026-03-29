using System.Linq.Expressions;

namespace FF.Application.Interfaces.Jobs;

public interface IBackgroundJobService
{
    string Enqueue(Expression<Action> methodCall);
    string Enqueue<T>(Expression<Action<T>> methodCall);
    void AddOrUpdateRecurring(string jobId, Expression<Action> methodCall, string cronExpression);
    void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression);
}