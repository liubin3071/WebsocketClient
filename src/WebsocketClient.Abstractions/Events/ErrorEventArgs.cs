using System;

namespace Websocket.Client.Events
{
    public class ErrorEventArgs : EventArgs
    {
        private readonly Exception _exception;

        /// <devdoc>
        ///     Initializes a new instance of the class.
        /// </devdoc>
        public ErrorEventArgs(Exception exception)
        {
            _exception = exception;
        }

        /// <devdoc>
        ///     Gets the <see cref='System.Exception' /> that represents the error that occurred.
        /// </devdoc>
        public Exception GetException()
        {
            return _exception;
        }
    }
}