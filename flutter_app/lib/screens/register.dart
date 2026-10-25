import 'package:flutter/material.dart';
import '../api.dart';
import '../push.dart';
import '../ui.dart';
import 'student_shell.dart';

class RegisterScreen extends StatefulWidget {
  const RegisterScreen({super.key});
  @override
  State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
  final form = GlobalKey<FormState>();
  final name = TextEditingController(), father = TextEditingController(), mother = TextEditingController(),
      address = TextEditingController(), mobile = TextEditingController(), email = TextEditingController(),
      pass = TextEditingController();
  List catalog = [];
  Map? cat, batch;
  String gender = 'Male';
  DateTime? dob;
  bool loading = true, busy = false;
  String? err;

  @override
  void initState() {
    super.initState();
    Api.get('/api/public/catalog').then((v) => setState(() { catalog = v; loading = false; })).catchError((e) => setState(() { err = e.toString(); loading = false; }));
  }

  Future<void> submit() async {
    if (!form.currentState!.validate()) return;
    if (batch == null) { toast(context, 'Batch select karein', error: true); return; }
    setState(() => busy = true);
    try {
      final r = await Api.post('/api/public/register', {
        'name': name.text.trim(), 'fatherName': father.text.trim(), 'motherName': mother.text.trim(),
        'address': address.text.trim(), 'mobile': mobile.text.trim(), 'email': email.text.trim(),
        'gender': gender, 'dateOfBirth': dob?.toIso8601String(), 'batchId': batch!['id'], 'password': pass.text,
      });
      await Api.saveSession(r);
      Push.register();
      if (!mounted) return;
      await showDialog(context: context, builder: (_) => AlertDialog(
        title: const Text('Registration ho gaya'),
        content: Text('Aapka Roll No: ${r['rollNo']}\n\n${r['message']}'),
        actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('OK'))],
      ));
      if (!mounted) return;
      Navigator.pushAndRemoveUntil(context, MaterialPageRoute(builder: (_) => const StudentShell()), (_) => false);
    } catch (e) {
      if (mounted) toast(context, e.toString(), error: true);
    }
    if (mounted) setState(() => busy = false);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Student Registration')),
        body: loading
            ? const Loader()
            : err != null
                ? ErrorView(err!, () => Navigator.pop(context))
                : Form(
                    key: form,
                    child: ListView(padding: const EdgeInsets.all(16), children: [
                      DropdownButtonFormField<Map>(
                        decoration: const InputDecoration(labelText: 'Category *', border: OutlineInputBorder()),
                        items: [for (final c in catalog) DropdownMenuItem(value: c as Map, child: Text(c['name']))],
                        onChanged: (v) => setState(() { cat = v; batch = null; }),
                      ),
                      const SizedBox(height: 10),
                      DropdownButtonFormField<Map>(
                        key: ValueKey(cat?['id']),
                        decoration: const InputDecoration(labelText: 'Batch *', border: OutlineInputBorder()),
                        items: [for (final b in (cat?['batches'] ?? []) as List) DropdownMenuItem(value: b as Map, child: Text('${b['name']} (${b['course']})'))],
                        onChanged: (v) => setState(() => batch = v),
                      ),
                      if (batch != null)
                        Padding(
                          padding: const EdgeInsets.symmetric(vertical: 8),
                          child: Text('Fees: Admission ${money(batch!['admissionFee'])} + ${money(batch!['monthlyFee'])}/month × ${batch!['durationMonths']} months',
                              style: const TextStyle(color: Colors.black54)),
                        ),
                      const SizedBox(height: 10),
                      tf(name, 'Name', required: true),
                      tf(father, "Father's name", required: true),
                      tf(mother, "Mother's name"),
                      tf(mobile, 'Mobile (10 digit)', type: TextInputType.phone, required: true, maxLength: 10),
                      tf(email, 'Email', type: TextInputType.emailAddress),
                      tf(address, 'Address', required: true, lines: 2),
                      DropdownButtonFormField<String>(
                        value: gender,
                        decoration: const InputDecoration(labelText: 'Gender', border: OutlineInputBorder()),
                        items: [for (final g in ['Male', 'Female', 'Other']) DropdownMenuItem(value: g, child: Text(g))],
                        onChanged: (v) => gender = v!,
                      ),
                      const SizedBox(height: 10),
                      OutlinedButton.icon(
                        icon: const Icon(Icons.cake),
                        label: Text(dob == null ? 'Date of birth chunein' : fdate(dob!.toIso8601String())),
                        onPressed: () async {
                          final d = await showDatePicker(context: context, initialDate: DateTime(2005), firstDate: DateTime(1970), lastDate: DateTime.now());
                          if (d != null) setState(() => dob = d);
                        },
                      ),
                      const SizedBox(height: 10),
                      tf(pass, 'App password (min 4)', obscure: true, required: true),
                      FilledButton(onPressed: busy ? null : submit, child: Text(busy ? 'Please wait...' : 'Register')),
                    ]),
                  ),
      );
}
