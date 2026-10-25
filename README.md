# Coaching ERP — .NET 9 Blazor Server + Flutter

Folders:
- `server/`      -> .NET 9 Blazor Server (admin web) + REST API (Flutter ke liye) + ID card generator
- `flutter_app/` -> Flutter app (Admin + Student, ek hi app me login se alag)

## 1) Server chalana
Requirements: .NET 9 SDK, SQL Server (LocalDB / Express).
```
cd server
dotnet restore
dotnet run
```
- Browser: http://localhost:5000  -> Admin login: `admin` / `admin123` (turant Settings me badal dein)
- Database apne aap ban jati hai (EnsureCreated). Connection string: `appsettings.json`
- `Jwt:Key` ko apni lambi random key se badal dein (production me zaroori).
- Photos `wwwroot/uploads/` me save hoti hain (backup me rakhein).

## 2) Pehla setup (web admin)
1. Categories -> category banayein
2. Batches -> batch banayein (Course name, duration months, admission fee, monthly fee, capacity)
3. Settings / ID Card -> institute naam, phone, website, colour, Logo + Stamp + Sign upload
4. New Admission -> category > batch chunein, student ki poori details + photo + discount

## 3) Fees kaise chalti hai
- Admission hote hi poora schedule ban jata hai: 1 admission fee + har month ki installment
- Discount: admission discount (ek baar) + monthly discount (har month)
- Fees receive: amount dalte hi sabse purani due se adjust hoti hai (partial payment bhi ok)
- Receipt print, galat entry ho to payment cancel (balance wapas due ho jata hai)
- Dues / Defaulters page: batch/category filter, WhatsApp + call button

## 4) ID Card (9 cm × 6 cm, landscape)
- Student page -> "ID Card" (front + back "Fees Card") ya Batches -> "ID Cards" (poore batch ki A4 sheet, 8 card/page)
- Browser me Print dabayein. Print dialog me: Margins = None/Default, "Background graphics" ON.
- Single card print ke liye page size 90mm × 60mm automatic set hota hai.

## 5) Flutter app
```
cd flutter_app
flutter create .          # android/ios folders banane ke liye (lib/ aur pubspec.yaml overwrite na karein)
flutter pub get
```
- `lib/config.dart` me `kBaseUrl` apne server ka address dalein
  (Android emulator: http://10.0.2.2:5000, real phone: PC ka LAN IP; live server ke liye https URL)
- Android `android/app/src/main/AndroidManifest.xml`:
  `<uses-permission android:name="android.permission.INTERNET"/>` aur `<application ... android:usesCleartextTraffic="true">` (sirf http testing ke liye)
- `flutter run`

Student login: Roll No ya Mobile + password (admin ne diya nahi to password = mobile number).
Student khud Register kar sakta hai -> status Pending -> admin approve kare -> fees schedule + ID card.

## 6) Chat / Messages (WhatsApp jaisa)
- Har student ka admin ke saath ek chat thread. Text + photo, time, date separator, ✓✓ (blue = read), unread badge.
- **Web admin:** sidebar me **Messages** — left me sabhi chats, right me conversation. Naya student message aate hi live dikhta hai,
  sidebar badge badhta hai aur beep + browser notification aata hai (browser permission "Allow" karein).
- **Broadcast:** sabhi / kisi batch ke students ko announcement. **Due reminder:** jinki fees due hai unko personal message + notification.
- **Flutter:** Admin ko "Chats" tab, student ko "Chat" tab. Chat khula ho to har 4 sec me refresh + push aate hi turant.
- Student detail page se "Message" button.
- Purani database me chat ki 2 tables (ChatMessages, DeviceTokens) server start par apne aap ban jati hain.

## 7) Push Notifications (FCM)
Notification in cases me jata hai: naya chat message (student -> admin ke phones, admin -> student), fees receive, admission approve, broadcast, due reminder.
App band ho ya background me ho tab bhi system notification aata hai; app khula ho to local notification dikhta hai; notification tap par seedha chat khulta hai.

**Firebase setup (ek baar):**
1. https://console.firebase.google.com par project banayein -> Android app add karein (package name = `flutter create` wala, `android/app/build.gradle` me `applicationId`).
2. Sabse aasan: `dart pub global activate flutterfire_cli` phir `flutterfire configure` (google-services.json aur gradle plugin khud laga deta hai).
3. `android/app/build.gradle` me (flutter_local_notifications ke liye):
   - `compileOptions { coreLibraryDesugaringEnabled true }`
   - `dependencies { coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.0.4' }`
   - minSdkVersion 21 ya zyada
4. `AndroidManifest.xml` me: `<uses-permission android:name="android.permission.POST_NOTIFICATIONS"/>`
5. **Server side key:** Firebase Console -> Project settings -> Service accounts -> *Generate new private key* -> JSON file server par rakhein
   (repo me commit na karein!) aur `appsettings.json` me:
   `"Fcm": { "ServiceAccountPath": "C:\\secrets\\firebase-key.json", "ProjectId": "" }` (ProjectId blank chhodne par JSON se le leta hai)
6. Server restart karein. Log me "FCM configure nahi hai" aaye to path galat hai. Bina FCM ke bhi chat chalta hai (sirf push band rehta hai).
- iOS ke liye: APNs key Firebase me upload karni hogi aur `flutterfire configure` se `firebase_options.dart` banakar `Firebase.initializeApp(options: ...)` use karein.

## Deploy
Live ke liye IIS / Linux (nginx + systemd) / Azure par publish: `dotnet publish -c Release`. HTTPS zaroor lagayein.
