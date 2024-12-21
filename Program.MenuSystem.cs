using System.Collections.Generic;

namespace IngameScript
{
    partial class Program
    {
        readonly MenuSystem menuSystem;
        class MenuSystem : MenuManager
        {
            public MenuSystem(Program program) : base(program)
            {
                BuildMainMenu();
            }
            void BuildMainMenu()
            {
                var menu = CreateMenu("DDAS");
                menu.AddArray(new[]
                {
                    new OptionItem { Label = "Systems >", Action = (m, i) => BuildSystemsMenu() },
                    new OptionItem { Label = "Path recorder >", Action = (m, i) => BuildRecorderMenu() },
                    new OptionItem { Label = "Toggle hight", Action = (m, i) => TaskManager.AddTaskOnce(program.ToggleHightModeTask()) }
                });
            }
            void BuildSystemsMenu()
            {
                var menu = CreateMenu("Systems");
                menu.AddArray(new[] 
                {
                    new OptionItem
                    {
                        Label = "Add wheels",
                        Value = (m, i) => (!program._AddWheelsTask.IsPaused).ToString(),
                        Action = (m, i) => program._AddWheelsTask.IsPaused = !program._AddWheelsTask.IsPaused
                    },
                    new OptionItem
                    {
                        Label = "Suspension strength",
                        Value = (m, i) => (!program._SuspensionStrengthTask.IsPaused).ToString(),
                        Action = (m, i) => program._SuspensionStrengthTask.IsPaused = !program._SuspensionStrengthTask.IsPaused
                    },
                    new OptionItem
                    {
                        Label = "Suspension power",
                        Value = (m, i) => (!program._PowerTask.IsPaused).ToString(),
                        Action = (m, i) => program._PowerTask.IsPaused = !program._PowerTask.IsPaused
                    },
                    new OptionItem
                    {
                        Label = "Suspension friction",
                        Value = (m, i) => (!program._FrictionTask.IsPaused).ToString(),
                        Action = (m, i) => program._FrictionTask.IsPaused = !program._FrictionTask.IsPaused
                    },
                    new OptionItem
                    {
                        Label = "Sub Suspension strength",
                        Value = (m, i) => (!program._SubSuspensionStrengthTask.IsPaused).ToString(),
                        Action = (m, i) => program._SubSuspensionStrengthTask.IsPaused = !program._SubSuspensionStrengthTask.IsPaused
                    },
                    new OptionItem
                    {
                        Label = "Stop lights",
                        Value = (m, i) => (!program._StopLightsTask.IsPaused).ToString(),
                        Action = (m, i) => program._StopLightsTask.IsPaused = !program._StopLightsTask.IsPaused
                    }
                });
            }
            void BuildRecorderMenu()
            {
                var menu = CreateMenu("Path recorder");
                menu.AddArray(new[]
                {
                    new OptionItem
                    {
                        Label = "Record path",
                        Action = (m, i) => TaskManager.AddTaskOnce(program.RecordPathTask())
                    },
                    new OptionItem
                    {
                        Label = "Stop recording",
                        Action = (m, i) => program.gridProps.Recording = false
                    },
                    new OptionItem
                    {
                        Label = "Import path",
                        Action = (m, i) => TaskManager.AddTaskOnce(program.ImportPathTask())
                    },
                    new OptionItem
                    {
                        Label = "Reverse path",
                        Action = (m, i) => TaskManager.AddTaskOnce(program.ReversePathTask())
                    },
                    new OptionItem
                    {
                        Label = "Export path",
                        Action = (m, i) => TaskManager.AddTaskOnce(program.ExportPathTask())
                    }
                });
            }
        }
    }
}
