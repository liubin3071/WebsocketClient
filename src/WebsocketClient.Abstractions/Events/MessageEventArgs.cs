using System;
using Dawn;

namespace Websocket.Client.Events
{
    public class MessageEventArgs : EventArgs
    {
        public MessageEventArgs(byte[] bytes) : this()
        {
            Bytes = Guard.Argument(bytes, nameof(bytes)).NotNull();
            IsBinary = true;
        }

        public MessageEventArgs(string text) : this()
        {
            Text = Guard.Argument(text, nameof(text)).NotNull();
            IsText = true;
        }

        private MessageEventArgs()
        {
        }

        public string? Text { get; }

        public byte[]? Bytes { get; }

        public bool IsBinary { get; }
        public bool IsText { get; }
    }
}