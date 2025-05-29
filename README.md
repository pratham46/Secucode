
# 🔐 SecuCode

SecuCode is a secure online assessment platform designed to uphold academic integrity through advanced face monitoring, session handling, and automated test submissions. Built using C#, EmguCV, and .NET technologies, SecuCode ensures a cheat-free testing environment ideal for schools, universities, and certification bodies.

---

## 📌 Features

- 🧠 **Face Detection & Monitoring**: Uses Emgu CV to continuously detect and monitor the candidate's presence during the test.
- ⏳ **Auto-Submission**: Automatically submits the test if the user's face is not detected for three consecutive checks.
- 📝 **Custom Test Interface**: Supports single and multiple choice questions with a clean, user-friendly UI.
- 🔐 **Session Handling**: Prevents multiple logins or tab switches to ensure a controlled testing session.
- 🗃️ **Admin Panel**: Upload and manage question banks, view results, and monitor active sessions.
- 💾 **Result Generation**: Automatically evaluates answers and stores test results in the backend.

---

## 🛠️ Technologies Used

- **Frontend**: Windows Forms / WPF (.NET)
- **Backend**: ASP.NET / .NET Core APIs
- **Face Recognition**: [Emgu CV](https://www.emgu.com/wiki/index.php/Main_Page)
- **Database**: SQL Server / MySQL
- **Languages**: C#, SQL, JavaScript (if web-based version is extended)

---

## 🚀 Getting Started

### Prerequisites

- Visual Studio 2022 or newer
- .NET Framework 4.8 or .NET 6 SDK
- Emgu CV (NuGet or manual install)
- SQL Server / MySQL installed

### Installation

1. **Clone the repository**:

   ```bash
   git clone https://github.com/yourusername/SecuCode.git
   cd SecuCode
   ```

2. **Set up the database**:

   - Create the database using the provided SQL script in `/Database/setup.sql`.
   - Update the connection string in `appsettings.json` or `Web.config`.

3. **Install dependencies**:

   ```bash
   dotnet restore
   ```

4. **Run the application**:

   ```bash
   dotnet run
   ```

---

## 🧪 How It Works

1. The student logs in using a unique test code.
2. A webcam feed is activated to begin face detection.
3. Test begins only if a face is consistently detected.
4. During the test:
   - If no face is detected for 3 consecutive checks, test auto-submits.
   - All answers are saved in real-time.
5. After submission, results are generated and accessible to admins.

---

## 🧑‍💻 Contributing

Contributions are welcome! Please open an issue or pull request with any improvements, bug fixes, or feature suggestions.

1. Fork the repo
2. Create your branch: `git checkout -b feature/your-feature`
3. Commit your changes: `git commit -m "Add new feature"`
4. Push to the branch: `git push origin feature/your-feature`
5. Open a pull request

---

## 👨‍🏫 Authors

- Pratham Mewada – [LinkedIn](https://www.linkedin.com/in/pratham-mewada/)

---

## 💡 Future Improvements

- Live proctoring with flagging
- Tab switch detection
- ChatGPT / AI tool detection
- Cloud deployment with Azure or AWS

