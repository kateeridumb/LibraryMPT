import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/password_rules.dart';
import '../data/auth_repository.dart';

class ResetPasswordScreen extends StatefulWidget {
  const ResetPasswordScreen({super.key});

  @override
  State<ResetPasswordScreen> createState() => _ResetPasswordScreenState();
}

class _ResetPasswordScreenState extends State<ResetPasswordScreen> {
  final _tokenController = TextEditingController();
  final _passwordController = TextEditingController();
  final _confirmController = TextEditingController();

  bool _loading = false;
  String? _error;

  @override
  void dispose() {
    _tokenController.dispose();
    _passwordController.dispose();
    _confirmController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final token = _tokenController.text.trim();
    final password = _passwordController.text;
    final confirm = _confirmController.text;

    if (token.isEmpty) {
      setState(() => _error = 'Укажите токен из письма.');
      return;
    }
    if (password != confirm) {
      setState(() => _error = 'Пароли не совпадают.');
      return;
    }

    String? validationMessage;
    final passwordOk = validatePasswordRequirements(password, (m) => validationMessage = m);
    if (!passwordOk) {
      setState(() => _error = validationMessage ?? 'Некорректный пароль.');
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final auth = context.read<AuthRepository>();
      await auth.validateResetToken(token);
      await auth.resetPassword(token: token, password: password);
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Пароль успешно изменён. Войдите с новым паролем.')),
      );
      Navigator.of(context).pop();
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (_) {
      setState(() => _error = 'Не удалось сменить пароль.');
    } finally {
      if (mounted) {
        setState(() => _loading = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Новый пароль'),
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(20),
          children: <Widget>[
            const Text(
              'Токен указан в ссылке из письма (параметр после token=) или приходит отдельно.',
              style: TextStyle(color: AppThemeColors.textLight),
            ),
            const SizedBox(height: 18),
            TextField(
              controller: _tokenController,
              decoration: const InputDecoration(
                labelText: 'Токен восстановления',
                prefixIcon: Icon(Icons.vpn_key_outlined),
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _passwordController,
              obscureText: true,
              decoration: const InputDecoration(
                labelText: 'Новый пароль',
                prefixIcon: Icon(Icons.lock_outline),
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _confirmController,
              obscureText: true,
              decoration: const InputDecoration(
                labelText: 'Подтверждение пароля',
                prefixIcon: Icon(Icons.lock_outline),
              ),
            ),
            if (_error != null) ...<Widget>[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: Theme.of(context).colorScheme.errorContainer,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Text(
                  _error!,
                  style: TextStyle(color: Theme.of(context).colorScheme.onErrorContainer),
                ),
              ),
            ],
            const SizedBox(height: 22),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: _loading ? null : _submit,
                child: _loading
                    ? const SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text('Сохранить пароль'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
