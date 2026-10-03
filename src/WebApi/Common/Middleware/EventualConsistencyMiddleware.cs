using SSW.VerticalSliceArchitecture.Common.Events;
using SSW.VerticalSliceArchitecture.Domain.Base.EventualConsistency;
using SSW.VerticalSliceArchitecture.Domain.Base.Interfaces;

namespace SSW.VerticalSliceArchitecture.Common.Middleware;

public class EventualConsistencyMiddleware
{
    public const string DomainEventsKey = "DomainEventsKey";

    private readonly RequestDelegate _next;

    public EventualConsistencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext, IDomainEventDispatcher dispatcher)
    {
        context.Response.OnCompleted(async () =>
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteInTransactionAsync(async () =>
            {
                await PublishEvents(context, dispatcher);
            }, null!);
        });

        await _next(context);
    }

    private static async Task PublishEvents(HttpContext context, IDomainEventDispatcher dispatcher)
    {
        try
        {
            if (context.Items.TryGetValue(DomainEventsKey, out var value) &&
                value is Queue<IDomainEvent> domainEvents)
            {
                // Not RequestAborted: this runs after the response is sent, and a client that
                // disconnects once it has its answer would cancel the handlers half way, leaving
                // the saved change without its follow-up (the team total never recalculated).
                while (domainEvents.TryDequeue(out var nextEvent))
                    await dispatcher.DispatchAsync(nextEvent, CancellationToken.None);
            }
        }
        // ReSharper disable once RedundantCatchClause
        catch (EventualConsistencyException)
        {
            // TODO: handle eventual consistency exception
            throw;
        }
    }
}
