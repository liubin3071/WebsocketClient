using System;

namespace Websocket.Client.Exceptions
{
    public class WebsocketOpenException : Exception
    {
        public WebsocketOpenException()
        {
        }

        public WebsocketOpenException(string message) : base(message)
        {
        }

        public WebsocketOpenException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}