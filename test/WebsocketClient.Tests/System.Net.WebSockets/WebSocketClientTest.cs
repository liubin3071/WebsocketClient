using System.Net.WebSockets;
using Xunit.Abstractions;

//using SuperWebSocket;

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
    }
}