﻿using System;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Websocket.Client
{
    public abstract class WebsocketLiteClientTestBase : WebsocketTestBase
    {
        protected WebsocketLiteClientTestBase(ITestOutputHelper testOutputHelper, int port) : base(testOutputHelper,
            port)
        {
        }

        protected abstract IWebsocketLiteClient CreateNewClient();

        [Fact]
        public async void ensure_close_when_closed_test()
        {
            //arrange
            using var client = CreateNewClient();
            var closedEvent = new AutoResetEvent(false);
            var closedCount = 0;
            client.Closed += (sender, args) =>
            {
                closedCount++;
                closedEvent.Set();
            };

            for (var i = 0; i < 10; i++)
            {
                TestOutputHelper.WriteLine($"test {i}......");
                await client.EnsureCloseAsync();
                //assert
                client.IsOpened.ShouldBeFalse();
            }

            closedCount.ShouldBe(0);
        }

        [Fact]
        public async void ensure_close_when_closing_test()
        {
            //arrange
            using var client = CreateNewClient();
            var closedEvent = new AutoResetEvent(false);
            var closedCount = 0;
            client.Closed += (sender, args) =>
            {
                closedCount++;
                closedEvent.Set();
            };

            for (var i = 0; i < 10; i++)
            {
                TestOutputHelper.WriteLine($"test {i}......");
                await client.EnsureCloseAsync();
                //assert
                client.IsOpened.ShouldBeFalse();
            }

            closedCount.ShouldBe(0);
        }

        [Fact]
        public async void ensure_close_when_is_open_test()
        {
            //arrange
            using var client = CreateNewClient();
            var closedEvent = new AutoResetEvent(false);
            var closedCount = 0;
            client.Closed += (sender, args) =>
            {
                closedCount++;
                closedEvent.Set();
            };

            for (var i = 0; i < 10; i++)
            {
                TestOutputHelper.WriteLine($"test {i}......");

                //act open=>close
                await client.OpenAsync();
                client.IsOpened.ShouldBeTrue();

                await client.EnsureCloseAsync();
                //assert
                client.IsOpened.ShouldBeFalse();
            }

            closedCount.ShouldBe(0);
        }

        [Fact]
        public async void ensure_close_when_opening_test()
        {
            //arrange
            using var client = CreateNewClient();
            var closedEvent = new AutoResetEvent(false);
            var closedCount = 0;
            client.Closed += (sender, args) =>
            {
                closedCount++;
                closedEvent.Set();
            };

            for (var i = 0; i < 10; i++)
            {
                TestOutputHelper.WriteLine($"test {i}......");
                _ = client.OpenAsync();
                await client.EnsureCloseAsync();
                //assert
                client.IsOpened.ShouldBeFalse();
            }

            closedCount.ShouldBe(0);
        }


        [Fact]
        public async void open_close_test()
        {
            //arrange
            using var client = CreateNewClient();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();

            for (var i = 0; i < 10; i++)
            {
                TestOutputHelper.WriteLine($"test {i}......");
                //act
                await client.OpenAsync(CancellationToken.None);
                //assert
                client.IsOpened.ShouldBeTrue();

                //act
                await client.EnsureCloseAsync();
                //assert
                closedEvent.WaitOne(100).ShouldBeFalse();
                client.IsOpened.ShouldBeFalse();
            }
        }

        [Fact]
        public async void open_test()
        {
            //arrange
            for (var i = 0; i < 10; i++)
            {
                //act
                using var client = CreateNewClient();
                await client.OpenAsync(CancellationToken.None);

                //assert
                client.IsOpened.ShouldBeTrue();
            }
        }

        [Fact]
        public async void receive_binary_test()
        {
            using var client = CreateNewClient();
            await client.OpenAsync(CancellationToken.None);
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();

            var session = GetLastSession();

            var messageCount = 0;
            var currentMsg = Array.Empty<byte>();
            var messageEvent = new AutoResetEvent(false);
            client.MessageReceived += (sender, args) =>
            {
                messageCount++;
                currentMsg = args.Bytes;
                messageEvent.Set();
            };

            for (var i = 0; i < 10; i++)
            {
                TestOutputHelper.WriteLine($"test {i}......");
                var msg = Encoding.UTF8.GetBytes($"test{i}");
                await session.Send(msg);
                Logger.LogDebug($"send\t\t {string.Join(' ', msg)}");

                messageEvent.WaitOne(1000).ShouldBeTrue();
                messageCount.ShouldBe(i + 1);
                currentMsg.ShouldBe(msg);
                Logger.LogDebug($"received\t {string.Join(' ', msg)}");
            }

            closedEvent.WaitOne(100).ShouldBeFalse();
            client.IsOpened.ShouldBeTrue();
        }

        [Fact]
        public async void receive_text_test()
        {
            using var client = CreateNewClient();
            await client.OpenAsync(CancellationToken.None);
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();

            var session = GetLastSession();

            var messageCount = 0;
            var currentMsg = "";
            var messageEvent = new AutoResetEvent(false);
            client.MessageReceived += (sender, args) =>
            {
                messageCount++;
                currentMsg = args.Text;
                messageEvent.Set();
            };

            for (var i = 0; i < 10; i++)
            {
                TestOutputHelper.WriteLine($"test {i}......");
                var msg = $"test{i}";
                await session.Send(msg);
                messageEvent.WaitOne(1000).ShouldBeTrue();
                messageCount.ShouldBe(i + 1);
                currentMsg.ShouldBe(msg);
            }

            closedEvent.WaitOne(100).ShouldBeFalse();
            client.IsOpened.ShouldBeTrue();
        }

        [Fact]
        public async void send_text_test()
        {
            //arrange
            using var client = CreateNewClient();
            await client.OpenAsync(CancellationToken.None);
            client.IsOpened.ShouldBeTrue();
            var closedEvent = new AutoResetEvent(false);
            client.Closed += (sender, args) => closedEvent.Set();
            var messageEvent = new AutoResetEvent(false);
            string? currentMsg = null;
            client.MessageReceived += (sender, args) =>
            {
                currentMsg = args.Text;
                messageEvent.Set();
            };
            for (var i = 0; i < 10; i++)
            {
                //act
                await client.SendAsync("test");

                //assert
                messageEvent.WaitOne(100).ShouldBeTrue();
                currentMsg.ShouldBe("test");
            }

            closedEvent.WaitOne(100).ShouldBeFalse();
            client.IsOpened.ShouldBeTrue();
        }
    }
}