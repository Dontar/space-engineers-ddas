using System.Linq;
using System.Text;

namespace IngameScript
{
    partial class Program
    {
        class InfoDisplay
        {
            public StringBuilder Sb;
            int _lineLength;

            public InfoDisplay(StringBuilder stringBuilder, int lineLength)
            {
                _lineLength = lineLength;
                Sb = stringBuilder;
            }

            public void Sep() => Label("");

            public void Label(string label, char filler = '=')
            {
                var prefix = string.Join("", Enumerable.Repeat(filler.ToString(), 2));
                var suffix = string.Join("", Enumerable.Repeat(filler.ToString(), _lineLength - label.Length - 2));
                Sb.AppendLine(prefix + label + suffix);
            }
            public void Row(string label, object value, string format = "", string unitType = "")
            {
                int width = _lineLength / 2;
                var labelWidth = width - 1;
                var valueWidth = label.Length > labelWidth ? width - unitType.Length - (label.Length - labelWidth) : width - unitType.Length;
                format = string.IsNullOrEmpty(format) ? "" : ":" + format;

                Sb.AppendFormat(" {0,-" + labelWidth + "}{1," + valueWidth + format + "}" + unitType + "\n", label, value);
            }

        }
    }
}
