# QPDF Decryptor

[![GitHub Release](https://img.shields.io/github/v/release/aleefakram/qpdf-gui?style=flat-square&color=blue)](../../releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg?style=flat-square)](LICENSE.txt)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=flat-square&logo=windows)](../../releases)
[![Powered By: QPDF](https://img.shields.io/badge/Powered%20by-QPDF-orange?style=flat-square)](https://github.com/qpdf/qpdf)

A fast, lightweight Windows app to remove passwords, restrictions, and encryption from PDF files. Drop your file, enter the password if needed, pick where to save — that's it.

Built on top of [QPDF](https://github.com/qpdf/qpdf), which handles the native C++ PDF processing under the hood.

---

## At a Glance

| Feature | Details |
|---|---|
| **OS** | Windows 10 / 11 (64-bit standalone executable & installer) |
| **Privacy** | 100% local processing. Files never leave your machine. |
| **Dependencies** | None. Single-file publish with embedded runtime binaries. |
| **License** | Open Source — MIT (GUI) / Apache 2.0 (QPDF) |

---

## Features

- **Remove Owner Restrictions**: Strips restrictions on printing, text copying, editing, and annotating — even if you don't know the owner password.
- **Remove User Passwords**: Unlocks password-protected PDFs when you enter the password.
- **100% Offline & Private**: All decryption happens locally. Your documents are never uploaded to third-party cloud servers.
- **Zero Dependencies**: Standalone release — no .NET runtime installation required.
- **Process-Level Password Security**: Passwords are passed directly to `qpdf` via `stdin`, keeping them out of Task Manager and process log arguments.
- **Non-Destructive**: Generates a decrypted copy while leaving your original PDF intact.

---

## Why use an offline app over web tools?

Most online PDF unlockers require uploading your private documents to an external server with strict file size limits and potential security risks. 

QPDF Decryptor runs entirely on your desktop:

- 🔒 **Complete Privacy**: Financial, legal, and personal files stay on your computer.
- ⚡ **No File Size Limits**: Handles large multi-gigabyte PDFs without timeouts.
- 📶 **Works Offline**: No active internet connection needed.
- 🆓 **No Ads or Subscriptions**: Free and open source.

---

## Download & Usage

1. Download the latest installer or portable executable from [Releases](../../releases).
2. Launch `QpdfDecryptor.exe`.
3. Drag & drop a PDF onto the window (or click **Choose PDF**).
4. Enter the password if required (leave blank if stripping printing/copying restrictions).
5. Click **Decrypt PDF**.

---

## Building from Source

### Prerequisites
- Visual Studio 2022 (with the **.NET desktop development** workload)
- A qpdf Windows build — either compile it yourself or grab prebuilt binaries from the [qpdf releases page](https://github.com/qpdf/qpdf/releases)

### Setup & Compilation
```powershell
git clone https://github.com/aleefakram/qpdf-gui.git
cd qpdf-gui
start QpdfGui.sln
```

To create a single-file standalone release, point `publish-gui.ps1` at your `qpdf` binary directory:

```powershell
.\build-scripts\publish-gui.ps1 -QpdfRuntimeDirectory C:\path\to\qpdf\bin
```
The compiled output will be placed in `artifacts\QpdfDecryptor\QpdfDecryptor.exe`.

To package the Inno Setup installer:
```powershell
.\build-scripts\package-installer.ps1 -QpdfRuntimeDirectory C:\path\to\qpdf\bin
```

---

## Project Layout

```
src/
  QPdfDecryptor/          WPF GUI frontend
  QPdfDecryptor.Core/     QPDF process wrapper & stdin stream handler
  QPdfDecryptor.Core.Tests/ Integration test suite
build-scripts/
  publish-gui.ps1         Single-file release build script
  package-installer.ps1   Inno Setup installer script
installer/
  QpdfDecryptor.iss       Inno Setup script
llms.txt                  Machine-readable overview manifest
```

---

## Running Tests

Tests run against an internal mock process framework without needing a native `qpdf` executable installed:

```powershell
dotnet run --project src/QPdfDecryptor.Core.Tests
```

---

## FAQ

<details>
<summary><b>Can I unlock a PDF if I can view it but can't print or copy text from it?</b></summary>
Yes. PDFs with printing or copying restrictions use an "owner password". Leave the password field blank and click <b>Decrypt PDF</b> — QPDF Decryptor will remove those restriction locks automatically.
</details>

<details>
<summary><b>Does this send any file data over the network?</b></summary>
No. The app operates 100% offline and makes zero network calls.
</details>

<details>
<summary><b>Does it require administrator rights to run?</b></summary>
No. The portable version runs standalone without installation or admin privileges.
</details>

---

## License

- **GUI Application (`src/`)**: Licensed under the [MIT License](MIT-LICENSE).
- **Bundled QPDF Engine**: Licensed under the [Apache 2.0 License](LICENSE.txt).


