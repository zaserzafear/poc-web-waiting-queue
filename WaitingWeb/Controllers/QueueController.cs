using Microsoft.AspNetCore.Mvc;
using WaitingWeb.Services;

namespace WaitingWeb.Controllers;

public class QueueController : Controller
{
    private readonly RedisQueueService _queueService;

    public QueueController(RedisQueueService queueService)
    {
        _queueService = queueService;
    }

    [HttpGet("{queueName}")]
    public async Task<IActionResult> EnterQueue(string queueName)
    {
        var requestId = Guid.NewGuid().ToString();

        bool canStart = await _queueService.TryEnterQueueAsync(queueName, requestId);

        if (canStart)
        {
            // ถ้า queue ว่าง เริ่มงานทันที
            return RedirectToAction("ProcessJob", new { queueName, requestId });
        }
        else
        {
            // อยู่ในคิวรอ → แสดงหน้า Waiting
            return RedirectToAction("Waiting", new { queueName, requestId });
        }
    }

    [HttpGet("waiting/{queueName}/{requestId}")]
    public async Task<IActionResult> Waiting(string queueName, string requestId)
    {
        var position = await _queueService.GetQueuePositionAsync(queueName, requestId);

        ViewBag.QueueName = queueName;
        ViewBag.RequestId = requestId;
        ViewBag.QueuePosition = position;

        return View();
    }

    [HttpGet("queue/process/{queueName}/{requestId}")]
    public IActionResult ProcessJob(string queueName, string requestId)
    {
        ViewBag.QueueName = queueName;
        ViewBag.RequestId = requestId;
        return View();
    }

    [HttpGet("queue/checkposition/{queueName}/{requestId}")]
    public async Task<IActionResult> CheckPosition(string queueName, string requestId)
    {
        var pos = await _queueService.GetQueuePositionAsync(queueName, requestId);
        var currentLength = await _queueService.GetQueueLengthAsync(queueName);
        var maxConcurrent = _queueService.GetMaxConcurrent(queueName);

        bool canStart = pos > 0 && pos <= maxConcurrent;

        return Json(new
        {
            position = pos,
            currentLength,
            maxConcurrent,
            canStart
        });
    }

    [HttpPost("queue/dequeue/{queueName}/{requestId}")]
    public async Task<IActionResult> Dequeue(string queueName, string requestId)
    {
        await _queueService.DequeueAsync(queueName, requestId);

        // หลังจากเสร็จสิ้น คุณจะเลือก redirect ไปหน้าไหนก็ได้
        // เช่นกลับไปหน้า Home หรือแสดงว่าเสร็จงานแล้ว
        return RedirectToAction("Finished", new { queueName });
    }

    [HttpGet("queue/finished/{queueName}")]
    public IActionResult Finished(string queueName)
    {
        ViewBag.QueueName = queueName;
        return View();
    }
}
