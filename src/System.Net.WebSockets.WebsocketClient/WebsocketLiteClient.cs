using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Websocket.Client;

namespace System.Net.WebSockets
{
    public class WebsocketLiteClient : WebsocketLiteClientBase<ClientWebSocket>
    {
        private readonly string _url;
        private bool _closeFlag;
        private ClientWebSocket _innerClientInternal;

        public WebsocketLiteClient(string url, InnerClientFactory<ClientWebSocket>? innerClientFactory = null,
            Encoding? encoding = null, ILogger? logger = null) : base(innerClientFactory, encoding, logger)
        {
            _url = url;
            _innerClientInternal = CreateNewInnerClient(url);
        }

        public override bool IsOpened => _innerClientInternal?.State == WebSocketState.Open;

        protected override ClientWebSocket InnerClientInternal => _innerClientInternal;

        protected override Task SendAsyncInternal(string text, CancellationToken cancellationToken)
        {
            var bytes = Encoding.GetBytes(text);
            return InnerClientInternal.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                cancellationToken);
        }

        protected override Task SendAsyncInternal(byte[] bytes, CancellationToken cancellationToken)
        {
            return InnerClientInternal.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true,
                cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) InnerClientInternal?.Dispose();
        }

        protected override async Task OpenAsyncInternal(CancellationToken cancellationToken)
        {
            var client = _innerClientInternal = CreateNewInnerClient(_url);

            await client.ConnectAsync(new Uri(_url), cancellationToken);
            _ = StartListen(client);
        }

        private async Task StartListen(ClientWebSocket client)
        {
            // define buffer here and reuse, to avoid more allocation
            const int chunkSize = 1024 * 8;
            // _listenCts = new CancellationTokenSource();
            // var token = _listenCts.Token;
            try
            {
                while (client.State == WebSocketState.Open)
                {
                    var buffer = new ArraySegment<byte>(new byte[chunkSize]);
                    WebSocketReceiveResult result;
                    using var ms = new MemoryStream();
                    do
                    {
                        result = await client.ReceiveAsync(buffer, CancellationToken.None);
                        // ReSharper disable once AssignNullToNotNullAttribute
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                    } while (!result.EndOfMessage);

                    ms.Seek(0, SeekOrigin.Begin);

                    Logger.LogTrace($"Received a {result.MessageType} message.");

                    if (_closeFlag)
                    {
                        Logger.LogWarning("正在关闭或已经关闭,停止监听,忽略消息...");
                        return;
                    }

                    if (!client.Equals(InnerClientInternal))
                    {
                        Logger.LogWarning("监听的客户端不等于当前客户端,停止监听,忽略消息...");
                        AbortInnerClient(client);
                        return;
                    }

                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Close:
                        {
                            Logger.LogTrace(
                                $"Received close message, Code: {result.CloseStatus}, Reason: {result.CloseStatusDescription}");

                            var closeCode = (CloseStatusCode?) (int?) result.CloseStatus ?? CloseStatusCode.NoStatus;
                            OnClosed(closeCode, result.CloseStatusDescription);
                            return;
                        }
                        case WebSocketMessageType.Text:
                        {
                            var message = Encoding.GetString(ms.ToArray());

                            OnMessage(message);
                            break;
                        }
                        case WebSocketMessageType.Binary:

                            OnMessage(ms.ToArray());
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                //state not open 
                Logger.LogTrace($"未知原因断开,当前状态{client.State}");
                if (_closeFlag)
                {
                    Logger.LogWarning("正在关闭或已经关闭,停止监听,不触发事件...");
                    return;
                }

                if (!client.Equals(InnerClientInternal))
                {
                    Logger.LogWarning("监听的客户端不等于当前客户端,停止监听,不触发事件...");
                    AbortInnerClient(client);
                    return;
                }

                OnClosed(CloseStatusCode.NoStatus, null);
            }
            catch (Exception e)
            {
                var msg = $"Receiving data error, Message: {e.Message}";
                Logger.LogError(msg, e);

                if (_closeFlag)
                {
                    Logger.LogWarning("正在关闭或已经关闭,忽略异常,不触发事件...");
                    return;
                }

                if (!client.Equals(InnerClientInternal))
                {
                    Logger.LogWarning("监听的客户端不等于当前客户端,忽略异常,不触发事件...");
                    AbortInnerClient(client);
                    return;
                }

                InnerClientInternal.Abort();
                OnError(e);
                OnClosed(CloseStatusCode.Away, msg);
            }
        }

        protected override async Task<bool> CloseAsyncInternal(CloseStatusCode closeStatusCode, string reason,
            CancellationToken cancellationToken)
        {
            _closeFlag = true;
            var client = InnerClientInternal;
            await client.CloseAsync((WebSocketCloseStatus) (int) closeStatusCode, reason, cancellationToken);
            return client.State == WebSocketState.Aborted ||
                   client.State == WebSocketState.Closed ||
                   client.State == WebSocketState.None;
        }

        protected sealed override ClientWebSocket CreateNewInnerClient(string url)
        {
            return InnerClientFactory?.Invoke(url) ?? new ClientWebSocket();
        }

        protected override void AbortInnerClient(ClientWebSocket client)
        {
            client.Abort(); //TODO  auto stop receive?
        }
    }
}