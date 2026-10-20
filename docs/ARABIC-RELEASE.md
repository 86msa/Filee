# Filee 1.12.1-arabic.3 — Arabic PDF preview / معاينة PDF العربية

## العربية

- إصلاح خطأ Windows `0x80073D02` أثناء تحديث إضافة مستكشف الملفات: يحتفظ المثبّت بتسجيل الإضافة إذا تطابقت هويتها ومجلدها، دون إغلاق Explorer. يتضمن اختبار ترقية مع بقاء إضافة COM قيد الاستخدام.

- إصلاح اتصال الحروف العربية واتجاه الفقرات باستخدام HarfBuzz وUnicode BiDi، مع تضمين مجموعات خطوط Noto داخل PDF وربطها بنص Unicode قابل للنسخ والبحث.
- يشمل مسارات TXT وMarkdown وHTML وDOCX وXLSX وPPTX وHWPX، والنص العربي البسيط داخل SVG. تبقى تحويلات الصور والصوت والفيديو والأرشيف على محركاتها الحالية.
- الحفاظ على الواجهة العربية السابقة ومفاتيحها الـ696 والتبديل الفوري وRTL.
- عشر حزم: Windows x64 بصيغتي EXE وZIP؛ Linux x64/ARM64 بصيغتي tar.gz وDEB؛ macOS Intel/Apple Silicon بصيغتي tar.gz وDMG.
- بوابة النشر تشترط اختبارات النص والبحث والنسخ والمقارنة البصرية، واختبار تثبيت الحزم وتشغيلها وإزالتها على الأجهزة الأصلية. تشمل الحزم اختبار Ubuntu 22.04 بمعماريتيه.
- نتائج الاختبارات والاستثناءات في `VALIDATION.json`؛ صور المقارنة والواجهة في `Screenshots-Arabic.zip`؛ المستندات الاصطناعية في `PDF-Arabic-Samples.zip`. تحقق من التنزيلات باستخدام `SHA256SUMS.txt`.

### قيود معلنة

نسخة تجريبية من [86msa/Filee](https://github.com/86msa/Filee)، وليست إصداراً من المستودع الأصلي. تحديثات المعاينة يدوية من صفحة هذا المستودع. لا يوجد توقيع Authenticode؛ تطبيق macOS موقّع محلياً ad-hoc وغير موثق لدى Apple، وقد تمنع الحماية تشغيل النسخة المحملة. لا تتضمن التعليمات تعطيل Gatekeeper. التثبيت الآلي لا يمثل اختباراً يدوياً لجميع إصدارات Windows 10/11 أو macOS أو توزيعات Linux. لا توجد RPM لعدم اختبار توزيعة مستهدفة.

الجودة تعتمد كذلك على قارئ تنسيق المصدر: القوالب المكتبية المعقدة وتوزيع الأعمدة والجداول ذات الدمج الرأسي وحقول المعادلات ليست مضمونة كتطابق طباعي مع Word/PowerPoint. نصوص SVG ذات textPath أو tspan أو توزيع أحرف خاص تحتفظ بالمسار الرسومي السابق وقد لا تكون قابلة للبحث. ملفات PDF الممسوحة تحتاج OCR خارج نطاق هذا الإصلاح. لا يضيف الإصلاح نصاً أصلياً مفقوداً إلى ملفات PDF القديمة. لا يُعلن توافق PDF/A من دون تحقق مستقل.

## English

Fix Windows Explorer menu upgrade error `0x80073D02` by preserving an unchanged sparse package registered at the same external location. A native upgrade check holds the registered COM extension in use and reproduces the old deployment failure before testing the installer. Explorer is not forcibly closed.

Arabic document PDFs now use HarfBuzz OpenType shaping, Unicode paragraph direction/line breaking and embedded Noto glyph subsets with genuine ToUnicode mappings. Required Arabic samples, vowels, mixed Latin/numbers, long paragraphs and tables are checked for content and rendering. Existing external PDFs are checked through merge, split and compression.

Downloads: Windows x64 Setup EXE and portable ZIP; Linux x64/ARM64 tar.gz and DEB; macOS Intel/Apple Silicon tar.gz and DMG. Publication requires native test suites and installation/conversion/desktop/uninstallation checks. Ubuntu 22.04 packages are exercised on both CPUs. See `VALIDATION.json` for executed tests and skips, plus the screenshot/sample archives and SHA-256 manifest.

This fork's preview updates are manual. Windows packages are not Authenticode signed. macOS apps are ad-hoc signed, not Developer ID signed or notarized; downloaded copies may be blocked. No protection bypass is provided. No RPM is claimed. Automated native runner checks do not certify every supported OS version. Complex office layouts remain subject to the existing document readers and compositor limitations; complex SVG text keeps the existing vector playback. Scanned PDFs need OCR; pre-existing PDF producer mapping errors are preserved, not magically repaired. PDF/A conformance is not certified.
