import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../core/theme/app_theme.dart';
import '../../../core/network/api_exception.dart';
import '../data/auth_repository.dart';
import '../data/login_step_result.dart';
import 'forgot_password_screen.dart';
import 'verify_twofactor_screen.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({required this.onLoggedIn, super.key});

  final VoidCallback onLoggedIn;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _usernameController = TextEditingController();
  final _passwordController = TextEditingController();

  bool _loading = false;
  String? _error;

  @override
  void dispose() {
    _usernameController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _signIn() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final step = await context.read<AuthRepository>().signIn(
            username: _usernameController.text.trim(),
            password: _passwordController.text,
          );

      if (!mounted) {
        return;
      }

      switch (step) {
        case LoginAuthenticated():
          widget.onLoggedIn();
        case LoginNeedsTwoFactor(:final twoFactorToken):
          final completed = await Navigator.of(context).push<bool>(
                MaterialPageRoute<bool>(
                  builder: (_) => VerifyTwoFactorScreen(twoFactorToken: twoFactorToken),
                ),
              ) ??
              false;
          if (!mounted) {
            return;
          }
          if (completed) {
            widget.onLoggedIn();
          }
      }
    } on ApiException catch (error) {
      setState(() {
        _error = error.message;
      });
    } catch (_) {
      setState(() {
        _error = 'Не удалось выполнить вход.';
      });
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Stack(
          children: <Widget>[
            Container(
              height: 250,
              decoration: const BoxDecoration(gradient: AppThemeColors.primaryGradient),
            ),
            ListView(
              padding: const EdgeInsets.fromLTRB(20, 36, 20, 24),
              children: <Widget>[
                Row(
                  children: <Widget>[
                    const Icon(Icons.local_library, color: Colors.white, size: 30),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        'Электронная библиотека',
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          color: Colors.white,
                          fontSize: 28,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                const Text(
                  'Клиентское мобильное приложение',
                  style: TextStyle(color: Colors.white70, fontSize: 14),
                ),
                const SizedBox(height: 26),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(18),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Text(
                          'Вход',
                          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                                fontWeight: FontWeight.w800,
                              ),
                        ),
                        const SizedBox(height: 6),
                        const Text(
                          'Доступ только для пользователей роли Student',
                          style: TextStyle(color: AppThemeColors.textLight),
                        ),
                        const SizedBox(height: 18),
                        TextField(
                          controller: _usernameController,
                          decoration: const InputDecoration(
                            labelText: 'Username',
                            prefixIcon: Icon(Icons.person_outline),
                          ),
                        ),
                        const SizedBox(height: 12),
                        TextField(
                          controller: _passwordController,
                          obscureText: true,
                          decoration: const InputDecoration(
                            labelText: 'Password',
                            prefixIcon: Icon(Icons.lock_outline),
                          ),
                        ),
                        const SizedBox(height: 18),
                        SizedBox(
                          width: double.infinity,
                          child: FilledButton(
                            onPressed: _loading ? null : _signIn,
                            child: _loading
                                ? const SizedBox.square(
                                    dimension: 18,
                                    child: CircularProgressIndicator(strokeWidth: 2),
                                  )
                                : const Text('Войти в библиотеку'),
                          ),
                        ),
                        const SizedBox(height: 8),
                        Align(
                          alignment: Alignment.centerLeft,
                          child: TextButton(
                            onPressed: _loading
                                ? null
                                : () {
                                    Navigator.of(context).push<void>(
                                      MaterialPageRoute<void>(
                                        builder: (_) => const ForgotPasswordScreen(),
                                      ),
                                    );
                                  },
                            child: const Text('Забыли пароль?'),
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
                              style: TextStyle(
                                color: Theme.of(context).colorScheme.onErrorContainer,
                              ),
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
