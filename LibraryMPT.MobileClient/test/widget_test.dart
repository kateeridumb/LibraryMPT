import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:library_mpt_mobile_client/src/app.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('App renders login screen', (WidgetTester tester) async {
    SharedPreferences.setMockInitialValues(<String, Object>{});

    await tester.pumpWidget(const LibraryMptMobileApp());
    await tester.pump();
    await tester.pump(const Duration(seconds: 1));

    expect(find.text('Электронная библиотека'), findsOneWidget);
  });
}
