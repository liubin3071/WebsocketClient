using System.Threading.Tasks;

namespace Websocket.Client
{
    public static class TaskExtensions
    {
        public static bool IsSuccessfully(this Task task)
        {
#if NETSTANDARD2_0
            return task.IsCompleted && !task.IsCanceled && !task.IsFaulted;
#else
            return task.IsCompletedSuccessfully;
#endif
        }
    }
}