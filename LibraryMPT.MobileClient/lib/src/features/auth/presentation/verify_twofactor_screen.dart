import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../data/auth_repository.dart';

class VerifyTwoFactorScreen extends StatefulWidget {
  const VerifyTwoFactorScreen({
    required this.twoFactorToken,
    super.key,
  });

  final String twoFactorToken;

  @override
  State<VerifyTwoFactorScreen> createState() => _VerifyTwoFactorScreenState();
}

class _VerifyTwoFactorScreenState extends State<VerifyTwoFactorScreen> {
  final _codeController = TextEditingController();

  bool _loading = false;
  bool _sendingCode = false;
  String? _error;
  String? _info;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _sendCode());
  }

  @override
  void dispose() {
    _codeController.dispose();
    super.dispose();
  }

  Future<void> _sendCode() async {
    setState(() {
      _sendingCode = true;
      _error = null;
    });
    try {
      await context.read<AuthRepository>().requestTwoFactorCode(widget.twoFactorToken);
      if (!mounted) {
        return;
      }
      setState(() {
        _info = 'Код отправлен на email, привязанный к аккаунту.';
      });
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
      });
    } catch (_) {
      setState(() {
        _error = 'Не удалось отправить код.';
      });
    } finally {
      if (mounted) {
        setState(() {
          _sendingCode = false;
        });
      }
    }
  }

  Future<void> _submit() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      await context.read<AuthRepository>().completeTwoFactorLogin(
            twoFactorToken: widget.twoFactorToken,
            code: _codeController.text.trim(),
          );
      if (!mounted) {
        return;
      }
      Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
      });
    } catch (_) {
      setState(() {
        _error = 'Не удалось подтвердить код.';
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
      appBar: AppBar(
        title: const Text('Подтверждение входа'),
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(20),
          children: <Widget>[
            const Text(
              'Для аккаунта включена двухфакторная аутентификация. Введите код из письма.',
              style: TextStyle(color: AppThemeColors.textLight),
            ),
            const SizedBox(height: 18),
            TextField(
              controller: _codeController,
              keyboardType: TextInputType.number,
              decoration: const InputDecoration(
                labelText: 'Код из email',
                prefixIcon: Icon(Icons.pin_outlined),
              ),
            ),
            const SizedBox(height: 14),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                onPressed: _sendingCode ? null : _sendCode,
                icon: _sendingCode
                    ? const SizedBox.square(
                        dimension: 16,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.mail_outline),
                label: Text(_sendingCode ? 'Отправка…' : 'Отправить код повторно'),
              ),
            ),
            if (_info != null) ...<Widget>[
              const SizedBox(height: 8),
              Text(_info!, style: TextStyle(color: Theme.of(context).colorScheme.primary)),
            ],
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
                    : const Text('Подтвердить'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
