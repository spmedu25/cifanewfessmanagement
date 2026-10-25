import 'dart:io';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import '../api.dart';
import '../ui.dart';

class AdmissionForm extends StatefulWidget {
  final Map? student; // edit mode
  const AdmissionForm({super.key, this.student});
  @override
  State<AdmissionForm> createState() => _AdmissionFormState();
}

class _AdmissionFormState extends State<AdmissionForm> {
  final form = GlobalKey<FormState>();
  final name = TextEditingController(), father = TextEditingController(), mother = TextEditingController(),
      address = TextEditingController(), mobile = TextEditingController(), alt = TextEditingController(),
      email = TextEditingController(), aadhaar = TextEditingController(), pass = TextEditingController(),
      monthlyDisc = TextEditingController(text: '0'), admDisc = TextEditingController(text: '0');
  List cats = [], batches = [];
  int? catId, batchId;
  String gender = 'Male';
  DateTime admDate = DateTime.now();
  DateTime? dob;
  String? photoPath;
  bool loading = true, busy = false;

  bool get edit => widget.student != null;
  Map? get batch => batches.cast<Map?>().firstWhere((b) => b!['id'] == batchId, orElse: () => null);

  @override
  void initState() {
    super.initState();
    () async {
      try {
        cats = await Api.get('/api/admin/categories');
        batches = await Api.get('/api/admin/batches');
        final s = widget.student;
        if (s != null) {
          name.text = s['name'] ?? ''; father.text = s['fatherName'] ?? ''; mother.text = s['motherName'] ?? '';
          address.text = s['address'] ?? ''; mobile.text = s['mobile'] ?? ''; alt.text = s['altMobile'] ?? '';
          email.text = s['email'] ?? ''; aadhaar.text = s['aadhaarNo'] ?? '';
          monthlyDisc.text = '${s['monthlyDiscount'] ?? 0}'; admDisc.text = '${s['admissionDiscount'] ?? 0}';
          gender = s['gender'] ?? 'Male';
          admDate = DateTime.parse(s['admissionDate']);
          dob = s['dateOfBirth'] != null ? DateTime.parse(s['dateOfBirth']) : null;
          batchId = s['batchId'];
          catId = batches.firstWhere((b) => b['id'] == batchId, orElse: () => {'categoryId': null})['categoryId'];
        }
      } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
      if (mounted) setState(() => loading = false);
    }();
  }

  Future<void> pickPhoto() async {
    final x = await ImagePicker().pickImage(source: ImageSource.gallery, imageQuality: 80, maxWidth: 800);
    if (x != null) setState(() => photoPath = x.path);
  }

  Future<void> save() async {
    if (!form.currentState!.validate()) return;
    if (batchId == null) { toast(context, 'Batch select karein', error: true); return; }
    setState(() => busy = true);
    final body = {
      'name': name.text.trim(), 'fatherName': father.text.trim(), 'motherName': mother.text.trim(),
      'address': address.text.trim(), 'mobile': mobile.text.trim(), 'altMobile': alt.text.trim(),
      'email': email.text.trim(), 'aadhaarNo': aadhaar.text.trim(), 'gender': gender,
      'dateOfBirth': dob?.toIso8601String(), 'admissionDate': admDate.toIso8601String(), 'batchId': batchId,
      'monthlyDiscount': num.tryParse(monthlyDisc.text) ?? 0, 'admissionDiscount': num.tryParse(admDisc.text) ?? 0,
      'password': pass.text.trim().isEmpty ? null : pass.text.trim(),
    };
    try {
      int id;
      if (edit) {
        await Api.put('/api/admin/students/${widget.student!['id']}', body);
        id = widget.student!['id'];
      } else {
        id = (await Api.post('/api/admin/students', body))['id'];
      }
      if (photoPath != null) await Api.upload('/api/admin/students/$id/photo', photoPath!);
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) toast(context, e.toString(), error: true);
    }
    if (mounted) setState(() => busy = false);
  }

  @override
  Widget build(BuildContext context) {
    final b = batch;
    num adm = b == null ? 0 : ((b['admissionFee'] as num) - (num.tryParse(admDisc.text) ?? 0)).clamp(0, double.infinity);
    num mon = b == null ? 0 : ((b['monthlyFee'] as num) - (num.tryParse(monthlyDisc.text) ?? 0)).clamp(0, double.infinity);
    return Scaffold(
      appBar: AppBar(title: Text(edit ? 'Edit Student' : 'New Admission')),
      body: loading
          ? const Loader()
          : Form(
              key: form,
              child: ListView(padding: const EdgeInsets.all(16), children: [
                Center(
                  child: GestureDetector(
                    onTap: pickPhoto,
                    child: CircleAvatar(
                      radius: 44,
                      backgroundImage: photoPath != null ? FileImage(File(photoPath!)) : (widget.student?['photoPath'] != null ? NetworkImage(Api.url(widget.student!['photoPath'])) as ImageProvider : null),
                      child: (photoPath == null && widget.student?['photoPath'] == null) ? const Icon(Icons.add_a_photo, size: 30) : null,
                    ),
                  ),
                ),
                const SizedBox(height: 14),
                DropdownButtonFormField<int>(
                  value: catId,
                  decoration: const InputDecoration(labelText: 'Category *', border: OutlineInputBorder()),
                  items: [for (final c in cats) DropdownMenuItem(value: c['id'] as int, child: Text(c['name']))],
                  onChanged: (v) => setState(() { catId = v; batchId = null; }),
                ),
                const SizedBox(height: 10),
                DropdownButtonFormField<int>(
                  key: ValueKey('b$catId'),
                  value: batchId,
                  decoration: const InputDecoration(labelText: 'Batch *', border: OutlineInputBorder()),
                  items: [for (final x in batches.where((x) => x['categoryId'] == catId && x['isActive'] == true)) DropdownMenuItem(value: x['id'] as int, child: Text('${x['name']} (${x['course']})'))],
                  onChanged: (v) => setState(() => batchId = v),
                ),
                const SizedBox(height: 10),
                tf(name, 'Name', required: true),
                tf(father, "Father's name", required: true),
                tf(mother, "Mother's name"),
                tf(mobile, 'Mobile (10 digit)', type: TextInputType.phone, required: true, maxLength: 10),
                tf(alt, 'Alternate mobile', type: TextInputType.phone),
                tf(aadhaar, 'Aadhaar no.', type: TextInputType.number),
                tf(email, 'Email', type: TextInputType.emailAddress),
                tf(address, 'Address', required: true, lines: 2),
                DropdownButtonFormField<String>(
                  value: gender,
                  decoration: const InputDecoration(labelText: 'Gender', border: OutlineInputBorder()),
                  items: [for (final g in ['Male', 'Female', 'Other']) DropdownMenuItem(value: g, child: Text(g))],
                  onChanged: (v) => gender = v!,
                ),
                const SizedBox(height: 10),
                Row(children: [
                  Expanded(child: OutlinedButton(
                    onPressed: () async {
                      final d = await showDatePicker(context: context, initialDate: dob ?? DateTime(2005), firstDate: DateTime(1970), lastDate: DateTime.now());
                      if (d != null) setState(() => dob = d);
                    },
                    child: Text(dob == null ? 'Date of birth' : 'DOB ${fdate(dob!.toIso8601String())}'),
                  )),
                  const SizedBox(width: 8),
                  Expanded(child: OutlinedButton(
                    onPressed: () async {
                      final d = await showDatePicker(context: context, initialDate: admDate, firstDate: DateTime(2020), lastDate: DateTime(2100));
                      if (d != null) setState(() => admDate = d);
                    },
                    child: Text('Admission ${fdate(admDate.toIso8601String())}'),
                  )),
                ]),
                const SizedBox(height: 12),
                Row(children: [
                  Expanded(child: TextFormField(controller: admDisc, keyboardType: TextInputType.number, onChanged: (_) => setState(() {}), decoration: const InputDecoration(labelText: 'Admission discount ₹', border: OutlineInputBorder()))),
                  const SizedBox(width: 8),
                  Expanded(child: TextFormField(controller: monthlyDisc, keyboardType: TextInputType.number, onChanged: (_) => setState(() {}), decoration: const InputDecoration(labelText: 'Monthly discount ₹', border: OutlineInputBorder()))),
                ]),
                if (b != null)
                  Card(
                    color: Colors.blue.shade50,
                    child: Padding(
                      padding: const EdgeInsets.all(12),
                      child: Text('Admission ${money(adm)} + ${money(mon)} × ${b['durationMonths']} months\nTotal payable: ${money(adm + mon * (b['durationMonths'] as num))}',
                          style: const TextStyle(fontWeight: FontWeight.w600)),
                    ),
                  ),
                const SizedBox(height: 10),
                tf(pass, edit ? 'Naya password (blank = no change)' : 'Student app password (blank = mobile number)'),
                FilledButton(onPressed: busy ? null : save, child: Text(busy ? 'Saving...' : (edit ? 'Save changes' : 'Admission karein'))),
              ]),
            ),
    );
  }
}
