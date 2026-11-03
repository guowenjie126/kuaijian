using System.Globalization;

namespace KuaiJian.WinUI;

public static class CalculatorEngine
{
    public static double Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) throw new FormatException("请输入表达式。");
        if (expression.Length > 256) throw new FormatException("表达式过长。");

        var parser = new Parser(expression);
        var result = parser.ParseExpression();
        parser.SkipWhitespace();
        if (!parser.AtEnd) throw new FormatException("表达式中包含无法识别的内容。");
        if (!double.IsFinite(result)) throw new ArithmeticException("计算结果超出有效范围。");
        return result;
    }

    private sealed class Parser(string expression)
    {
        private int _position;
        public bool AtEnd => _position >= expression.Length;

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(expression[_position])) _position++;
        }

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Take('+')) value += ParseTerm();
                else if (Take('-') || Take('−')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (Take('*') || Take('×')) value *= ParseUnary();
                else if (Take('/') || Take('÷'))
                {
                    var divisor = ParseUnary();
                    value = divisor == 0 ? 0 : value / divisor;
                }
                else return value;
            }
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (Take('+')) return ParseUnary();
            if (Take('-') || Take('−')) return -ParseUnary();
            var value = ParsePrimary();
            while (true)
            {
                SkipWhitespace();
                if (!Take('%')) return value;
                value /= 100;
            }
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (Take('('))
            {
                var groupedValue = ParseExpression();
                SkipWhitespace();
                if (!Take(')')) throw new FormatException("括号不匹配。");
                return groupedValue;
            }

            var start = _position;
            var hasDigit = false;
            while (!AtEnd && char.IsAsciiDigit(expression[_position])) { _position++; hasDigit = true; }
            if (!AtEnd && expression[_position] == '.')
            {
                _position++;
                while (!AtEnd && char.IsAsciiDigit(expression[_position])) { _position++; hasDigit = true; }
            }
            if (!hasDigit) throw new FormatException("表达式格式不正确。");
            if (!double.TryParse(expression.AsSpan(start, _position - start), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new FormatException("数字格式不正确。");
            return value;
        }

        private bool Take(char value)
        {
            if (AtEnd || expression[_position] != value) return false;
            _position++;
            return true;
        }
    }
}
