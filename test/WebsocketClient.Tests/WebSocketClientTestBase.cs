using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fleck;
using Shouldly;
using Websocket.Client.Events;
using Websocket.Client.Exceptions;
using Xunit;
using Xunit.Abstractions;

namespace Websocket.Client
{
    public abstract class WebsocketClientTestBase : WebsocketTestBase
    {
        protected WebsocketClientTestBase(ITestOutputHelper testOutputHelper, int port) : base(testOutputHelper, port)
        {
        }

        protected abstract WebsocketClientBase CreateNewClient();

        protected virtual void RaiseErrorEvent(WebsocketClientBase client, IWebSocketConnection session)
        {
            //closed the WebSocket connection without completing the close handshake.
            ((WebSocketConnection) session).Socket.Dispose();
        }


        [Fact]
        public async void auto_reopen_throttle_test()
        {
            //arrange
            using var client = CreateNewClient();
            //client.AutoReopenOnClosed = true;
            client.AutoReopenOnKeepAliveTimeout = true;
            client.AutoReopenThrottleTimeSpan = TimeSpan.FromSeconds(3);
            client.KeepAliveTimeout = TimeSpan.FromSeconds(1);

            var errorEvent = new AutoResetEvent(false);
            client.Error += (sender, args) => errorEvent.Set();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();
            var reopenEvent = new AutoResetEvent(false);
            client.Reopened += (sender, args) => reopenEvent.Set();
            //act
            await client.OpenAsync();

            //assert
            reopenEvent.WaitOne(900).ShouldBeFalse(); // 1s timeout
            reopenEvent.WaitOne(200).ShouldBeTrue();

            reopenEvent.WaitOne(2900).ShouldBeFalse();
            reopenEvent.WaitOne(200).ShouldBeTrue();

            reopenEvent.WaitOne(2900).ShouldBeFalse();
            reopenEvent.WaitOne(200).ShouldBeTrue();
        }

        [Fact]
        public async void auto_reopen_when_alive_timeout()
        {
            //arrange
            using var client = CreateNewClient();
            client.AutoReopenOnKeepAliveTimeout = true;
            client.AutoReopenThrottleTimeSpan = TimeSpan.Zero;
            client.KeepAliveTimeout = TimeSpan.FromSeconds(1);

            var errorEvent = new AutoResetEvent(false);
            client.Error += (sender, args) => errorEvent.Set();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();
            var reopenEvent = new AutoResetEvent(false);
            client.Reopened += (sender, args) => reopenEvent.Set();
            //act
            await client.OpenAsync();

            //assert
            reopenEvent.WaitOne(2000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Open);

            reopenEvent.WaitOne(4000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Open);

            reopenEvent.WaitOne(3000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Open);
        }

        [Fact]
        public async void auto_reopen_when_error()
        {
            //arrange

            using var client = CreateNewClient();
            client.AutoReopenOnClosed = true;

            var errorEvent = new AutoResetEvent(false);
            client.Error += (sender, args) => errorEvent.Set();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();
            var reopenEvent = new AutoResetEvent(false);
            client.Reopened += (sender, args) => reopenEvent.Set();
            //act
            await client.OpenAsync();
            var session = GetLastSession();
            RaiseErrorEvent(client, session);
            errorEvent.WaitOne(1000).ShouldBeTrue();
            closedEvent.WaitOne(1000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Closed);
            //assert
            reopenEvent.WaitOne(3000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Open);
        }

        [Fact]
        public async void auto_reopen_when_server_closed()
        {
            //arrange

            using var client = CreateNewClient();
            client.AutoReopenOnClosed = true;
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => { closedEvent.Set(); };
            var reopenEvent = new AutoResetEvent(false);
            client.Reopened += (sender, args) => reopenEvent.Set();
            await client.OpenAsync();

            //act
            var session = GetLastSession();
            session.Close();
            closedEvent.WaitOne(3000).ShouldBe(true);
            client.State.ShouldBe(ReadyState.Closed);
            //assert
            reopenEvent.WaitOne(3000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Open);
        }

        [Fact]
        public void closeAsync_when_closed_state_should_ignore_test()
        {
            //arrange

            using var client = CreateNewClient();
            client.State.ShouldBe(ReadyState.Closed);
            var closingEvent = new AutoResetEvent(false);
            client.Closing += (sender, args) => closingEvent.Set();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();
            var errorEvent = new AutoResetEvent(false);
            client.Error += (sender, args) => errorEvent.Set();
            //act
            _ = client.CloseAsync();

            //assert
            errorEvent.WaitOne(1000).ShouldBeFalse();
            closingEvent.WaitOne(1000).ShouldBeFalse();
            closedEvent.WaitOne(1000).ShouldBeFalse();
        }

        [Fact]
        public async void closeAsync_when_closing_state_should_raise_closed_event_only_once_test()
        {
            //arrange

            using var client = CreateNewClient();
            var closingCount = 0;
            client.Closing += (sender, args) => closingCount++;
            var closedCount = 0;
            var closeReason = string.Empty;
            client.Closed += (sender, args) => closedCount++;
            await client.OpenAsync();
            client.State.ShouldBe(ReadyState.Open);

            //act
            var task1 = client.CloseAsync();
            var task2 = client.CloseAsync();

            //assert
            Task.WaitAll(task1, task2);
            closingCount.ShouldBe(1);
            closedCount.ShouldBe(1);
            client.State.ShouldBe(ReadyState.Closed);
        }

        [Fact]
        public void closeAsync_when_connecting_state_should_raise_closing_closed_event_test()
        {
            //arrange

            using var client = CreateNewClient();
            var connectingEvent = new AutoResetEvent(false);
            client.Opening += (sender, args) => connectingEvent.Set();
            var closingEvent = new AutoResetEvent(false);
            client.Closing += (sender, args) => closingEvent.Set();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();
            _ = client.OpenAsync();
            connectingEvent.WaitOne(1000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Connecting);

            //act
            _ = client.CloseAsync();

            //assert
            closedEvent.WaitOne(1000).ShouldBe(true);
            client.State.ShouldBe(ReadyState.Closed);
        }

        [Fact]
        public async void closeAsync_when_open_should_raise_closing_closed_event_test()
        {
            //arrange

            using var client = CreateNewClient();
            var closingEvent = new AutoResetEvent(false);
            client.Closing += (sender, args) => closingEvent.Set();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();
            await client.OpenAsync();
            client.State.ShouldBe(ReadyState.Open);
            //act
            _ = client.CloseAsync();

            //assert
            closingEvent.WaitOne(1000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Closing);
            closedEvent.WaitOne(1000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Closed);
        }

        [Fact]
        public async void error_should_raise_error_and_closed_event()
        {
            //arrange
            using var client = CreateNewClient();
            var errorEvent = new AutoResetEvent(false);
            client.Error += (sender, args) => errorEvent.Set();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();
            await client.OpenAsync();
            //act
            var session = GetLastSession();
            RaiseErrorEvent(client, session); //assert
            errorEvent.WaitOne(1000).ShouldBeTrue();
            closedEvent.WaitOne(1000).ShouldBeTrue();

            errorEvent.WaitOne(1000).ShouldBeFalse();
            closedEvent.WaitOne(3000).ShouldBeFalse();
        }

        [Fact]
        public async void openAsync_timeout_should_throw_and_reset_closed_state_test()
        {
            //arrange
            using var client = CreateNewClient();
            client.OpenTimeout = TimeSpan.Zero;
            var openingEvent = new AutoResetEvent(false);
            client.Opening += (sender, args) => openingEvent.Set();
            var openedEvent = new AutoResetEvent(false);
            client.Opened += (sender, args) => openedEvent.Set();
            var errorEvent = new AutoResetEvent(false);
            client.Error += (sender, args) => errorEvent.Set();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();

            //assert
            await Assert.ThrowsAsync<WebsocketException>(async () => { await client.OpenAsync(); });
            client.State.ShouldBe(ReadyState.Closed);
            //assert
            //errorEvent.WaitOne(1000).ShouldBeTrue();
            //closedEvent.WaitOne(1000).ShouldBeTrue();
            //client.State.ShouldBe(ReadyState.Closed);
            //openedEvent.WaitOne(1000).ShouldBeFalse();
        }

        [Fact]
        public void openAsync_when_closed_should_raise_opening_opened_event_test()
        {
            //arrange

            using var client = CreateNewClient();
            var openingEvent = new AutoResetEvent(false);
            client.Opening += (sender, args) => openingEvent.Set();
            var openedEvent = new AutoResetEvent(false);
            client.Opened += (sender, args) => openedEvent.Set();
            //act
            _ = client.OpenAsync();
            //assert
            openingEvent.WaitOne(1000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Connecting);
            openedEvent.WaitOne(1000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Open);
        }

        [Fact]
        public async void openAsync_when_closing_should_wait_closed_then_open_test()
        {
            //arrange

            using var client = CreateNewClient();
            await client.OpenAsync();
            client.State.ShouldBe(ReadyState.Open);

            var openingEvent = new AutoResetEvent(false);
            client.Opening += (sender, args) => openingEvent.Set();
            var openedEvent = new AutoResetEvent(false);
            client.Opened += (sender, args) => openedEvent.Set();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();
            _ = client.CloseAsync();
            client.State.ShouldBe(ReadyState.Closing);

            //act
            _ = client.OpenAsync();

            //assert
            closedEvent.WaitOne(1000).ShouldBeTrue();
            openingEvent.WaitOne(1000).ShouldBeTrue();
            openedEvent.WaitOne(1000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Open);
        }

        [Fact]
        public void openAsync_when_connecting_should_raise_opening_opened_event_once_test()
        {
            //arrange

            using var client = CreateNewClient();
            var openingEventCount = 0;
            client.Opening += (sender, args) => openingEventCount++;
            var openedEventCount = 0;
            client.Opened += (sender, args) => openedEventCount++;

            //act
            var t1 = client.OpenAsync();
            client.State.ShouldBe(ReadyState.Connecting);
            var t2 = client.OpenAsync();
            Task.WaitAll(t1, t2);
            //assert
            openingEventCount.ShouldBe(1);
            openedEventCount.ShouldBe(1);
        }

        [Fact]
        public async void openAsync_when_open_should_ignore_test()
        {
            //arrange

            using var client = CreateNewClient();
            await client.OpenAsync();
            client.State.ShouldBe(ReadyState.Open);

            //act
            var openingEvent = new AutoResetEvent(false);
            client.Opening += (sender, args) => openingEvent.Set();
            var openedEvent = new AutoResetEvent(false);
            client.Opened += (sender, args) => openedEvent.Set();
            _ = client.OpenAsync();

            //assert
            openingEvent.WaitOne(1000).ShouldBeFalse();
            openedEvent.WaitOne(1000).ShouldBeFalse();
            client.State.ShouldBe(ReadyState.Open);
        }

        [Fact]
        public async void sendAsync_bytes_test()
        {
            //arrange

            using var client = CreateNewClient();
            MessageEventArgs currentMessage = null;
            var receivedEvent = new AutoResetEvent(false);
            client.MessageReceived += (sender, args) =>
            {
                currentMessage = args;
                receivedEvent.Set();
            };
            client.MessageObservable.Subscribe(c => receivedEvent.Set());
            await client.OpenAsync();

            //act
            var bytes = Encoding.UTF8.GetBytes("test");
            _ = client.SendAsync(bytes);
            //assert
            receivedEvent.WaitOne(2000).ShouldBe(true);
            receivedEvent.WaitOne(2000).ShouldBe(true);
            currentMessage.Bytes.ShouldBe(bytes);
        }

        [Fact]
        public async void sendAsync_bytes_when_closed_state_should_throw_test()
        {
            //arrange

            using var client = CreateNewClient();
            await client.OpenAsync();
            await client.CloseAsync();
            client.State.ShouldBe(ReadyState.Closed);

            //assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new byte[] {0}));
        }

        [Fact]
        public async void sendAsync_bytes_when_closing_state_should_throw_test()
        {
            //arrange

            using var client = CreateNewClient();
            var closingEvent = new AutoResetEvent(false);
            await client.OpenAsync();

            _ = client.CloseAsync();
            //closingEvent.WaitOne(1000).ShouldBe(true);
            client.State.ShouldBe(ReadyState.Closing);

            //act assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new byte[] {0}));
        }

        [Fact]
        public async void sendAsync_bytes_when_connecting_state_should_throw_test()
        {
            //arrange

            using var client = CreateNewClient();
            var openingEvent = new AutoResetEvent(false);
            _ = client.OpenAsync();
            client.State.ShouldBe(ReadyState.Connecting);
            //act assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new byte[] {0}));
        }

        [Fact]
        public async void sendAsync_text_test()
        {
            //arrange

            using var client = CreateNewClient();
            MessageEventArgs currentMessage = null;
            var receivedEvent = new AutoResetEvent(false);
            client.MessageReceived += (sender, args) =>
            {
                currentMessage = args;
                receivedEvent.Set();
            };
            client.MessageObservable.Subscribe(c => receivedEvent.Set());
            await client.OpenAsync();
            client.State.ShouldBe(ReadyState.Open);

            //act
            var text = "test";
            _ = client.SendAsync(text);

            //assert
            receivedEvent.WaitOne(1000).ShouldBe(true);
            receivedEvent.WaitOne(1000).ShouldBe(true);
            currentMessage.Text.ShouldBe(text);
        }

        [Fact]
        public async void sendAsync_text_when_closed_state_should_throw_test()
        {
            //arrange

            using var client = CreateNewClient();
            // await client.OpenAsync();
            // await client.EnsureCloseAsync();
            client.State.ShouldBe(ReadyState.Closed);

            //act assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("test"));
        }

        [Fact]
        public async void sendAsync_text_when_closing_state_should_throw_test()
        {
            //arrange

            using var client = CreateNewClient();
            var closingEvent = new AutoResetEvent(false);
            await client.OpenAsync();
            _ = client.CloseAsync();
            //closingEvent.WaitOne(1000).ShouldBe(true);
            client.State.ShouldBe(ReadyState.Closing);
            //act assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("test"));
        }

        [Fact]
        public async void sendAsync_text_when_connecting_state_should_throw_test()
        {
            //arrange

            using var client = CreateNewClient();
            _ = client.OpenAsync();
            client.State.ShouldBe(ReadyState.Connecting);
            //act assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("test"));
        }


        [Fact]
        public async void server_closed_should_raise_closed_event_Test()
        {
            //arrange

            using var client = CreateNewClient();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => { closedEvent.Set(); };
            await client.OpenAsync();
            client.State.ShouldBe(ReadyState.Open);
            await Task.Delay(200);
            //act
            var session = GetLastSession();
            session.Close();
            //assert
            closedEvent.WaitOne(3000).ShouldBeTrue();
            client.State.ShouldBe(ReadyState.Closed);
        }
    }
}