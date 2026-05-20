import 'package:shared_preferences/shared_preferences.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/api_exception.dart';
import '../../common/models.dart';
import 'login_step_result.dart';

class AuthRepository {
  AuthRepository(this._apiClient);

  static const _tokenKey = 'access_token';
  final ApiClient _apiClient;

  Future<LoginStepResult> signIn({
    required String username,
    required String password,
  }) async {
    final loginResponse = await _apiClient.postJson(
      '/api/account/login',
      body: <String, dynamic>{
        'username': username,
        'password': password,
      },
    );

    final success = loginResponse['success'] as bool? ?? false;
    if (!success) {
      final error = (loginResponse['error'] ?? 'Ошибка авторизации.').toString();
      throw ApiException(error);
    }

    final roleName = (loginResponse['roleName'] ?? '').toString();
    if (roleName.trim().toLowerCase() != 'student') {
      throw ApiException('Мобильное приложение доступно только для роли Student.');
    }

    final requiresTwoFactor = loginResponse['requiresTwoFactor'] as bool? ?? false;
    if (requiresTwoFactor) {
      final twoFactorToken = (loginResponse['twoFactorToken'] ?? '').toString().trim();
      if (twoFactorToken.isEmpty) {
        throw ApiException('Сервер не вернул токен для двухфакторного входа.');
      }
      return LoginNeedsTwoFactor(twoFactorToken);
    }

    final accessToken = (loginResponse['accessToken'] ?? '').toString().trim();
    if (accessToken.isEmpty) {
      throw ApiException('API не вернул access token.');
    }

    await _persistAccessToken(accessToken);

    return LoginAuthenticated(
      UserSession(
        username: (loginResponse['username'] ?? username).toString(),
        roleName: roleName,
        token: accessToken,
      ),
    );
  }

  Future<void> requestTwoFactorCode(String twoFactorToken) async {
    final res = await _apiClient.postJson(
      '/api/account/request-twofactor-code',
      body: <String, dynamic>{'twoFactorToken': twoFactorToken},
    );
    final ok = res['success'] as bool? ?? false;
    if (!ok) {
      throw ApiException((res['message'] ?? 'Не удалось отправить код.').toString());
    }
  }

  Future<UserSession> completeTwoFactorLogin({
    required String twoFactorToken,
    required String code,
  }) async {
    final res = await _apiClient.postJson(
      '/api/account/complete-twofactor-login',
      body: <String, dynamic>{
        'twoFactorToken': twoFactorToken,
        'code': code,
      },
    );
    final ok = res['success'] as bool? ?? false;
    if (!ok) {
      throw ApiException((res['message'] ?? 'Неверный код.').toString());
    }

    final accessToken = (res['accessToken'] ?? '').toString().trim();
    if (accessToken.isEmpty) {
      throw ApiException('API не вернул access token после 2FA.');
    }

    final username = (res['username'] ?? '').toString();
    final role = (res['roleName'] ?? '').toString();
    if (role.trim().toLowerCase() != 'student') {
      throw ApiException('Мобильное приложение доступно только для роли Student.');
    }

    await _persistAccessToken(accessToken);

    return UserSession(username: username, roleName: role, token: accessToken);
  }

  Future<void> requestPasswordResetEmail(String email) async {
    final res = await _apiClient.postJson(
      '/api/account/forgot-password',
      body: <String, dynamic>{
        'email': email,
        'sendEmail': true,
      },
    );
    final err = _readForgotPasswordError(res);
    if (err != null && err.isNotEmpty) {
      throw ApiException(err);
    }
    final emailSent = _readForgotPasswordEmailSent(res);
    if (emailSent == false) {
      throw ApiException(
        'Не удалось отправить письмо. Проверьте настройки SMTP в приложении API и доступ к почтовому серверу из сети, где запущен API.',
      );
    }
  }

  /// Ответ API может быть в camelCase или PascalCase (зависит от сериализатора).
  static String? _readForgotPasswordError(Map<String, dynamic> res) {
    final v = res['error'] ?? res['Error'];
    if (v == null) {
      return null;
    }
    final s = v.toString().trim();
    return s.isEmpty ? null : s;
  }

  static bool? _readForgotPasswordEmailSent(Map<String, dynamic> res) {
    final v = res['emailSent'] ?? res['EmailSent'];
    if (v == null) {
      return null;
    }
    if (v is bool) {
      return v;
    }
    if (v is num) {
      return v != 0;
    }
    final lower = v.toString().trim().toLowerCase();
    if (lower == 'true') {
      return true;
    }
    if (lower == 'false') {
      return false;
    }
    return null;
  }

  Future<void> validateResetToken(String token) async {
    final res = await _apiClient.getJson(
      '/api/account/validate-reset-token',
      queryParameters: <String, String>{'token': token},
    );
    final ok = res['success'] as bool? ?? false;
    if (!ok) {
      throw ApiException(
        (res['message'] ?? 'Ссылка восстановления недействительна или устарела.').toString(),
      );
    }
  }

  Future<void> resetPassword({
    required String token,
    required String password,
  }) async {
    final res = await _apiClient.postJson(
      '/api/account/reset-password',
      body: <String, dynamic>{
        'token': token,
        'password': password,
      },
    );
    final ok = res['success'] as bool? ?? false;
    if (!ok) {
      throw ApiException((res['message'] ?? 'Не удалось сменить пароль.').toString());
    }
  }

  Future<void> _persistAccessToken(String accessToken) async {
    _apiClient.setAccessToken(accessToken);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_tokenKey, accessToken);
  }

  Future<String?> restoreToken() async {
    final prefs = await SharedPreferences.getInstance();
    final token = prefs.getString(_tokenKey);
    if (token != null && token.isNotEmpty) {
      _apiClient.setAccessToken(token);
      return token;
    }
    return null;
  }

  Future<void> signOut() async {
    _apiClient.setAccessToken(null);
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_tokenKey);
  }
}
