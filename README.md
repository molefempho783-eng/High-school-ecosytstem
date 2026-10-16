# HighSchoolEcosystem

A school learning management system (LMS) built for South African high schools. One app for learners, teachers, parents and school admins to manage classes, assignments, results and school communication.

Built with **.NET MAUI** and developed by **[DAYLY Technologies](https://dayly.site)**.

---

## ✨ Features

- **Role-based access**: separate experiences for Learners, Teachers, Parents and Admins
- **Classes & subjects**: aligned to the CAPS curriculum (Grades 8–12)
- **Assignments & submissions**: teachers post work, learners submit, teachers mark
- **Results & reports**: track marks per subject, term and grade
- **Announcements**: school-wide and class-level notices
- **Timetables**: per learner and per teacher
- **Parent portal**: parents follow their child's progress and notices
- **Cross-platform**: Android, iOS, Windows and macOS from one codebase

> Edit this list to match what's actually implemented.

---

## 🛠 Tech Stack

| Layer        | Technology                         |
|--------------|------------------------------------|
| Framework    | .NET MAUI (.NET <8/9>)             |
| Language     | C#                                 |
| UI           | XAML                               |
| Architecture | MVVM (<CommunityToolkit.Mvvm>)     |
| Data         | <SQLite / EF Core / Firebase / API>|
| Auth         | <your auth approach>               |

---

## 🚀 Getting Started

### Prerequisites

- [.NET SDK <8/9>](https://dotnet.microsoft.com/download)
- Visual Studio 2022 (17.8+) with the **.NET Multi-platform App UI development** workload
- Android emulator or a physical device (for Android testing)

### Installation

```bash
git clone https://github.com/molefempho783-eng/<repo-name>.git
cd <repo-name>
dotnet restore
```

### Run

**Visual Studio:** open the `.sln`, choose a target (Android Emulator / Windows Machine) and press **F5**.

**CLI:**
```bash
# Windows
dotnet build -t:Run -f net<8/9>.0-windows10.0.19041.0

# Android
dotnet build -t:Run -f net<8/9>.0-android
```

---

## 🔑 Test Accounts

The app seeds sample data on first run so you can try every role.

| Role    | Email / Username       | Password     |
|---------|------------------------|--------------|
| Admin   | <admin@school.test>    | <password>   |
| Teacher | <teacher@school.test>  | <password>   |
| Learner | <learner@school.test>  | <password>   |
| Parent  | <parent@school.test>   | <password>   |

> ⚠️ Test accounts are for development only. Remove or disable seeding before deploying to a school.

---

## 🏫 For School

HighSchoolEcosystem is offered to schools by DAYLY Technologies, with setup, onboarding and ongoing maintenance.
---

## 👤 Author

**Mpho Molefe**
- GitHub: [@molefempho783-eng](https://github.com/molefempho783-eng)

---

## 📄 License

© 2026 DAYLY Technologies. All rights reserved.
This is proprietary software. Do not copy, modify or distribute it without written permission.
