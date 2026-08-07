# qpdf Decryptor

A small Windows app to remove encryption from a PDF. Drop the file, enter the password, pick where to save — that's it.

Built on top of [qpdf](https://github.com/qpdf/qpdf), which does the actual work.

---

## Download

Grab the installer from the [Releases](../../releases) page. No dependencies, no .NET runtime to install.

If the PDF opens fine in your reader but you can't print or copy from it, the app can handle that too — qpdf strips both user and owner passwords.

---

## Usage

1. Drop a PDF onto the window (or click **Choose PDF**)
2. Enter the password
3. Choose where to save the decrypted copy
4. Click **Decrypt PDF**

The original file is never modified. The password is sent to qpdf over stdin so it doesn't show up in Task Manager or log files.

---

## Building from source

You'll need:
- Visual Studio 2022 (with the **.NET desktop development** workload)
- A qpdf Windows build — either compile it yourself or grab a prebuilt binary from the [qpdf releases page](https://github.com/qpdf/qpdf/releases)

Clone and open the solution:

```
git clone https://github.com/aleefakram/qpdf-gui.git
cd qpdf-gui
start QpdfGui.sln
```

For a self-contained single-file publish, point `publish-gui.ps1` at a folder containing `qpdf.exe` and its DLLs:

```powershell
.\build-scripts\publish-gui.ps1 -QpdfRuntimeDirectory C:\path\to\qpdf\bin
```

The script strips debug symbols from the native binaries and produces `artifacts\QpdfDecryptor\QpdfDecryptor.exe`.

To build the installer (requires [Inno Setup](https://jrsoftware.org/isinfo.php)):

```powershell
.\build-scripts\package-installer.ps1 -QpdfRuntimeDirectory C:\path\to\qpdf\bin
```

---

## Project layout

```
src/
  QPdfDecryptor/          WPF app
  QPdfDecryptor.Core/     Process wrapper around qpdf.exe
  QPdfDecryptor.Core.Tests/  Integration tests (no qpdf binary required)
build-scripts/
  publish-gui.ps1         Builds a single-file release
  package-installer.ps1   Packages the Inno Setup installer
  install-build-tools.ps1 Sets up MSYS2 + the qpdf C++ build toolchain
installer/
  QpdfDecryptor.iss       Inno Setup script
```

---

## Running the tests

The test project acts as its own fake qpdf, so you don't need a real binary:

```
dotnet run --project src/QPdfDecryptor.Core.Tests
```

---

## License

The GUI source code (`src/`) is MIT — see [MIT-LICENSE](MIT-LICENSE).

The bundled qpdf binary is licensed separately under the Apache 2.0 license by Jay Berkenbilt and contributors. See [LICENSE.txt](LICENSE.txt).
