namespace Websocket.Client
{
    public delegate T InnerClientFactory<out T>(string url);
}