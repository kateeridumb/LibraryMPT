import '../../common/models.dart';

sealed class LoginStepResult {
  const LoginStepResult();
}

final class LoginAuthenticated extends LoginStepResult {
  LoginAuthenticated(this.session);

  final UserSession session;
}

final class LoginNeedsTwoFactor extends LoginStepResult {
  LoginNeedsTwoFactor(this.twoFactorToken);

  final String twoFactorToken;
}
