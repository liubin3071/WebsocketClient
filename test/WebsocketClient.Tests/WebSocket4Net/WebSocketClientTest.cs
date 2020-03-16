using Fleck;
using WebSocket4Net;
using WebSocket4Net.Command;
using Xunit.Abstractions;

namespace Websocket.Client.WebSocket4Net
{
    public class WebSocketClientTest : WebsocketClientTestBase
    {
        private const int Port = 12003;

        public WebSocketClientTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper, Port)
        {
        }

        protected override WebsocketClientBase CreateNewClient()
        {
            return new WebsocketClient(Url, logger: Logger);
        }

        protected override void RaiseErrorEvent(WebsocketClientBase client, IWebSocketConnection session)
        {
            //fix: WebSocket4Net do not raise error event when server closed the WebSocket connection without completing the close handshake.
            var badRequest = new BadRequest();
            badRequest.ExecuteCommand((WebSocket) client.LiteClient.InnerClient,
                new WebSocketCommandInfo(0x9.ToString(), ""));
        }
    }
}