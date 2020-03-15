using SuperWebSocket;
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

        protected override void RaiseErrorEvent(WebsocketClientBase client, WebSocketSession session)
        {
            var badRequest = new BadRequest();
            badRequest.ExecuteCommand((WebSocket) client.LiteClient.InnerClient,
                new WebSocketCommandInfo(0x9.ToString(), ""));
        }
    }
}