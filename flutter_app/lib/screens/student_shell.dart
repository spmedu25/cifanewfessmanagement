import 'dart:async';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:webview_flutter/webview_flutter.dart';
import '../api.dart';
import '../push.dart';
import '../ui.dart';
import 'admin_shell.dart' show openUrl;
import 'login.dart';
import 'chat.dart';

class StudentShell extends StatefulWidget {
  const StudentShell({super.key});
  @override
  State<StudentShell> createState() => _StudentShellState();
}

class _StudentShellState extends State<StudentShell> {
  int tab = 0, unread = 0;
  Timer? timer;
  StreamSubscription? sub;
  Map? me;
  List fees = [], pays = [];
  String? err;

  @override
  void initState() {
    super.initState();
    load();
    timer = Timer.periodic(const Duration(seconds: 10), (_) => loadUnread());
    sub = Push.bus.stream.listen((_) => loadUnread());
  }

  @override
  void dispose() { timer?.cancel(); sub?.cancel(); super.dispose(); }

  Future<void> loadUnread() async {
    try {
      final r = await Api.get('/api/student/chat/unread');
      if (mounted && r['count'] != unread) setState(() => unread = r['count']);
    } catch (_) {}
  }

  Future<void> load() async {
    try {
      final a = await Api.get('/api/student/me');
      final b = await Api.get('/api/student/fees');
      final c = await Api.get('/api/student/payments');
      if (mounted) setState(() { me = a; fees = b; pays = c; err = null; });
    } catch (e) {
      if (mounted) setState(() => err = e.toString());
    }
  }

  Future<void> logout() async {
    await Push.unregister();
    await Api.logout();
    if (!mounted) return;
    Navigator.pushAndRemoveUntil(context, MaterialPageRoute(builder: (_) => const LoginScreen()), (_) => false);
  }

  Future<void> changePhoto() async {
    final x = await ImagePicker().pickImage(source: ImageSource.gallery, imageQuality: 80, maxWidth: 800);
    if (x == null) return;
    try { await Api.upload('/api/student/photo', x.path); await load(); } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
  }

  Future<void> changePassword() async {
    final o = TextEditingController(), n = TextEditingController();
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Password badlein'),
      content: Column(mainAxisSize: MainAxisSize.min, children: [
        TextField(controller: o, obscureText: true, decoration: const InputDecoration(labelText: 'Purana password')),
        TextField(controller: n, obscureText: true, decoration: const InputDecoration(labelText: 'Naya password')),
      ]),
      actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')), FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Change'))],
    ));
    if (ok != true) return;
    try { await Api.post('/api/student/change-password', {'oldPassword': o.text, 'newPassword': n.text}); if (mounted) toast(context, 'Password badal gaya'); }
    catch (e) { if (mounted) toast(context, e.toString(), error: true); }
  }

  @override
  Widget build(BuildContext context) {
    if (err != null) return Scaffold(appBar: AppBar(actions: [IconButton(icon: const Icon(Icons.logout), onPressed: logout)]), body: ErrorView(err!, load));
    if (me == null) return const Scaffold(body: Loader());
    final s = me!['student'], sum = me!['summary'];
    final pages = [
      // ---- Home ----
      RefreshIndicator(
        onRefresh: load,
        child: ListView(padding: const EdgeInsets.all(14), children: [
          Center(
            child: GestureDetector(
              onTap: changePhoto,
              child: CircleAvatar(radius: 48, backgroundImage: s['photoPath'] != null ? NetworkImage(Api.url(s['photoPath'])) : null,
                  child: s['photoPath'] == null ? const Icon(Icons.add_a_photo) : null),
            ),
          ),
          const SizedBox(height: 8),
          Center(child: Text(s['name'], style: const TextStyle(fontSize: 22, fontWeight: FontWeight.bold))),
          Center(child: Text('Roll No. ${s['rollNo']} • ${s['status']}')),
          if (s['status'] == 'Pending')
            const Card(color: Color(0xFFFFF3CD), child: Padding(padding: EdgeInsets.all(12), child: Text('Aapka admission admin approval ke intezaar me hai. Approve hone ke baad fees schedule aur ID card dikhega.'))),
          const SizedBox(height: 8),
          GridView.count(crossAxisCount: 2, shrinkWrap: true, physics: const NeverScrollableScrollPhysics(), childAspectRatio: 2.2, children: [
            Stat('Total fees', money(sum['totalNet'])),
            Stat('Paid', money(sum['paid']), color: Colors.green),
            Stat('Remaining', money(sum['balance'])),
            Stat('Due abhi', money(sum['overdueNow']), color: (sum['overdueNow'] as num) > 0 ? Colors.red : Colors.green),
          ]),
          Card(child: Column(children: [
            ListTile(dense: true, leading: const Icon(Icons.school), title: Text('${s['category']} › ${s['batch']}'), subtitle: Text('Course: ${s['course']} • ${s['duration']} months')),
            ListTile(dense: true, leading: const Icon(Icons.person), title: Text("Father: ${s['fatherName']}"), subtitle: Text(s['address'] ?? '')),
            ListTile(dense: true, leading: const Icon(Icons.phone), title: Text(s['mobile'] ?? ''), subtitle: Text('Admission: ${fdate(s['admissionDate'])}')),
          ])),
          OutlinedButton.icon(onPressed: changePassword, icon: const Icon(Icons.lock), label: const Text('Password badlein')),
        ]),
      ),
      // ---- Fees ----
      RefreshIndicator(
        onRefresh: load,
        child: ListView(children: [
          for (final f in fees)
            ListTile(
              tileColor: (f['balance'] as num) == 0 ? Colors.green.shade50 : (f['isOverdue'] == true ? Colors.red.shade50 : null),
              title: Text(f['title']),
              subtitle: Text('Due ${fdate(f['dueDate'])} • ${money(f['amount'])} − ${money(f['discount'])} discount\nPaid ${money(f['paidAmount'])}'),
              isThreeLine: true,
              trailing: Text((f['balance'] as num) == 0 ? 'PAID' : money(f['balance']),
                  style: TextStyle(fontWeight: FontWeight.bold, color: (f['balance'] as num) == 0 ? Colors.green : Colors.red)),
            ),
          if (fees.isEmpty) const Padding(padding: EdgeInsets.all(30), child: Center(child: Text('Abhi fees schedule nahi bana'))),
        ]),
      ),
      // ---- Payments ----
      RefreshIndicator(
        onRefresh: load,
        child: ListView(children: [
          for (final p in pays)
            ListTile(
              title: Text('${p['receiptNo']} • ${money(p['amount'])}${p['isCancelled'] == true ? ' (cancelled)' : ''}'),
              subtitle: Text('${fdate(p['paidOn'])} • ${p['mode']}'),
              trailing: const Icon(Icons.receipt_long),
              onTap: () => openUrl(Api.printUrl('/receipt/${p['id']}')),
            ),
          if (pays.isEmpty) const Padding(padding: EdgeInsets.all(30), child: Center(child: Text('Abhi koi payment nahi'))),
        ]),
      ),
      // ---- ID card ----
      s['status'] == 'Active' ? CardView(id: s['id']) : const Center(child: Text('ID card admission approve hone ke baad milega')),
      const ChatView(path: '/api/student/chat', admin: false, activeKey: 'student'),
    ];
    return Scaffold(
      appBar: AppBar(title: const Text('My Coaching'), actions: [IconButton(icon: const Icon(Icons.logout), onPressed: logout)]),
      body: pages[tab],
      bottomNavigationBar: NavigationBar(
        selectedIndex: tab,
        onDestinationSelected: (v) { setState(() => tab = v); loadUnread(); },
        destinations: [
          const NavigationDestination(icon: Icon(Icons.home), label: 'Home'),
          const NavigationDestination(icon: Icon(Icons.currency_rupee), label: 'Fees'),
          const NavigationDestination(icon: Icon(Icons.receipt_long), label: 'Payments'),
          const NavigationDestination(icon: Icon(Icons.badge), label: 'ID Card'),
          NavigationDestination(icon: Badge(label: Text('$unread'), isLabelVisible: unread > 0 && tab != 4, child: const Icon(Icons.chat)), label: 'Chat'),
        ],
      ),
    );
  }
}

class CardView extends StatefulWidget {
  final int id;
  const CardView({super.key, required this.id});
  @override
  State<CardView> createState() => _CardViewState();
}

class _CardViewState extends State<CardView> {
  late final WebViewController c;
  @override
  void initState() {
    super.initState();
    c = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.disabled)
      ..loadRequest(Uri.parse(Api.printUrl('/idcard/student/${widget.id}?embed=1')));
  }
  @override
  Widget build(BuildContext context) => Column(children: [
        Expanded(child: WebViewWidget(controller: c)),
        Padding(
          padding: const EdgeInsets.all(8),
          child: OutlinedButton.icon(
            onPressed: () => openUrl(Api.printUrl('/idcard/student/${widget.id}')),
            icon: const Icon(Icons.print),
            label: const Text('Browser me kholein / print'),
          ),
        ),
      ]);
}
