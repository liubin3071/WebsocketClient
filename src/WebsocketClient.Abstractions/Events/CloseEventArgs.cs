using System;

namespace Websocket.Client.Events
{
    public class CloseEventArgs : EventArgs
    {
        public CloseEventArgs(CloseStatusCode? closeStatusCode, string? reason)
        {
            CloseStatusCode = closeStatusCode;
            Reason = reason;
        }

        public CloseStatusCode? CloseStatusCode { get; }
        public string? Reason { get; }
    }
}