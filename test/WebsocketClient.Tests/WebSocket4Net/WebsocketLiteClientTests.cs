using WebSocket4Net;
using Xunit.Abstractions;

namespace Websocket.Client.WebSocket4Net
{
    public class WebsocketLiteClientTests : WebsocketLiteClientTestBase
    {
        private const int Port = 12004;

        public WebsocketLiteClientTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper, Port)
        {
        }

        protected override IWebsocketLiteClient CreateNewClient()
        {
            return new WebsocketLiteClient(Url, logger: Logger);
        }
    }
}