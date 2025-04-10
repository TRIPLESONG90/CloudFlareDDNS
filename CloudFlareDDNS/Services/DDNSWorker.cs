
using CloudFlare.Client;
using CloudFlare.Client.Api.Authentication;
using CloudFlare.Client.Api.Zones.DnsRecord;
using CloudFlare.Client.Enumerators;
using CloudFlareDDNS.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;

namespace CloudFlareDDNS.Services
{
    public static class ExternalIpProviders
    {
        public static IEnumerable<string> Providers { get; }

        static ExternalIpProviders()
        {
            Providers = new List<string>
            {
                "https://ipecho.net/plain",
                "https://icanhazip.com/",
                "https://whatismyip.akamai.com",
                "https://tnx.nl/ip"
            };
        }
    }
    public class DDNSWorker
    {
        private readonly DDNSWorkerOptions _options;
        public DDNSWorkerOptions Options => _options;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        private Task? _workerTask;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public DDNSWorker(DDNSWorkerOptions options, ILogger logger)
        {
            _options = options;
            _logger = logger;
        }

        public void Start()
        {
            if (IsRunning) return;

            _logger.LogInformation($"Starting worker");
            _cts = new CancellationTokenSource();
            _workerTask = Task.Run(() => RunAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _logger.LogInformation($"Stopping worker");
            _cts?.Cancel();
        }
        private async Task<IPAddress> GetIpAddressAsync(CancellationToken cancellationToken)
        {
            IPAddress ipAddress = null;
            foreach (var provider in ExternalIpProviders.Providers)
            {
                if (ipAddress != null)
                {
                    break;
                }
                using(var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(provider, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var ip = await response.Content.ReadAsStringAsync(cancellationToken);
                    Regex.Replace(ip, @"\t|\n|\r", string.Empty);
                    ipAddress = IPAddress.Parse(ip);
                }
            }

            return ipAddress;
        }
        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var client = new CloudFlareClient(_options.Email, _options.ApiKey);
                    var externalIpAddress = await GetIpAddressAsync(token);
                    _logger.LogInformation("Got ip from external provider: {IP}", externalIpAddress?.ToString());

                    if (externalIpAddress == null)
                    {
                        _logger.LogError("All external IP providers failed to resolve the IP");
                        return;
                    }

                    var zones = (await client.Zones.GetAsync(cancellationToken: token)).Result;
                    _logger.LogInformation("Found the following zones : {@Zones}", zones.Select(x => x.Name));
                    
                    foreach (var zone in zones)
                    {
                        if(zone.Id != _options.ZoneId)
                        {
                            continue;
                        }
                        var records = (await client.Zones.DnsRecords.GetAsync(zone.Id,
                            new DnsRecordFilter { Type = DnsRecordType.A }, null, token)).Result;
                        _logger.LogDebug("Found the following 'A' records in zone '{Zone}': {@Records}", zone.Name, records.Select(x => x.Name));

                        foreach (var record in records)
                        {
                            if(record.Type != DnsRecordType.A)
                            {
                                continue;
                            }

                            if (record.Id != _options.RecordId)
                            {
                                continue;
                            }

                            if (record.Type == DnsRecordType.A && record.Content != externalIpAddress.ToString())
                            {
                                var modified = new ModifiedDnsRecord
                                {
                                    Type = DnsRecordType.A,
                                    Name = record.Name,
                                    Content = externalIpAddress.ToString(),
                                };
                                var updateResult =
                                    (await client.Zones.DnsRecords.UpdateAsync(zone.Id, record.Id, modified,
                                        token));

                                if (!updateResult.Success)
                                {
                                    _logger.LogError(
                                        "The following errors happened during update of record '{Record}' in zone '{Zone}': {@Error}",
                                        record.Name, zone.Name, updateResult.Errors);
                                }
                                else
                                {
                                    _logger.LogInformation(
                                        "Successfully updated record '{Record}' ip from '{PreviousIp}' to '{ExternalIpAddress}' in zone '{Zone}'",
                                        record.Name, record.Content, externalIpAddress.ToString(), zone.Name);
                                }
                            }
                            else
                            {
                                _logger.LogInformation(
                                    "The IP for record '{Record}' in zone '{Zone}' is already '{ExternalIpAddress}'",
                                    record.Name, zone.Name, externalIpAddress.ToString());
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected exception happened");
                }
                await Task.Delay(1000, token); // 취소 가능
            }

            _logger.LogInformation($"Stopped.");
        }
    }
}
