using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace WaitingWeb.Services;

public class RedisQueueService
{
    private readonly IDatabase _db;
    private readonly QueueManagementOptions _options;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);

    public RedisQueueService(IConnectionMultiplexer redis, IOptions<QueueManagementOptions> options)
    {
        _db = redis.GetDatabase();
        _options = options.Value;
    }

    private string GetQueueKey(string queueName) => $"queue:{queueName}";

    public async Task<bool> TryEnterQueueAsync(string queueName, string requestId)
    {
        var key = GetQueueKey(queueName);

        // ลบ expired ก่อน
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, now);

        // เพิ่ม request พร้อม expire time
        var expireAt = now + (long)MaxDuration.TotalSeconds;
        await _db.SortedSetAddAsync(key, requestId, expireAt);

        // ตรวจจำนวน
        var length = await _db.SortedSetLengthAsync(key);

        // ดึง MaxConcurrent จาก config
        int maxConcurrent = _options.QueueManagement.ContainsKey(queueName)
            ? _options.QueueManagement[queueName]
            : 1; // default ถ้าไม่มี config

        return length <= maxConcurrent;
    }

    public async Task ExitQueueAsync(string queueName, string requestId)
    {
        var key = GetQueueKey(queueName);
        await _db.SortedSetRemoveAsync(key, requestId);
    }

    public async Task<int> GetQueuePositionAsync(string queueName, string requestId)
    {
        var key = GetQueueKey(queueName);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, now);

        var rank = await _db.SortedSetRankAsync(key, requestId);
        return rank.HasValue ? (int)rank.Value + 1 : -1;
    }

    public async Task DequeueAsync(string queueName, string requestId)
    {
        var key = GetQueueKey(queueName);
        await _db.SortedSetRemoveAsync(key, requestId);
    }
}
