using Microsoft.Extensions.Logging;

namespace Websocket.Client.Reopen
{
    public class TestWebsocketClient : WebsocketClientBase
    {
        public TestWebsocketClient(IWebsocketLiteClient websocketLiteClient, ILogger logger) : base(websocketLiteClient,
            logger)
        {
        }
    }
}