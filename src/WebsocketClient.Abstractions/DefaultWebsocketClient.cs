using Microsoft.Extensions.Logging;

namespace Websocket.Client
{
    public abstract class DefaultWebsocketClient : WebsocketClientBase
    {
        protected DefaultWebsocketClient(IWebsocketLiteClient websocketLiteClient, ILogger? logger = null) : base(
            websocketLiteClient, logger)
        {
        }
    }
}