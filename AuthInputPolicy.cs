namespace KuaiJian.WinUI;

public static class AuthInputPolicy
{
    public const int MaxPhoneInputLength = 32;
    public const int MaxEmailLength = 254;
    public const int MaxPasswordLength = 256;

    public static bool IsPasswordLengthValid(string? password, int minimumLength = 1) =>
        password is not null
        && password.Length >= minimumLength
        && password.Length <= MaxPasswordLength
        && !password.Any(char.IsControl);

    public static bool IsEmailLengthValid(string? email) =>
        email is null || email.Trim().Length <= MaxEmailLength;

    public static bool IsPhoneInputLengthValid(string? phone) =>
        phone is not null && phone.Length <= MaxPhoneInputLength;
}
