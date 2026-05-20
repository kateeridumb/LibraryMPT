bool validatePasswordRequirements(String password, void Function(String message) onError) {
  if (password.length < 12) {
    onError('Пароль должен быть не менее 12 символов.');
    return false;
  }
  if (!password.contains(RegExp('[A-Z]'))) {
    onError('Пароль должен содержать хотя бы одну заглавную букву.');
    return false;
  }
  if (!password.contains(RegExp('[a-z]'))) {
    onError('Пароль должен содержать хотя бы одну строчную букву.');
    return false;
  }
  if (!password.contains(RegExp('[0-9]'))) {
    onError('Пароль должен содержать хотя бы одну цифру.');
    return false;
  }
  return true;
}
