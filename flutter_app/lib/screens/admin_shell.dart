import 'dart:async';
import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../api.dart';
import '../push.dart';
import '../ui.dart';
import 'login.dart';
import 'admission_form.dart';
import 'student_detail.dart';
import 'chat.dart';

Future<void> openUrl(String u) => launchUrl(Uri.parse(u), mode: LaunchMode.externalApplication);

class AdminShell extends StatefulWidget {
  const AdminShell({super.key});
  @override
  State<AdminShell> createState() => _AdminShellState();
}

class _AdminShellState extends State<AdminShell> {
  int i = 0, unread = 0;
  Timer? timer;
  StreamSubscription? sub;

  @override
  void initState() {
    super.initState();
    loadUnread();
    timer = Timer.periodic(const Duration(seconds: 10), (_) => loadUnread());
    sub = Push.bus.stream.listen((_) => loadUnread());
  }

  @override
  void dispose() { timer?.cancel(); sub?.cancel(); super.dispose(); }

  Future<void> loadUnread() async {
    try {
      final r = await Api.get('/api/admin/chats/unread');
      if (mounted && r['count'] != unread) setState(() => unread = r['count']);
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) {
    final tabs = [const DashboardTab(), const StudentsTab(), const ChatsTab(), const DuesTab(), const BatchesTab()];
    return Scaffold(
      appBar: AppBar(
        title: const Text('Admin'),
        actions: [
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: () async {
              await Push.unregister();
              await Api.logout();
              if (!context.mounted) return;
              Navigator.pushAndRemoveUntil(context, MaterialPageRoute(builder: (_) => const LoginScreen()), (_) => false);
            },
          )
        ],
      ),
      body: tabs[i],
      bottomNavigationBar: NavigationBar(
        selectedIndex: i,
        onDestinationSelected: (v) { setState(() => i = v); loadUnread(); },
        destinations: [
          const NavigationDestination(icon: Icon(Icons.dashboard), label: 'Home'),
          const NavigationDestination(icon: Icon(Icons.people), label: 'Students'),
          NavigationDestination(icon: Badge(label: Text('$unread'), isLabelVisible: unread > 0, child: const Icon(Icons.chat)), label: 'Chats'),
          const NavigationDestination(icon: Icon(Icons.currency_rupee), label: 'Dues'),
          const NavigationDestination(icon: Icon(Icons.collections_bookmark), label: 'Batches'),
        ],
      ),
    );
  }
}

class DashboardTab extends StatefulWidget {
  const DashboardTab({super.key});
  @override
  State<DashboardTab> createState() => _DashboardTabState();
}

class _DashboardTabState extends State<DashboardTab> {
  Map? d;
  List recent = [];
  String? err;
  @override
  void initState() { super.initState(); load(); }
  Future<void> load() async {
    try {
      final a = await Api.get('/api/admin/dashboard');
      final b = await Api.get('/api/admin/payments/recent');
      if (mounted) setState(() { d = a; recent = b; err = null; });
    } catch (e) { if (mounted) setState(() => err = e.toString()); }
  }
  @override
  Widget build(BuildContext context) {
    if (err != null) return ErrorView(err!, load);
    if (d == null) return const Loader();
    return RefreshIndicator(
      onRefresh: load,
      child: ListView(padding: const EdgeInsets.all(12), children: [
        GridView.count(crossAxisCount: 2, shrinkWrap: true, physics: const NeverScrollableScrollPhysics(), childAspectRatio: 2, children: [
          Stat('Active students', '${d!['active']}'),
          Stat('Pending approvals', '${d!['pending']}', color: Colors.orange),
          Stat('Aaj ka collection', money(d!['todayCollection']), color: Colors.green),
          Stat('Is mahine', money(d!['monthCollection']), color: Colors.green),
          Stat('Total due', money(d!['totalOverdue']), color: Colors.red),
          Stat('Defaulters', '${d!['defaulters']}', color: Colors.red),
        ]),
        const Padding(padding: EdgeInsets.all(8), child: Text('Recent payments', style: TextStyle(fontWeight: FontWeight.bold))),
        for (final p in recent)
          ListTile(
            dense: true,
            title: Text('${p['student']} (${p['rollNo']})'),
            subtitle: Text('${p['receiptNo']} • ${p['mode']} • ${fdate(p['paidOn'])}'),
            trailing: Text(money(p['amount']), style: const TextStyle(fontWeight: FontWeight.bold)),
            onTap: () => openUrl(Api.printUrl('/receipt/${p['id']}')),
          ),
      ]),
    );
  }
}

class StudentsTab extends StatefulWidget {
  const StudentsTab({super.key});
  @override
  State<StudentsTab> createState() => _StudentsTabState();
}

class _StudentsTabState extends State<StudentsTab> {
  List items = [];
  String status = '', q = '';
  bool loading = true;
  String? err;
  @override
  void initState() { super.initState(); load(); }
  Future<void> load() async {
    setState(() => loading = true);
    try {
      final r = await Api.get('/api/admin/students?q=${Uri.encodeQueryComponent(q)}&status=$status');
      if (mounted) setState(() { items = r['items']; err = null; });
    } catch (e) { if (mounted) err = e.toString(); }
    if (mounted) setState(() => loading = false);
  }
  @override
  Widget build(BuildContext context) => Scaffold(
        floatingActionButton: FloatingActionButton.extended(
          icon: const Icon(Icons.person_add),
          label: const Text('Admission'),
          onPressed: () async {
            final ok = await Navigator.push(context, MaterialPageRoute(builder: (_) => const AdmissionForm()));
            if (ok == true) load();
          },
        ),
        body: Column(children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
            child: TextField(
              decoration: const InputDecoration(prefixIcon: Icon(Icons.search), hintText: 'Name / Roll / Mobile', border: OutlineInputBorder()),
              onChanged: (v) => q = v,
              onSubmitted: (_) => load(),
            ),
          ),
          Wrap(spacing: 6, children: [
            for (final s in ['', 'Active', 'Pending', 'Left'])
              ChoiceChip(label: Text(s.isEmpty ? 'All' : s), selected: status == s, onSelected: (_) { status = s; load(); }),
          ]),
          Expanded(
            child: loading
                ? const Loader()
                : err != null
                    ? ErrorView(err!, load)
                    : RefreshIndicator(
                        onRefresh: load,
                        child: ListView.builder(
                          itemCount: items.length,
                          itemBuilder: (_, i) {
                            final s = items[i]['student'];
                            final od = (items[i]['overdue'] ?? 0) as num;
                            return ListTile(
                              leading: CircleAvatar(
                                backgroundImage: s['photoPath'] != null ? NetworkImage(Api.url(s['photoPath'])) : null,
                                child: s['photoPath'] == null ? Text('${s['name']}'[0]) : null,
                              ),
                              title: Text('${s['name']}  (${s['rollNo']})'),
                              subtitle: Text('${s['batch']} • ${s['mobile']} • ${s['status']}'),
                              trailing: od > 0 ? Text(money(od), style: const TextStyle(color: Colors.red, fontWeight: FontWeight.bold)) : null,
                              onTap: () async {
                                await Navigator.push(context, MaterialPageRoute(builder: (_) => StudentDetail(id: s['id'])));
                                load();
                              },
                            );
                          },
                        ),
                      ),
          ),
        ]),
      );
}

class DuesTab extends StatefulWidget {
  const DuesTab({super.key});
  @override
  State<DuesTab> createState() => _DuesTabState();
}

class _DuesTabState extends State<DuesTab> {
  List rows = [];
  bool loading = true;
  String? err;
  @override
  void initState() { super.initState(); load(); }
  Future<void> load() async {
    try {
      final r = await Api.get('/api/admin/dues');
      if (mounted) setState(() { rows = r; err = null; });
    } catch (e) { if (mounted) err = e.toString(); }
    if (mounted) setState(() => loading = false);
  }
  @override
  Widget build(BuildContext context) {
    if (loading) return const Loader();
    if (err != null) return ErrorView(err!, load);
    final total = rows.fold<num>(0, (a, r) => a + (r['due'] as num));
    return RefreshIndicator(
      onRefresh: load,
      child: ListView(children: [
        Padding(padding: const EdgeInsets.all(12), child: Text('Total due: ${money(total)}  (${rows.length} students)', style: const TextStyle(fontWeight: FontWeight.bold, color: Colors.red))),
        for (final r in rows)
          ListTile(
            title: Text('${r['name']} (${r['rollNo']})'),
            subtitle: Text('${r['batch']} • ${r['months']} month pending • since ${fdate(r['oldest'])}'),
            trailing: Row(mainAxisSize: MainAxisSize.min, children: [
              Text(money(r['due']), style: const TextStyle(color: Colors.red, fontWeight: FontWeight.bold)),
              IconButton(icon: const Icon(Icons.call, size: 20), onPressed: () => openUrl('tel:${r['mobile']}')),
              IconButton(
                icon: const Icon(Icons.chat, size: 20, color: Colors.green),
                onPressed: () => openUrl('https://wa.me/91${r['mobile']}?text=${Uri.encodeComponent('Dear ${r['name']}, aapki ${money(r['due'])} fees pending hai. Kripya jaldi jama karein.')}'),
              ),
            ]),
            onTap: () => Navigator.push(context, MaterialPageRoute(builder: (_) => StudentDetail(id: r['studentId']))).then((_) => load()),
          ),
      ]),
    );
  }
}

class BatchesTab extends StatefulWidget {
  const BatchesTab({super.key});
  @override
  State<BatchesTab> createState() => _BatchesTabState();
}

class _BatchesTabState extends State<BatchesTab> {
  List cats = [], batches = [];
  bool loading = true;
  String? err;
  @override
  void initState() { super.initState(); load(); }
  Future<void> load() async {
    try {
      final c = await Api.get('/api/admin/categories');
      final b = await Api.get('/api/admin/batches');
      if (mounted) setState(() { cats = c; batches = b; err = null; });
    } catch (e) { if (mounted) err = e.toString(); }
    if (mounted) setState(() => loading = false);
  }

  Future<void> addCategory() async {
    final n = TextEditingController();
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('New category'),
      content: TextField(controller: n, decoration: const InputDecoration(labelText: 'Category name')),
      actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')), FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Add'))],
    ));
    if (ok != true || n.text.trim().isEmpty) return;
    try { await Api.post('/api/admin/categories', {'id': 0, 'name': n.text.trim(), 'description': null, 'isActive': true}); load(); }
    catch (e) { if (mounted) toast(context, e.toString(), error: true); }
  }

  Future<void> addBatch() async {
    if (cats.isEmpty) { toast(context, 'Pehle category banayein', error: true); return; }
    int? cat = cats.first['id'];
    final name = TextEditingController(), course = TextEditingController(), dur = TextEditingController(text: '12'),
        adm = TextEditingController(text: '0'), mon = TextEditingController(), cap = TextEditingController(text: '60');
    final ok = await showDialog<bool>(context: context, builder: (_) => StatefulBuilder(builder: (ctx, set) => AlertDialog(
      title: const Text('New batch'),
      content: SingleChildScrollView(child: Column(children: [
        DropdownButtonFormField<int>(
          value: cat,
          items: [for (final c in cats) DropdownMenuItem(value: c['id'] as int, child: Text(c['name']))],
          onChanged: (v) => set(() => cat = v),
          decoration: const InputDecoration(labelText: 'Category'),
        ),
        TextField(controller: name, decoration: const InputDecoration(labelText: 'Batch name')),
        TextField(controller: course, decoration: const InputDecoration(labelText: 'Course (ID card pe)')),
        TextField(controller: dur, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Duration (months)')),
        TextField(controller: adm, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Admission fee')),
        TextField(controller: mon, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Monthly fee')),
        TextField(controller: cap, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Capacity')),
      ])),
      actions: [TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')), FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Add'))],
    )));
    if (ok != true) return;
    try {
      await Api.post('/api/admin/batches', {
        'id': 0, 'categoryId': cat, 'name': name.text.trim(), 'course': course.text.trim(),
        'durationMonths': int.tryParse(dur.text) ?? 12, 'admissionFee': num.tryParse(adm.text) ?? 0,
        'monthlyFee': num.tryParse(mon.text) ?? 0, 'capacity': int.tryParse(cap.text) ?? 60,
        'startDate': DateTime.now().toIso8601String(), 'isActive': true,
      });
      load();
    } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
  }

  @override
  Widget build(BuildContext context) {
    if (loading) return const Loader();
    if (err != null) return ErrorView(err!, load);
    return Scaffold(
      floatingActionButton: Column(mainAxisSize: MainAxisSize.min, children: [
        FloatingActionButton.small(heroTag: 'c', onPressed: addCategory, tooltip: 'Category', child: const Icon(Icons.label)),
        const SizedBox(height: 8),
        FloatingActionButton.extended(heroTag: 'b', onPressed: addBatch, icon: const Icon(Icons.add), label: const Text('Batch')),
      ]),
      body: RefreshIndicator(
        onRefresh: load,
        child: ListView(padding: const EdgeInsets.only(bottom: 140), children: [
          for (final c in cats) ...[
            Padding(padding: const EdgeInsets.fromLTRB(16, 12, 16, 4), child: Text(c['name'], style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16))),
            for (final b in batches.where((b) => b['categoryId'] == c['id']))
              ListTile(
                title: Text('${b['name']} (${b['course']})'),
                subtitle: Text('${b['durationMonths']} months • Adm ${money(b['admissionFee'])} • ${money(b['monthlyFee'])}/mo • ${b['students']}/${b['capacity']} students'),
                trailing: IconButton(icon: const Icon(Icons.badge), tooltip: 'Batch ID cards', onPressed: () => openUrl(Api.printUrl('/idcard/batch/${b['id']}'))),
              ),
          ],
        ]),
      ),
    );
  }
}
