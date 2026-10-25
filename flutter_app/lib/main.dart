import 'package:flutter/material.dart';
import 'api.dart';
import 'push.dart';
import 'screens/login.dart';
import 'screens/admin_shell.dart';
import 'screens/student_shell.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Api.load();
  await Push.init();
  Push.register();
  runApp(const CoachingApp());
}

class CoachingApp extends StatelessWidget {
  const CoachingApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
        navigatorKey: navKey,
        title: 'Coaching Manager',
        debugShowCheckedModeBanner: false,
        theme: ThemeData(colorSchemeSeed: const Color(0xFF1565C0), useMaterial3: true),
        home: Api.token == null ? const LoginScreen() : (Api.role == 'Admin' ? const AdminShell() : const StudentShell()),
      );
}
