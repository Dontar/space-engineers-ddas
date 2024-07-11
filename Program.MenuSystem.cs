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
                menu.Add(new OptionItem { Label = "Systems >", Action = (m, i) => BuildSystemsMenu() });
                menu.Add(new OptionItem { Label = "Path recorder >", Action = (m, i) => BuildRecorderMenu() });
                menu.Add(new OptionItem { Label = "Lower vehicle", Action = (m, i) => TaskManager.AddTaskOnce(program.LowModeTask()) });
                menu.Add(new OptionItem { Label = "Raise vehicle", Action = (m, i) => TaskManager.AddTaskOnce(program.HighModeTask()) });
            }
            void BuildSystemsMenu()
            {
                var menu = CreateMenu("Systems");
                menu.Add(new OptionItem
                {
                    Label = "Add wheels",
                    Value = (m, i) => program._AddWheelsTask.IsPaused.ToString(),
                    Action = (m, i) => program._AddWheelsTask.IsPaused = !program._AddWheelsTask.IsPaused
                });
                menu.Add(new OptionItem
                {
                    Label = "Suspension strength",
                    Value = (m, i) => program._SuspensionStrengthTask.IsPaused.ToString(),
                    Action = (m, i) => program._SuspensionStrengthTask.IsPaused = !program._SuspensionStrengthTask.IsPaused
                });
                menu.Add(new OptionItem
                {
                    Label = "Suspension power",
                    Value = (m, i) => program._PowerTask.IsPaused.ToString(),
                    Action = (m, i) => program._PowerTask.IsPaused = !program._PowerTask.IsPaused
                });
                menu.Add(new OptionItem
                {
                    Label = "Suspension friction",
                    Value = (m, i) => program._FrictionTask.IsPaused.ToString(),
                    Action = (m, i) => program._FrictionTask.IsPaused = !program._FrictionTask.IsPaused
                });
                menu.Add(new OptionItem
                {
                    Label = "Sub Suspension strength",
                    Value = (m, i) => program._SubSuspensionStrengthTask.IsPaused.ToString(),
                    Action = (m, i) => program._SubSuspensionStrengthTask.IsPaused = !program._SubSuspensionStrengthTask.IsPaused
                });
                menu.Add(new OptionItem
                {
                    Label = "Stop lights",
                    Value = (m, i) => program._StopLightsTask.IsPaused.ToString(),
                    Action = (m, i) => program._StopLightsTask.IsPaused = !program._StopLightsTask.IsPaused
                });
            }
            void BuildRecorderMenu()
            {
                var menu = CreateMenu("Path recorder");
                menu.Add(new OptionItem
                {
                    Label = "Record path",
                    Action = (m, i) => TaskManager.AddTaskOnce(program.RecordPathTask())
                });
                menu.Add(new OptionItem
                {
                    Label = "Stop recording",
                    Action = (m, i) => program.gridProps.Recording = false
                });
                menu.Add(new OptionItem
                {
                    Label = "Import path",
                    Action = (m, i) => TaskManager.AddTaskOnce(program.ImportPathTask())
                });
                menu.Add(new OptionItem
                {
                    Label = "Reverse path",
                    Action = (m, i) => TaskManager.AddTaskOnce(program.ReversePathTask())
                });
                menu.Add(new OptionItem
                {
                    Label = "Export path",
                    Action = (m, i) => TaskManager.AddTaskOnce(program.ExportPathTask())
                });
            }
        }
    }
}
