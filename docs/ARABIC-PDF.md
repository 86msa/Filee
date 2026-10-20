# العربية في PDF — التشخيص والإصلاح والتحقق

## السبب

في rhwp 0.8.7، مسار تشكيل الأسطر الأفقي مخصص أساساً للكورية القديمة، ويرفض مقاطع RTL صراحةً (`horizontal_ltr_bidi0` وشرط مستوى BiDi). اختيار خط يغطي Unicode في RhwpFonts يحل الحروف المفقودة فقط؛ لا يجري OpenType shaping أو ترتيب Unicode. إضافة Noto إلى `--font-path` لم تصلح الملف المعاد إنتاجه. كذلك كان مسار SVG يحول النص إلى مسارات، وقارئ PDF يرتب الكلمات من اليسار إلى اليمين هندسياً، فيفقد ترتيب العربية.

## التنفيذ

- `ArabicTextLayout`: خوارزميات Unicode #9/#14/#29 وHarfBuzz، تشكيل مع سياق الكلمة، التفاف عند حدود grapheme، خطوط Noto ثابتة مضمنة في التجميع، وخط احتياطي للمحارف الأخرى. اتجاه المحتوى مستقل عن لغة الواجهة.
- `ArabicDocumentPdf`: يستعمل نموذج HDocument الموجود، ويختار المسار العربي للمستندات التي تحتوي العربية، مع الحفاظ على المسار السابق لغيرها. فقرات وهوامش وصفحات ورؤوس وتذييلات وجداول وصور ومربعات نص وأشكال. يحترم اتجاه DOCX/HTML/PPTX الصريح والمحاذاة الصريحة.
- `ArabicPdfGlyphWriter`: مجموعات خطوط PDF Type 3 حقيقية، تضم أشكال glyphs من الخط بعد HarfBuzz، وToUnicode لكل scalar أصلي. الحركات مرتبطة بخط الأساس؛ السطر المختلط يستخدم مجموعة واحدة عندما تسمح أحجامه وألوانه. لا تحويل للصفحات إلى صور، ولا طبقة نص مخفية، ولا نص منسوخ في metadata.
- العلامة المؤقتة داخل الملف الوسيط تُستبدل كلها، ويُزال مورد خطها قبل تسليم الناتج. فشل الاستبدال يوقف التحويل.
- `PdfDocumentReader`: يقرأ Unicode الحقيقي ويستعيد ترتيب محارف المجموعات من رموز الخط وعمليات PDF؛ يدعم ActualText ويعالج اتجاه ملفات PDF الأخرى هندسياً عند غياب البنية المنطقية.
- `SvgTextSource`: يحتفظ بالنص المنطقي الأصلي لعناصر SVG البسيطة، قبل تقسيمها المرئي. على Windows وmacOS قد يُرجع Svg.Skia كتل glyphs بلا Unicode؛ تُربط بعناصر المصدر باستخدام خط الأساس والمحاذاة. العناصر المتكررة تبقى مستقلة. النصوص ذات مسارات أو توزيع أحرف خاص تستمر في مسار الرسومات السابق.
- لا استبدال لمحركات خارجية غير موثوقة، ولا تغييرات لمسارات الوسائط والأرشيف.

## الاختبارات

`ArabicPdfTests` يولّد TXT/Markdown/HTML/DOCX/XLSX/PPTX/HWPX بأسماء عربية ويختبر PDF→TXT. قارئ PDFium مستقل يتحقق من البحث والنسخ للعينات الخمس، بما فيها العبارة المشكّلة كاملة والتاريخ `2026/125`. يوجد اختبار SVG بسيط، وفقرات طويلة وجدول 25 صفاً مع الدمج والتقسيم والضغط. المقارنة البصرية تستعمل ثلاث صور مرجعية ولّدها Pillow/libraqm خارج Filee بالخط الثابت نفسه. ملف LibreOffice أصلي مستقل يتحقق من ثبات البكسلات وUnicode بعد عمليات PDF.

كل تشغيل CI يحفظ ملفات المصدر الاصطناعية وPDF والصور والنصوص، إضافة إلى صور الواجهة العربية السابقة. النشر مشروط بنجاح هذه الاختبارات على الأنظمة الخمسة، وباختبار EXE/ZIP وDEB وDMG الفعلية. الاختبارات المتخطاة تُدرج منفصلة ولا تُحسب ناجحة.

## التثبيت والتحديثات

التنزيل: https://github.com/86msa/Filee/releases

- Windows x64: شغّل Setup.exe أو فك Portable.zip؛ احذف التثبيت من إعدادات Windows.
- Ubuntu 22.04/24.04 وDebian ذي الاعتماديات المتوافقة: `sudo apt install ./Filee-1.12.1-arabic.2-linux-x64.deb` (استبدل arm64 عند الحاجة). الإزالة: `sudo apt remove filee`؛ تبقى إعدادات المستخدم ومستنداته.
- Linux tar.gz: فك الحزمة وشغّل `./install.sh` دون sudo وفق INSTALL.txt.
- macOS: افتح DMG واسحب Filee.app إلى Applications ثم أخرج القرص. النسخة غير موثقة وقد تمنع الحماية تشغيلها؛ لا يوجد تجاوز للحماية. راجع INSTALL.txt للإزالة والتنظيف.
- استخدم `sha256sum --check SHA256SUMS.txt` على Linux، أو `shasum -a 256` على macOS، أو `Get-FileHash -Algorithm SHA256` على Windows.
- تحديثات هذا الإصدار التجريبي يدوية من صفحة fork؛ بيانات المستودع وروابط التحديث لا تستهدف KnifeLemon/Filee.

## الحدود

راجع [ملاحظات الإصدار](ARABIC-RELEASE.md). دمج الخلايا رأسياً والأعمدة والقوالب المكتبية المعقدة لا تحمل ضمان تطابق طباعي؛ فهذه القيود مرتبطة بنموذج المصدر ومحرك التخطيط. لا يوجد OCR، ولا ضمان PDF/A من دون أداة تحقق مستقلة. توقيع Apple/Notarization وWindows Authenticode يحتاج بيانات اعتماد غير متاحة في هذه المهمة. RPM غير منشورة لعدم اختبار توزيعة مستهدفة.

## الصور

| قبل الإصلاح (rhwp) | بعد الإصلاح (نص قابل للبحث) |
|---|---|
| ![قبل الإصلاح](media/arabic-pdf/before.png) | ![بعد الإصلاح](media/arabic-pdf/after.png) |

## تحقق البيئة المحلية

نجح `dotnet build Filee.slnx -c Release` دون أخطاء (تسعة تحذيرات سابقة)، وشُغّل `dotnet format`. اختبارات PDF العربية: **14 ناجحاً، صفر فشل أو تخطٍ**، بما فيها نسخ العينات الخمس في PDFium والمقارنة البصرية المستقلة.

تشغيل الحل المحلي الكامل: **1699 ناجحاً، 18 متخطى، 4 فاشلة**. الفاشلة هي اختبار تصدير صفحة PDF إلى SVG في LibreOffice 26.8 alpha المقدم بالبيئة، عرض أعمدة XLS في النسخة نفسها (15.8945 بدلاً من 14)، ترميز AMR في FFmpeg النظامي، واختبار عرض النسخ لمحركي Ghostscript/LibreOffice النظاميين ذوي بيانات إصدار غير متاحة. هذه ليست نتائج ناجحة. النسخة المحملة من FFmpeg 9 تدعم AMR لكنها تفتقد Speex، بينما نسخة النظام تدعم Speex وتفتقد AMR؛ لم يُضعف أي اختبار لتجاهل ذلك. الحزم لا تضم هذين المحركين الاختياريين؛ نتائج CI للحزم الفعلية وعمليات التثبيت هي بوابة النشر. اختبارات IPC الخاصة بالتشغيل المفرد اجتازت بعد السماح لمسار .NET النظامي بإنشاء mutex.

## الملفات المعدلة

المصدر البرمجي المفحوص: `09a36bba0ff68603b848581099d5a632043d194f` على فرع `feature/arabic-pdf`.

```text
.github/workflows/arabic-builds.yml
.github/workflows/portable.yml
.github/workflows/publish-arabic.yml
CHANGELOG.md
Directory.Build.props
Directory.Packages.props
README.md
THIRD-PARTY-NOTICES.md
build/build-installer.ps1
build/build-native-installer.py
build/smoke-arabic-pdf.py
build/smoke-native-installer.py
build/smoke-windows-installer.ps1
build/verify-arabic-release.py
docs/ARABIC-PDF.md
docs/ARABIC-RELEASE.md
docs/ARCHITECTURE.md
docs/ENGINES.md
docs/media/arabic-pdf/after.png
docs/media/arabic-pdf/before.png
global.json
installer/Filee.iss
installer/linux/INSTALL.txt
installer/macos/INSTALL.txt
src/Filee.App/Services/UpdateService.cs
src/Filee.Core/Conversion/IConverter.cs
src/Filee.Core/Conversion/JobQueue.cs
src/Filee.Engines/Filee.Engines.csproj
src/Filee.Engines/Hwp/Hwpx/Docx/DocxReader.Styles.cs
src/Filee.Engines/Hwp/Hwpx/Docx/DocxReader.cs
src/Filee.Engines/Hwp/Hwpx/Docx/DocxWriter.Body.cs
src/Filee.Engines/Hwp/Hwpx/HtmlReader.cs
src/Filee.Engines/Hwp/Hwpx/HwpxModel.cs
src/Filee.Engines/Hwp/Hwpx/Pptx/PptxReader.Text.cs
src/Filee.Engines/Hwp/RhwpConverter.cs
src/Filee.Engines/Office/LibreOfficeConverter.cs
src/Filee.Engines/Pdf/ArabicDocumentPdf.cs
src/Filee.Engines/Pdf/ArabicPdfGlyphWriter.cs
src/Filee.Engines/Pdf/ArabicTextLayout.cs
src/Filee.Engines/Pdf/PdfDocumentReader.cs
src/Filee.Engines/Vector/OutlinedTextPlayback.cs
src/Filee.Engines/Vector/SvgRenderer.cs
src/Filee.Engines/Vector/SvgTextSource.cs
tests/Filee.App.Tests/UpdateServiceTests.cs
tests/Filee.Engines.Tests/ArabicPdfNative.cs
tests/Filee.Engines.Tests/ArabicPdfTests.cs
tests/Filee.Engines.Tests/Filee.Engines.Tests.csproj
tests/Filee.Engines.Tests/Fixtures/Arabic/README.md
tests/Filee.Engines.Tests/Fixtures/Arabic/libreoffice-original.pdf
tests/Filee.Engines.Tests/Fixtures/Arabic/reference-0.png
tests/Filee.Engines.Tests/Fixtures/Arabic/reference-1.png
tests/Filee.Engines.Tests/Fixtures/Arabic/reference-2.png
```

تحقق محلي إضافي: اجتاز DEB x64 التثبيت وPDF→TXT/PNG وتشغيل الواجهة والإغلاق والإزالة في حاوية Ubuntu 22.04 دون شبكة، بعد تثبيت اعتماديات الاختبار. فُحصت 29 مكتبة/ملف ELF؛ الحد الأعلى `GLIBC_2.35`. هذه نتيجة البناء المحلي؛ حزم التنزيل تُفحص مستقلة في CI.

عند الانتقال من تثبيت tar السابق إلى DEB، شغّل `uninstall.sh` للتثبيت السابق أولاً حتى لا يبقى اختصار قديم في `~/.local/bin` أو قائمة التطبيقات يتقدّم على نسخة DEB. تبقى الإعدادات والمستندات محفوظة.

تصحيح اختلافات المنصات: أخفقت جولة CI السابقة في SVG على Windows وmacOS ARM64، وكُشف عبر تقارير TRX أن النصوص تتحول إلى كتل glyphs لا تحتفظ بسلسلة Unicode. عولجت الكتل اعتماداً على عناصر SVG الأصلية ومواضعها، واجتازت مجموعة PDF العربية الـ14 فحص Windows الموجّه (Run 39376783859). لم تُنشر حزم الجولة الفاشلة.

تصحيح DMG: حافظ استخراج tar بواسطة Python على الملفات العادية لكنه أسقط سمات macOS الممتدة التي تحمل توقيع بعض تجميعات .NET مثل `System.Web.dll`. نجح تحقق التوقيع عند فك الأرشيف بأداة النظام، وفشل بعد فك Python. يستخدم مصنّع DMG الآن BSD tar على macOS بعد التحقق من سلامة مسارات أعضاء الأرشيف وروابطهم؛ يبقى `codesign --verify --deep --strict` إلزامياً. التوقيع المحلي ad-hoc محفوظ؛ الحزمة ما تزال غير موثقة لدى Apple. مصدر تصحيح التغليف: `09a36bba0ff68603b848581099d5a632043d194f`، وجولة التحقق `39378403704` اجتازت فحوص التطبيق والحزم على المنصات الخمس. توقفت محاولة Intel الأولى بسبب قفل DiskImages مؤقت (`Resource busy`)؛ أُعيدت مهمتها على بيئة نظيفة واجتازت إنشاء DMG والتحقق منه وتشغيله وإزالته. بقي مسار البناء المستخدم في الإصدار مطابقاً للمصدر المفحوص.

نسخ مرجع المقارنة المرئية التي تحققت منها فعلياً: Pillow **12.3.0** وlibraqm **0.10.5**. صُححت تسمية النسخة في README المرافق للصور؛ الصور والخطوط نفسها ثابتة دون تغيير.

نجحت جولة CI الأصلية **39378403704** بعد إعادة مهمة Intel وحدها على بيئة نظيفة؛ اجتازت المنصات الخمس جميع الاختبارات المنفذة واختبارات الحزم، ثم اجتازت حزم Ubuntu 22.04 بمعماريتي x64 وARM64. توثّق `VALIDATION.json` أعداد النجاح والتخطي واختبارات PDF والتثبيت لكل منصة. مصدر ملفات التطبيق المنشورة هو `09a36bba0ff68603b848581099d5a632043d194f`؛ تلي هذا المصدر تصحيحات توثيقية وتنظيف سير النشر فقط.

## الإصدار المنشور والتحقق النهائي

- الإصدار: **1.12.1-arabic.2** (Pre-release)، منشور في [86msa/Filee](https://github.com/86msa/Filee/releases/tag/arabic-v1.12.1-arabic.2).
- المصدر: `09a36bba0ff68603b848581099d5a632043d194f`.
- [بناء واختبارات المنصات والحزم](https://github.com/86msa/Filee/actions/runs/39378403704)، ثم [بوابة النشر](https://github.com/86msa/Filee/actions/runs/39382150285): نجحا.
- اجتازت اختبارات الإصدار **8211** حالة تنفيذ، مع **386 متخطاة** و**صفر فشل**؛ مجموعة PDF العربية **14/14 على كل منصة** (70 تنفيذًا). أسماء الحالات المتخطاة مرفقة كاملة في VALIDATION.json.
- على أنظمة Unix استُبعدت قبل التنفيذ حالتان خاصتان بـWindows (`ExplorerMenuTests.Every_native_import_exists_in_the_dll_it_names` و`ExplorerMenuTests.Reading_the_registered_package_never_throws`): ثماني حالات تنفيذ عبر المنصات الأربع، تُضاف إلى غير المشغّل ولا تُحسب ناجحة.
- فُحص تثبيت وتشغيل وتحويل العربية وإزالة EXE وDEB، وفتح/نسخ/تشغيل/إزالة DMG؛ فُحصت ZIP والأرشيفات المحمولة نفسها. نجحت حزم Linux على Ubuntu 22.04 بمعماريتيه.
- نُزّلت الحزم العشر المنشورة والتقارير الثلاثة، وطوبقت **13 قيمة SHA-256**؛ نجح فحص CRC لأرشيف Windows وملفي الصور/العينات.
- أُعيد اختبار DEB x64 **المنشور** في حاوية Ubuntu 22.04 دون شبكة: تثبيت، PDF→TXT/PNG مع العينات العربية الخمس، تشغيل الواجهة وإغلاقها، وإزالة التثبيت: نجاح.
- أُرفقت **325 صورة** (230 للواجهة العربية و95 لملفات PDF ومراجعها)، والمستندات الاصطناعية وPDF والنصوص المستخرجة.

| المنصة | ناجحة | متخطاة | PDF عربية | التثبيت/التشغيل/الإزالة |
|---|---:|---:|---:|---|
| win-x64 | 1655 | 66 | 14 | نجاح |
| linux-x64 | 1642 | 77 | 14 | نجاح |
| linux-arm64 | 1640 | 79 | 14 | نجاح |
| osx-x64 | 1637 | 82 | 14 | نجاح |
| osx-arm64 | 1637 | 82 | 14 | نجاح |

### التنزيلات

| المنصة | التثبيت | محمولة |
|---|---|---|
| win | [EXE](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-win-Setup.exe) | [zip](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-win-Portable.zip) |
| linux-x64 | [DEB](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-linux-x64.deb) | [tar.gz](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-linux-x64.tar.gz) |
| linux-arm64 | [DEB](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-linux-arm64.deb) | [tar.gz](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-linux-arm64.tar.gz) |
| osx-x64 | [DMG](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-osx-x64.dmg) | [tar.gz](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-osx-x64.tar.gz) |
| osx-arm64 | [DMG](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-osx-arm64.dmg) | [tar.gz](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Filee-1.12.1-arabic.2-osx-arm64.tar.gz) |

[SHA256SUMS.txt](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/SHA256SUMS.txt) · [VALIDATION.json](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/VALIDATION.json) · [Screenshots-Arabic.zip](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/Screenshots-Arabic.zip) · [PDF-Arabic-Samples.zip](https://github.com/86msa/Filee/releases/download/arabic-v1.12.1-arabic.2/PDF-Arabic-Samples.zip)

### ما لم يُعتمد

الاختبارات المتخطاة ليست نجاحاً. الإخفاقات المحلية الأربع للمحركات الاختيارية مدونة أعلاه ولا تُمحى بنجاح حزم الإصدار. لم تُختبر Windows 10/11 وجميع إصدارات macOS/توزيعات Linux يدوياً؛ استُخدمت بيئات CI الأصلية Windows وmacOS 15 وUbuntu 24.04، إضافة إلى Ubuntu 22.04. لم يُختبر قبول Gatekeeper لتنزيل عام ولم تتوفر بيانات Developer ID/Notarization أو Authenticode. نسخة macOS ad-hoc وغير موثقة، وWindows غير موقعة. لا توجد RPM لعدم اختبار توزيعة مستهدفة. لم تُختبر مطابقة PDF/A بأداة مستقلة أو OCR أو تطابق القوالب المكتبية المعقدة مع Word.

لم تُدمج التعديلات في `main`؛ بقي عند `41ed3b271d29a5d94fc101000f017ff2ea6cf20f`. حُفظ إعداد البيئة السحابية القابل لإعادة الاستخدام بإصدار SDK 10.0.402 وتعليمات التفعيل والتشغيل؛ حفظ المسودة لا يعني نشر لقطة بيئة جديدة.


## متابعة إصلاح مثبّت Windows

يعالج **1.12.1-arabic.3** إعادة تسجيل إضافة مستكشف الملفات دون حاجة أثناء الترقية، والتي سببت `0x80073D02` عند استخدام الإضافة. التفاصيل والاختبار الذي يعيد إنتاج الخطأ قبل التحقق من الإصلاح في [WINDOWS-EXPLORER-UPGRADE.md](WINDOWS-EXPLORER-UPGRADE.md). نتائج arabic.2 أعلاه تخص إصدارها ومصدرها؛ الإصدار التالي يعاد اختباره على المنصات الخمس قبل النشر.
