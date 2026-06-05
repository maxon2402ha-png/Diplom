using System;
using System.Linq;

namespace КР_Ханников.Services
{
    public enum PasswordStrength { Weak, Medium, Strong }

    public static class PasswordValidator
    {
        public static (bool IsValid, string[] Errors) Validate(string password)
        {
            var errors = new System.Collections.Generic.List<string>();

            if (string.IsNullOrEmpty(password) || password.Length < 8)
                errors.Add("Минимум 8 символов");

            if (!password.Any(char.IsUpper))
                errors.Add("Минимум одна заглавная буква");

            if (!password.Any(char.IsLower))
                errors.Add("Минимум одна строчная буква");

            if (!password.Any(char.IsDigit))
                errors.Add("Минимум одна цифра");

            if (!password.Any(c => !char.IsLetterOrDigit(c)))
                errors.Add("Минимум один специальный символ (!@#$%^&* и т.п.)");

            return (errors.Count == 0, errors.ToArray());
        }

        public static void ValidateOrThrow(string password)
        {
            var (isValid, errors) = Validate(password);
            if (!isValid)
                throw new ArgumentException("Пароль не соответствует требованиям:\n• " + string.Join("\n• ", errors));
        }

        public static PasswordStrength GetStrength(string password)
        {
            if (string.IsNullOrEmpty(password)) return PasswordStrength.Weak;

            int score = 0;

            if (password.Length >= 8) score++;
            if (password.Length >= 12) score++;
            if (password.Any(char.IsUpper)) score++;
            if (password.Any(char.IsLower)) score++;
            if (password.Any(char.IsDigit)) score++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) score++;

            return score switch
            {
                >= 5 => PasswordStrength.Strong,
                >= 3 => PasswordStrength.Medium,
                _ => PasswordStrength.Weak
            };
        }

        public static string GetStrengthLabel(PasswordStrength strength) => strength switch
        {
            PasswordStrength.Strong => "Надёжный",
            PasswordStrength.Medium => "Средний",
            _ => "Слабый"
        };
    }
}
