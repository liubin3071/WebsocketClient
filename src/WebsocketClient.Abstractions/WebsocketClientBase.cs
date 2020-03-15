using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Websocket.Client.Events;
using Websocket.Client.Exceptions;

namespace Websocket.Client
{
    public abstract class WebsocketClientBase : IWebsocketClient
    {
        private readonly ILogger _logger;
        private readonly ISubject<MessageEventArgs> _messageSubject = new Subject<MessageEventArgs>();
        private readonly BroadcastBlock<MessageEventArgs> _msgQueue;

        /// <summary>
        ///     after open by user is true; after close by user is false;
        /// </summary>
        private bool _autoReopenEnable;

        private bool _autoReopenOnClosed;
        private bool _autoReopenOnKeepAliveTimeout;

        private Task? _closeTask;

        private DateTimeOffset _lastReceivedTime;

        private DateTimeOffset _lastReopenTime = DateTimeOffset.MinValue;
        private TaskCompletionSource<object?>? _openTaskSrc;
        private IDisposable? _reopenDisposable;


        protected WebsocketClientBase(IWebsocketLiteClient websocketLiteClient, ILogger? logger = null)
        {
            LiteClient = websocketLiteClient;
            _logger = logger ?? NullLogger.Instance;
            AutoReopenThrottleTimeSpan = DefaultAutoReopenThrottle;
            OpenTimeout = DefaultOpenTimeout;
            CloseTimeout = DefaultCloseTimeout;
            KeepAliveTimeout = DefaultKeepAliveTimeout;
            _msgQueue = new BroadcastBlock<MessageEventArgs>(args => args);
            var rxAct = new ActionBlock<MessageEventArgs>(args => _messageSubject.OnNext(args));
            var eventAct = new ActionBlock<MessageEventArgs>(args => MessageReceived?.Invoke(this, args));
            _msgQueue.LinkTo(rxAct);
            _msgQueue.LinkTo(eventAct);

            State = ReadyState.Closed;


            LiteClient.Closed += WebsocketLiteClientOnClosed;
            LiteClient.Error += WebsocketLiteClientOnError;
            LiteClient.MessageReceived += WebsocketLiteClientOnMessageReceived;
        }

        private static TimeSpan DefaultAutoReopenThrottle => TimeSpan.FromSeconds(10);
        private static TimeSpan DefaultKeepAliveTimeout => TimeSpan.FromSeconds(10);
        private static TimeSpan DefaultCloseTimeout => TimeSpan.FromSeconds(10);
        private static TimeSpan DefaultOpenTimeout => TimeSpan.FromSeconds(10);

        public IWebsocketLiteClient LiteClient { get; }

        public event EventHandler<CloseEventArgs>? Closed;

        public event EventHandler<CloseEventArgs>? Closing;

        public event EventHandler<ErrorEventArgs>? Error;

        public event EventHandler<MessageEventArgs>? MessageReceived;

        public event EventHandler<OpenedEventArgs>? Opened;

        public event EventHandler<OpeningEventArgs>? Opening;

        public event EventHandler<ReopenedEventArgs>? Reopened;

        public bool AutoReopenOnClosed
        {
            get => _autoReopenOnClosed;
            set
            {
                _autoReopenOnClosed = value;
                if (_autoReopenEnable)
                {
                    if (!_autoReopenOnClosed && !_autoReopenOnKeepAliveTimeout)
                        DeactivateReopen();
                    else
                        ActivateReopen();
                }
            }
        }

        public bool AutoReopenOnKeepAliveTimeout
        {
            get => _autoReopenOnKeepAliveTimeout;
            set
            {
                _autoReopenOnKeepAliveTimeout = value;
                if (_autoReopenEnable)
                {
                    if (!_autoReopenOnClosed && !_autoReopenOnKeepAliveTimeout)
                        DeactivateReopen();
                    else
                        ActivateReopen();
                }
            }
        }

        public TimeSpan KeepAliveTimeout { get; set; }

        public TimeSpan AutoReopenThrottleTimeSpan { get; set; }
        public TimeSpan OpenTimeout { get; set; }
        public TimeSpan CloseTimeout { get; set; }

        public Encoding Encoding
        {
            get => LiteClient.Encoding;
            set => LiteClient.Encoding = value;
        }

        public bool IsOpened => State == ReadyState.Open;
        public IObservable<MessageEventArgs> MessageObservable => _messageSubject.AsObservable();
        public ReadyState State { get; private set; }

        public async Task OpenAsync()
        {
            _logger.LogDebug($"{nameof(OpenAsync)} entry.");
            _autoReopenEnable = true;

            switch (State)
            {
                case ReadyState.Connecting:
                    _logger.LogWarning(
                        FormatLog($"The close operation is ignored, because the current state is {State}."));
                    _logger.LogInformation(FormatLog("Wait for the current open operation to complete."));
                    await OpenAsyncInternal();
                    break;
                case ReadyState.Open:
                    _logger.LogWarning(
                        FormatLog($"The close operation is ignored, because the current state is {State}."));
                    break;
                case ReadyState.Closing:
                    _logger.LogInformation("Wait to cancel the current closing operation.");
                    if (_closeTask != null) await _closeTask;

                    State = ReadyState.Connecting;
                    Opening?.Invoke(this, new OpeningEventArgs());
                    await OpenAsyncInternal();
                    OnOpened();
                    break;
                case ReadyState.Closed:
                    _logger.LogDebug($"Current state {State}");
                    State = ReadyState.Connecting;
                    Opening?.Invoke(this, new OpeningEventArgs());
                    await OpenAsyncInternal();
                    OnOpened();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            ActivateReopen();
            _logger.LogDebug($"{nameof(OpenAsync)} success exit.");
        }

        public async Task CloseAsync()
        {
            _logger.LogDebug($"{nameof(CloseAsync)} entry.");
            _autoReopenEnable = false;
            DeactivateReopen();
            var closeStatusCode = CloseStatusCode.Normal;
            var reason = "Normal closure";

            switch (State)
            {
                case ReadyState.Connecting:
                    _logger.LogWarning(
                        FormatLog($"The close operation is ignored, because the current state is {State}."));
                    _logger.LogInformation("Wait to cancel the current open operation.");
                    _openTaskSrc?.TrySetCanceled();
                    _openTaskSrc = null;

                    OnClosed(new CloseEventArgs(closeStatusCode, reason));
                    break;
                case ReadyState.Open:
                    State = ReadyState.Closing;
                    var eventArgs = new CloseEventArgs(closeStatusCode, reason);
                    Closing?.Invoke(this, eventArgs);
                    await CloseAsyncInternal(closeStatusCode, reason);
                    OnClosed(eventArgs);
                    break;
                case ReadyState.Closing:
                    //返回上次关闭任务完成
                    _logger.LogWarning(
                        FormatLog($"The close operation is ignored, because the current state is {State}."));
                    _logger.LogInformation(FormatLog("Wait for the current close operation to complete."));
                    //await CloseAsyncInternal(closeStatusCode, reason);
                    if (_closeTask != null) await _closeTask;
                    break;
                case ReadyState.Closed:
                    _logger.LogWarning(
                        FormatLog($"The close operation is ignored, because the current state is {State}."));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _logger.LogDebug($"{nameof(CloseAsync)} exit.");
        }

        private void WebsocketLiteClientOnMessageReceived(object sender, MessageEventArgs e)
        {
            OnMessageReceived(e);
        }

        private void WebsocketLiteClientOnError(object sender, ErrorEventArgs e)
        {
            OnCommunicatorError(e.GetException());
        }

        private void WebsocketLiteClientOnClosed(object sender, CloseEventArgs e)
        {
            OnCommunicatorClosed(e.CloseStatusCode, e.Reason);
        }

        private Task OpenAsyncInternal()
        {
            var openTaskSrc = _openTaskSrc;
            if (openTaskSrc != null) return openTaskSrc.Task;
            openTaskSrc = _openTaskSrc = new TaskCompletionSource<object?>();
            _logger.LogInformation(FormatLog("open starting..."));

            var openCts = new CancellationTokenSource(OpenTimeout);

            var openTask = LiteClient.OpenAsync(openCts.Token);


            openTask.ContinueWith(task =>
            {
                if (task.IsSuccessfully())
                {
                    openTaskSrc.TrySetResult(null);
                    return;
                }

                var msg = task.Status switch
                {
                    TaskStatus.Canceled => "Communicator open failed, because task was cancelled.",
                    TaskStatus.Faulted => "Communicator open failed, because exception.",
                    _ => string.Empty
                };

                var exception = ToWebsocketException(task.Exception, msg);
                task.Exception?.Handle(c => true);
                openTaskSrc.TrySetException(exception);
            }, CancellationToken.None);


            var continueTask = openTaskSrc.Task.ContinueWith(task =>
            {
                if (task.IsSuccessfully())
                {
                    _lastReceivedTime = DateTimeOffset.UtcNow;

                    ActivateReopen();
                    _logger.LogInformation(FormatLog("open success."));
                    _openTaskSrc = null;
                    return;
                }

                if (!openCts.IsCancellationRequested)
                    openCts.Cancel();

                if (State != ReadyState.Closed)
                    State = ReadyState.Closed;
                _openTaskSrc = null;

                var msg = string.Empty;
                if (task.IsCanceled)
                    msg = "Open failed, because task was cancelled.";
                else if (task.IsFaulted)
                    msg = "Open failed, because exception.";

                var wsException = ToWebsocketException(task.Exception, msg);
                _logger.LogError(FormatLog(msg), wsException);
                throw wsException;
            }, TaskContinuationOptions.ExecuteSynchronously);

            return continueTask;
        }

        private async Task CloseAsyncInternal(CloseStatusCode closeStatusCode, string reason)
        {
            _closeTask = LiteClient.EnsureCloseAsync(closeStatusCode, reason,
                new CancellationTokenSource(CloseTimeout).Token);
            await _closeTask;
            _closeTask = null;
        }

        private static WebsocketException ToWebsocketException(AggregateException? exception, string message)
        {
            var inner = exception?.InnerExceptions.Count == 1
                ? exception.InnerExceptions.Single()
                : exception;
            // if (inner != null)
            //     message = $"{message} {inner.GetType()} Message: '{inner.Message}'";
            if (inner != null)
                message = $"{message} Type: {inner.GetType().FullName}, Message: {inner.Message}";
            return new WebsocketException(message, inner);
        }


        private void OnClosed(CloseEventArgs args)
        {
            _logger.LogDebug(FormatLog($"{nameof(OnClosed)}, {args.Reason}"));
            State = ReadyState.Closed;
            Closed?.Invoke(this, args);
        }

        protected virtual string FormatLog(string msg)
        {
            return $"[ws {State} {DateTimeOffset.Now:s}] {msg}";
        }

        private void OnError(Exception exception)
        {
            _logger.LogError(FormatLog($"{nameof(OnError)}, {exception.GetType()} {exception.Message}"), exception);
            var args = new ErrorEventArgs(exception);
            Error?.Invoke(this, args);
        }

        protected virtual void OnMessageReceived(MessageEventArgs e)
        {
            _lastReceivedTime = DateTimeOffset.UtcNow;
            _msgQueue.Post(e);
        }

        private void OnOpened()
        {
            _logger.LogDebug(FormatLog($"{nameof(OnOpened)}"));
            State = ReadyState.Open;
            Opened?.Invoke(this, new OpenedEventArgs());
        }


        protected virtual void OnCommunicatorClosed(CloseStatusCode? closeStatusCode, string? reason)
        {
            var msg = $"{nameof(OnCommunicatorClosed)}, " +
                      $"{nameof(CloseEventArgs.CloseStatusCode)}: {closeStatusCode}; " +
                      $"{nameof(CloseEventArgs.Reason)}: {reason ?? "null"}.";
            _logger.LogDebug(FormatLog(msg));
            OnClosed(new CloseEventArgs(closeStatusCode, reason));
        }

        protected virtual void OnCommunicatorError(Exception exception)
        {
            var msg = $"{nameof(OnCommunicatorError)}, {exception.GetType()} Message: {exception.Message}";
            OnError(new WebsocketException(msg, exception));
        }

        private async Task TryReopen()
        {
            if (!_autoReopenEnable || !_autoReopenOnKeepAliveTimeout && !_autoReopenOnClosed)
            {
                DeactivateReopen();
                return;
            }

            if (AutoReopenThrottleTimeSpan > TimeSpan.Zero)
            {
                var diff = _lastReopenTime + AutoReopenThrottleTimeSpan - DateTimeOffset.UtcNow;
                if (diff > TimeSpan.Zero)
                {
                    _logger.LogWarning(
                        $"last reopen at {_lastReopenTime:s}, Next attempt in {Math.Round(diff.TotalSeconds)} seconds.");
                    return;
                }
            }

            if (_autoReopenOnClosed && !LiteClient.IsOpened)
                try
                {
                    _logger.LogInformation(FormatLog("检测到当前状态异常, 将自动重新连接..."));
                    _lastReopenTime = DateTimeOffset.UtcNow;
                    DeactivateReopen();
                    await CloseAsyncInternal(CloseStatusCode.Away, "状态异常, reopen.");
                    await OpenAsyncInternal();
                    State = ReadyState.Open;
                    Reopened?.Invoke(this, new ReopenedEventArgs());
                    return;
                }
                catch (Exception e)
                {
                    var msg = $"Reopen failed(state closed), {e.Message}";
                    _logger.LogError(FormatLog(msg), e);
                }
                finally
                {
                    ActivateReopen();
                }

            if (_autoReopenOnKeepAliveTimeout && KeepAliveTimeout > TimeSpan.Zero)
            {
                var timeout = DateTimeOffset.UtcNow - _lastReceivedTime > KeepAliveTimeout;
                if (timeout)
                    try
                    {
                        _logger.LogInformation(FormatLog("检测到接收消息超时, 将自动重新连接..."));
                        _lastReopenTime = DateTimeOffset.UtcNow;
                        DeactivateReopen();
                        await CloseAsyncInternal(CloseStatusCode.Away, "keep alive timeout.");
                        await OpenAsyncInternal();
                        State = ReadyState.Open;
                        OnReopened(new ReopenedEventArgs());
                    }
                    catch (Exception e)
                    {
                        var msg = $"Reopen(keep live timeout) failed, {e.Message}";
                        _logger.LogError(FormatLog(msg), e);
                    }
                    finally
                    {
                        ActivateReopen();
                    }
            }
        }

        private void ActivateReopen(bool forceReset = false)
        {
            if (!_autoReopenEnable) return;
            if (_reopenDisposable == null || forceReset)
                _reopenDisposable = Observable
                    .Interval(TimeSpan.FromSeconds(1))
                    .Subscribe(l =>
                    {
                        _logger.LogDebug(FormatLog($"will try reopen {l}."));
                        _ = TryReopen();
                    });
        }


        private void DeactivateReopen()
        {
            _logger.LogDebug(FormatLog($"{nameof(DeactivateReopen)}"));
            _reopenDisposable?.Dispose();
            _reopenDisposable = null;
        }

        private void OnReopened(ReopenedEventArgs args)
        {
            _logger.LogDebug(FormatLog($"{nameof(OnReopened)}"));
            State = ReadyState.Open;
            Reopened?.Invoke(this, args);
        }


        #region send

        public async Task SendAsync(string text, CancellationToken cancellationToken = default)
        {
            if (State != ReadyState.Open) throw new InvalidOperationException();
            try
            {
                await LiteClient.SendAsync(text, cancellationToken);
                _logger.LogTrace("send success.");
            }
            catch (Exception e)
            {
                var msg = $"Send error, Message: {e.Message}";
                _logger.LogError(FormatLog(msg), e);
                throw new WebsocketException(msg, e);
            }
        }

        public async Task SendAsync(byte[] bytes, CancellationToken cancellationToken = default)
        {
            if (State != ReadyState.Open) throw new InvalidOperationException();
            try
            {
                await LiteClient.SendAsync(bytes, cancellationToken);
                _logger.LogTrace("send success.");
            }
            catch (Exception e)
            {
                var msg = $"Send error, Message: {e.Message}";
                _logger.LogError(FormatLog(msg), e);
                throw new WebsocketException(msg, e);
            }
        }

        #endregion send

        #region IDisposable Support

        private bool _disposedValue; // 要检测冗余调用


        // 添加此代码以正确实现可处置模式。
        public void Dispose()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(true);
            // TO DO: 如果在以上内容中替代了终结器，则取消注释以下行。
            // GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                    // TO DO: 释放托管状态(托管对象)。
                    _reopenDisposable?.Dispose();

                // TO DO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TO DO: 将大型字段设置为 null。

                _disposedValue = true;
            }
        }

        // TO DO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~WebsocketClientBase2()
        // {
        //   // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
        //   Dispose(false);
        // }

        #endregion IDisposable Support
    }
}