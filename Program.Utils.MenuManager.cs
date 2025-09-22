using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game.GUI.TextPanel;

namespace IngameScript
{
    partial class Program
    {
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
