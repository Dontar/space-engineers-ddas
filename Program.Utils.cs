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
            class CacheValue
            {
                public int Age;
                public object Value;
                public object[] Dependency;
                public bool Decay()
                {
                    Age--;
                    return Age >= 0;
                }
                public CacheValue(int age, object value)
                {
                    Age = age;
                    Value = value;
                }
                public CacheValue(object[] dep, object value)
                {
                    Dependency = dep;
                    Value = value;
                }
            }
            static readonly Dictionary<string, CacheValue> _dependencyCache = new Dictionary<string, CacheValue>();

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

            public static R Of<R>(Func<R> f, string context, object[] dep)
            {
                if (_dependencyCache.Count > 1000) throw new Exception("Cache overflow");
                CacheValue value;
                if (_dependencyCache.TryGetValue(context, out value))
                {
                    if (value.Dependency.SequenceEqual(dep)) return (R)value.Value;
                }
                var result = f();
                _dependencyCache[context] = new CacheValue(dep, result);
                return result;
            }
            public static R Of<R>(Func<R> f, string context, int age)
            {
                if (_dependencyCache.Count > 1000) throw new Exception("Cache overflow");
                CacheValue value;
                if (_dependencyCache.TryGetValue(context, out value))
                {
                    if (value.Decay()) return (R)value.Value;
                }
                var result = f();
                _dependencyCache[context] = new CacheValue(age, result);
                return result;
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
                return GetScreens(b => IsTagged(b, screenTag));
            }

            public static IEnumerable<IMyTextSurface> GetScreens(Func<IMyTerminalBlock, bool> collect)
            {
                var screens = GetBlocks<IMyTerminalBlock>(b => (b is IMyTextSurface || HasScreens(b)) && collect(b));
                return screens.Select(s =>
                {
                    if (s is IMyTextSurface)
                        return s as IMyTextSurface;
                    var provider = s as IMyTextSurfaceProvider;
                    var regex = new System.Text.RegularExpressions.Regex(@"^@(\d+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
                    var match = regex.Match(s.CustomData);
                    if (match.Success)
                    {
                        var screenIndex = int.Parse(match.Groups[1].Value) - 1;
                        return provider.GetSurface(screenIndex);
                    }
                    return provider.GetSurface(0);
                });
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

            public static void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed, IMyGyro gyro, MatrixD worldMatrix)
            {
                ApplyGyroOverride(pitchSpeed, yawSpeed, rollSpeed, new IMyGyro[] { gyro }, worldMatrix);

            }

            public static void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed, IEnumerable<IMyGyro> gyros, MatrixD worldMatrix)
            {
                var rotationVec = new Vector3D(pitchSpeed, yawSpeed, rollSpeed);
                var relativeRotationVec = Vector3D.TransformNormal(rotationVec, worldMatrix);

                foreach (var g in gyros)
                {
                    var transformedRotationVec = Vector3D.TransformNormal(relativeRotationVec, Matrix.Transpose(g.WorldMatrix));
                    g.Pitch = (float)transformedRotationVec.X;
                    g.Yaw = (float)transformedRotationVec.Y;
                    g.Roll = (float)transformedRotationVec.Z;
                }
            }

            public static IEnumerable DisplayLogo(string logo, IMyTextSurface screen)
            {
                var progress = (new char[] { '/', '-', '\\', '|' }).GetEnumerator();
                var pbLabel = $"{logo} - ";

                while (true)
                {
                    if (!progress.MoveNext())
                    {
                        progress.Reset();
                        progress.MoveNext();
                    }
                    ;
                    var size = screen.MeasureStringInPixels(new StringBuilder(pbLabel), screen.Font, screen.FontSize);
                    screen.Alignment = TextAlignment.CENTER;
                    screen.ContentType = ContentType.TEXT_AND_IMAGE;
                    screen.WriteText(string.Join("", Enumerable.Repeat("\n", (int)(screen.SurfaceSize.Y / size.Y) / 2)) + pbLabel + progress.Current);
                    yield return null;
                }
            }
            public static StringBuilder StatusText = new StringBuilder();
            public static void Echo(string text)
            {
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
                    ;

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
                    StatusText.Clear();
                    yield return null;
                }
            }

        }

        static class TaskManager
        {
            public class Task
            {
                public IEnumerator Enumerator;
                public IEnumerable Ref;
                public TimeSpan Interval;
                public TimeSpan TimeSinceLastRun = TimeSpan.Zero;
                public object TaskResult = null;
                public bool IsPaused = false;
                public bool IsOnce = false;
            }
            static readonly List<Task> tasks = new List<Task>();

            public static Task AddTask(IEnumerable task, float intervalSeconds = 0)
            {
                Task item = new Task { Ref = task, Enumerator = task.GetEnumerator(), Interval = TimeSpan.FromSeconds(intervalSeconds) };
                tasks.Add(item);
                return item;
            }

            public static Task AddTaskOnce(IEnumerable task, float intervalSeconds = 0)
            {
                Task item = AddTask(task, intervalSeconds);
                item.IsOnce = true;
                return item;
            }

            public static TimeSpan CurrentTaskLastRun;
            public static List<object> TaskResults => tasks.Select(t => t.TaskResult).ToList();
            public static void RunTasks(TimeSpan TimeSinceLastRun)
            {
                var executionList = new List<Task>(tasks);
                for (int i = 0; i < executionList.Count; i++)
                {
                    var task = executionList[i];
                    if (task.IsPaused) continue;

                    task.TaskResult = null;
                    
                    task.TimeSinceLastRun += TimeSinceLastRun;
                    if (task.TimeSinceLastRun < task.Interval) continue;

                    CurrentTaskLastRun = task.TimeSinceLastRun;
                    if (!task.Enumerator.MoveNext())
                    {
                        if (task.IsOnce)
                        {
                            tasks.RemoveAt(i);
                            continue;
                        }
                        task.Enumerator = task.Ref.GetEnumerator();
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
                    var size = screen.SurfaceSize / screen.MeasureStringInPixels(new StringBuilder("="), screen.Font, screen.FontSize);

                    var output = new StringBuilder();
                    output.AppendLine(_title);
                    output.AppendLine(string.Join("", Enumerable.Repeat("=", (int)size.X)));

                    var pageSize = (int)size.Y - 3;
                    var start = Math.Max(0, _selectedOption - pageSize / 2);

                    for (int i = start; i < Math.Min(Count, start + pageSize); i++)
                    {
                        var value = this[i].Value?.Invoke(this, i);
                        output.AppendLine($"{(i == _activeOption ? "-" : "")}{(i == _selectedOption ? "> " : "  ")}{this[i].Label}{(value != null ? $": {value}" : "")}");
                    }

                    var remainingLines = (int)(size.Y - output.ToString().Split('\n').Length);
                    for (int i = 0; i < remainingLines; i++)
                    {
                        output.AppendLine();
                    }
                    size = screen.SurfaceSize / screen.MeasureStringInPixels(new StringBuilder("-"), screen.Font, screen.FontSize);
                    output.AppendLine(string.Join("", Enumerable.Repeat("-", (int)size.X)));
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
