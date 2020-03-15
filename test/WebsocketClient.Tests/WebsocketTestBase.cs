using System;
using System.Linq;
using System.Threading.Tasks;
using Divergic.Logging.Xunit;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using SuperSocket.SocketBase.Logging;
using SuperWebSocket;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Websocket.Client
{
    public abstract class WebsocketTestBase : IDisposable
    {
        private const string Host = "ws://127.0.0.1";
        private readonly int _port;

        protected readonly ICacheLogger Logger;
        //private const int Port = 10086;

        protected readonly ITestOutputHelper TestOutputHelper;

        protected WebsocketTestBase(ITestOutputHelper testOutputHelper, int port)
        {
            TestOutputHelper = testOutputHelper;
            _port = port;
            Logger = testOutputHelper.BuildLogger();
            ResetServer();
        }


        protected WebSocketServer Server { get; private set; }

        public string Url => $"{Host}:{_port}/websocket";

        public void Dispose()
        {
            TestOutputHelper.WriteLine("Dispose.................................................................");
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void ResetServer()
        {
            try
            {
                Server?.Stop();
            }
            catch
            {
                // ignored
            }
            finally
            {
                Server?.Dispose();
                Server = null;
            }

            Server = CreateServer(_port);

            Server.NewDataReceived += ServerOnNewDataReceived;
            Server.NewMessageReceived += ServerOnNewMessageReceived;
            Server.NewSessionConnected += ServerOnNewSessionConnected;
            Server.SessionClosed += ServerOnSessionClosed;

            for (var i = 0; i < 3; i++)
                if (Server.Start())
                {
                    TestOutputHelper.WriteLine($"server start success. Url: {Url}.");
                    return;
                }

            throw new XunitException("server start failed.");
        }

        public async Task<WebSocketSession> GetLastSession()
        {
            while (true)
            {
                var last = Server.GetAllSessions().LastOrDefault();
                if (last != null) return last;
                await Task.Delay(100);
            }
        }

        private static WebSocketServer CreateServer(int port, string password = "", string security = "",
            string certificateFile = "")
        {
            var webSocketServer = new WebSocketServer();

            webSocketServer.Setup(new ServerConfig
            {
                Port = port,
                Ip = "Any",
                MaxConnectionNumber = 100,
                Mode = SocketMode.Tcp,
                Name = "SuperWebSocket Server",
                Security = security,
                LogAllSocketException = true,
                Certificate = new CertificateConfig {FilePath = certificateFile, Password = password}
            }, logFactory: new ConsoleLogFactory());

            return webSocketServer;
        }

        private void ServerOnNewSessionConnected(WebSocketSession session)
        {
        }

        private void ServerOnSessionClosed(WebSocketSession session, CloseReason value)
        {
        }

        private void ServerOnNewMessageReceived(WebSocketSession session, string value)
        {
            //echo
            session.Send(value);
        }

        private void ServerOnNewDataReceived(WebSocketSession session, byte[] value)
        {
            //Echo
            session.Send(new ArraySegment<byte>(value, 0, value.Length));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Server.Stop();
                Server?.Dispose();
            }
        }
    }
}