using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client.Events;
using Websocket.Client.Exceptions;

namespace Websocket.Client
{
    public interface IWebsocketClient : IDisposable
    {
        #region Public Events

        event EventHandler<CloseEventArgs>? Closed;

        event EventHandler<CloseEventArgs>? Closing;

        event EventHandler<ErrorEventArgs>? Error;

        event EventHandler<MessageEventArgs>? MessageReceived;

        event EventHandler<OpenedEventArgs>? Opened;

        event EventHandler<OpeningEventArgs>? Opening;

        event EventHandler<ReopenedEventArgs>? Reopened;

        #endregion Public Events

        #region Public Properties

        /// <summary>
        ///     当非主动连接关闭时,是否自动重新打开.
        /// </summary>
        bool AutoReopenOnClosed { get; set; }

        bool AutoReopenOnKeepAliveTimeout { get; set; }

        /// <summary>
        ///     The timespan to wait before the keep alive times out.
        /// </summary>
        TimeSpan KeepAliveTimeout { get; set; }

        /// <summary>
        ///     reopen节流阀
        /// </summary>
        TimeSpan AutoReopenThrottleTimeSpan { get; set; }

        TimeSpan OpenTimeout { get; set; }
        TimeSpan CloseTimeout { get; set; }
        Encoding Encoding { get; set; }
        bool IsOpened { get; }
        IObservable<MessageEventArgs> MessageObservable { get; }
        ReadyState State { get; }

        #endregion Public Properties

        #region Public Methods

        /// <summary>
        ///     close anyway.
        /// </summary>
        /// <para></para>
        /// <returns>
        ///     <para>正常关闭返回true;</para>
        ///     <para>调用时正在关闭,等待完成后返回false;此种情况不重复触发事件.</para>
        ///     <para>关闭过程中发生异常,重置通信器并返回false.</para>
        /// </returns>
        Task CloseAsync();

        /// <summary>
        ///     open
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">当前状态无法操作.</exception>
        /// <exception cref="WebsocketException">通讯异常</exception>
        Task OpenAsync();

        Task SendAsync(string text, CancellationToken cancellationToken = default);

        Task SendAsync(byte[] bytes, CancellationToken cancellationToken = default);

        #endregion Public Methods
    }
}