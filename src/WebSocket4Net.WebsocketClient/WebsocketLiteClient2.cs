using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuperSocket.ClientEngine;
using Websocket.Client;

namespace WebSocket4Net
{
    public class WebsocketLiteClient : WebsocketLiteClientBase<WebSocket>
    {
        private readonly string _url;
        private TaskCompletionSource<bool>? _closeTaskSrc;
        private TaskCompletionSource<bool>? _openTaskSrc;

        public WebsocketLiteClient(string url, InnerClientFactory<WebSocket>? innerClientFactory = null,
            Encoding? encoding = null,
            ILogger? logger = null) : base(innerClientFactory, encoding, logger)
        {
            _url = url;
            InnerClientInternal = CreateNewInnerClient(_url);
        }

        public override bool IsOpened => InnerClientInternal?.State == WebSocketState.Open;

        protected sealed override WebSocket InnerClientInternal { get; set; }

        protected override async Task OpenAsyncInternal(CancellationToken cancellationToken)
        {
            if (_openTaskSrc != null) throw new InvalidOperationException(); //opening

            var openTaskSrc = _openTaskSrc = new TaskCompletionSource<bool>();
            var cancelAction = new Action<object>(tcs =>
            {
                ((TaskCompletionSource<bool>) tcs).TrySetCanceled(cancellationToken);
                _openTaskSrc = null;
            });
            using var register = cancellationToken.Register(cancelAction, openTaskSrc);

            var client = InnerClientInternal = CreateNewInnerClient(_url);
            try
            {
                await Task.Factory.StartNew(() => client.Open(), cancellationToken);
            }
            catch (Exception e)
            {
                AbortInnerClient(client);
                openTaskSrc.TrySetException(e);
                _openTaskSrc = null;
            }

            await openTaskSrc.Task.ConfigureAwait(false);
            _openTaskSrc = null;
        }

        protected override Task SendAsyncInternal(string text, CancellationToken cancellationToken)
        {
            return Task.Run(() => InnerClientInternal.Send(text), cancellationToken);
        }

        protected override Task SendAsyncInternal(byte[] bytes, CancellationToken cancellationToken)
        {
            return Task.Run(() => InnerClientInternal.Send(bytes, 0, bytes.Length), cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) InnerClientInternal?.Dispose();
        }

        protected override async Task<bool> CloseAsyncInternal(CloseStatusCode closeStatusCode, string reason,
            CancellationToken cancellationToken)
        {
            var closeTaskSrc = _closeTaskSrc = new TaskCompletionSource<bool>();
            var cancelAction = new Action<object>(tcs =>
            {
                ((TaskCompletionSource<bool>) tcs).TrySetCanceled(cancellationToken);
            });
            using (cancellationToken.Register(cancelAction, closeTaskSrc))
            {
                var client = InnerClientInternal;

                try
                {
                    await Task.Factory.StartNew(() => client.Close((int) closeStatusCode, reason),
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    AbortInnerClient(client);
                    closeTaskSrc.TrySetException(exception);
                }

                await closeTaskSrc.Task.ConfigureAwait(false);
                _closeTaskSrc = null;
                return client.State == WebSocketState.Closed || client.State == WebSocketState.None;
            }
        }

        protected sealed override WebSocket CreateNewInnerClient(string url)
        {
            var newClient = new WebSocket(url);
            newClient.Error += WsClient_Error;
            newClient.Opened += WsClient_Opened;
            newClient.Closed += WsClient_Closed;
            newClient.MessageReceived += WsClient_MessageReceived;
            newClient.DataReceived += WsClient_DataReceived;
            return newClient;
        }

        protected override void AbortInnerClient(WebSocket client)
        {
            client.Error -= WsClient_Error;
            client.Opened -= WsClient_Opened;
            client.Closed -= WsClient_Closed;
            client.MessageReceived -= WsClient_MessageReceived;
            client.DataReceived -= WsClient_DataReceived;
            client.Dispose();
        }

        private void WsClient_Opened(object sender, EventArgs e)
        {
            _closeTaskSrc?.TrySetException(new Exception("关闭期间收到Open事件."));
            _openTaskSrc?.TrySetResult(InnerClientInternal.State == WebSocketState.Open);
        }

        private void WsClient_Closed(object sender, EventArgs e)
        {
            Debug.Assert(InnerClientInternal.State == WebSocketState.Closed);

            if (_closeTaskSrc == null)
                OnClosed(CloseStatusCode.Normal, "closed event pass.");

            _openTaskSrc?.TrySetException(new Exception("打开期间收到Closed事件."));
            _closeTaskSrc?.TrySetResult(true);
            // if (_closeTaskSrc == null)
            // {
            //     _logger.LogTrace($"_closeTaskSrc == null");
            //     Closed?.Invoke(this, new CloseEventArgs(CloseStatusCode.Normal, "closed event."));
            // }
            // else
            // {
            //     if (InnerClientInternal.State != WebSocketState.Closed)
            //         ResetInnerClient();
            //     _closeTaskSrc?.TrySetResult(null);
            //     _closeTaskSrc = null;
            // }
        }

        private void WsClient_Error(object sender, ErrorEventArgs errorEventArgs)
        {
            _openTaskSrc?.TrySetException(new Exception("打开期间收到Error事件."));

            // if (_sendTextTaskSrc != null)
            // {
            //     _sendTextTaskSrc?.TrySetException(errorEventArgs.Exception);
            //     _sendTextTaskSrc = null;
            // }
            //
            // if (_sendBytesTaskSrc != null)
            // {
            //     _sendBytesTaskSrc?.TrySetException(errorEventArgs.Exception);
            //     _sendBytesTaskSrc = null;
            // }
            //
            // Error?.Invoke(this, new ErrorEventArgs(errorEventArgs.Exception));

            OnError(errorEventArgs.Exception);
        }

        private void WsClient_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            OnMessage(e.Message);
            //MessageReceived?.Invoke(this, new MessageEventArgs(e.Message));
        }

        private void WsClient_DataReceived(object sender, DataReceivedEventArgs e)
        {
            OnMessage(e.Data);
            //MessageReceived?.Invoke(this, new MessageEventArgs(e.Data));
        }
    }
}