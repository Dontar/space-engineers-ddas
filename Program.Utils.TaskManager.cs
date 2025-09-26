using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace IngameScript
{
    partial class Program
    {
        static class TaskManager
        {
            public interface ITask
            {
                ITask Every(float seconds);
                ITask Pause(bool pause = true);
                ITask Once();
                void Restart();
                T Result<T>();
            }
            class Task : ITask
            {
                public IEnumerator Enumerator;
                public IEnumerable Ref;
                public TimeSpan Interval;
                public TimeSpan TimeSinceLastRun;
                public object TaskResult;
                public bool IsPaused;
                public bool IsOnce;
                public ITask Every(float seconds)
                {
                    Interval = TimeSpan.FromSeconds(seconds);
                    return this;
                }
                public ITask Pause(bool pause)
                {
                    IsPaused = pause;
                    return this;
                }
                public ITask Once()
                {
                    IsOnce = true;
                    return this;
                }

                public void Restart()
                {
                    Enumerator = Ref.GetEnumerator();
                    TimeSinceLastRun = TimeSpan.Zero;
                    TaskResult = null;
                }
                public T Result<T>() => (T)TaskResult;
            }
            static readonly List<Task> tasks = new List<Task>();

            public static ITask RunTask(IEnumerable task)
            {
                var newTask = new Task
                {
                    Ref = task,
                    Enumerator = task.GetEnumerator(),
                    Interval = TimeSpan.FromSeconds(0),
                    TimeSinceLastRun = TimeSpan.Zero,
                    TaskResult = null,
                    IsPaused = false,
                    IsOnce = false
                };
                tasks.Add(newTask);
                return newTask;
            }

            static IEnumerable InternalTask(Action<object> cb, bool timeout = false)
            {
                if (timeout)
                {
                    cb(null);
                    yield break;
                }
                var context = new Dictionary<string, object>();
                while (true)
                {
                    cb(context);
                    yield return null;
                }
            }
            public static ITask SetInterval(Action<Dictionary<string, object>> cb, float intervalSeconds) =>
                RunTask(InternalTask(ctx => cb((Dictionary<string, object>)ctx))).Every(intervalSeconds);

            public static ITask SetTimeout(Action cb, float delaySeconds) =>
                RunTask(InternalTask(_ => cb())).Once().Every(delaySeconds);
            public static void ClearTask(ITask task) => tasks.Remove((Task)task);

            public static T GetTaskResult<T>() => tasks.Select(t => t.TaskResult).OfType<T>().FirstOrDefault();
            public static TimeSpan CurrentTaskLastRun;
            public static void Tick(TimeSpan TimeSinceLastRun)
            {
                for (int i = tasks.Count - 1; i >= 0; i--)
                {
                    var task = tasks[i];
                    if (task.IsPaused) continue;

                    task.TaskResult = null;

                    task.TimeSinceLastRun += TimeSinceLastRun;
                    if (task.TimeSinceLastRun < task.Interval) continue;

                    CurrentTaskLastRun = task.TimeSinceLastRun;
                    try
                    {
                        if (!task.Enumerator.MoveNext())
                        {
                            if (task.IsOnce)
                            {
                                tasks.RemoveAt(i);
                                continue;
                            }
                            task.Enumerator = task.Ref.GetEnumerator();
                        }
                    }
                    catch (Exception e)
                    {
                        Util.Echo(e.ToString());
                    }
                    task.TimeSinceLastRun = TimeSpan.Zero;
                    task.TaskResult = task.Enumerator.Current;
                }
            }
        }
    }
}
