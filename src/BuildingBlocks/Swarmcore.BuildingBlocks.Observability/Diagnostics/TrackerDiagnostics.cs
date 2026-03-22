using System.Diagnostics.Metrics;

namespace Swarmcore.BuildingBlocks.Observability.Diagnostics;

public static class TrackerDiagnostics
{
    public const string MeterName = "Swarmcore.Tracker";

    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> TelemetryDropped = Meter.CreateCounter<long>("tracker.telemetry.dropped");

    public static readonly Counter<long> AnnounceTotal = Meter.CreateCounter<long>("tracker.announce.total");
    public static readonly Counter<long> AnnounceDenied = Meter.CreateCounter<long>("tracker.announce.denied");
    public static readonly Counter<long> ScrapeTotal = Meter.CreateCounter<long>("tracker.scrape.total");
    public static readonly Counter<long> ScrapeDenied = Meter.CreateCounter<long>("tracker.scrape.denied");
    public static readonly Counter<long> UdpConnectTotal = Meter.CreateCounter<long>("tracker.udp.connect.total");
    public static readonly Counter<long> UdpAnnounceTotal = Meter.CreateCounter<long>("tracker.udp.announce.total");
    public static readonly Counter<long> UdpScrapeTotal = Meter.CreateCounter<long>("tracker.udp.scrape.total");
    public static readonly Counter<long> UdpErrorTotal = Meter.CreateCounter<long>("tracker.udp.error.total");
    public static readonly Counter<long> CacheHit = Meter.CreateCounter<long>("tracker.cache.hit");
    public static readonly Counter<long> CacheMiss = Meter.CreateCounter<long>("tracker.cache.miss");
    public static readonly Counter<long> AbuseThrottled = Meter.CreateCounter<long>("tracker.abuse.throttled");
    public static readonly Counter<long> RequestMalformed = Meter.CreateCounter<long>("tracker.request.malformed");
    public static readonly Counter<long> RequestParseFailed = Meter.CreateCounter<long>("tracker.request.parse_failed");
    public static readonly Counter<long> RequestValidationFailed = Meter.CreateCounter<long>("tracker.request.validation_failed");
    public static readonly Histogram<double> AnnounceDuration = Meter.CreateHistogram<double>("tracker.announce.duration", unit: "ms");
    public static readonly Histogram<double> ScrapeDuration = Meter.CreateHistogram<double>("tracker.scrape.duration", unit: "ms");

    public static void RegisterSwarmStoreGauges(Func<long> peersCallback, Func<long> swarmsCallback)
    {
        Meter.CreateObservableGauge("tracker.peers.active", peersCallback);
        Meter.CreateObservableGauge("tracker.swarms.active", swarmsCallback);
    }

    public static void RegisterTelemetryQueueGauge(Func<int> queueLengthCallback)
    {
        Meter.CreateObservableGauge("tracker.telemetry.queue_length", () => queueLengthCallback());
    }
}
