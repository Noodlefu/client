using LaciSynchroni.Services.Mediator;
using LaciSynchroni.SyncConfiguration.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace LaciSynchroni.WebAPI.SignalR.Utils;

public class ForeverRetryPolicy : IRetryPolicy
{
    private readonly SyncMediator _mediator;
    private readonly Guid _serverUuid;
    private bool _sentDisconnected = false;

    public ForeverRetryPolicy(SyncMediator mediator, Guid serverUuid)
    {
        _mediator = mediator;
        _serverUuid = serverUuid;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        TimeSpan timeToWait = TimeSpan.FromSeconds(new Random().Next(10, 20));
        if (retryContext.PreviousRetryCount == 0)
        {
            _sentDisconnected = false;
            timeToWait = TimeSpan.FromSeconds(3);
        }
        else if (retryContext.PreviousRetryCount == 1) timeToWait = TimeSpan.FromSeconds(5);
        else if (retryContext.PreviousRetryCount == 2) timeToWait = TimeSpan.FromSeconds(10);
        else
        {
            if (!_sentDisconnected)
            {
                _mediator.Publish(new NotificationMessage("Connection lost", "Connection lost to server", NotificationType.Warning, TimeSpan.FromSeconds(10)));
                _mediator.Publish(new DisconnectedMessage(_serverUuid));
            }
            _sentDisconnected = true;
        }

        return timeToWait;
    }
}