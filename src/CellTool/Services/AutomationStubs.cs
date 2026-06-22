namespace CellTool.Services;

public interface IAutomationApiService
{
    Task StartAsync(int port, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IFileMonitorService
{
    Task StartAsync(string inputDirectory, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public class AutomationApiServiceStub : IAutomationApiService
{
    public Task StartAsync(int port, CancellationToken cancellationToken) =>
        throw new NotImplementedException("HTTP API automation is reserved for a later phase.");

    public Task StopAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException("HTTP API automation is reserved for a later phase.");
}

public class FileMonitorServiceStub : IFileMonitorService
{
    public Task StartAsync(string inputDirectory, CancellationToken cancellationToken) =>
        throw new NotImplementedException("File monitor automation is reserved for a later phase.");

    public Task StopAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException("File monitor automation is reserved for a later phase.");
}
