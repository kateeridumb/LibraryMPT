using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LibraryMPT.Services
{
    public class EmailService
    {
        public static long? LastSendDurationMs { get; private set; }
        public static DateTime? LastSendUtc { get; private set; }
        public static string? LastSendError { get; private set; }

        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _smtpUser;
        private readonly string _smtpPass;
        private readonly string _fromEmail;
        private readonly int _smtpTimeoutMs;
        private readonly SecureSocketOptions _socketOptions;
        private readonly SmtpEndpoint _primaryEndpoint;
        private readonly SmtpEndpoint? _fallbackEndpoint;

        public EmailService(IConfiguration configuration)
        {
            var smtpSettings = configuration.GetSection("SmtpSettings");
            _smtpHost = (smtpSettings["SmtpHost"] ?? throw new InvalidOperationException("SmtpHost не настроен")).Trim();
            _smtpPort = int.Parse(smtpSettings["SmtpPort"] ?? "587");
            _smtpUser = (smtpSettings["SmtpUser"] ?? throw new InvalidOperationException("SmtpUser не настроен")).Trim();
            _smtpPass = (smtpSettings["SmtpPass"] ?? throw new InvalidOperationException("SmtpPass не настроен")).Trim();
            _fromEmail = (smtpSettings["FromEmail"] ?? throw new InvalidOperationException("FromEmail не настроен")).Trim();
            _smtpTimeoutMs = int.TryParse(smtpSettings["SmtpTimeoutMs"], out var toMs) && toMs >= 5000 ? toMs : 120000;
            var sec = (smtpSettings["SmtpSocketSecurity"] ?? "").Trim();
            if (string.Equals(sec, "SslOnConnect", StringComparison.OrdinalIgnoreCase) || _smtpPort == 465)
            {
                _socketOptions = SecureSocketOptions.SslOnConnect;
            }
            else if (string.Equals(sec, "None", StringComparison.OrdinalIgnoreCase))
            {
                _socketOptions = SecureSocketOptions.None;
            }
            else if (string.Equals(sec, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                _socketOptions = SecureSocketOptions.Auto;
            }
            else
            {
                _socketOptions = SecureSocketOptions.StartTls;
            }

            _primaryEndpoint = new SmtpEndpoint(
                _smtpHost,
                _smtpPort,
                _smtpUser,
                _smtpPass,
                _socketOptions,
                _fromEmail);
            _fallbackEndpoint = BuildFallbackEndpoint(configuration.GetSection("SmtpFallbackSettings"));
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            var subject = "Восстановление пароля - Электронная библиотека";
            var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px; }}
        .button {{ display: inline-block; padding: 12px 30px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        .footer {{ text-align: center; margin-top: 20px; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h2>Восстановление пароля</h2>
        </div>
        <div class=""content"">
            <p>Здравствуйте!</p>
            <p>Вы запросили восстановление пароля для вашего аккаунта в электронной библиотеке.</p>
            <p>Для восстановления пароля перейдите по ссылке ниже:</p>
            <p style=""text-align: center;"">
                <a href=""{resetLink}"" class=""button"">Восстановить пароль</a>
            </p>
            <p>Если кнопка не работает, скопируйте и вставьте следующую ссылку в браузер:</p>
            <p style=""word-break: break-all; color: #667eea;"">{resetLink}</p>
            <p><strong>Важно:</strong> Ссылка действительна в течение 24 часов.</p>
            <p>Если вы не запрашивали восстановление пароля, просто проигнорируйте это письмо.</p>
        </div>
        <div class=""footer"">
            <p>С уважением,<br>Команда электронной библиотеки</p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync(toEmail, subject, body);
        }

        private async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var message = new MimeMessage();
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = body };

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await SendViaEndpointAsync(message, _primaryEndpoint);
                sw.Stop();
                LastSendDurationMs = sw.ElapsedMilliseconds;
                LastSendUtc = DateTime.UtcNow;
                LastSendError = null;
            }
            catch (Exception ex)
            {
                if (_fallbackEndpoint is not null)
                {
                    try
                    {
                        await SendViaEndpointAsync(message, _fallbackEndpoint);
                        LastSendDurationMs = null;
                        LastSendUtc = DateTime.UtcNow;
                        LastSendError = null;
                        return;
                    }
                    catch (Exception fallbackEx)
                    {
                        LastSendError = $"{ex.Message} | fallback: {fallbackEx.Message}";
                        throw new InvalidOperationException(
                            $"Ошибка при отправке email через основной SMTP ({_smtpHost}:{_smtpPort}) и резервный SMTP ({_fallbackEndpoint.Host}:{_fallbackEndpoint.Port}): {fallbackEx.Message}",
                            fallbackEx);
                    }
                }

                LastSendError = ex.Message;
                throw new InvalidOperationException(
                    $"Ошибка при отправке email (SMTP: {_smtpHost}:{_smtpPort}): {ex.Message}",
                    ex);
            }
        }

        private async Task SendViaEndpointAsync(MimeMessage message, SmtpEndpoint endpoint)
        {
            var appPassword = (endpoint.Password ?? string.Empty).Replace(" ", "");
            message.From.Clear();
            message.From.Add(MailboxAddress.Parse(endpoint.FromEmail));
            using var client = new MailKit.Net.Smtp.SmtpClient();
            client.Timeout = _smtpTimeoutMs;
            await client.ConnectAsync(endpoint.Host, endpoint.Port, endpoint.SocketOptions);
            await client.AuthenticateAsync(endpoint.User, appPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private static SmtpEndpoint? BuildFallbackEndpoint(IConfigurationSection section)
        {
            var host = section["SmtpHost"]?.Trim();
            var user = section["SmtpUser"]?.Trim();
            var pass = section["SmtpPass"]?.Trim();
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                return null;

            var port = int.TryParse(section["SmtpPort"], out var p) ? p : 465;
            var sec = (section["SmtpSocketSecurity"] ?? "").Trim();
            var socketOptions = string.Equals(sec, "StartTls", StringComparison.OrdinalIgnoreCase)
                ? SecureSocketOptions.StartTls
                : string.Equals(sec, "None", StringComparison.OrdinalIgnoreCase)
                    ? SecureSocketOptions.None
                    : string.Equals(sec, "Auto", StringComparison.OrdinalIgnoreCase)
                        ? SecureSocketOptions.Auto
                        : SecureSocketOptions.SslOnConnect;

            var fromEmail = section["FromEmail"]?.Trim();
            if (string.IsNullOrWhiteSpace(fromEmail))
                fromEmail = user;

            return new SmtpEndpoint(host, port, user, pass, socketOptions, fromEmail);
        }

        private sealed record SmtpEndpoint(
            string Host,
            int Port,
            string User,
            string Password,
            SecureSocketOptions SocketOptions,
            string FromEmail);

        public async Task SendStaffRegistrationEmailAsync(
            string toEmail,
            string firstName,
            string lastName,
            string username,
            string password,
            string roleName,
            string loginUrl)
        {
            var subject = "Регистрация в системе электронной библиотеки";
            var roleDisplayName = roleName switch
            {
                "Admin" => "Администратор",
                "Librarian" => "Библиотекарь",
                "InstitutionRepresentative" => "Представитель учебного заведения",
                _ => "Сотрудник"
            };

            var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px; }}
        .credentials-box {{ background: white; border: 2px solid #667eea; border-radius: 8px; padding: 20px; margin: 20px 0; }}
        .credential-item {{ margin: 10px 0; padding: 10px; background: #f0f0f0; border-radius: 5px; }}
        .credential-label {{ font-weight: 600; color: #667eea; }}
        .credential-value {{ font-family: 'Courier New', monospace; font-size: 1.1em; color: #333; word-break: break-all; }}
        .password-box {{ background: #fff3cd; border-left: 4px solid #f39c12; padding: 15px; margin: 15px 0; }}
        .footer {{ text-align: center; margin-top: 20px; color: #666; font-size: 12px; }}
        .warning {{ background: #f8d7da; border-left: 4px solid #dc3545; padding: 15px; margin: 15px 0; color: #721c24; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h2>Добро пожаловать в систему!</h2>
        </div>
        <div class=""content"">
            <p>Здравствуйте, <strong>{firstName} {lastName}</strong>!</p>
            <p>Ваш аккаунт был успешно создан администратором в системе электронной библиотеки.</p>
            
            <div class=""credentials-box"">
                <h3 style=""margin-top: 0; color: #667eea;"">Ваши данные для входа:</h3>
                
                <div class=""credential-item"">
                    <div class=""credential-label"">Роль:</div>
                    <div>{roleDisplayName}</div>
                </div>
                
                <div class=""credential-item"">
                    <div class=""credential-label"">Логин:</div>
                    <div class=""credential-value"">{username}</div>
                </div>
                
                <div class=""password-box"">
                    <div class=""credential-label"">🔑 Пароль:</div>
                    <div class=""credential-value"">{password}</div>
                </div>
            </div>

            <div class=""warning"">
                <strong>⚠️ Важно:</strong> Сохраните эти данные в безопасном месте. Рекомендуется изменить пароль после первого входа в систему.
            </div>

            <p>Для входа в систему перейдите по адресу: <a href=""{loginUrl}"">{loginUrl}</a></p>
            
            <p>Если у вас возникнут вопросы, обратитесь к администратору системы.</p>
        </div>
        <div class=""footer"">
            <p>С уважением,<br>Команда электронной библиотеки</p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync(toEmail, subject, body);
        }

        public async Task SendTwoFactorCodeEmailAsync(string toEmail, string code, string? firstName)
        {
            var subject = "Код двухфакторной аутентификации - Электронная библиотека";
            var displayName = string.IsNullOrWhiteSpace(firstName) ? "Пользователь" : firstName;
            var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px; }}
        .code-box {{ background: white; border: 3px solid #667eea; border-radius: 10px; padding: 30px; text-align: center; margin: 20px 0; }}
        .code {{ font-size: 36px; font-weight: bold; color: #667eea; letter-spacing: 8px; font-family: 'Courier New', monospace; }}
        .warning {{ background: #fff3cd; border-left: 4px solid #f39c12; padding: 15px; margin: 15px 0; color: #856404; }}
        .footer {{ text-align: center; margin-top: 20px; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h2>🔒 Код двухфакторной аутентификации</h2>
        </div>
        <div class=""content"">
            <p>Здравствуйте, <strong>{displayName}</strong>!</p>
            <p>Вы запросили вход в систему электронной библиотеки с двухфакторной аутентификацией.</p>
            
            <div class=""code-box"">
                <p style=""margin: 0 0 10px 0; color: #666;"">Ваш код подтверждения:</p>
                <div class=""code"">{code}</div>
            </div>

            <div class=""warning"">
                <strong>⚠️ Важно:</strong> Этот код действителен в течение 20 минут. Никому не сообщайте этот код!
            </div>

            <p>Если вы не запрашивали вход в систему, немедленно измените пароль и обратитесь к администратору.</p>
        </div>
        <div class=""footer"">
            <p>С уважением,<br>Команда электронной библиотеки</p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync(toEmail, subject, body);
        }
    }
}

