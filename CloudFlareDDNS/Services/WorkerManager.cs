using CloudFlareDDNS.Models;

namespace CloudFlareDDNS.Services
{

    public class WorkerManager : IHostedService
    {
        private readonly List<DDNSWorker> _workers = new();
        private readonly ILoggerFactory _loggerFactory;

        public WorkerManager(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public void AddWorker(DDNSWorkerOptions options)
        {
            var logger = _loggerFactory.CreateLogger<DDNSWorker>();
            var worker = new DDNSWorker(options, logger);
            _workers.Add(worker);
        }

        public void StartWorker(DDNSWorker worker)
        {
            worker.Start();
        }

        public void StopWorker(DDNSWorker worker)
        {
            worker.Stop();
        }
        public void RemoveWorker(DDNSWorker worker)
        {
            _workers.Remove(worker);
        }   

        public IEnumerable<DDNSWorker> GetAllWorkers() => _workers;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var worker in _workers)
                worker.Stop();

            return Task.CompletedTask;
        }
    }
}
