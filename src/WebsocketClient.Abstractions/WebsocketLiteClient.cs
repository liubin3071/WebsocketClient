using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Websocket.Client.Events;
using Websocket.Client.Exceptions;

namespace Websocket.Client
{
    public abstract class WebsocketLiteClientBase<T> : WebsocketLiteClientBase, IWebsocketLiteClient<T> where T : class?
    {
        protected readonly InnerClientFactory<T>? InnerClientFactory;

        private bool _closing;

        /// <summary>
        ///     1: true; 0: false
        /// </summary>
        private int _opening;

        protected WebsocketLiteClientBase(InnerClientFactory<T>? innerClientFactory,
            Encoding? encoding,
            ILogger? logger) : base(encoding, logger)
        {
            InnerClientFactory = innerClientFactory;
        }

        // ReSharper disable once InconsistentNaming
        protected abstract T InnerClientInternal { get; set; }

        public T GetInnerClient()
        {
            return InnerClientInternal;
        }

        public override object InnerClient => InnerClientInternal;

        public override async Task EnsureCloseAsync(CloseStatusCode? closeStatusCode = null, string? reason = null,
            CancellationToken cancellationToken = default)
        {
            if (_opening == 0 && !_closing && IsOpened)
                //未正在打开 且 未正在关闭 且已打开
                try
                {
                    _closing = true;
                    using var register = cancellationToken.Register(() => AbortInnerClient(InnerClientInternal));
                    var closed = await CloseAsyncInternal(closeStatusCode ?? CloseStatusCode.Normal,
                        reason ?? string.Empty,
                        cancellationToken);
                    if (!closed) AbortInnerClient(InnerClientInternal);
                }
                catch (Exception)
                {
                    AbortInnerClient(InnerClientInternal);
                }
                finally
                {
                    _closing = false;
                }
            else
                AbortInnerClient(InnerClientInternal);
        }

        public override async Task OpenAsync(CancellationToken cancellationToken = default)
        {
            //1: true; 0: false
            if (Interlocked.CompareExchange(ref _opening, 1, 0) == 1)
                throw new InvalidOperationException();

            if (IsOpened) throw new InvalidOperationException();

            try
            {
                await OpenAsyncInternal(cancellationToken);
                if (!IsOpened)
                {
                    AbortInnerClient(InnerClientInternal);
                    throw new WebsocketException("Open failed, unknown reason.");
                }
            }
            catch (Exception e)
            {
                AbortInnerClient(InnerClientInternal);
                throw new WebsocketException("Open failed.", e);
            }
            finally
            {
                _opening = 0;
            }
        }


        protected abstract Task<bool> CloseAsyncInternal(CloseStatusCode closeStatusCode, string reason,
            CancellationToken cancellationToken);

        protected abstract T CreateNewInnerClient(string url);

        /// <summary>
        ///     close, remove event handler, dispose
        /// </summary>
        /// <param name="client"></param>
        protected abstract void AbortInnerClient(T client);
    }

    public abstract class WebsocketLiteClientBase : IWebsocketLiteClient
    {
        protected readonly ILogger Logger;

        protected WebsocketLiteClientBase(Encoding? encoding, ILogger? logger)
        {
            Logger = logger ?? NullLogger.Instance;
            Encoding = encoding ?? Encoding.UTF8;
        }

        public abstract object InnerClient { get; }
        public abstract bool IsOpened { get; }
        public Encoding Encoding { get; set; }
        public event EventHandler<MessageEventArgs>? MessageReceived;
        public event EventHandler<CloseEventArgs>? Closed;
        public event EventHandler<ErrorEventArgs>? Error;

        public abstract Task OpenAsync(CancellationToken cancellationToken = default);

        public abstract Task EnsureCloseAsync(CloseStatusCode? closeStatusCode = null, string? reason = null,
            CancellationToken cancellationToken = default);

        public async Task SendAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!IsOpened) throw new InvalidOperationException();
            try
            {
                await SendAsyncInternal(text, cancellationToken);
            }
            catch (Exception e)
            {
                throw new WebsocketException("Send Error.", e);
            }
        }


        public async Task SendAsync(byte[] bytes, CancellationToken cancellationToken = default)
        {
            if (!IsOpened) throw new InvalidOperationException();
            try
            {
                await SendAsyncInternal(bytes, cancellationToken);
            }
            catch (Exception e)
            {
                throw new WebsocketException("Send Error.", e);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected abstract Task OpenAsyncInternal(CancellationToken cancellationToken);

        protected virtual void OnClosed(CloseStatusCode closeStatusCode, string? reason)
        {
            Logger.LogTrace($"{nameof(OnClosed)}", $"Raise {nameof(Closed)} event.");
            Closed?.Invoke(this, new CloseEventArgs(closeStatusCode, reason));
        }

        protected virtual void OnError(Exception exception)
        {
            Error?.Invoke(this, new ErrorEventArgs(exception));
        }

        protected virtual void OnMessage(byte[] bytes)
        {
            MessageReceived?.Invoke(this, new MessageEventArgs(bytes));
        }

        protected virtual void OnMessage(string text)
        {
            MessageReceived?.Invoke(this, new MessageEventArgs(text));
        }

        protected abstract Task SendAsyncInternal(string text, CancellationToken cancellationToken);
        protected abstract Task SendAsyncInternal(byte[] bytes, CancellationToken cancellationToken);

        protected abstract void Dispose(bool disposing);
    }
}