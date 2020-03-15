using System.Text;
using Microsoft.Extensions.Logging;
using Websocket.Client;

namespace System.Net.WebSockets
{
    public class WebsocketClient : WebsocketClientBase
    {
        public WebsocketClient(string url,
            InnerClientFactory<ClientWebSocket>? innerClientFactory = null,
            string? key = null,
            Encoding? encoding = null,
            ILogger? logger = null)
            : base(new WebsocketLiteClient(url, innerClientFactory, encoding, logger), logger)
        {
        }
    }
}