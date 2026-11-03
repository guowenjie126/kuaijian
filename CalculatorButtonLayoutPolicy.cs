namespace KuaiJian.WinUI;

public static class CalculatorButtonLayoutPolicy
{
    private static IReadOnlyList<string> Row(params string[] labels) => Array.AsReadOnly(labels);

    public static IReadOnlyList<IReadOnlyList<string>> ButtonRows { get; } =
        Array.AsReadOnly(new IReadOnlyList<string>[]
        {
            Row("AC", "⌫", "%", "÷"),
            Row("7", "8", "9", "×"),
            Row("4", "5", "6", "−"),
            Row("1", "2", "3", "+"),
            Row("±", "0", ".", "=")
        });
}
