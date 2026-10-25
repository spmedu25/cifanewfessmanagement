import 'package:flutter/material.dart';
import '../api.dart';
import '../push.dart';
import '../ui.dart';
import 'admin_shell.dart';
import 'student_shell.dart';
import 'register.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  bool admin = false, busy = false;
  final user = TextEditingController(), pass = TextEditingController();

  Future<void> login() async {
    setState(() => busy = true);
    try {
      final r = admin
          ? await Api.post('/api/auth/admin-login', {'username': user.text.trim(), 'password': pass.text})
          : await Api.post('/api/auth/student-login', {'login': user.text.trim(), 'password': pass.text});
      await Api.saveSession(r);
      Push.register();
      if (!mounted) return;
      Navigator.pushAndRemoveUntil(
          context, MaterialPageRoute(builder: (_) => admin ? const AdminShell() : const StudentShell()), (_) => false);
    } catch (e) {
      if (mounted) toast(context, e.toString(), error: true);
    }
    if (mounted) setState(() => busy = false);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        body: SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(24),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 400),
                child: Column(children: [
                  const Icon(Icons.school, size: 72, color: Color(0xFF1565C0)),
                  const Text('Coaching Manager', style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold)),
                  const SizedBox(height: 20),
                  SegmentedButton<bool>(
                    segments: const [ButtonSegment(value: false, label: Text('Student')), ButtonSegment(value: true, label: Text('Admin'))],
                    selected: {admin},
                    onSelectionChanged: (s) => setState(() => admin = s.first),
                  ),
                  const SizedBox(height: 20),
                  tf(user, admin ? 'Username' : 'Roll No ya Mobile'),
                  tf(pass, admin ? 'Password' : 'Password (pehli baar: mobile number)', obscure: true),
                  SizedBox(width: double.infinity, child: FilledButton(onPressed: busy ? null : login, child: Text(busy ? 'Please wait...' : 'Login'))),
                  if (!admin)
                    TextButton(
                        onPressed: () => Navigator.push(context, MaterialPageRoute(builder: (_) => const RegisterScreen())),
                        child: const Text('Naya student? Register karein')),
                ]),
              ),
            ),
          ),
        ),
      );
}
