using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace WaitingWeb.Services;

public class RedisQueueService
{
    private readonly IDatabase _db;
    private readonly QueueManagementOptions _options;
    private readonly ILogger<RedisQueueService> _logger;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);

    public RedisQueueService(
        IConnectionMultiplexer redis,
        IOptions<QueueManagementOptions> options,
        ILogger<RedisQueueService> logger)
    {
        _db = redis.GetDatabase();
        _options = options.Value;
        _logger = logger;
    }

    private string GetQueueKey(string queueName) => $"queue:{queueName}";

    public async Task<bool> TryEnterQueueAsync(string queueName, string requestId)
    {
        var key = GetQueueKey(queueName);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _logger.LogInformation("Trying to enter queue '{Queue}' with requestId {RequestId}", queueName, requestId);

        // ลบ expired ก่อน
        var removedCount = await _db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, now);
        if (removedCount > 0)
            _logger.LogInformation("Removed {Count} expired items from queue '{Queue}'", removedCount, queueName);

        // เพิ่ม request พร้อม expire time
        var expireAt = now + (long)MaxDuration.TotalSeconds;
        await _db.SortedSetAddAsync(key, requestId, expireAt);
        _logger.LogInformation("Added requestId {RequestId} to queue '{Queue}' with expire at {ExpireAt}", requestId, queueName, expireAt);

        // ตรวจจำนวน
        var length = await _db.SortedSetLengthAsync(key);
        _logger.LogInformation("Current length of queue '{Queue}': {Length}", queueName, length);

        // ดึง MaxConcurrent จาก config
        int maxConcurrent = _options.QueueManagement.ContainsKey(queueName)
            ? _options.QueueManagement[queueName]
            : 1; // default
        _logger.LogInformation("MaxConcurrent for queue '{Queue}': {Max}", queueName, maxConcurrent);

        bool canStart = length <= maxConcurrent;
        _logger.LogInformation("RequestId {RequestId} can start? {CanStart}", requestId, canStart);

        return canStart;
    }

    public async Task ExitQueueAsync(string queueName, string requestId)
    {
        var key = GetQueueKey(queueName);
        _logger.LogInformation("Exiting queue '{Queue}' requestId {RequestId}", queueName, requestId);
        await _db.SortedSetRemoveAsync(key, requestId);
    }

    public async Task<int> GetQueuePositionAsync(string queueName, string requestId)
    {
        var key = GetQueueKey(queueName);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _logger.LogInformation("Checking queue position for requestId {RequestId} in queue '{Queue}'", requestId, queueName);

        // ลบ expired
        var removedCount = await _db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, now);
        if (removedCount > 0)
            _logger.LogInformation("Removed {Count} expired items from queue '{Queue}'", removedCount, queueName);

        var rank = await _db.SortedSetRankAsync(key, requestId);
        int position = rank.HasValue ? (int)rank.Value + 1 : -1;

        _logger.LogInformation("Queue position for requestId {RequestId} in queue '{Queue}': {Position}", requestId, queueName, position);

        return position;
    }

    public async Task DequeueAsync(string queueName, string requestId)
    {
        var key = GetQueueKey(queueName);
        _logger.LogInformation("Dequeuing requestId {RequestId} from queue '{Queue}'", requestId, queueName);
        await _db.SortedSetRemoveAsync(key, requestId);
    }

    public async Task<long> GetQueueLengthAsync(string queueName)
    {
        var key = GetQueueKey(queueName);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, now);
        return await _db.SortedSetLengthAsync(key);
    }

    public int GetMaxConcurrent(string queueName)
    {
        return _options.QueueManagement.ContainsKey(queueName)
            ? _options.QueueManagement[queueName]
            : 1;
    }
}
