using Microsoft.Build.Framework.Profiler;
using Microsoft.Build.Utilities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Library;
using VRageMath;
using VRageRender.ExternalApp;

namespace IngameScript
{
    partial class Program
    {
        static class Memo
        {
            private class CacheValue
            {
                public object Value { get; }
                public object[] Dependency { get; }
                public int Age { get; private set; }

                public CacheValue(object[] dep, object value)
                {
                    Dependency = dep;
                    Value = value;
                    Age = 0;
                }

                public CacheValue(int age, object value)
                {
                    Age = age;
                    Value = value;
                }

                public bool Decay()
                {
                    if (Age-- > 0) return true;
                    return false;
                }
            }

            private static readonly Dictionary<string, CacheValue> _dependencyCache = new Dictionary<string, CacheValue>();
            private const int MaxCacheSize = 1000;

            public static object[] Refs(object p1, object p2 = null, object p3 = null)
            {
                if (p2 != null)
                {
                    if (p3 != null)
                    {
                        return new object[] { p1, p2, p3 };
                    }
                    return new object[] { p1, p2 };
                }
                return new object[] { p1 };
            }

            private static object IntOf(Func<object> f, string context, object dep)
            {
                if (_dependencyCache.Count > MaxCacheSize)
                {
                    EvictOldestCacheItem();
                }

                bool isAge = dep is int;
                CacheValue value;
                if (_dependencyCache.TryGetValue(context, out value))
                {
                    bool isNotStale = isAge ? value.Decay() : value.Dependency.SequenceEqual((object[])dep);
                    if (isNotStale)
                    {
                        return value.Value;
                    }
                }

                var result = f();
                _dependencyCache[context] = isAge ? new CacheValue((int)dep, result) : new CacheValue((object[])dep, result);
                return result;
            }

            public static R Of<R>(Func<R> f, string context, object dep)
            {
                return (R)IntOf(() => f(), context, dep);
            }
            public static void Of(Action f, string context, object dep)
            {
                IntOf(() => { f(); return null; }, context, dep);
            }

            private static void EvictOldestCacheItem()
            {
                var oldestKey = _dependencyCache.Last().Key;
                _dependencyCache.Remove(oldestKey);
            }
        }

        static class Util
        {
            static Program p;

            public static void Init(Program p)
            {
                Util.p = p;
            }

            public static IEnumerable<T> GetBlocks<T>(Func<T, bool> collect = null) where T : class
            {
                List<T> blocks = new List<T>();
                p.GridTerminalSystem.GetBlocksOfType(blocks, collect);
                return blocks;
            }

            public static IEnumerable<T> GetBlocks<T>(string blockTag) where T : class
            {
                return GetBlocks<IMyTerminalBlock>(b => IsTagged(b, blockTag)).Cast<T>();
            }

            public static IEnumerable<T> GetGroup<T>(string name, Func<T, bool> collect = null) where T : class
            {
                var groupBlocks = new List<T>();
                var group = p.GridTerminalSystem.GetBlockGroupWithName(name);
                group?.GetBlocksOfType(groupBlocks, collect);
                return groupBlocks;
            }

            public static IEnumerable<T> GetGroupOrBlocks<T>(string name, Func<T, bool> collect = null) where T : class
            {
                var groupBlocks = new List<IMyTerminalBlock>();
                var group = p.GridTerminalSystem.GetBlockGroupWithName(name);
                if (group != null)
                {
                    group.GetBlocksOfType(groupBlocks, v => v is T && (collect == null || collect(v as T)));
                }
                else
                {
                    p.GridTerminalSystem.GetBlocksOfType(groupBlocks, b => b.CustomName == name && b is T && (collect == null || collect(b as T)));
                }
                return groupBlocks.Cast<T>();
            }

            public static IEnumerable<IMyTextSurface> GetScreens(string screenTag = "")
            {
                return GetScreens(b => IsTagged(b, screenTag), screenTag);
            }

            public static IEnumerable<IMyTextSurface> GetScreens(Func<IMyTerminalBlock, bool> collect, string screenTag = "")
            {
                var screens = GetBlocks<IMyTerminalBlock>(b => (b is IMyTextSurface || HasScreens(b)) && collect(b));
                return screens.Select(s =>
                {
                    if (s is IMyTextSurface)
                        return s as IMyTextSurface;
                    var provider = s as IMyTextSurfaceProvider;
                    var regex = new System.Text.RegularExpressions.Regex(screenTag + @"@(\d+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
                    var match = regex.Match(s.CustomData);
                    if (match.Success)
                    {
                        var screenIndex = int.Parse(match.Groups[1].Value) - 1;
                        return provider.GetSurface(screenIndex);
                    }
                    return provider.GetSurface(0);
                });
            }

            public static int ScreenLines(IMyTextSurface screen, char symbol = 'S')
            {
                var symbolSize = screen.MeasureStringInPixels(new StringBuilder(symbol.ToString()), screen.Font, screen.FontSize);
                var paddingY = NormalizeValue(screen.TextPadding, 0, 100, 0, screen.SurfaceSize.Y);
                var screenY = screen.SurfaceSize.Y - paddingY;
                return (int)Math.Floor(screenY / (symbolSize.Y + 2) * (512 / screen.TextureSize.Y)) + 1;
            }

            public static int ScreenColumns(IMyTextSurface screen, char symbol = 'S')
            {
                var symbolSize = screen.MeasureStringInPixels(new StringBuilder(symbol.ToString()), screen.Font, screen.FontSize);
                var paddingX = NormalizeValue(screen.TextPadding, 0, 100, 0, screen.SurfaceSize.X);
                var screenX = screen.SurfaceSize.X - paddingX;
                return (int)Math.Floor(screenX / symbolSize.X * (512 / screen.TextureSize.X));
            }

            public static double NormalizeValue(double value, double oldMin, double oldMax, double min, double max)
            {
                double originalRange = oldMax - oldMin;
                double newRange = max - min;
                double normalizedValue = ((value - oldMin) * newRange / originalRange) + min;
                return normalizedValue;
            }

            public static double NormalizeClamp(double value, double oldMin, double oldMax, double min, double max)
            {
                return MathHelper.Clamp(NormalizeValue(value, oldMin, oldMax, min, max), min, max);
            }

            public static bool IsNotIgnored(IMyTerminalBlock block, string ignoreTag = "{Ignore}")
            {
                return !(block.CustomName.Contains(ignoreTag) || block.CustomData.Contains(ignoreTag));
            }

            public static bool IsTagged(IMyTerminalBlock block, string tag = "{DDAS}")
            {
                return block.CustomName.Contains(tag) || block.CustomData.Contains(tag);
            }

            public static bool IsBetween(double value, double min, double max)
            {
                return value >= min && value <= max;
            }

            public static bool HasScreens(IMyTerminalBlock block)
            {
                return block is IMyTextSurfaceProvider && (block as IMyTextSurfaceProvider).SurfaceCount > 0;
            }

            public static void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed, float power, IMyGyro gyro, MatrixD worldMatrix)
            {
                ApplyGyroOverride(pitchSpeed, yawSpeed, rollSpeed, power, new IMyGyro[] { gyro }, worldMatrix);

            }

            public static void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed, float power, IEnumerable<IMyGyro> gyros, MatrixD worldMatrix)
            {
                var rotationVec = new Vector3D(pitchSpeed, yawSpeed, rollSpeed);
                var relativeRotationVec = Vector3D.TransformNormal(rotationVec, worldMatrix);

                foreach (var g in gyros)
                {
                    var transformedRotationVec = Vector3D.TransformNormal(relativeRotationVec, Matrix.Transpose(g.WorldMatrix));
                    g.Pitch = (float)transformedRotationVec.X;
                    g.Yaw = (float)transformedRotationVec.Y;
                    g.Roll = (float)transformedRotationVec.Z;
                    g.GyroOverride = true;
                    g.GyroPower = power;
                }
            }

            public static double ToAzimuth(Vector3D v)
            {
                v.Y = 0;
                v.Normalize();
                return v.X >= 0.0 ? 0.0 - Math.Acos(v.Dot(Vector3D.Forward)) : Math.Acos(v.Dot(Vector3D.Forward));
            }

            public static IEnumerable DisplayLogo(string logo, IMyTextSurface screen)
            {
                var progress = (new char[] { '/', '-', '\\', '|' }).GetEnumerator();
                var pbLabel = $"{logo} - ";
                var screenLines = ScreenLines(screen);
                screen.Alignment = TextAlignment.CENTER;
                screen.ContentType = ContentType.TEXT_AND_IMAGE;

                while (true)
                {
                    if (!progress.MoveNext())
                    {
                        progress.Reset();
                        progress.MoveNext();
                    }

                    yield return screen.WriteText(
                        string.Join("", Enumerable.Repeat("\n", screenLines / 2))
                        + pbLabel
                        + progress.Current
                    );
                }
            }
            static readonly StringBuilder StatusText = new StringBuilder();
            public static void Echo(string text)
            {
                StatusText.Clear();
                StatusText.AppendLine(text);
            }
            public static IEnumerable StatusMonitor(Program p)
            {
                var runtimeText = new StringBuilder();
                var runtime = p.Runtime;
                var progress = (new char[] { '/', '-', '\\', '|' }).GetEnumerator();

                while (true)
                {
                    if (!progress.MoveNext())
                    {
                        progress.Reset();
                        progress.MoveNext();
                    }

                    runtimeText.Clear();
                    runtimeText.AppendLine($"Runtime Info - {progress.Current}");
                    runtimeText.AppendLine("----------------------------");
                    runtimeText.AppendLine($"Last Run: {runtime.LastRunTimeMs}ms");
                    runtimeText.AppendLine($"Time Since Last Run: {runtime.TimeSinceLastRun.TotalMilliseconds}ms");
                    runtimeText.AppendLine($"Instruction Count: {runtime.CurrentInstructionCount}/{runtime.MaxInstructionCount}");
                    runtimeText.AppendLine($"Call depth Count: {runtime.CurrentCallChainDepth}/{runtime.MaxCallChainDepth}");
                    runtimeText.AppendLine();
                    runtimeText.AppendStringBuilder(StatusText);
                    p.Echo(runtimeText.ToString());
                    yield return null;
                }
            }

            public static string VectorToGPS(Vector3D coords, string name)
            {
                return string.Format("GPS:{0}:{1:R}:{2:R}:{3:R}:", name, coords.X, coords.Y, coords.Z);
            }

        }

        static class TaskManager
        {
            class Task
            {
                public IEnumerator Enumerator;
                public IEnumerable Ref;
                public TimeSpan Interval;
                public TimeSpan TimeSinceLastRun;
                public object TaskResult;
                public bool IsPaused;
                public bool IsOnce;
            }
            static readonly List<Task> tasks = new List<Task>();

            public static int AddTask(IEnumerable task, float intervalSeconds = 0, bool IsPaused = false, bool IsOnce = false)
            {
                tasks.Add(new Task
                {
                    Ref = task,
                    Enumerator = task.GetEnumerator(),
                    Interval = TimeSpan.FromSeconds(intervalSeconds),
                    TimeSinceLastRun = TimeSpan.Zero,
                    TaskResult = null,
                    IsPaused = IsPaused,
                    IsOnce = IsOnce
                });
                return tasks.Count - 1;
            }
            public static int AddTaskOnce(IEnumerable task, float intervalSeconds = 0, bool IsPaused = false)
            {
                return AddTask(task, intervalSeconds, IsPaused, true);
            }

            public static void PauseTask(int taskId, bool pause) => tasks[taskId].IsPaused = pause;

            public static T GetTaskResult<T>() => tasks.Select(t => t.TaskResult).OfType<T>().FirstOrDefault();
            public static TimeSpan CurrentTaskLastRun;
            public static void RunTasks(TimeSpan TimeSinceLastRun)
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

        class MenuManager
        {
            protected class OptionItem
            {
                public string Label;
                public Func<Menu, int, string> Value = (m, j) => null;
                public Action<Menu, int> Action = null;
                public Action<Menu, int, int> IncDec = null;
            }

            protected class Menu : List<OptionItem>
            {
                private int _selectedOption = 0;
                private int _activeOption = -1;
                private string _title;

                public Menu(string title) : base()
                {
                    _title = title;
                }

                public void Up()
                {
                    if (_activeOption > -1)
                    {
                        this[_activeOption].IncDec?.Invoke(this, _activeOption, -1);
                    }
                    else
                    {
                        _selectedOption = (_selectedOption - 1 + Count) % Count;
                    }
                }

                public void Down()
                {
                    if (_activeOption > -1)
                    {
                        this[_activeOption].IncDec?.Invoke(this, _activeOption, 1);
                    }
                    else
                    {
                        _selectedOption = (_selectedOption + 1) % Count;
                    }
                }

                public void Apply()
                {
                    _activeOption = _activeOption == _selectedOption ? -1 : this[_selectedOption].IncDec != null ? _selectedOption : -1;
                    this[_selectedOption].Action?.Invoke(this, _selectedOption);
                }

                public void Render(IMyTextSurface screen)
                {
                    screen.ContentType = ContentType.TEXT_AND_IMAGE;
                    screen.Alignment = TextAlignment.LEFT;
                    var screenLines = Util.ScreenLines(screen);
                    var screenColumns = Util.ScreenColumns(screen, '=');

                    var output = new StringBuilder();
                    output.AppendLine(_title);
                    output.AppendLine(string.Join("", Enumerable.Repeat("=", screenColumns)));

                    var pageSize = screenLines - 3;
                    var start = Math.Max(0, _selectedOption - pageSize / 2);

                    for (int i = start; i < Math.Min(Count, start + pageSize); i++)
                    {
                        var value = this[i].Value?.Invoke(this, i);
                        output.AppendLine($"{(i == _activeOption ? "-" : "")}{(i == _selectedOption ? "> " : "  ")}{this[i].Label}{(value != null ? $": {value}" : "")}");
                    }

                    var remainingLines = screenLines - output.ToString().Split('\n').Length;
                    for (int i = 0; i < remainingLines; i++)
                    {
                        output.AppendLine();
                    }
                    screenColumns = Util.ScreenColumns(screen, '-');
                    output.AppendLine(string.Join("", Enumerable.Repeat("-", screenColumns)));
                    screen.WriteText(output.ToString());
                }
            }

            protected readonly Stack<Menu> menuStack = new Stack<Menu>();

            protected readonly Program program;

            public MenuManager(Program program)
            {
                this.program = program;
            }
            public void Up() => menuStack.Peek().Up();
            public void Down() => menuStack.Peek().Down();
            public void Apply() => menuStack.Peek().Apply();
            public void Render(IMyTextSurface screen) => menuStack.Peek().Render(screen);

            protected Menu CreateMenu(string title)
            {
                var menu = new Menu(title);
                if (menuStack.Count > 0)
                {
                    menu.Add(new OptionItem { Label = "< Back", Action = (m, j) => { menuStack.Pop(); } });
                }
                menuStack.Push(menu);
                return menu;
            }

            public bool ProcessMenuCommands(string command = "")
            {
                switch (command.ToLower())
                {
                    case "up":
                        Up();
                        break;
                    case "apply":
                        Apply();
                        break;
                    case "down":
                        Down();
                        break;
                    default:
                        return false;
                }
                return true;
            }
        }
    }
}
