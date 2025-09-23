using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
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
                    var regex = new System.Text.RegularExpressions.Regex(@"^(\S*)@(\d+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
                    var match = regex.Match(s.CustomData);
                    if (match.Success && match.Groups[1].Value == screenTag)
                    {
                        var screenIndex = int.Parse(match.Groups[2].Value) - 1;
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
                ApplyGyroOverride(pitchSpeed, yawSpeed, rollSpeed, power, new[] { gyro }, worldMatrix);

            }

            public static void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed, float power, IEnumerable<IMyGyro> gyros, MatrixD worldMatrix)
            {
                var rotationVec = new Vector3D(pitchSpeed, yawSpeed, rollSpeed);
                var relativeRotationVec = Vector3D.TransformNormal(rotationVec, worldMatrix);

                foreach (var g in gyros)
                {
                    if (g.GyroPower != power) g.GyroPower = power;
                    g.GyroOverride = true;
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
        }
    }
}
