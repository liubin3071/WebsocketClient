using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client.Events;

namespace Websocket.Client
{
    public interface IWebsocketLiteClient<out T> : IWebsocketLiteClient where T : class?
    {
        T GetInnerClient();
    }

    public interface IWebsocketLiteClient : IDisposable
    {
        object InnerClient { get; }

        bool IsOpened { get; }
        Encoding Encoding { get; set; }

        event EventHandler<MessageEventArgs>? MessageReceived;
        event EventHandler<CloseEventArgs>? Closed;
        event EventHandler<ErrorEventArgs>? Error;


        /// <summary>
        ///     打开连接,连接失败或发生异常直接抛出
        /// </summary>
        /// <remarks>
        ///     <para> *不要处理任何异常</para>
        ///     <para> *连接失败直接封装为异常抛出.</para>
        ///     <para> *发生异常,重置通信器</para>
        ///     <para> *确保调用完成后State==Open, 否则抛出异常. </para>
        /// </remarks>
        /// <param name="cancellationToken"></param>
        /// <exception cref="OperationCanceledException"> <paramref name="cancellationToken" /> cancelled.</exception>
        /// <exception cref="Exception">others.</exception>
        Task OpenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     close
        /// </summary>
        /// <remarks>
        ///     <para> *不抛出任何异常</para>
        ///     <para> *运行后确保关闭.</para>
        /// </remarks>
        /// <param name="closeStatusCode"></param>
        /// <param name="reason"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task EnsureCloseAsync(CloseStatusCode? closeStatusCode = null, string? reason = null,
            CancellationToken token = default);

        Task SendAsync(string text, CancellationToken cancellationToken = default);

        Task SendAsync(byte[] bytes, CancellationToken cancellationToken = default);
    }
}