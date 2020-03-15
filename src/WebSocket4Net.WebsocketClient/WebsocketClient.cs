using System.Text;
using Microsoft.Extensions.Logging;
using Websocket.Client;

namespace WebSocket4Net
{
    public class WebsocketClient : WebsocketClientBase
    {
        public WebsocketClient(string url,
            InnerClientFactory<WebSocket>? innerClientFactory = null,
            string? key = null,
            Encoding? encoding = null,
            ILogger? logger = null)
            : base(new WebsocketLiteClient(url, innerClientFactory, encoding, logger), logger)
        {
        }
    }
}