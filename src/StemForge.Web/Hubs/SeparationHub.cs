using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StemForge.Web.Hubs;

public sealed class SeparationHub : Hub
{
    private static int _activeConnections = 0;
    private static bool _hasConnectedOnce = false;
    private static CancellationTokenSource? _shutdownCts;
    private static readonly object _lock = new();

    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SeparationHub> _logger;

    public SeparationHub(IHostApplicationLifetime lifetime, ILogger<SeparationHub> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _activeConnections);
            _hasConnectedOnce = true;

            if (_shutdownCts != null)
            {
                try
                {
                    _shutdownCts.Cancel();
                    _shutdownCts.Dispose();
                }
                catch { }
                _shutdownCts = null;
                _logger.LogInformation("瀏覽器已連線，已取消伺服器自動關閉倒數。");
            }
        }

        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        int remaining = Interlocked.Decrement(ref _activeConnections);
        await base.OnDisconnectedAsync(exception);

        // 當最後一個瀏覽器視窗/標籤頁關閉時 (剩餘連線 0 且曾連線過)
        if (remaining <= 0 && _hasConnectedOnce)
        {
            lock (_lock)
            {
                try
                {
                    _shutdownCts?.Cancel();
                    _shutdownCts?.Dispose();
                }
                catch { }

                _shutdownCts = new CancellationTokenSource();
                var token = _shutdownCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("====================================================================");
                        _logger.LogInformation("所有瀏覽器網頁已關閉！");
                        _logger.LogInformation("5 秒後自動退出伺服器以進入 Step 3 專案同步 (若重新開啟頁面則取消)...");
                        _logger.LogInformation("====================================================================");

                        await Task.Delay(5000, token);
                        if (!token.IsCancellationRequested)
                        {
                            _logger.LogInformation("正在優雅退出伺服器...");
                            _lifetime.StopApplication();
                        }
                    }
                    catch (TaskCanceledException) { }
                }, token);
            }
        }
    }
}
