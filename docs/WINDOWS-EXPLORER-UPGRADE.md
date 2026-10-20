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
