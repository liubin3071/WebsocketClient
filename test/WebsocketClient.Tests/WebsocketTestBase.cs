using System;
using Fleck;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Websocket.Client
{
    public abstract class WebsocketTestBase : IDisposable
    {
        private const string Host = "ws://127.0.0.1";
        private readonly int _port;
        private readonly WebSocketServer _server;

        protected readonly ILogger Logger;

        private IWebSocketConnection? _webSocketConnection;

        protected WebsocketTestBase(ITestOutputHelper testOutputHelper, int port)
        {
            Logger = testOutputHelper.BuildLogger();
            _port = port;

            Url = $"{Host}:{port}";
            _server = new WebSocketServer(Url)
            {
                RestartAfterListenError = false
            };
            try
            {
                _server.Start(socket =>
                {
                    _webSocketConnection = socket;
                    socket.OnOpen = () => testOutputHelper.WriteLine("Server Connection OnOpen.");
                    socket.OnClose = () => testOutputHelper.WriteLine("Server Connection OnClose.");
                    socket.OnMessage = message => socket.Send(message);
                    socket.OnBinary = bytes => socket.Send(bytes);
                });
                Logger.LogInformation("Server start success.");
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message, e);
            }
        }

        protected string Url { get; }

        public void Dispose()
        {
            Logger.LogWarning("Dispose.................................................................");
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected IWebSocketConnection GetLastSession()
        {
            return _webSocketConnection ?? throw new NullReferenceException();
        }


        protected virtual void Dispose(bool disposing)
        {
            if (disposing) _server?.Dispose();
        }
    }
}