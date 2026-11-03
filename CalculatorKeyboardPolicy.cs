namespace KuaiJian.WinUI;

public static class CalculatorKeyboardPolicy
{
    public static string? TokenFor(int virtualKey, bool shiftPressed)
    {
        if (shiftPressed)
        {
            var shiftedToken = virtualKey switch
            {
                0x30 => ")", // VK_0
                0x35 => "%", // VK_5
                0x38 => "×", // VK_8
                0x39 => "(", // VK_9
                _ => null
            };
            if (shiftedToken is not null) return shiftedToken;
        }

        return virtualKey switch
        {
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
            >= 0x60 and <= 0x69 => ((char)('0' + virtualKey - 0x60)).ToString(),
            0x6B or 0xBB => "+", // VK_ADD / VK_OEM_PLUS
            0x6D or 0xBD => "−", // VK_SUBTRACT / VK_OEM_MINUS
            0x6A => "×", // VK_MULTIPLY
            0x6F or 0xBF => "÷", // VK_DIVIDE / VK_OEM_2
            0x6E or 0xBE => ".", // VK_DECIMAL / VK_OEM_PERIOD
            0x0D => "=", // VK_RETURN
            0x08 => "⌫", // VK_BACK
            _ => null
        };
    }
}
