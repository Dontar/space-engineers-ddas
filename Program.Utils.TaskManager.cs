using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace IngameScript
{
    partial class Program
    {
        public interface ITask
        {
            ITask Every(float seconds);
            ITask Pause(bool pause = true);
            bool Paused { get; }
            ITask Once();
            void Restart();
            T Result<T>();
        }
        class Task : ITask
        {
            IEnumerator Enumerator;
            IEnumerable Ref;
            TimeSpan Interval;
            TimeSpan TimeSinceLastRun;
            object TaskResult;
            bool IsPaused;
            bool IsOnce;

            bool ITask.Paused => IsPaused;

            ITask ITask.Every(float seconds) {
                Interval = TimeSpan.FromSeconds(seconds);
                return this;
            }
            ITask ITask.Pause(bool pause) {
                IsPaused = pause;
                return this;
            }

            ITask ITask.Once() {
                IsOnce = true;
                return this;
            }

            void ITask.Restart() {
                Enumerator = Ref.GetEnumerator();
                TimeSinceLastRun = TimeSpan.Zero;
                TaskResult = null;
            }
            T ITask.Result<T>() => (T)TaskResult;

            static List<Task> tasks = new List<Task>();

            public static ITask RunTask(IEnumerable task) {
                var newTask = new Task {
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

            static IEnumerable InternalTask(Action<object> cb, bool timeout = false) {
                if (timeout) {
                    cb(null);
                    yield break;
                }
                var context = new Dictionary<string, object>();
                while (true) {
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
            public static void Tick(TimeSpan TimeSinceLastRun) {
                for (int i = tasks.Count - 1; i >= 0; i--) {
                    var task = tasks[i];
                    if (task.IsPaused) continue;

                    task.TaskResult = null;

                    task.TimeSinceLastRun += TimeSinceLastRun;
                    if (task.TimeSinceLastRun < task.Interval) continue;

                    CurrentTaskLastRun = task.TimeSinceLastRun;
                    try {
                        if (!task.Enumerator.MoveNext()) {
                            if (task.IsOnce) {
                                tasks.RemoveAt(i);
                                continue;
                            }
                            task.Enumerator = task.Ref.GetEnumerator();
                        }
                    }
                    catch (Exception e) {
                        Util.Echo(e.ToString());
                    }
                    task.TimeSinceLastRun = TimeSpan.Zero;
                    task.TaskResult = task.Enumerator.Current;
                }
            }
        }
    }
}
