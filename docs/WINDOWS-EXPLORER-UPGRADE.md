# Windows Explorer menu upgrades

## Reported failure

Filee 1.12.1-arabic.2 could display `0x80073D02` while updating an existing installation. The installer had already copied Filee successfully, but then redeployed `Filee.ExplorerMenu_1.0.0.0_x64__c73vxh346rtay` while Windows was using its COM server. This warning concerns the Windows 11 top-level menu; it does not indicate a failed PDF conversion or a failed installation of the application files.

## Correction in 1.12.1-arabic.3

The sparse package manifest has its own version, independent of the application version. Its COM DLL is loaded from the external installation folder. Updating that DLL does not require redeploying an unchanged manifest.

- Setup enumerates the current user's registered package identities, requires the exact expected identity, and reads its effective external location with `GetPackagePathByFullName2` from the Windows API set.
- When identity and folder match, Setup preserves that registration. It continues to replace the external DLL using the existing loaded-DLL rename procedure. Windows loads the updated DLL when the old COM server unloads naturally.
- A missing registration, different manifest version, ambiguous registration or different external folder still takes the registration path. Real deployment failures remain visible. This fix does not promise to relocate or replace a changed manifest while its package is busy.
- The application's Settings registration action uses the same identity/version/folder condition before requesting elevation.
- Explorer and other applications are not forcibly closed. Existing settings and conversion engines remain in place.

## Verification

`build/test-explorer-menu-upgrade.ps1` runs inside the Windows installer smoke check. It activates the registered IExplorerCommand through Windows COM, keeps the instance alive, and requires the old Add-AppxPackage command to reproduce `0x80073D02`. It then runs Setup over the existing installation, requires the log to confirm that registration was preserved, rejects deployment errors, and verifies that the package remains registered. The native test releases its COM reference in a finally block. Publication requires `explorer-upgrade.json` with both the reproduced error and successful upgrade.

Other installer checks continue to exercise Arabic paths, Arabic PDF conversion, desktop startup/shutdown, uninstall and the portable ZIP. Linux/macOS packages retain the existing native release gates.

Local targeted checks: 12 tests passed, 6 Windows/package tests skipped on Linux; `dotnet format --verify-no-changes` passed. Native [build 39383687728](https://github.com/86msa/Filee/actions/runs/39383687728) passed Windows installation, loaded COM upgrade, conversion, desktop and uninstall checks. All five native runtime jobs passed, including the native macOS and Linux installer checks. Application/package source: `f20178e4e090ade5c9c68bc1ddf257de37682f7f`. Publication separately verifies checksums and every native test report, including the required original error and successful upgrade.

The PDF implementation, platform support and existing limitations remain documented in [ARABIC-PDF.md](ARABIC-PDF.md). Windows packages are unsigned; macOS packages are ad-hoc signed and not notarized. No change was merged into `main`.

## العربية

يعالج الإصدار **1.12.1-arabic.3** خطأ `0x80073D02` عند الترقية فوق النسخة الحالية، مع الحفاظ على الإعدادات والمحركات. يتحقق المثبّت من هوية الإضافة المسجّلة ومجلدها ويحتفظ بالتسجيل المطابق. لا يغلق Explorer بالقوة. أعاد اختبار Windows إنتاج الخطأ مع بقاء إضافة COM محمّلة، ثم اجتازت الترقية الاختبار مع بقاء التسجيل صحيحاً. نجحت اختبارات التشغيل وتحويل PDF العربية والإزالة. اجتازت المنصات الخمس اختبارات التطبيق والحزم؛ لم تُدمج التعديلات في `main`.

Release: https://github.com/86msa/Filee/releases/tag/arabic-v1.12.1-arabic.3

## Changed files

- `installer/Filee.iss`: check the registered identity and effective external folder before redeployment.
- `src/Filee.Platform.Windows/ExplorerMenuRegistration.cs`: preserve matching registration in the Settings action.
- `tests/Filee.App.Tests/InstallerTests.cs`: keep Setup's expected full package identity in sync with the manifest.
- `build/test-explorer-menu-upgrade.ps1`: reproduce the locked-package failure and test the real installer while COM remains active.
- `build/smoke-windows-installer.ps1`: enable the modern menu during fresh installation and run the upgrade check.
- `build/verify-arabic-release.py`: block publication without the successful reproduction/upgrade report.
- `Directory.Build.props`, `.github/workflows/arabic-builds.yml`: preview version `1.12.1-arabic.3`.
- `docs/ARABIC-RELEASE.md`, `docs/ARABIC-PDF.md`, this report: release notes, previous-report reference and verification details.

## Published release verification

[Release 1.12.1-arabic.3](https://github.com/86msa/Filee/releases/tag/arabic-v1.12.1-arabic.3) is public. [Publisher run 39386010845](https://github.com/86msa/Filee/actions/runs/39386010845) passed the native report and checksum gates, including the mandatory loaded COM upgrade result. Ubuntu 22.04 installation/conversion/desktop/uninstallation checks passed on both CPUs. All ten package download links were checked after publication. The downloaded public `VALIDATION.json` matches its SHA-256 in the public 13-entry manifest. The publisher independently verified all ten package checksums before publication.

| Runtime | Passed | Skipped | Arabic PDF tests |
|---|---:|---:|---:|
| win-x64 | 1655 | 66 | 14 |
| linux-x64 | 1642 | 77 | 14 |
| linux-arm64 | 1640 | 79 | 14 |
| osx-x64 | 1637 | 82 | 14 |
| osx-arm64 | 1637 | 82 | 14 |

Total: **8211 passed, 386 skipped, zero failures**, plus the installer/upgrade smoke checks. Each runtime passed 14 Arabic PDF tests (70 executions). Skips are listed in VALIDATION.json; they are not passes. As in the previous release, two Windows-only tests are excluded before execution on each Unix runtime (8 additional executions not run). The Linux local run could not execute six Windows/package-specific tests; those are separate from the native release results. Automated checks do not constitute a manual test on the reporter's PC or every Windows 10/11 version.

[Download Windows Setup](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.3/Filee-1.12.1-arabic.3-win-Setup.exe) · [VALIDATION.json](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.3/VALIDATION.json) · [SHA256SUMS.txt](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.3/SHA256SUMS.txt)
