using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracker.UdpTracker.Protocol;

namespace Tracker.UdpTracker.Service;

public sealed class UdpTrackerListener : BackgroundService
{
    private readonly UdpTrackerRequestHandler _handler;
    private readonly ConnectionIdManager _connectionIdManager;
    private readonly UdpTrackerOptions _options;
    private readonly ILogger<UdpTrackerListener> _logger;

    public UdpTrackerListener(
        UdpTrackerRequestHandler handler,
        ConnectionIdManager connectionIdManager,
        IOptions<UdpTrackerOptions> options,
        ILogger<UdpTrackerListener> logger)
    {
        _handler = handler;
        _connectionIdManager = connectionIdManager;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("UDP tracker is disabled.");
            return;
        }

        var bindAddress = IPAddress.TryParse(_options.BindAddress, out var parsedAddress)
            ? parsedAddress
            : IPAddress.Any;

        using var udpClient = new UdpClient(new IPEndPoint(bindAddress, _options.Port));
        udpClient.Client.ReceiveBufferSize = _options.ReceiveBufferSize;

        _logger.LogInformation("UDP tracker listening on {BindAddress}:{Port}.", bindAddress, _options.Port);

        var sweepTask = SweepConnectionIdsAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await udpClient.ReceiveAsync(stoppingToken);
                    _ = ProcessDatagramAsync(udpClient, result.Buffer, result.RemoteEndPoint, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogWarning(ex, "UDP socket error.");
                }
            }
        }
        finally
        {
            try
            {
                await sweepTask;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ProcessDatagramAsync(UdpClient udpClient, byte[] datagram, IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
    {
        var responseBuffer = ArrayPool<byte>.Shared.Rent(_options.MaxDatagramSize);
        try
        {
            var written = await _handler.HandleAsync(datagram, datagram.Length, remoteEndpoint, responseBuffer, cancellationToken);

            if (written > 0)
            {
                await udpClient.SendAsync(responseBuffer.AsMemory(0, written), remoteEndpoint, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing UDP datagram from {RemoteEndpoint}.", remoteEndpoint);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(responseBuffer);
        }
    }

    private async Task SweepConnectionIdsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.ConnectionIdSweepIntervalSeconds), stoppingToken);
                _connectionIdManager.Sweep();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sweeping UDP connection IDs.");
            }
        }
    }
}
