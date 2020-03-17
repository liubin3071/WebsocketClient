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
        private readonly ReopenWatcher _reopenWatcher;

        private bool _autoReopenOnClosed;
        private bool _autoReopenOnKeepAliveTimeout;

        private Task? _closeTask;

        private DateTimeOffset _lastReceivedTime;

        private DateTimeOffset _lastReopenTime = DateTimeOffset.MinValue;
        private TaskCompletionSource<object?>? _openTaskSrc;


        private bool _reopening;

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

            _reopenWatcher = new ReopenWatcher(this, _logger);
        }

        private static TimeSpan DefaultAutoReopenThrottle => TimeSpan.FromSeconds(10);
        private static TimeSpan DefaultKeepAliveTimeout => TimeSpan.FromSeconds(10);
        private static TimeSpan DefaultCloseTimeout => TimeSpan.FromSeconds(10);
        private static TimeSpan DefaultOpenTimeout => TimeSpan.FromSeconds(10);

        public IWebsocketLiteClient LiteClient { get; }

        public virtual event EventHandler<CloseEventArgs>? Closed;

        public event EventHandler<CloseEventArgs>? Closing;

        public virtual event EventHandler<ErrorEventArgs>? Error;

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
                if (_reopenWatcher.Enable)
                {
                    if (!_autoReopenOnClosed && !_autoReopenOnKeepAliveTimeout)
                        _reopenWatcher.Stop();
                    else
                        _reopenWatcher.Start();
                }
            }
        }

        public bool AutoReopenOnKeepAliveTimeout
        {
            get => _autoReopenOnKeepAliveTimeout;
            set
            {
                _autoReopenOnKeepAliveTimeout = value;
                if (_reopenWatcher.Enable)
                {
                    if (!_autoReopenOnClosed && !_autoReopenOnKeepAliveTimeout)
                        _reopenWatcher.Stop();
                    else
                        _reopenWatcher.Start();
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

            _reopenWatcher.Enable = true;
            _logger.LogDebug($"{nameof(OpenAsync)} success exit.");
        }

        public async Task CloseAsync()
        {
            _logger.LogDebug($"{nameof(CloseAsync)} entry.");
            _reopenWatcher.Enable = false;
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void WebsocketLiteClientOnMessageReceived(object sender, MessageEventArgs e)
        {
            OnMessageReceived(e);
        }

        private void WebsocketLiteClientOnError(object sender, ErrorEventArgs e)
        {
            OnError(e);
        }

        private void WebsocketLiteClientOnClosed(object sender, CloseEventArgs e)
        {
            OnClosed(e);
        }

        protected internal virtual Task OpenAsyncInternal()
        {
            var openTaskSrc = _openTaskSrc;
            if (openTaskSrc != null)
            {
                _logger.LogInformation("open task is running, wait...");
                return openTaskSrc.Task;
            }

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

                    //ActivateReopen();
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

        private void OnError(ErrorEventArgs args)
        {
            OnError(args.GetException());
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

        protected internal virtual async Task<bool> TryReopenAsyncInternal()
        {
            try
            {
                _lastReopenTime = DateTimeOffset.UtcNow;
                await CloseAsyncInternal(CloseStatusCode.Away, "reopen.");
                await OpenAsyncInternal();
                OnReopened(new ReopenedEventArgs());
            }
            catch (Exception e)
            {
                var msg = $"Reopen failed, Message: {e.Message}";
                _logger.LogError(FormatLog(msg), e);
            }

            return State == ReadyState.Open;
        }

        protected internal virtual async Task TryReopen()
        {
            if (AutoReopenThrottleTimeSpan > TimeSpan.Zero)
            {
                var diff = _lastReopenTime + AutoReopenThrottleTimeSpan - DateTimeOffset.UtcNow;
                if (diff > TimeSpan.Zero)
                {
                    _logger.LogWarning(
                        $"last reopen at {_lastReopenTime:s}, Next attempt in {Math.Ceiling(diff.TotalSeconds)} seconds.");
                    return;
                }
            }

            try
            {
                if (_reopening) return;
                _reopening = true;
                _logger.LogDebug("reopening...");


                if (_autoReopenOnClosed)
                {
                    if (!LiteClient.IsOpened)
                    {
                        _logger.LogWarning(FormatLog("will try reopen, current state is not opened."));
                        await TryReopenAsyncInternal();
                        return;
                    }

                    _logger.LogDebug("skip reopen, current state is opened.");
                }

                if (_autoReopenOnKeepAliveTimeout && KeepAliveTimeout > TimeSpan.Zero)
                {
                    var timeout = DateTimeOffset.UtcNow - _lastReceivedTime > KeepAliveTimeout;
                    if (timeout)
                    {
                        _logger.LogWarning(FormatLog(
                            $"will try reopen, message keep live timeout({KeepAliveTimeout:g}), LastReceivedTime: {_lastReceivedTime:s}"));
                        await TryReopenAsyncInternal();
                        return;
                    }

                    _logger.LogDebug($"skip reopen, keep live, LastReceivedTime: {_lastReceivedTime:s}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                _reopening = false;
            }
        }

        private void OnReopened(ReopenedEventArgs args)
        {
            _logger.LogDebug(FormatLog($"{nameof(OnReopened)}"));
            State = ReadyState.Open;
            Reopened?.Invoke(this, args);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reopenWatcher.Stop();
                LiteClient.Dispose();
            }
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

        #endregion IDisposable Support
    }
}