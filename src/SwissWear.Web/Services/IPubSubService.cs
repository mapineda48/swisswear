namespace SwissWear.Web.Services;

public interface IPubSubService
{
    Task PublishAsync(string channel, string message, CancellationToken cancellationToken = default);
    void Subscribe(string channel, Action<string> handler);
    void UnsubscribeAll();
}
