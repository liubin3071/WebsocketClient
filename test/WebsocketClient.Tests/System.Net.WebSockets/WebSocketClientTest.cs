using System.Net.WebSockets;
using SuperWebSocket;
using Xunit.Abstractions;

namespace Websocket.Client.System.Net.WebSockets
{
    public class WebSocketClientTest : WebsocketClientTestBase
    {
        private const int Port = 12001;

        public WebSocketClientTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper, Port)
        {
        }

        protected override WebsocketClientBase CreateNewClient()
        {
            return new WebsocketClient(Url, logger: Logger);
        }

        protected override void RaiseErrorEvent(WebsocketClientBase client, WebSocketSession session)
        {
            session.Close();
        }
    }
}