using System;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;

namespace Websocket.Client
{
    public class ReopenWatcher
    {
        private readonly ILogger _logger;
        private readonly WebsocketClientBase _websocketClient;
        private bool _enable;
        private IDisposable? _reopenDisposable;

        public ReopenWatcher(WebsocketClientBase websocketClient, ILogger logger)
        {
            _websocketClient = websocketClient;
            _logger = logger;
        }

        public bool Enable
        {
            get => _enable;
            set
            {
                _enable = value;
                if (_enable)
                    Start();
                else
                    Stop();
            }
        }


        public void Start()
        {
            _logger.LogDebug("start watch.");
            if (!Enable) return;
            //var forceReset = false;
            _reopenDisposable?.Dispose();
            _reopenDisposable = Observable
                .Interval(TimeSpan.FromSeconds(1))
                .Subscribe(times =>
                {
                    if (Enable && (_websocketClient.AutoReopenOnKeepAliveTimeout ||
                                   _websocketClient.AutoReopenOnClosed))
                    {
                        _logger.LogDebug($"will try reopen {times}.");
                        _ = _websocketClient.TryReopen();
                    }
                    else
                    {
                        Stop();
                    }
                });
        }

        public void Stop()
        {
            _logger.LogDebug("stop watch.");
            _reopenDisposable?.Dispose();
            //_reopenDisposable = null;
        }
    }
}